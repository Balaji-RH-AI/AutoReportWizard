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
    public class ReportField
    {
        /// <summary>Raw column name as it appears in SQL Server (e.g. "SalesAmount").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// SQL data type name from sys.types (e.g. "nvarchar", "int", "decimal").
        /// Populated automatically by schema discovery or set to "nvarchar" for manual entries.
        /// </summary>
        public string SqlDataType { get; set; } = "nvarchar";

        /// <summary>
        /// Fully-qualified .NET System.Type name derived from SqlDataType
        /// (e.g. "System.String", "System.Int32", "System.Decimal").
        /// Written verbatim into the RDLC &lt;Field TypeName=""&gt; attribute.
        /// </summary>
        public string DotNetType { get; set; } = "System.String";

        /// <summary>When true, this field is added to the GROUP BY clause.</summary>
        public bool IsGroupBy { get; set; }

        /// <summary>
        /// The aggregate function to apply. Must be None when IsGroupBy is true.
        /// Mutually exclusive with IsGroupBy.
        /// </summary>
        public AggregateFunction Aggregate { get; set; } = AggregateFunction.None;

        /// <summary>
        /// When true, this field appears in the Tablix detail rows of the RDLC report.
        /// </summary>
        public bool IsDetailField { get; set; } = true;
        public string SourceDatabase { get; set; } = string.Empty;
        public string SourceSchema { get; set; } = string.Empty;
        public string SourceTable { get; set; } = string.Empty;
        /// <summary>
        /// Zero-based display order within the Tablix column set.
        /// Used to calculate relative column widths and column ordering.
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Returns the aliased column expression used in the SELECT list.
        /// e.g. "SUM([SalesAmount]) AS [SalesAmount_SUM]" or "[Region]"
        /// </summary>
        /// <summary>
        /// Builds the [Database].[Schema].[Table].[Column] string.
        /// </summary>
        public string GetFullyQualifiedName()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(SourceDatabase)) parts.Add(QuoteName(SourceDatabase));
            if (!string.IsNullOrEmpty(SourceSchema)) parts.Add(QuoteName(SourceSchema));
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
                return $"{fqn} AS {QuoteName(Name)}"; // Alias back to short name for the RDLC

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
    }
}
