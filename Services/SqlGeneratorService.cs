using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoReportWizard.Infrastructure;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Deterministic T-SQL generation engine.
    /// Natively resolves table prefixes for JOINs and automatically injects 
    /// parameter-based dynamic WHERE clauses.
    /// </summary>
    public class SqlGeneratorService
    {
        public string Generate(ReportDefinition def)
        {
            using var span = TelemetryService.StartGenerationSpan(
                "generation.sql", def.ReportName, def.Fields.Count);

            try
            {
                string result = !string.IsNullOrWhiteSpace(def.CustomSql)
                    ? BuildFromCustomSql(def)
                    : BuildFromFields(def);

                TelemetryService.RecordSuccess(span, def.StoredProcName);
                return result;
            }
            catch (Exception ex)
            {
                TelemetryService.RecordFailure(span, ex, $"Report: {def.ReportName}");
                throw;
            }
        }

        private static string BuildFromCustomSql(ReportDefinition def)
        {
            var sb = new StringBuilder();
            AppendProcedureHeader(sb, def);

            string executableQuery = SqlQueryExtractor.ExtractExecutableQuery(
                def.CustomSql,
                def.PreQueryLogic);

            if (string.IsNullOrWhiteSpace(executableQuery))
                throw new InvalidOperationException("Custom SQL does not contain a valid SELECT statement.");

            sb.AppendLine(executableQuery.TrimEnd());
            if (!executableQuery.Contains("OPTION (RECOMPILE)", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("OPTION (RECOMPILE);");

            sb.AppendLine("SET NOCOUNT OFF;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            return sb.ToString();
        }

        private static string BuildFromFields(ReportDefinition def)
        {
            var sb = new StringBuilder();
            var groupByFields = def.Fields.Where(f => f.IsGroupBy).ToList();
            var selectItems = BuildSelectList(def);

            AppendProcedureHeader(sb, def);

            if (!string.IsNullOrWhiteSpace(def.PreQueryLogic))
            {
                string pql = def.PreQueryLogic.Trim();
                if (pql.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase))
                    pql = ";" + pql;

                sb.AppendLine(pql);
                sb.AppendLine();
            }

            if (selectItems.Count == 0)
                throw new InvalidOperationException("At least one field is required to generate SQL.");

            sb.AppendLine("    SELECT");
            for (int i = 0; i < selectItems.Count; i++)
            {
                string comma = i < selectItems.Count - 1 ? "," : string.Empty;
                sb.AppendLine($"        {selectItems[i]}{comma}");
            }

            sb.AppendLine($"    FROM {QuoteName(def.DatabaseName)}.{QuoteName(def.SchemaName)}.{QuoteName(def.TableOrViewName)} AS {QuoteName(def.TableOrViewName)}");

            foreach (var join in def.Joins.Where(j => j is not null))
                sb.AppendLine($"    {join.GetJoinExpression(def.DatabaseName, def.SchemaName)}");

            // ── DYNAMIC WHERE CLAUSE INJECTION ──────────────────────────────────────────
            var whereConditions = new List<string>();

            // 1. Add any custom hardcoded logic
            if (!string.IsNullOrWhiteSpace(def.CustomWhereClause))
                whereConditions.Add($"({def.CustomWhereClause.Trim()})");

            // 2. Auto-generate the "Space = Retrieve All" parameter logic
            var paramSource = def.DynamicParameters.Count > 0
                ? def.DynamicParameters
                : def.Parameters.Select(p => new DynamicParameter { ParameterName = p.Name }).ToList();

            foreach (var param in paramSource)
            {
                string cleanName = param.ParameterName.TrimStart('@');

                // Find the field that matches this parameter name
                var matchingField = def.Fields.FirstOrDefault(f =>
                    string.Equals(f.Name, cleanName, StringComparison.OrdinalIgnoreCase));

                if (matchingField != null)
                {
                    string tableName = !string.IsNullOrWhiteSpace(matchingField.SourceTable)
                        ? matchingField.SourceTable
                        : def.TableOrViewName;

                    string columnRef = $"{QuoteName(tableName)}.{QuoteName(matchingField.Name)}";
                    whereConditions.Add($"(@{cleanName} = ' ' OR {columnRef} = @{cleanName})");
                }
            }

            if (whereConditions.Count > 0)
            {
                sb.AppendLine("    WHERE");
                for (int i = 0; i < whereConditions.Count; i++)
                {
                    string prefix = i == 0 ? "        " : "        AND ";
                    sb.AppendLine($"{prefix}{whereConditions[i]}");
                }
            }

            // ── GROUP BY CLAUSE ─────────────────────────────────────────────────────────
            if (groupByFields.Count > 0)
            {
                sb.Append("    GROUP BY ");
                var gbItems = groupByFields.Select(f =>
                {
                    string tableName = !string.IsNullOrWhiteSpace(f.SourceTable) ? f.SourceTable : def.TableOrViewName;
                    return $"{QuoteName(tableName)}.{QuoteName(f.Name)}";
                });
                sb.AppendLine(string.Join(", ", gbItems));
            }

            sb.AppendLine("    OPTION (RECOMPILE);");
            sb.AppendLine("    SET NOCOUNT OFF;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
            return sb.ToString();
        }

        private static void AppendProcedureHeader(StringBuilder sb, ReportDefinition def)
        {
            sb.AppendLine($"CREATE OR ALTER PROCEDURE {QuoteName(def.SchemaName)}.{QuoteName(def.StoredProcName)}");

            var paramSource = def.DynamicParameters.Count > 0
                ? def.DynamicParameters
                : def.Parameters.Select(p => new DynamicParameter
                {
                    ParameterName = p.Name,
                    DataType = p.SqlDataType
                }).ToList();

            if (paramSource.Count == 0)
            {
                sb.AppendLine("AS BEGIN");
            }
            else
            {
                for (int i = 0; i < paramSource.Count; i++)
                {
                    var param = paramSource[i];
                    string name = param.ParameterName.StartsWith('@')
                        ? param.ParameterName
                        : $"@{param.ParameterName}";
                    string comma = i < paramSource.Count - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {name} {param.DataType.ToUpperInvariant()}{comma}");
                }

                sb.AppendLine("AS BEGIN");
            }

            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();
        }

        private static List<string> BuildSelectList(ReportDefinition def)
        {
            var items = new List<string>();

            foreach (var field in def.Fields.OrderBy(f => f.DisplayOrder))
            {
                // Ensure every column is prefixed with its source table to prevent ambiguous column errors
                string tableName = !string.IsNullOrWhiteSpace(field.SourceTable) ? field.SourceTable : def.TableOrViewName;
                string colRef = $"{QuoteName(tableName)}.{QuoteName(field.Name)}";

                if (field.IsGroupBy || field.Aggregate == AggregateFunction.None)
                    items.Add($"{colRef} AS {QuoteName(field.Name)}");
                else
                {
                    string aggName = field.Aggregate.ToString();
                    string alias = $"{field.Name}_{aggName}";
                    items.Add($"{aggName}({colRef}) AS {QuoteName(alias)}");
                }
            }

            return items;
        }

        public static string QuoteName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            return "[" + identifier.Replace("]", "]]") + "]";
        }
    }
}