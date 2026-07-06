namespace AutoReportWizard.Infrastructure;

/// <summary>
/// Shared logic for extracting executable T-SQL from a wizard procedure script.
/// Used by live preview and final SQL generation so both paths stay aligned.
/// </summary>
public static class SqlQueryExtractor
{
    public static string ExtractExecutableQuery(string customSql, string? preQueryLogic = null)
    {
        if (string.IsNullOrWhiteSpace(customSql))
            return string.Empty;

        string rawQuery = customSql;
        int selectIndex = rawQuery.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        if (selectIndex >= 0)
        {
            rawQuery = rawQuery[selectIndex..];
            rawQuery = rawQuery.Replace("OPTION (RECOMPILE);", "", StringComparison.OrdinalIgnoreCase);
            rawQuery = rawQuery.Replace("END", "", StringComparison.OrdinalIgnoreCase);
        }

        rawQuery = rawQuery.Trim();

        if (!string.IsNullOrWhiteSpace(preQueryLogic))
            return preQueryLogic.Trim() + Environment.NewLine + rawQuery;

        return rawQuery;
    }
}
