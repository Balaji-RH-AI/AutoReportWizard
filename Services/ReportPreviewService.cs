using System.Data;
using System.IO;
using System.Xml.Linq;
using AutoReportWizard.Models;
using Microsoft.Reporting.WinForms;

namespace AutoReportWizard.Services;

/// <summary>
/// Renders a local RDLC file with an in-memory DataTable via the WinForms ReportViewer control.
/// </summary>
public static class ReportPreviewService
{

    /// <summary>
    /// Generates RDLC XML to a temp file and returns the path.
    /// </summary>
    public static async Task<string> ScaffoldRdlcToTempAsync(ReportDefinition def, CancellationToken ct = default)
    {
        XDocument document = RdlcXmlEngine.GenerateRdlcXml(def);
        string fileName = $"Preview_{SanitizeFileName(def.ReportName)}_{Guid.NewGuid():N}.rdlc";
        string path = Path.Combine(Path.GetTempPath(), fileName);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await document.SaveAsync(stream, SaveOptions.None, ct);

        return path;
    }

    /// <summary>
    /// Binds a local RDLC and DataTable to a ReportViewer instance and refreshes the render.
    /// </summary>
    public static void RenderLocalReport(
        ReportViewer viewer,
        string rdlcPath,
        DataTable data,
        IEnumerable<DynamicParameter>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(rdlcPath) || !File.Exists(rdlcPath))
            throw new FileNotFoundException("RDLC preview file was not found.", rdlcPath);

        viewer.ProcessingMode = ProcessingMode.Local;
        viewer.LocalReport.ReportPath = rdlcPath;
        viewer.LocalReport.DataSources.Clear();
        viewer.LocalReport.DataSources.Add(new ReportDataSource("MainDataSet", data));

        if (parameters is not null)
        {
            var reportParams = parameters
                .Where(p => !string.IsNullOrWhiteSpace(p.RdlcParameterName))
                .Select(p => new Microsoft.Reporting.WinForms.ReportParameter(
                    p.RdlcParameterName, p.Value ?? string.Empty))
                .ToArray();

            if (reportParams.Length > 0)
                viewer.LocalReport.SetParameters(reportParams);
        }

        viewer.RefreshReport();
    }

    /// <summary>
    /// Deletes a temp RDLC file created for preview. Failures are swallowed.
    /// </summary>
    public static void TryDeleteTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Report";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }
}
