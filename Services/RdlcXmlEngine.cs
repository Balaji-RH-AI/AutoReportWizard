using System.Xml.Linq;
using System.Xml.Schema;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// RDLC XML generation engine using System.Xml.Linq.XDocument exclusively.
    ///
    /// No string concatenation of XML at any point — all nodes are constructed
    /// via XElement/XAttribute to prevent malformed XML crashes.
    ///
    /// The generated RDLC targets the official SSRS 2016 schema:
    ///   http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition
    ///
    /// Field bindings use =Fields!FieldName.Value syntax in the Value expressions.
    /// </summary>
    public class RdlcXmlEngine
    {
        // ── SSRS 2016 Schema Namespace ────────────────────────────────────────
        private static readonly XNamespace Rdl =
            "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";

        // ── Defaults ──────────────────────────────────────────────────────────
        private const double TotalWidthInches  = 10.0;
        private const double PageHeightInches  = 11.0;
        private const double MarginInches      = 0.5;
        private const double HeaderHeightInches = 0.4;
        private const double RowHeightInches    = 0.25;
        private const double FontSizePt         = 10;
        private const double HeaderFontSizePt   = 8;

        /// <summary>
        /// Builds the complete RDLC XDocument from a ReportDefinition.
        /// The caller saves this document to disk.
        /// </summary>
        public XDocument Generate(ReportDefinition def)
        {
            double bodyWidth = TotalWidthInches - (MarginInches * 2);

            // Columns that appear in the Tablix
            var detailFields = def.Fields
                .Where(f => f.IsDetailField)
                .OrderBy(f => f.DisplayOrder)
                .ToList();

            double colWidth = detailFields.Count > 0
                ? Math.Round(bodyWidth / detailFields.Count, 3)
                : bodyWidth;

            // ── Root Report element ──────────────────────────────────────────
            var report = new XElement(Rdl + "Report",
                new XAttribute("xmlns", Rdl.NamespaceName),

                // ── DataSources ─────────────────────────────────────────────
                BuildDataSources(def),

                // ── DataSets ────────────────────────────────────────────────
                BuildDataSets(def),

                // ── ReportSections (Body + Tablix) ───────────────────────────
                new XElement(Rdl + "ReportSections",
                    new XElement(Rdl + "ReportSection",
                        new XElement(Rdl + "Body",
                            new XElement(Rdl + "ReportItems",
                                BuildTablix(def, detailFields, colWidth)
                            ),
                            new XElement(Rdl + "Height", $"{PageHeightInches - MarginInches * 2 - HeaderHeightInches}in")
                        ),
                        new XElement(Rdl + "Width",  $"{TotalWidthInches}in"),
                        new XElement(Rdl + "Page",
                            BuildPageHeader(def),
                            BuildPageFooter(def),
                            new XElement(Rdl + "TopMargin",    $"{MarginInches}in"),
                            new XElement(Rdl + "BottomMargin", $"{MarginInches}in"),
                            new XElement(Rdl + "LeftMargin",   $"{MarginInches}in"),
                            new XElement(Rdl + "RightMargin",  $"{MarginInches}in"),
                            new XElement(Rdl + "PageHeight",   $"{PageHeightInches}in"),
                            new XElement(Rdl + "PageWidth",    $"{TotalWidthInches}in")
                        )
                    )
                )
            );

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                report
            );
        }

        // ── DataSources ───────────────────────────────────────────────────────

        private XElement BuildDataSources(ReportDefinition def)
        {
            return new XElement(Rdl + "DataSources",
                new XElement(Rdl + "DataSource",
                    new XAttribute("Name", "AutoReportDS"),
                    new XElement(Rdl + "ConnectionProperties",
                        new XElement(Rdl + "DataProvider",  "SQL"),
                        new XElement(Rdl + "ConnectString",
                            $"Data Source={def.ServerName};" +
                            $"Initial Catalog={def.DatabaseName};" +
                            "Integrated Security=True"),
                        new XElement(Rdl + "IntegratedSecurity", "true")
                    )
                )
            );
        }

        // ── DataSets ──────────────────────────────────────────────────────────

        private XElement BuildDataSets(ReportDefinition def)
        {
            // Build Fields — each has TypeName from ReportField.DotNetType
            var fieldElements = def.Fields
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new XElement(Rdl + "Field",
                    new XAttribute("Name", f.GetDatasetFieldName()),
                    new XElement(Rdl + "DataField", f.GetDatasetFieldName()),
                    new XElement(Rdl + "TypeName",  f.DotNetType)
                ));

            return new XElement(Rdl + "DataSets",
                new XElement(Rdl + "DataSet",
                    new XAttribute("Name", "MainDataSet"),
                    new XElement(Rdl + "Query",
                        new XElement(Rdl + "DataSourceName", "AutoReportDS"),
                        new XElement(Rdl + "CommandType",    "StoredProcedure"),
                        new XElement(Rdl + "CommandText",
                            $"{def.SchemaName}.{def.StoredProcName}")
                    ),
                    new XElement(Rdl + "Fields", fieldElements)
                )
            );
        }

        // ── Tablix ────────────────────────────────────────────────────────────

        private XElement BuildTablix(
            ReportDefinition def,
            List<ReportField> detailFields,
            double colWidth)
        {
            return new XElement(Rdl + "Tablix",
                new XAttribute("Name", "MainTablix"),
                new XElement(Rdl + "TablixBody",
                    // Column definitions — each column gets a calculated equal share
                    new XElement(Rdl + "TablixColumns",
                        detailFields.Select(_ =>
                            new XElement(Rdl + "TablixColumn",
                                new XElement(Rdl + "Width", $"{colWidth}in")
                            )
                        )
                    ),
                    new XElement(Rdl + "TablixRows",
                        // Row 1: Column header labels
                        BuildHeaderRow(detailFields, colWidth),
                        // Row 2: Detail data row
                        BuildDetailRow(detailFields, colWidth),
                        // Row 3 (optional): Grand totals
                        def.IncludeGrandTotals ? BuildGrandTotalRow(detailFields, colWidth) : null!
                    )
                ),
                new XElement(Rdl + "TablixColumnHierarchy",
                    new XElement(Rdl + "TablixMembers",
                        detailFields.Select(_ =>
                            new XElement(Rdl + "TablixMember")
                        )
                    )
                ),
                new XElement(Rdl + "TablixRowHierarchy",
                    new XElement(Rdl + "TablixMembers",
                        new XElement(Rdl + "TablixMember"),  // Header
                        new XElement(Rdl + "TablixMember",   // Detail
                            new XElement(Rdl + "Group",
                                new XAttribute("Name", "Detail")
                            )
                        ),
                        def.IncludeGrandTotals
                            ? new XElement(Rdl + "TablixMember")  // Total
                            : null!
                    )
                ),
                new XElement(Rdl + "DataSetName", "MainDataSet"),
                new XElement(Rdl + "Top",    $"{HeaderHeightInches * 2}in"),
                new XElement(Rdl + "Left",   "0in"),
                new XElement(Rdl + "Height", $"{RowHeightInches * 3}in"),
                new XElement(Rdl + "Width",  $"{colWidth * detailFields.Count}in")
            );
        }

        private XElement BuildHeaderRow(List<ReportField> fields, double colWidth)
        {
            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", $"{RowHeightInches}in"),
                new XElement(Rdl + "TablixCells",
                    fields.Select(f =>
                        new XElement(Rdl + "TablixCell",
                            new XElement(Rdl + "CellContents",
                                MakeTextBox(
                                    $"hdr_{f.GetDatasetFieldName()}",
                                    f.GetDatasetFieldName(),   // literal label
                                    isExpression: false,
                                    bold: true,
                                    fontSize: HeaderFontSizePt,
                                    bgColor: "#1F4E79",
                                    fgColor: "White"
                                )
                            )
                        )
                    )
                )
            );
        }

        private XElement BuildDetailRow(List<ReportField> fields, double colWidth)
        {
            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", $"{RowHeightInches}in"),
                new XElement(Rdl + "TablixCells",
                    fields.Select(f =>
                        new XElement(Rdl + "TablixCell",
                            new XElement(Rdl + "CellContents",
                                MakeTextBox(
                                    $"det_{f.GetDatasetFieldName()}",
                                    $"=Fields!{f.GetDatasetFieldName()}.Value",
                                    isExpression: true,
                                    bold: false,
                                    fontSize: FontSizePt,
                                    bgColor: "Transparent",
                                    fgColor: "Black"
                                )
                            )
                        )
                    )
                )
            );
        }

        private XElement BuildGrandTotalRow(List<ReportField> fields, double colWidth)
        {
            return new XElement(Rdl + "TablixRow",
                new XElement(Rdl + "Height", $"{RowHeightInches}in"),
                new XElement(Rdl + "TablixCells",
                    fields.Select((f, idx) =>
                    {
                        bool canSum = f.Aggregate is AggregateFunction.SUM or AggregateFunction.COUNT
                            or AggregateFunction.AVG;

                        string value = idx == 0
                            ? "Grand Total"
                            : canSum
                                ? $"=Sum(Fields!{f.GetDatasetFieldName()}.Value)"
                                : string.Empty;

                        return new XElement(Rdl + "TablixCell",
                            new XElement(Rdl + "CellContents",
                                MakeTextBox(
                                    $"tot_{f.GetDatasetFieldName()}",
                                    value,
                                    isExpression: value.StartsWith("="),
                                    bold: true,
                                    fontSize: FontSizePt,
                                    bgColor: "#D9E1F2",
                                    fgColor: "Black"
                                )
                            )
                        );
                    })
                )
            );
        }

        // ── Page Header / Footer ──────────────────────────────────────────────

        private XElement BuildPageHeader(ReportDefinition def)
        {
            var items = new List<XElement>();
            double top = 0;

            // Report title
            if (!string.IsNullOrWhiteSpace(def.ReportTitle))
            {
                items.Add(MakeTextBox(
                    "txtTitle", def.ReportTitle,
                    isExpression: false, bold: true, fontSize: 14,
                    bgColor: "Transparent", fgColor: "Black",
                    top: top, left: 0, width: TotalWidthInches - MarginInches * 2));
                top += 0.25;
            }

            // Subtitle
            if (!string.IsNullOrWhiteSpace(def.ReportSubtitle))
            {
                items.Add(MakeTextBox(
                    "txtSubtitle", def.ReportSubtitle,
                    isExpression: false, bold: false, fontSize: 10,
                    bgColor: "Transparent", fgColor: "#555555",
                    top: top, left: 0, width: TotalWidthInches - MarginInches * 2));
                top += 0.2;
            }

            // Dynamic field injection
            if (!string.IsNullOrWhiteSpace(def.DynamicHeaderFieldName))
            {
                items.Add(MakeTextBox(
                    "txtDynHeader",
                    $"={def.DynamicHeaderFieldName}: \" & First(Fields!{def.DynamicHeaderFieldName}.Value, \"MainDataSet\")",
                    isExpression: true, bold: false, fontSize: 10,
                    bgColor: "Transparent", fgColor: "Black",
                    top: top, left: 0, width: TotalWidthInches - MarginInches * 2));
            }

            return new XElement(Rdl + "PageHeader",
                new XElement(Rdl + "Height", $"{HeaderHeightInches}in"),
                new XElement(Rdl + "PrintOnFirstPage",  "true"),
                new XElement(Rdl + "PrintOnLastPage",   "true"),
                new XElement(Rdl + "ReportItems", items)
            );
        }

        private XElement BuildPageFooter(ReportDefinition def)
        {
            var items = new List<XElement>();
            double rightEdge = TotalWidthInches - MarginInches * 2;

            // Execution timestamp
            if (def.IncludeExecutionTime)
            {
                items.Add(MakeTextBox(
                    "txtExecTime",
                    "=Globals!ExecutionTime",
                    isExpression: true, bold: false, fontSize: 8,
                    bgColor: "Transparent", fgColor: "#888888",
                    top: 0, left: 0, width: rightEdge / 2));
            }

            // Page numbers
            if (def.IncludePageNumbers)
            {
                items.Add(MakeTextBox(
                    "txtPageNum",
                    "=\"Page \" & Globals!PageNumber & \" of \" & Globals!TotalPages",
                    isExpression: true, bold: false, fontSize: 8,
                    bgColor: "Transparent", fgColor: "#888888",
                    top: 0, left: rightEdge / 2, width: rightEdge / 2));
            }

            return new XElement(Rdl + "PageFooter",
                new XElement(Rdl + "Height",            "0.25in"),
                new XElement(Rdl + "PrintOnFirstPage",  "true"),
                new XElement(Rdl + "PrintOnLastPage",   "true"),
                new XElement(Rdl + "ReportItems", items)
            );
        }

        // ── TextBox Factory ───────────────────────────────────────────────────

        private static XElement MakeTextBox(
            string name,
            string value,
            bool   isExpression,
            bool   bold,
            double fontSize,
            string bgColor,
            string fgColor,
            double top   = 0,
            double left  = 0,
            double width = 2.0,
            double height = 0.25)
        {
            return new XElement(Rdl + "Textbox",
                new XAttribute("Name", name),
                new XElement(Rdl + "CanGrow", "true"),
                new XElement(Rdl + "Paragraphs",
                    new XElement(Rdl + "Paragraph",
                        new XElement(Rdl + "TextRuns",
                            new XElement(Rdl + "TextRun",
                                new XElement(Rdl + "Value", isExpression ? value : $"\"{value}\""),
                                new XElement(Rdl + "Style",
                                    new XElement(Rdl + "FontSize",   $"{fontSize}pt"),
                                    new XElement(Rdl + "FontWeight", bold ? "Bold" : "Normal"),
                                    new XElement(Rdl + "Color",      fgColor)
                                )
                            )
                        )
                    )
                ),
                new XElement(Rdl + "Style",
                    new XElement(Rdl + "BackgroundColor", bgColor),
                    new XElement(Rdl + "BorderStyle",
                        new XElement(Rdl + "Default", "Solid")
                    ),
                    new XElement(Rdl + "BorderColor",
                        new XElement(Rdl + "Default", "#CCCCCC")
                    )
                ),
                new XElement(Rdl + "Top",    $"{top}in"),
                new XElement(Rdl + "Left",   $"{left}in"),
                new XElement(Rdl + "Height", $"{height}in"),
                new XElement(Rdl + "Width",  $"{width}in")
            );
        }
    }
}
