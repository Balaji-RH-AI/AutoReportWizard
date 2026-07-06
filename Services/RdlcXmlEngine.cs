using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Programmatically builds an RDLC 2016 report definition (ReportViewerCore.WinForms /
    /// VS2019+ designer schema, xmlns "…/2016/01/reportdefinition") entirely from a
    /// <see cref="ReportDefinition"/> instance. No static/mock markup — every Textbox value
    /// is either a live <c>Fields!</c>/<c>Parameters!</c>/<c>Globals!</c> expression or a piece
    /// of literal text the user actually configured in the wizard (report title, static header
    /// lines, parameter prompt text).
    ///
    /// IMPLEMENTATION NOTES / ASSUMPTIONS (please read before wiring this in):
    ///
    /// 1. Schema element order. RDL is a strict XSD sequence — children must appear in the
    ///    order the schema declares, or ReportViewer will throw "The definition of the report
    ///    is not valid." This class follows the ordering used by real Visual Studio–generated
    ///    .rdlc files: item-specific content first (e.g. Paragraphs for a Textbox, TablixBody/
    ///    TablixColumnHierarchy/TablixRowHierarchy for a Tablix), then position/size
    ///    (Top/Left/Height/Width/ZIndex), then Style. No .NET/schema tooling was available in
    ///    the environment this was written in to compile- or schema-validate the output, so
    ///    please open the generated file in the RDLC designer (or run it through
    ///    ReportViewerCore.WinForms once) before shipping.
    ///
    /// 2. Spanned/merged cells (the "BATCH NBR : 3292" band that stretches across every
    ///    column). RDL Tablix cells don't have a ColSpan attribute — a spanned cell is
    ///    represented by repeating an IDENTICAL Textbox definition in every TablixCell of that
    ///    row; the renderer detects the duplicate content across adjacent cells and merges
    ///    them visually into a single band. <see cref="BuildSpannedRow"/> implements that by
    ///    cloning the same Textbox element into each cell.
    ///
    /// 3. Tablix row/column hierarchy. Every TablixMember that has no nested TablixMembers
    ///    maps to exactly one TablixRow, in document order. Members that DO have nested
    ///    TablixMembers are pure grouping nodes and don't themselves produce a row. The row
    ///    hierarchy built here is:
    ///       [ static: column-header row ]
    ///       [ dynamic: group member for the 1st IsGroupBy field
    ///           [ static: group header row ("BATCH NBR : …", spanned) ]
    ///           [ dynamic: next group level, or the Details group + one detail row ]
    ///       ]
    ///       [ static: grand-totals row ]  (only if IncludeGrandTotals)
    ///    Multiple IsGroupBy fields nest recursively in DisplayOrder.
    ///
    /// 4. Local processing mode. Since this targets ReportViewerCore.WinForms hosted locally
    ///    (no report server), the DataSource's ConnectionProperties are design-time metadata
    ///    only — actual rows are supplied at runtime via
    ///    <c>LocalReport.DataSources.Add(new ReportDataSource("MainDataSet", table))</c> using
    ///    the DataTable your SqlGeneratorService returns. The DataSet's Fields must match that
    ///    table's column names, which is why they're derived from
    ///    <see cref="ReportField.GetDatasetFieldName"/> — the exact same alias
    ///    <see cref="ReportField.GetSelectExpression"/> produces in the generated T-SQL.
    /// </summary>
    public static class RdlcXmlEngine
    {
        private static readonly XNamespace Rdl =
            "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
        private static readonly XNamespace Rd =
            "http://schemas.microsoft.com/SQLServer/reporting/reportdesigner";

        // Overall canvas — tuned for a wide, many-column detail grid like the Batch Detail
        // Report. Adjust freely; everything downstream (column widths, header zone widths)
        // is computed relative to these.
        private const double PageWidthIn = 14.0;
        private const double PageHeightIn = 8.5;
        private const double MarginIn = 0.25;
        private const double TablixTotalWidthIn = PageWidthIn - (2 * MarginIn);

        private const string GridLineColor = "LightGrey";
        private const double DefaultRowHeightIn = 0.20;
        private const double HeaderRowHeightIn = 0.25;
        private const double SpannedRowHeightIn = 0.22;

        // ══════════════════════════════════════════════════════════════════════════════
        // Entry point
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>Builds the full RDLC XDocument for the given report definition.</summary>
        public static XDocument GenerateRdlcXml(ReportDefinition definition)
        {
            if (definition is null) throw new ArgumentNullException(nameof(definition));
            if (definition.Fields is null || definition.Fields.Count == 0)
                throw new InvalidOperationException(
                    "ReportDefinition.Fields must be populated (Step 2) before RDLC generation.");
            if (!definition.Fields.Any(f => f.IsDetailField))
                throw new InvalidOperationException(
                    "At least one ReportField must have IsDetailField = true to form the Tablix column set.");

            var body = BuildBody(definition, out double reportSectionWidthIn);
            var pageHeader = BuildPageHeader(definition);
            var pageFooter = BuildPageFooter(definition);

            var page = new XElement(Rdl + "Page",
                pageHeader,
                pageFooter,
                new XElement(Rdl + "PageHeight", In(PageHeightIn)),
                new XElement(Rdl + "PageWidth", In(PageWidthIn)),
                new XElement(Rdl + "LeftMargin", In(MarginIn)),
                new XElement(Rdl + "RightMargin", In(MarginIn)),
                new XElement(Rdl + "TopMargin", In(MarginIn)),
                new XElement(Rdl + "BottomMargin", In(MarginIn)),
                new XElement(Rdl + "ColumnSpacing", "0.13in"));

            var reportSection = new XElement(Rdl + "ReportSection",
                body,
                new XElement(Rdl + "Width", In(reportSectionWidthIn)),
                page);

            var report = new XElement(Rdl + "Report",
                new XAttribute(XNamespace.Xmlns + "rd", Rd.NamespaceName),
                new XAttribute("Name", SanitizeIdentifier(definition.ReportName)),
                BuildDataSources(definition),
                BuildDataSets(definition),
                new XElement(Rdl + "ReportSections", reportSection),
                BuildReportParametersXml(definition),
                new XElement(Rdl + "Language", "en-US"));

            return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), report);
        }

        /// <summary>Convenience overload: generates and saves the .rdlc file to disk.</summary>
        public static void GenerateRdlcFile(ReportDefinition definition, string outputPath)
        {
            var doc = GenerateRdlcXml(definition);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            using var writer = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
            doc.Save(writer);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // DataSources / DataSets
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildDataSources(ReportDefinition definition) =>
            new XElement(Rdl + "DataSources",
                new XElement(Rdl + "DataSource",
                    new XAttribute("Name", "MainDataSource"),
                    new XElement(Rdl + "ConnectionProperties",
                        new XElement(Rdl + "DataProvider", "SQL"),
                        new XElement(Rdl + "ConnectString",
                            $"Data Source={Escape(definition.ServerName)};Initial Catalog={Escape(definition.DatabaseName)}")),
                    new XElement(Rd + "DataSourceID", Guid.NewGuid().ToString())));

        private static XElement BuildDataSets(ReportDefinition definition)
        {
            // Include every field that shows up either as a Tablix column or as a grouping
            // key — both need a matching entry so `Fields!X.Value` resolves at render time.
            var fields = definition.Fields
                .Where(f => f.IsDetailField || f.IsGroupBy)
                .OrderBy(f => f.DisplayOrder)
                .Select(f =>
                {
                    string alias = SanitizeIdentifier(f.GetDatasetFieldName());
                    return new XElement(Rdl + "Field",
                        new XAttribute("Name", alias),
                        new XElement(Rdl + "DataField", f.GetDatasetFieldName()),
                        new XElement(Rd + "TypeName", string.IsNullOrWhiteSpace(f.DotNetType) ? "System.String" : f.DotNetType));
                });

            string commandText = string.IsNullOrWhiteSpace(definition.CustomSql)
                ? "-- Populated by SqlGeneratorService at generation time; the DataSet's Fields\n-- below are what matter for local rendering — see LocalReport.DataSources at runtime."
                : definition.CustomSql;

            return new XElement(Rdl + "DataSets",
                new XElement(Rdl + "DataSet",
                    new XAttribute("Name", "MainDataSet"),
                    new XElement(Rdl + "Query",
                        new XElement(Rdl + "DataSourceName", "MainDataSource"),
                        new XElement(Rdl + "CommandText", commandText)),
                    new XElement(Rdl + "Fields", fields)));
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // ReportParameters (Step: Parameter binding requirement)
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildReportParametersXml(ReportDefinition definition)
        {
            // Parameters is meant to be synced from DynamicParameters before generation
            // (see ReportDefinition's doc comment) — but fall back to deriving it here so
            // this engine still works if that sync step hasn't run yet.
            var sourceParams = definition.Parameters is { Count: > 0 }
                ? definition.Parameters
                : definition.DynamicParameters.Select(dp => dp.ToReportParameter()).ToList();

            var dynamicByName = definition.DynamicParameters
                .GroupBy(p => NormalizeParamName(p.ParameterName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var parameterElements = sourceParams.Select(rp =>
            {
                string rdlcName = NormalizeParamName(rp.Name);
                dynamicByName.TryGetValue(rdlcName, out var dyn);
                string prompt = !string.IsNullOrWhiteSpace(dyn?.PromptText) ? dyn!.PromptText : rdlcName;

                return new XElement(Rdl + "ReportParameter",
                    new XAttribute("Name", rdlcName),
                    new XElement(Rdl + "DataType", string.IsNullOrWhiteSpace(rp.RdlcDataType) ? "String" : rp.RdlcDataType),
                    rp.IsHidden ? new XElement(Rdl + "Hidden", "true") : null,
                    new XElement(Rdl + "AllowBlank", rp.AllowBlank ? "true" : "false"),
                    new XElement(Rdl + "Prompt", prompt));
            });

            return new XElement(Rdl + "ReportParameters", parameterElements);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Page Header — Left / Center / Right zones
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildPageHeader(ReportDefinition definition)
        {
            double leftZoneLeftIn = MarginIn;
            double leftZoneWidthIn = TablixTotalWidthIn * 0.30;
            double centerZoneLeftIn = leftZoneLeftIn + leftZoneWidthIn + 0.1;
            double centerZoneWidthIn = TablixTotalWidthIn * 0.40;
            double rightZoneLeftIn = centerZoneLeftIn + centerZoneWidthIn + 0.1;
            double rightZoneWidthIn = TablixTotalWidthIn - (rightZoneLeftIn - leftZoneLeftIn);

            var items = new List<XElement>();

            var leftParams = definition.DynamicParameters
                .Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Left)
                .OrderBy(p => p.HeaderOrder).ToList();
            var centerParams = definition.DynamicParameters
                .Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Center)
                .OrderBy(p => p.HeaderOrder).ToList();
            var rightParams = definition.DynamicParameters
                .Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Right)
                .OrderBy(p => p.HeaderOrder).ToList();

            // ── Left zone: optional static letterhead lines first, then dynamic "Label : @Param" lines ──
            var leftLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(definition.StaticHeaderLeftLine1)) leftLines.Add(Literal(definition.StaticHeaderLeftLine1));
            if (!string.IsNullOrWhiteSpace(definition.StaticHeaderLeftLine2)) leftLines.Add(Literal(definition.StaticHeaderLeftLine2));
            foreach (var p in leftParams) leftLines.Add(LabelledParamExpr(p.PromptText, p.RdlcParameterName));

            double leftY = 0.05;
            for (int i = 0; i < leftLines.Count; i++)
            {
                items.Add(BuildTextbox($"HdrLeft_{i}", leftLines[i], bold: true, align: "Left", fontSize: "9pt",
                    top: In(leftY), left: In(leftZoneLeftIn), width: In(leftZoneWidthIn), height: In(0.20)));
                leftY += 0.22;
            }

            // ── Center zone: Report Title (line 1, large/bold), Subtitle line(s), then any center-mapped params ──
            double centerY = 0.05;
            if (!string.IsNullOrWhiteSpace(definition.ReportTitle))
            {
                items.Add(BuildTextbox("HdrCenter_Title", Literal(definition.ReportTitle), bold: true, align: "Center", fontSize: "16pt",
                    top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.30)));
                centerY += 0.32;
            }
            if (!string.IsNullOrWhiteSpace(definition.ReportSubtitle))
            {
                // ReportDefinition only exposes a single ReportSubtitle string. If you need more
                // than one subtitle line as a first-class field (rather than newline-delimited,
                // as done here), consider adding a `List<string> ReportSubtitleLines` to
                // ReportDefinition — this split is a pragmatic bridge until then.
                var subtitleLines = definition.ReportSubtitle
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int i = 0; i < subtitleLines.Length; i++)
                {
                    items.Add(BuildTextbox($"HdrCenter_Subtitle_{i}", Literal(subtitleLines[i]), bold: true, align: "Center", fontSize: "11pt",
                        top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.22)));
                    centerY += 0.22;
                }
            }
            foreach (var p in centerParams)
            {
                items.Add(BuildTextbox($"HdrCenter_{SanitizeIdentifier(p.RdlcParameterName)}", LabelledParamExpr(p.PromptText, p.RdlcParameterName),
                    bold: true, align: "Center", fontSize: "9pt",
                    top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.20)));
                centerY += 0.22;
            }

            // ── Right zone: dynamic "Label : @Param" lines, then built-in Page X / Y ──
            double rightY = 0.05;
            foreach (var p in rightParams)
            {
                items.Add(BuildTextbox($"HdrRight_{SanitizeIdentifier(p.RdlcParameterName)}", LabelledParamExpr(p.PromptText, p.RdlcParameterName),
                    bold: true, align: "Right", fontSize: "9pt",
                    top: In(rightY), left: In(rightZoneLeftIn), width: In(rightZoneWidthIn), height: In(0.20)));
                rightY += 0.22;
            }
            if (definition.IncludePageNumbers)
            {
                items.Add(BuildTextbox("HdrRight_PageNumber",
                    "=\"Page : \" & Globals!PageNumber & \" / \" & Globals!TotalPages",
                    bold: true, align: "Right", fontSize: "9pt",
                    top: In(rightY), left: In(rightZoneLeftIn), width: In(rightZoneWidthIn), height: In(0.20)));
                rightY += 0.22;
            }

            double headerHeightIn = Math.Max(0.30, new[] { leftY, centerY, rightY }.Max() + 0.05);

            return new XElement(Rdl + "PageHeader",
                new XElement(Rdl + "Height", In(headerHeightIn)),
                new XElement(Rdl + "PrintOnFirstPage", "true"),
                new XElement(Rdl + "PrintOnLastPage", "true"),
                new XElement(Rdl + "ReportItems", items));
        }

        private static XElement? BuildPageFooter(ReportDefinition definition)
        {
            if (!definition.IncludeExecutionTime)
                return null;

            var tb = BuildTextbox("FtrExecutionTime", "=\"Printed: \" & Globals!ExecutionTime",
                bold: false, align: "Left", fontSize: "8pt",
                top: "0.03in", left: In(MarginIn), width: "4in", height: "0.18in");

            return new XElement(Rdl + "PageFooter",
                new XElement(Rdl + "Height", "0.25in"),
                new XElement(Rdl + "PrintOnFirstPage", "true"),
                new XElement(Rdl + "PrintOnLastPage", "true"),
                new XElement(Rdl + "ReportItems", tb));
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Body: Tablix with grouping (Requirement #2)
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildBody(ReportDefinition definition, out double reportSectionWidthIn)
        {
            var columns = definition.Fields.Where(f => f.IsDetailField).OrderBy(f => f.DisplayOrder).ToList();
            var groupFields = definition.Fields.Where(f => f.IsGroupBy).OrderBy(f => f.DisplayOrder).ToList();

            var widths = ResolveColumnWidths(columns);
            double tablixWidthIn = widths.Sum();

            var tablixColumns = widths.Select(w => new XElement(Rdl + "TablixColumn", new XElement(Rdl + "Width", In(w))));
            var columnMembers = columns.Select(_ => new XElement(Rdl + "TablixMember"));

            var rows = new List<XElement> { BuildColumnHeaderRow(columns) };
            var topLevelRowMembers = new List<XElement> { new XElement(Rdl + "TablixMember") }; // maps to column-header row

            if (groupFields.Count > 0)
            {
                var (groupMember, groupRows) = BuildGroupLevel(groupFields, 0, columns);
                rows.AddRange(groupRows);
                topLevelRowMembers.Add(groupMember);
            }
            else
            {
                // No grouping configured — flat detail table under an unnamed Details group.
                rows.Add(BuildDetailRow(columns));
                topLevelRowMembers.Add(new XElement(Rdl + "TablixMember",
                    new XElement(Rdl + "Group", new XAttribute("Name", "Details"))));
            }

            if (definition.IncludeGrandTotals)
            {
                rows.Add(BuildGrandTotalsRow(columns));
                topLevelRowMembers.Add(new XElement(Rdl + "TablixMember"));
            }

            double tablixHeightIn = rows.Count * DefaultRowHeightIn + 0.10;

            var tablix = new XElement(Rdl + "Tablix",
                new XAttribute("Name", "Tablix_Main"),
                new XElement(Rdl + "TablixBody",
                    new XElement(Rdl + "TablixColumns", tablixColumns),
                    new XElement(Rdl + "TablixRows", rows)),
                new XElement(Rdl + "TablixColumnHierarchy", new XElement(Rdl + "TablixMembers", columnMembers)),
                new XElement(Rdl + "TablixRowHierarchy", new XElement(Rdl + "TablixMembers", topLevelRowMembers)),
                new XElement(Rdl + "DataSetName", "MainDataSet"),
                new XElement(Rdl + "Top", "0in"),
                new XElement(Rdl + "Left", "0in"),
                new XElement(Rdl + "Height", In(tablixHeightIn)),
                new XElement(Rdl + "Width", In(tablixWidthIn)));

            reportSectionWidthIn = Math.Max(tablixWidthIn, TablixTotalWidthIn);

            return new XElement(Rdl + "Body",
                new XElement(Rdl + "ReportItems", tablix),
                new XElement(Rdl + "Height", In(tablixHeightIn + 0.10)));
        }

        /// <summary>
        /// Recursively builds one level of the row-grouping hierarchy for the given ordered
        /// list of IsGroupBy fields. Returns the TablixMember for this level plus the flat,
        /// document-ordered list of TablixRow elements it (and its descendants) contribute.
        /// </summary>
        private static (XElement Member, List<XElement> Rows) BuildGroupLevel(
            List<ReportField> groupFields, int level, List<ReportField> columns)
        {
            if (level >= groupFields.Count)
            {
                var detailsMember = new XElement(Rdl + "TablixMember",
                    new XElement(Rdl + "Group", new XAttribute("Name", "Details")));
                return (detailsMember, new List<XElement> { BuildDetailRow(columns) });
            }

            var field = groupFields[level];
            string fieldRef = SanitizeIdentifier(field.GetDatasetFieldName());
            string groupName = $"{fieldRef}Group";
            string label = field.DisplayHeaderLabel.ToUpperInvariant();
            string headerExpr = $"=\"{Escape(label)} : \" & Fields!{fieldRef}.Value";

            var headerRow = BuildSpannedRow($"GroupHeader_{groupName}", headerExpr, columns.Count);
            var headerMember = new XElement(Rdl + "TablixMember"); // static leaf → maps to headerRow

            var (childMember, childRows) = BuildGroupLevel(groupFields, level + 1, columns);

            var groupMember = new XElement(Rdl + "TablixMember",
                new XElement(Rdl + "Group",
                    new XAttribute("Name", groupName),
                    new XElement(Rdl + "GroupExpressions",
                        new XElement(Rdl + "GroupExpression", $"=Fields!{fieldRef}.Value"))),
                new XElement(Rdl + "TablixMembers", headerMember, childMember));

            var rows = new List<XElement> { headerRow };
            rows.AddRange(childRows);
            return (groupMember, rows);
        }

        private static XElement BuildColumnHeaderRow(List<ReportField> columns)
        {
            var cells = columns.Select(f =>
                new XElement(Rdl + "TablixCell",
                    new XElement(Rdl + "CellContents",
                        BuildTextbox($"Hdr_{SanitizeIdentifier(f.Name)}", Literal(f.DisplayHeaderLabel.ToUpperInvariant()),
                            bold: true, align: "Center", fontSize: "9pt",
                            topBorderColor: GridLineColor, bottomBorderColor: GridLineColor))));

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(HeaderRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        private static XElement BuildDetailRow(List<ReportField> columns)
        {
            var cells = columns.Select(f =>
            {
                string fieldName = SanitizeIdentifier(f.GetDatasetFieldName());
                string expr = $"=Fields!{fieldName}.Value";
                return new XElement(Rdl + "TablixCell",
                    new XElement(Rdl + "CellContents",
                        BuildTextbox($"Txt_{fieldName}", expr, bold: false, align: IsRightAligned(f) ? "Right" : "Left",
                            fontSize: "9pt", format: ResolveFormat(f))));
            });

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(DefaultRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        private static XElement BuildGrandTotalsRow(List<ReportField> columns)
        {
            var cells = new List<XElement>();
            for (int i = 0; i < columns.Count; i++)
            {
                var f = columns[i];
                XElement textbox;

                if (i == 0)
                {
                    textbox = BuildTextbox("Txt_GrandTotalsLabel", Literal("GRAND TOTALS"),
                        bold: true, align: "Left", fontSize: "9pt", topBorderColor: GridLineColor);
                }
                else if (f.Aggregate != AggregateFunction.None)
                {
                    string fieldName = SanitizeIdentifier(f.GetDatasetFieldName());
                    textbox = BuildTextbox($"Txt_Total_{fieldName}", $"=Sum(Fields!{fieldName}.Value)",
                        bold: true, align: "Right", fontSize: "9pt",
                        format: ResolveFormat(f), topBorderColor: GridLineColor);
                }
                else
                {
                    textbox = BuildTextbox($"Txt_TotalBlank_{i}", string.Empty,
                        bold: false, align: "Left", fontSize: "9pt", topBorderColor: GridLineColor);
                }

                cells.Add(new XElement(Rdl + "TablixCell", new XElement(Rdl + "CellContents", textbox)));
            }

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(DefaultRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        /// <summary>
        /// Builds a row where the SAME Textbox definition is repeated in every cell so the
        /// renderer merges them into a single band spanning the full table width. Used for
        /// the "BATCH NBR : 3292" style group header.
        /// </summary>
        private static XElement BuildSpannedRow(string baseName, string expression, int columnCount)
        {
            var template = BuildTextbox(baseName, expression, bold: true, align: "Left", fontSize: "9pt",
                topBorderColor: GridLineColor, bottomBorderColor: GridLineColor);

            var cells = Enumerable.Range(0, columnCount)
                .Select(_ => new XElement(Rdl + "TablixCell", new XElement(Rdl + "CellContents", new XElement(template))));

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(SpannedRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        private static List<double> ResolveColumnWidths(List<ReportField> columns)
        {
            var widths = new double[columns.Count];
            double explicitSum = 0;
            int autoCount = 0;

            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].ColumnWidth > 0)
                {
                    widths[i] = columns[i].ColumnWidth;
                    explicitSum += widths[i];
                }
                else
                {
                    autoCount++;
                }
            }

            double remaining = Math.Max(TablixTotalWidthIn - explicitSum, autoCount * 0.5);
            double autoWidth = autoCount > 0 ? remaining / autoCount : 0;

            for (int i = 0; i < columns.Count; i++)
                if (columns[i].ColumnWidth <= 0)
                    widths[i] = autoWidth;

            return widths.ToList();
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Shared Textbox builder
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a Textbox element. When top/left/width/height are supplied, it's built as a
        /// free-floating item (PageHeader/PageFooter); when they're omitted, it's built for use
        /// inside a TablixCell, where the cell/column/row dimensions govern its geometry.
        /// </summary>
        private static XElement BuildTextbox(
            string name,
            string value,
            bool bold = false,
            string align = "Left",
            string fontSize = "9pt",
            string? format = null,
            string? topBorderColor = null,
            string? bottomBorderColor = null,
            string? top = null,
            string? left = null,
            string? width = null,
            string? height = null)
        {
            var textRunStyle = new XElement(Rdl + "Style",
                new XElement(Rdl + "FontWeight", bold ? "Bold" : "Normal"),
                new XElement(Rdl + "FontSize", fontSize));
            if (format is not null)
                textRunStyle.Add(new XElement(Rdl + "Format", format));

            var textbox = new XElement(Rdl + "Textbox",
                new XAttribute("Name", name),
                new XElement(Rdl + "CanGrow", "true"),
                new XElement(Rdl + "KeepTogether", "true"),
                new XElement(Rdl + "Paragraphs",
                    new XElement(Rdl + "Paragraph",
                        new XElement(Rdl + "TextRuns",
                            new XElement(Rdl + "TextRun",
                                new XElement(Rdl + "Value", value),
                                textRunStyle)),
                        new XElement(Rdl + "Style", new XElement(Rdl + "TextAlign", align)))),
                new XElement(Rd + "DefaultName", name));

            if (top is not null) textbox.Add(new XElement(Rdl + "Top", top));
            if (left is not null) textbox.Add(new XElement(Rdl + "Left", left));
            if (height is not null) textbox.Add(new XElement(Rdl + "Height", height));
            if (width is not null) textbox.Add(new XElement(Rdl + "Width", width));

            var boxStyle = new XElement(Rdl + "Style",
                new XElement(Rdl + "Border", new XElement(Rdl + "Style", "None")),
                new XElement(Rdl + "PaddingLeft", "2pt"),
                new XElement(Rdl + "PaddingRight", "2pt"),
                new XElement(Rdl + "PaddingTop", "1pt"),
                new XElement(Rdl + "PaddingBottom", "1pt"));

            if (topBorderColor is not null)
                boxStyle.Add(new XElement(Rdl + "TopBorder",
                    new XElement(Rdl + "Style", "Solid"),
                    new XElement(Rdl + "Width", "0.5pt"),
                    new XElement(Rdl + "Color", topBorderColor)));
            if (bottomBorderColor is not null)
                boxStyle.Add(new XElement(Rdl + "BottomBorder",
                    new XElement(Rdl + "Style", "Solid"),
                    new XElement(Rdl + "Width", "0.5pt"),
                    new XElement(Rdl + "Color", bottomBorderColor)));

            textbox.Add(boxStyle);
            return textbox;
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Small helpers
        // ══════════════════════════════════════════════════════════════════════════════

        private static string In(double inches) => inches.ToString("0.00", CultureInfo.InvariantCulture) + "in";

        private static string Literal(string text) => $"=\"{Escape(text)}\"";

        private static string Escape(string s) => (s ?? string.Empty).Replace("\"", "\"\"");

        private static string LabelledParamExpr(string label, string paramName)
        {
            string safeLabel = string.IsNullOrWhiteSpace(label) ? paramName : label;
            return $"=\"{Escape(safeLabel)} : \" & Parameters!{SanitizeIdentifier(paramName)}.Value";
        }

        private static string NormalizeParamName(string raw) => SanitizeIdentifier((raw ?? string.Empty).TrimStart('@'));

        /// <summary>Strips characters that aren't valid in an RDL/CLR identifier (element Name / Fields!x / Parameters!x).</summary>
        private static string SanitizeIdentifier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Field";
            var sanitized = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (sanitized.Length == 0) sanitized = "Field";
            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_') sanitized = "_" + sanitized;
            return sanitized;
        }

        /// <summary>4-decimal numeric format for money/decimal-like types, whole-number format for
        /// integers, short date for date/time types — matches the "AMOUNT PAID: 172.0000" requirement.</summary>
        private static string? ResolveFormat(ReportField f)
        {
            return f.SqlDataType.Trim().ToLowerInvariant() switch
            {
                "decimal" or "money" or "numeric" or "smallmoney" or "float" or "real" => "N4",
                "int" or "bigint" or "smallint" or "tinyint" => "N0",
                "date" or "datetime" or "datetime2" or "smalldatetime" => "d",
                _ => null
            };
        }

        private static bool IsRightAligned(ReportField f) => f.SqlDataType.Trim().ToLowerInvariant() is
            "decimal" or "money" or "numeric" or "smallmoney" or "float" or "real" or
            "int" or "bigint" or "smallint" or "tinyint";
    }
}
