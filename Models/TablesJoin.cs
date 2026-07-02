namespace AutoReportWizard.Models
{
    /// <summary>
    /// Represents an inner or left join mapping for the template-driven SQL generator.
    /// </summary>
    public class TableJoin
    {
        public string PrimaryTable { get; set; } = string.Empty;
        public string PrimaryColumn { get; set; } = string.Empty;

        public string JoinedTable { get; set; } = string.Empty;
        public string JoinedColumn { get; set; } = string.Empty;

        public string GetJoinExpression()
        {
            return $"INNER JOIN [{JoinedTable}] ON [{PrimaryTable}].[{PrimaryColumn}] = [{JoinedTable}].[{JoinedColumn}]";
        }
    }
}