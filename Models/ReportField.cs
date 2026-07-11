using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoReportWizard.Models
{
    /// <summary>
    /// Canonical aggregate functions supported by the T-SQL generator.
    /// </summary>
    public enum AggregateFunction
    {
        None,
        SUM,
        COUNT,
        AVG,
        MAX,
        MIN
    }

    /// <summary>
    /// Represents a single column in the report dataset.
    /// This is the strongly-typed contract that flows through every step
    /// of the wizard and is consumed directly by the generation engines —
    /// no JSON serialization required.
    /// </summary>
    public class ReportField : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _sqlDataType = "nvarchar";
        private string _dotNetType = "System.String";
        private bool _isGroupBy;
        private AggregateFunction _aggregate = AggregateFunction.None;
        private bool _isDetailField = true;
        private int _displayOrder;
        private string _customHeaderLabel = string.Empty;
        private double _columnWidth;

        /// <summary>Raw column name as it appears in SQL Server (e.g. "SalesAmount").</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayHeaderLabel)); }
        }

        /// <summary>
        /// SQL data type name from sys.types (e.g. "nvarchar", "int", "decimal").
        /// Populated automatically by schema discovery or set to "nvarchar" for manual entries.
        /// </summary>
        public string SqlDataType
        {
            get => _sqlDataType;
            set { _sqlDataType = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Fully-qualified .NET System.Type name derived from SqlDataType
        /// (e.g. "System.String", "System.Int32", "System.Decimal").
        /// Written verbatim into the RDLC &lt;Field TypeName=""&gt; attribute.
        /// </summary>
        public string DotNetType
        {
            get => _dotNetType;
            set { _dotNetType = value; OnPropertyChanged(); }
        }

        /// <summary>When true, this field is added to the GROUP BY clause.</summary>
        public bool IsGroupBy
        {
            get => _isGroupBy;
            set { _isGroupBy = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The aggregate function to apply. Must be None when IsGroupBy is true.
        /// Mutually exclusive with IsGroupBy.
        /// </summary>
        public AggregateFunction Aggregate
        {
            get => _aggregate;
            set { _aggregate = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// When true, this field appears in the Tablix detail rows of the RDLC report.
        /// </summary>
        public bool IsDetailField
        {
            get => _isDetailField;
            set { _isDetailField = value; OnPropertyChanged(); }
        }

        // ─── VISUAL SELECTION & POSITIONING ────────────────────────────────
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        private double _canvasX = 16;
        public double CanvasX
        {
            get => _canvasX;
            set { _canvasX = value; OnPropertyChanged(); }
        }

        private double _canvasY = 16;
        public double CanvasY
        {
            get => _canvasY;
            set { _canvasY = value; OnPropertyChanged(); }
        }

        private double _itemWidth = 120;
        public double ItemWidth
        {
            get => _itemWidth;
            set { _itemWidth = value; OnPropertyChanged(); }
        }

        private double _itemHeight = 32;
        public double ItemHeight
        {
            get => _itemHeight;
            set { _itemHeight = value; OnPropertyChanged(); }
        }

        // ─── STYLING & MAPPING ─────────────────────────────────────────────
        private string _textAlign = "Default";
        public string TextAlign
        {
            get => _textAlign;
            set { _textAlign = value; OnPropertyChanged(); }
        }

        private string _fontWeight = "Normal";
        public string FontWeight
        {
            get => _fontWeight;
            set { _fontWeight = value; OnPropertyChanged(); }
        }

        private string _borderColor = "#CCCCCC";
        public string BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; OnPropertyChanged(); }
        }

        private string _customExpression = string.Empty;
        public string CustomExpression
        {
            get => _customExpression;
            set { _customExpression = value; OnPropertyChanged(); }
        }

        public string SourceDatabase { get; set; } = string.Empty;
        public string SourceSchema { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;

        /// <summary>
        /// Zero-based display order within the Tablix column set.
        /// Used to calculate relative column widths and column ordering.
        /// </summary>
        public int DisplayOrder
        {
            get => _displayOrder;
            set { _displayOrder = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Custom column header label shown in the RDLC Tablix header row.
        /// When empty, the dataset field name is used.
        /// </summary>
        public string CustomHeaderLabel
        {
            get => _customHeaderLabel;
            set { _customHeaderLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayHeaderLabel)); }
        }

        /// <summary>
        /// Column width in inches for the RDLC Tablix. Zero means auto-equal split.
        /// </summary>
        public double ColumnWidth
        {
            get => _columnWidth;
            set { _columnWidth = value; OnPropertyChanged(); }
        }

        /// <summary>Resolved header text for layout preview and RDLC generation.</summary>
        public string DisplayHeaderLabel =>
            string.IsNullOrWhiteSpace(CustomHeaderLabel) ? GetDatasetFieldName() : CustomHeaderLabel;

        /// <summary>
        /// Builds the [Database].[Schema].[Table].[Column] string.
        /// </summary>
        public string GetFullyQualifiedName()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(SourceTable)) parts.Add(QuoteName(SourceTable));

            parts.Add(QuoteName(Name));
            return string.Join(".", parts);
        }

        /// <summary>
        /// Returns the aliased column expression used in the SELECT list.
        /// e.g. "SUM([DB].[dbo].[Table].[SalesAmount]) AS [SalesAmount_SUM]"
        /// </summary>
        public string GetSelectExpression()
        {
            string fqn = GetFullyQualifiedName();

            if (IsGroupBy || Aggregate == AggregateFunction.None)
                return $"{fqn} AS {QuoteName(Name)}";

            return $"{Aggregate}({fqn}) AS {QuoteName($"{Name}_{Aggregate}")}";
        }

        /// <summary>
        /// The field name as it will appear in the RDLC dataset field list.
        /// Matches the alias produced by GetSelectExpression().
        /// </summary>
        public string GetDatasetFieldName()
        {
            if (IsGroupBy || Aggregate == AggregateFunction.None)
                return Name;
            return $"{Name}_{Aggregate}";
        }

        /// <summary>C# equivalent of T-SQL QUOTENAME() — wraps identifier in brackets.</summary>
        private static string QuoteName(string identifier) =>
            "[" + identifier.Replace("]", "]]") + "]";

        public override string ToString() => Name;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
