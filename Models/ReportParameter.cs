namespace AutoReportWizard.Models
{
    public class ReportParameter
    {
        public string Name { get; set; } = string.Empty;
        public string SqlDataType { get; set; } = "varchar(50)";
        public string RdlcDataType { get; set; } = "String";
        public string? Value { get; set; } = string.Empty;
        public bool IsHidden { get; set; } = false;
        public bool AllowBlank { get; set; } = true;
    }
}