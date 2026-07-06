namespace AutoReportWizard.Models;

/// <summary>
/// A single line in the Step 5 layout designer page-header preview.
/// Built from dynamic parameters and dataset field mappings — no static mock text.
/// </summary>
public class PageHeaderPreviewLine
{
    public string DisplayText { get; set; } = string.Empty;
    public HeaderZone Zone { get; set; } = HeaderZone.Left;
}
