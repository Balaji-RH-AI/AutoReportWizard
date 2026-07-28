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
    /// <see cref="ReportDefinition"/> instance.
    ///
    /// STORED PROCEDURE FIRST ARCHITECTURE:
    /// This engine generates RDLC files that use CommandType=StoredProcedure with proper
    /// QueryParameters mapped to ReportParameters. The DataSet Fields are derived from
    /// the SP's output schema (via sys.dm_exec_describe_first_result_set), and parameters
    /// are extracted from sys.parameters. No dynamic SQL generation occurs.
    ///
    /// IMPLEMENTATION NOTES / ASSUMPTIONS (please read before wiring this in):
    ///
    /// 1. Schema element order. RDL is a strict XSD sequence - children must appear in the
    ///    order the schema declares, or ReportViewer will throw "The definition of the report
    ///    is not valid." This class follows the ordering used by real Visual Studio-generated
    ///    .rdlc files: item-specific content first (e.g. Paragraphs for a Textbox, TablixBody/
    ///    TablixColumnHierarchy/TablixRowHierarchy for a Tablix), then position/size
    ///    (Top/Left/Height/Width/ZIndex), then Style.
    ///
    /// 2. Spanned/merged cells. RDL Tablix cells don't have a ColSpan attribute - a spanned cell is
    ///    represented by repeating an IDENTICAL Textbox definition in every TablixCell of that
    ///    row; the renderer detects the duplicate content across adjacent cells and merges
    ///    them visually into a single band. <see cref="BuildSpannedRow"/> implements that by
    ///    cloning the same Textbox element into each cell.
    ///
    /// 3. Tablix row/column hierarchy. Every TablixMember that has no nested TablixMembers
    ///    maps to exactly one TablixRow, in document order. Members that DO have nested
    ///    TablixMembers are pure grouping nodes and don't themselves produce a row.
    ///
    /// 4. Local processing mode. Since this targets ReportViewerCore.WinForms hosted locally
    ///    (no report server), actual rows are supplied at runtime via
    ///    <c>LocalReport.DataSources.Add(new ReportDataSource("MainDataSet", table))</c> using
    ///    the DataTable returned by ExecuteStoredProcedurePreviewAsync. The DataSet's Fields
    ///    must match that table's column names.
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
            // Use OutputFields if available (from SP metadata), otherwise fall back to Fields
            var sourceFields = definition.OutputFields.Any() ? definition.OutputFields : definition.Fields;
            
            // Include every field that shows up either as a Tablix column or as a grouping
            // key - both need a matching entry so `Fields!X.Value` resolves at render time.
            var fields = sourceFields
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

            // Build the stored procedure command text
            string spFullName = $"[{definition.SchemaName}].[{definition.StoredProcedureName}]";

            // Build QueryParameters to wire ReportParameters into the SP execution
            var queryParameters = new List<XElement>();
            
            // Use ProcedureParameters if available (extracted from SP metadata)
            var sourceParams = definition.ProcedureParameters.Any() 
                ? definition.ProcedureParameters 
                : definition.Parameters;

            foreach (var param in sourceParams)
            {
                string paramName = param.Name.StartsWith("@") ? param.Name : "@" + param.Name;
                string rdlcParamName = paramName.TrimStart('@');
                
                queryParameters.Add(new XElement(Rdl + "QueryParameter",
                    new XAttribute("Name", paramName),
                    new XElement(Rdl + "Value", $"=Parameters!{rdlcParamName}.Value")));
            }

            var queryElement = new XElement(Rdl + "Query",
                new XElement(Rdl + "DataSourceName", "MainDataSource"),
                new XElement(Rdl + "CommandType", "StoredProcedure"),
                new XElement(Rdl + "CommandText", spFullName));

            // Only add QueryParameters if there are parameters
            if (queryParameters.Any())
            {
                queryElement.Add(new XElement(Rdl + "QueryParameters", queryParameters));
            }

            return new XElement(Rdl + "DataSets",
                new XElement(Rdl + "DataSet",
                    new XAttribute("Name", "MainDataSet"),
                    queryElement,
                    new XElement(Rdl + "Fields", fields)));
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // ReportParameters (Step: Parameter binding requirement)
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildReportParametersXml(ReportDefinition definition)
        {
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
        // Visual Canvas Conversion Helpers
        // ══════════════════════════════════════════════════════════════════════════════
        
        private static XElement? BuildVisualItem(ReportComponent c, double offsetY = 0)
        {
            double top = c.Y - offsetY;
            
            if (c is TextComponent tc)
            {
                return BuildTextbox($"Txt_{c.Id:N}", ParseLiteralOrExpr(tc.Text),
                    align: "Left", fontSize: $"{tc.FontSize}pt", 
                    top: RdlMeasure.PixelsToIn(top), left: RdlMeasure.PixelsToIn(c.X),
                    width: RdlMeasure.PixelsToIn(c.Width), height: RdlMeasure.PixelsToIn(c.Height));
            }
            if (c is LineComponent lc)
            {
                return new XElement(Rdl + "Line",
                    new XAttribute("Name", $"Line_{c.Id:N}"),
                    new XElement(Rdl + "Top", RdlMeasure.PixelsToIn(top)),
                    new XElement(Rdl + "Left", RdlMeasure.PixelsToIn(c.X)),
                    new XElement(Rdl + "Height", RdlMeasure.PixelsToIn(lc.Orientation == "Vertical" ? lc.Length : 0)),
                    new XElement(Rdl + "Width", RdlMeasure.PixelsToIn(lc.Orientation == "Horizontal" ? lc.Length : 0)),
                    new XElement(Rdl + "Style", new XElement(Rdl + "Border", new XElement(Rdl + "Style", "Solid"))));
            }
            if (c is ImageComponent ic)
            {
                return new XElement(Rdl + "Image",
                    new XAttribute("Name", $"Img_{c.Id:N}"),
                    new XElement(Rdl + "Source", "External"),
                    new XElement(Rdl + "Value", ic.SourcePath),
                    new XElement(Rdl + "Top", RdlMeasure.PixelsToIn(top)),
                    new XElement(Rdl + "Left", RdlMeasure.PixelsToIn(c.X)),
                    new XElement(Rdl + "Height", RdlMeasure.PixelsToIn(c.Height)),
                    new XElement(Rdl + "Width", RdlMeasure.PixelsToIn(c.Width)));
            }
            return null;
        }

        private static string ParseLiteralOrExpr(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (text.StartsWith("=")) return text; // Already rigidly formatted
            
            if (text.Contains("[="))
            {
                string formatted = text.Replace("\"", "\"\"");
                formatted = formatted.Replace("[=", "\" & ").Replace("]", " & \"");
                return $"=\"{formatted}\"".Replace("\"\" & ", "\" & ").Replace(" & \"\"", " & \"");
            }
            return Literal(text);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Page Header — Left / Center / Right zones
        // ══════════════════════════════════════════════════════════════════════════════

        private static XElement BuildPageHeader(ReportDefinition definition)
        {
            var headerItems = definition.CanvasItems.Where(c => c.Y < 180 && !(c is TabularColumnComponent)).ToList();
            if (headerItems.Any())
            {
                var elements = headerItems.Select(c => BuildVisualItem(c, 0)).Where(e => e != null).ToList();
                return new XElement(Rdl + "PageHeader",
                    new XElement(Rdl + "Height", RdlMeasure.PixelsToIn(180)),
                    new XElement(Rdl + "PrintOnFirstPage", "true"),
                    new XElement(Rdl + "PrintOnLastPage", "true"),
                    new XElement(Rdl + "ReportItems", elements!));
            }

            // Fallback Logic
            double leftZoneLeftIn = MarginIn;
            double leftZoneWidthIn = TablixTotalWidthIn * 0.30;
            double centerZoneLeftIn = leftZoneLeftIn + leftZoneWidthIn + 0.1;
            double centerZoneWidthIn = TablixTotalWidthIn * 0.40;
            double rightZoneLeftIn = centerZoneLeftIn + centerZoneWidthIn + 0.1;
            double rightZoneWidthIn = TablixTotalWidthIn - (rightZoneLeftIn - leftZoneLeftIn);

            var items = new List<XElement>();

            var leftParams = definition.DynamicParameters.Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Left).OrderBy(p => p.HeaderOrder).ToList();
            var centerParams = definition.DynamicParameters.Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Center).OrderBy(p => p.HeaderOrder).ToList();
            var rightParams = definition.DynamicParameters.Where(p => p.MapsToHeader && p.HeaderZone == HeaderZone.Right).OrderBy(p => p.HeaderOrder).ToList();

            var leftLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(definition.StaticHeaderLeftLine1)) leftLines.Add(Literal(definition.StaticHeaderLeftLine1));
            if (!string.IsNullOrWhiteSpace(definition.StaticHeaderLeftLine2)) leftLines.Add(Literal(definition.StaticHeaderLeftLine2));
            foreach (var p in leftParams) leftLines.Add(LabelledParamExpr(p.PromptText, p.RdlcParameterName));

            double leftY = 0.05;
            for (int i = 0; i < leftLines.Count; i++)
            {
                items.Add(BuildTextbox($"HdrLeft_{i}", leftLines[i], bold: true, align: "Left", fontSize: "9pt", top: In(leftY), left: In(leftZoneLeftIn), width: In(leftZoneWidthIn), height: In(0.20)));
                leftY += 0.22;
            }

            double centerY = 0.05;
            if (!string.IsNullOrWhiteSpace(definition.ReportTitle))
            {
                items.Add(BuildTextbox("HdrCenter_Title", Literal(definition.ReportTitle), bold: true, align: "Center", fontSize: "16pt", top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.30)));
                centerY += 0.32;
            }
            if (!string.IsNullOrWhiteSpace(definition.ReportSubtitle))
            {
                var subtitleLines = definition.ReportSubtitle.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                for (int i = 0; i < subtitleLines.Length; i++)
                {
                    items.Add(BuildTextbox($"HdrCenter_Subtitle_{i}", Literal(subtitleLines[i]), bold: true, align: "Center", fontSize: "11pt", top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.22)));
                    centerY += 0.22;
                }
            }
            foreach (var p in centerParams)
            {
                items.Add(BuildTextbox($"HdrCenter_{SanitizeIdentifier(p.RdlcParameterName)}", LabelledParamExpr(p.PromptText, p.RdlcParameterName), bold: true, align: "Center", fontSize: "9pt", top: In(centerY), left: In(centerZoneLeftIn), width: In(centerZoneWidthIn), height: In(0.20)));
                centerY += 0.22;
            }

            double rightY = 0.05;
            foreach (var p in rightParams)
            {
                items.Add(BuildTextbox($"HdrRight_{SanitizeIdentifier(p.RdlcParameterName)}", LabelledParamExpr(p.PromptText, p.RdlcParameterName), bold: true, align: "Right", fontSize: "9pt", top: In(rightY), left: In(rightZoneLeftIn), width: In(rightZoneWidthIn), height: In(0.20)));
                rightY += 0.22;
            }
            if (definition.IncludePageNumbers)
            {
                items.Add(BuildTextbox("HdrRight_PageNumber", "=\"Page : \" & Globals!PageNumber & \" / \" & Globals!TotalPages", bold: true, align: "Right", fontSize: "9pt", top: In(rightY), left: In(rightZoneLeftIn), width: In(rightZoneWidthIn), height: In(0.20)));
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
            var footerItems = definition.CanvasItems.Where(c => c.Y >= 970 && !(c is TabularColumnComponent)).ToList();
            if (footerItems.Any())
            {
                var elements = footerItems.Select(c => BuildVisualItem(c, 970)).Where(e => e != null).ToList();
                return new XElement(Rdl + "PageFooter",
                    new XElement(Rdl + "Height", RdlMeasure.PixelsToIn(1123 - 970)),
                    new XElement(Rdl + "PrintOnFirstPage", "true"),
                    new XElement(Rdl + "PrintOnLastPage", "true"),
                    new XElement(Rdl + "ReportItems", elements!));
            }

            // Fallback Logic
            if (!definition.IncludeExecutionTime) return null;

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
            // Parse visual columns instead of blind data looping
            var visualColumns = definition.CanvasItems.OfType<TabularColumnComponent>().OrderBy(c => c.X).ToList();
            
            // Core Logic Safety Net
            if (visualColumns.Count == 0)
            {
                // Fallback to OutputFields (from SP) or Fields if visual canvas isn't used
                var sourceFields = definition.OutputFields.Any() ? definition.OutputFields : definition.Fields;
                var columns = sourceFields.Where(f => f.IsDetailField).OrderBy(f => f.DisplayOrder).ToList();
                var groupFields = sourceFields.Where(f => f.IsGroupBy).OrderBy(f => f.DisplayOrder).ToList();

                var widths = ResolveColumnWidths(columns);
                double tablixWidthIn = widths.Sum();

                var tablixColumns = widths.Select(w => new XElement(Rdl + "TablixColumn", new XElement(Rdl + "Width", In(w))));
                var columnMembers = columns.Select(_ => new XElement(Rdl + "TablixMember"));

                var rows = new List<XElement> { BuildColumnHeaderRow(columns) };
                var topLevelRowMembers = new List<XElement> { new XElement(Rdl + "TablixMember") };

                if (groupFields.Count > 0)
                {
                    var (groupMember, groupRows) = BuildGroupLevel(groupFields, 0, columns);
                    rows.AddRange(groupRows);
                    topLevelRowMembers.Add(groupMember);
                }
                else
                {
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

                var fallbackTablix = new XElement(Rdl + "Tablix",
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
                    new XElement(Rdl + "ReportItems", fallbackTablix),
                    new XElement(Rdl + "Height", In(tablixHeightIn + 0.10)));
            }

            // Using Visual Canvas Layout
            var visualTablixColumns = visualColumns.Select(c =>
                new XElement(Rdl + "TablixColumn", new XElement(Rdl + "Width", RdlMeasure.PixelsToIn(c.Width))));
            var visualColumnMembers = visualColumns.Select(_ => new XElement(Rdl + "TablixMember"));

            var visualRows = new List<XElement> { BuildColumnHeaderRow(visualColumns) };
            var visualTopLevelRowMembers = new List<XElement> { new XElement(Rdl + "TablixMember") };

            // Use OutputFields (from SP) if available, otherwise fall back to Fields
            var allFields = definition.OutputFields.Any() ? definition.OutputFields : definition.Fields.ToList();
            var currentGroupFields = allFields.Where(f => f.IsGroupBy).OrderBy(f => f.DisplayOrder).ToList();
            
            if (currentGroupFields.Count > 0)
            {
                var (groupMember, groupRows) = BuildGroupLevel(currentGroupFields, 0, visualColumns, allFields);
                visualRows.AddRange(groupRows);
                visualTopLevelRowMembers.Add(groupMember);
            }
            else
            {
                visualRows.Add(BuildDetailRow(visualColumns, allFields));
                visualTopLevelRowMembers.Add(new XElement(Rdl + "TablixMember", new XElement(Rdl + "Group", new XAttribute("Name", "Details"))));
            }

            if (definition.IncludeGrandTotals)
            {
                visualRows.Add(BuildGrandTotalsRow(visualColumns, allFields));
                visualTopLevelRowMembers.Add(new XElement(Rdl + "TablixMember"));
            }

            double tablixWidthPx = visualColumns.Sum(c => c.Width);
            reportSectionWidthIn = Math.Max(tablixWidthPx / 96.0, PageWidthIn - (2 * MarginIn));

            double minX = visualColumns.Min(c => c.X);
            double minY = visualColumns.Min(c => c.Y);
            double topPx = Math.Max(0, minY - 180);
            double heightIn = visualRows.Count * DefaultRowHeightIn;

            var tablix = new XElement(Rdl + "Tablix",
                new XAttribute("Name", "Tablix_Main"),
                new XElement(Rdl + "TablixBody",
                    new XElement(Rdl + "TablixColumns", visualTablixColumns),
                    new XElement(Rdl + "TablixRows", visualRows)),
                new XElement(Rdl + "TablixColumnHierarchy", new XElement(Rdl + "TablixMembers", visualColumnMembers)),
                new XElement(Rdl + "TablixRowHierarchy", new XElement(Rdl + "TablixMembers", visualTopLevelRowMembers)),
                new XElement(Rdl + "DataSetName", "MainDataSet"),
                new XElement(Rdl + "Top", RdlMeasure.PixelsToIn(topPx)),
                new XElement(Rdl + "Left", RdlMeasure.PixelsToIn(minX)),
                new XElement(Rdl + "Height", In(heightIn)),
                new XElement(Rdl + "Width", RdlMeasure.PixelsToIn(tablixWidthPx)));

            var bodyItems = definition.CanvasItems.Where(c => c.Y >= 180 && c.Y < 970 && !(c is TabularColumnComponent)).ToList();
            var elements = bodyItems.Select(c => BuildVisualItem(c, 180)).Where(e => e != null).ToList();
            elements.Add(tablix);

            return new XElement(Rdl + "Body",
                new XElement(Rdl + "ReportItems", elements!),
                new XElement(Rdl + "Height", In(Math.Max(heightIn + 0.10, (970 - 180) / 96.0))));
        }

        // Overloads for visual canvas row building
        private static (XElement Member, List<XElement> Rows) BuildGroupLevel(
            List<ReportField> groupFields, int level, List<TabularColumnComponent> columns, List<ReportField> allFields)
        {
            if (level >= groupFields.Count)
            {
                var detailsMember = new XElement(Rdl + "TablixMember",
                    new XElement(Rdl + "Group", new XAttribute("Name", "Details")));
                return (detailsMember, new List<XElement> { BuildDetailRow(columns, allFields) });
            }

            var field = groupFields[level];
            string fieldRef = SanitizeIdentifier(field.GetDatasetFieldName());
            string groupName = $"{fieldRef}Group";
            string label = field.DisplayHeaderLabel.ToUpperInvariant();
            string headerExpr = $"=\"{Escape(label)} : \" & Fields!{fieldRef}.Value";

            var headerRow = BuildSpannedRow($"GroupHeader_{groupName}", headerExpr, columns.Count);
            var headerMember = new XElement(Rdl + "TablixMember");

            var (childMember, childRows) = BuildGroupLevel(groupFields, level + 1, columns, allFields);

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

        private static XElement BuildColumnHeaderRow(List<TabularColumnComponent> columns)
        {
            var cells = columns.Select(c =>
                new XElement(Rdl + "TablixCell",
                    new XElement(Rdl + "CellContents",
                        BuildTextbox($"Hdr_{c.Id:N}", Literal(c.HeaderString.ToUpperInvariant()),
                            bold: true, align: "Center", fontSize: "9pt",
                            topBorderColor: GridLineColor, bottomBorderColor: GridLineColor))));

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(HeaderRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        private static XElement BuildDetailRow(List<TabularColumnComponent> columns, List<ReportField> allFields)
        {
            var cells = columns.Select(c =>
            {
                var fieldData = allFields.FirstOrDefault(f => f.Name == c.BoundField);
                
                // Safely handle null expressions from dynamically generated columns
                string expr = c.DataExpression ?? string.Empty; 
                
                // If the expression is empty (auto-generated) OR it's a standard field mapping, build the SSRS expression
                if (fieldData != null && (string.IsNullOrWhiteSpace(expr) || expr.StartsWith("=Fields!")))
                {
                    string fieldName = SanitizeIdentifier(fieldData.GetDatasetFieldName());
                    expr = $"=Fields!{fieldName}.Value";
                }

                return new XElement(Rdl + "TablixCell",
                    new XElement(Rdl + "CellContents",
                        BuildTextbox($"Txt_{c.Id:N}", expr, bold: false, 
                            align: fieldData != null && IsRightAligned(fieldData) ? "Right" : "Left",
                            fontSize: "9pt", format: fieldData != null ? ResolveFormat(fieldData) : null)));
            });

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(DefaultRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        private static XElement BuildGrandTotalsRow(List<TabularColumnComponent> columns, List<ReportField> allFields)
        {
            var cells = new List<XElement>();
            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var f = allFields.FirstOrDefault(field => field.Name == c.BoundField);
                XElement textbox;

                if (i == 0)
                {
                    textbox = BuildTextbox("Txt_GrandTotalsLabel", Literal("GRAND TOTALS"),
                        bold: true, align: "Left", fontSize: "9pt", topBorderColor: GridLineColor);
                }
                else if (f != null && f.Aggregate != AggregateFunction.None)
                {
                    string fieldName = SanitizeIdentifier(f.GetDatasetFieldName());
                    textbox = BuildTextbox($"Txt_Total_{c.Id:N}", $"=Sum(Fields!{fieldName}.Value)",
                        bold: true, align: "Right", fontSize: "9pt",
                        format: ResolveFormat(f), topBorderColor: GridLineColor);
                }
                else
                {
                    textbox = BuildTextbox($"Txt_TotalBlank_{c.Id:N}", string.Empty,
                        bold: false, align: "Left", fontSize: "9pt", topBorderColor: GridLineColor);
                }

                cells.Add(new XElement(Rdl + "TablixCell", new XElement(Rdl + "CellContents", textbox)));
            }

            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", In(DefaultRowHeightIn)),
                new XElement(Rdl + "TablixCells", cells));
        }

        // Fallback overloads for legacy definition.Fields processing
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
            var headerMember = new XElement(Rdl + "TablixMember");

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

        private static string SanitizeIdentifier(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Field";
            var sanitized = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (sanitized.Length == 0) sanitized = "Field";
            if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_') sanitized = "_" + sanitized;
            return sanitized;
        }

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