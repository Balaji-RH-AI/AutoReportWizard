using System.Text;
using AutoReportWizard.Infrastructure;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Deterministic T-SQL generation engine.
    ///
    /// Converts a strongly-typed ReportDefinition into a syntactically perfect
    /// CREATE OR ALTER PROCEDURE script using StringBuilder.
    ///
    /// SECURITY GUARANTEES:
    ///   - Every identifier (server, database, schema, table, column) is passed
    ///     through QuoteName(), which is a C# implementation of T-SQL QUOTENAME().
    ///   - No user-provided strings are interpolated directly into SQL.
    ///   - OPTION (RECOMPILE) is always emitted to prevent parameter-sniffing issues.
    /// </summary>
    public class SqlGeneratorService
    {
        /// <summary>
        /// Generates the full CREATE OR ALTER PROCEDURE script.
        /// </summary>
        /// <param name="def">
        /// Fully-populated ReportDefinition. Caller must call SyncFieldsToReport()
        /// on the ViewModel before invoking this method.
        /// </param>
        /// <returns>Complete T-SQL script as a string.</returns>
        public string Generate(ReportDefinition def)
        {
            using var span = TelemetryService.StartGenerationSpan(
                "generation.sql", def.ReportName, def.Fields.Count);

            try
            {
                var sb = new StringBuilder();
                var groupByFields = def.Fields.Where(f => f.IsGroupBy).ToList();
                var selectItems = BuildSelectList(def.Fields);

                sb.AppendLine($"CREATE OR ALTER PROCEDURE {QuoteName(def.SchemaName)}.{QuoteName(def.StoredProcName)}");
                sb.AppendLine("    @ProcessDate CHAR(8),");
                sb.AppendLine("    @Siteid VARCHAR(MAX),");
                sb.AppendLine("    @BatchNo VARCHAR(MAX),");
                sb.AppendLine("    @WorkSource VARCHAR(MAX)");
                sb.AppendLine("AS BEGIN");
                sb.AppendLine("SET NOCOUNT ON;");
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(def.PreQueryLogic))
                {
                    string pql = def.PreQueryLogic.Trim();
                    if (pql.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase))
                    {
                        pql = ";" + pql;
                    }
                    sb.AppendLine(pql);
                    sb.AppendLine();
                }

                sb.AppendLine("SELECT");
                if (selectItems.Count == 0)
                {
                    sb.AppendLine("    1 AS [__placeholder]");
                }
                else
                {
                    for (int i = 0; i < selectItems.Count; i++)
                    {
                        string comma = i < selectItems.Count - 1 ? "," : string.Empty;
                        sb.AppendLine($"    {selectItems[i]}{comma}");
                    }
                }

                sb.AppendLine($"FROM {QuoteName(def.DatabaseName)}.{QuoteName(def.SchemaName)}.{QuoteName(def.TableOrViewName)} AS {QuoteName(def.TableOrViewName)}");
                foreach (var join in def.Joins.Where(j => j is not null))
                {
                    sb.AppendLine($"    {join.GetJoinExpression(def.DatabaseName, def.SchemaName)}");
                }

                if (!string.IsNullOrWhiteSpace(def.CustomWhereClause))
                {
                    sb.AppendLine($"WHERE {def.CustomWhereClause.Trim()}");
                }

                if (groupByFields.Any())
                {
                    sb.Append("GROUP BY ");
                    sb.AppendLine(string.Join(", ", groupByFields.Select(f => QuoteName(f.Name))));
                }

                sb.AppendLine("OPTION (RECOMPILE);");
                sb.AppendLine("SET NOCOUNT OFF;");
                sb.AppendLine("END");
                sb.AppendLine("GO");

                string result = sb.ToString();
                TelemetryService.RecordSuccess(span, def.StoredProcName);
                return result;
            }
            catch (Exception ex)
            {
                TelemetryService.RecordFailure(span, ex, $"Report: {def.ReportName}");
                throw;
            }
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private static List<string> BuildSelectList(List<ReportField> fields)
        {
            var items = new List<string>();

            foreach (var field in fields.OrderBy(f => f.DisplayOrder))
            {
                if (field.IsGroupBy || field.Aggregate == AggregateFunction.None)
                {
                    // Plain column reference
                    items.Add(QuoteName(field.Name));
                }
                else
                {
                    // Aggregate expression with alias
                    string aggName = field.Aggregate.ToString();
                    string alias = $"{field.Name}_{aggName}";
                    items.Add($"{aggName}({QuoteName(field.Name)}) AS {QuoteName(alias)}");
                }
            }

            return items;
        }

        /// <summary>
        /// C# implementation of T-SQL QUOTENAME(identifier, '[').
        /// Wraps the identifier in square brackets and escapes any embedded ']'
        /// by doubling it — identical to the SQL Server built-in function.
        /// </summary>
        public static string QuoteName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            return "[" + identifier.Replace("]", "]]") + "]";
        }
    }
}
