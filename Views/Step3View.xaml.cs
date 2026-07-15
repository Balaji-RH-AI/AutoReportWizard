using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AutoReportWizard.Models;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step3View : UserControl
    {
        private static readonly AggregateFunction[] AggregateOptions =
            (AggregateFunction[])Enum.GetValues(typeof(AggregateFunction));

        private bool _isManualMode;

        public Step3View()
        {
            InitializeComponent();
        }

        private void AggCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox combo || combo.Tag is not ReportField field) return;
            combo.ItemsSource = AggregateOptions;
            combo.SelectedItem = field.Aggregate;
        }

        private void GroupByCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ReportField field && field.IsGroupBy)
                field.Aggregate = AggregateFunction.None;
        }

        private void Agg_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is ReportField field
                && combo.SelectedItem is AggregateFunction agg)
            {
                field.Aggregate = agg;
            }
        }

        private void PreviewText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isManualMode) return;
            if (DataContext is not WizardViewModel vm) return;
            BuildPreviewSql(vm);
        }

        private void ScaffoldQuery_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            if (_isManualMode)
            {
                var result = MessageBox.Show(
                    "This will overwrite your manual changes. Do you want to proceed?",
                    "Reset to Base Query",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            BuildPreviewSql(vm);
        }

        private void ManualMode_Changed(object sender, RoutedEventArgs e)
        {
            _isManualMode = ManualModeToggle.IsChecked == true;
        }

        // ── SafeBracket ────────────────────────────────────────────────────────
        /// <summary>
        /// Wraps an identifier in square brackets for safe T-SQL quoting.
        /// Returns <c>null</c> if <paramref name="input"/> is null or whitespace,
        /// preventing the generation of malformed <c>[]</c> tokens that crash SQL Server.
        /// Any embedded closing-bracket characters are doubled per T-SQL escaping rules.
        /// </summary>
        private static string? SafeBracket(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Escape any embedded ] characters per SQL Server identifier rules
            return "[" + input.Trim().Replace("]", "]]") + "]";
        }

        // ── BuildPreviewSql ────────────────────────────────────────────────────
        private static void BuildPreviewSql(WizardViewModel vm)
        {
            var sb = new StringBuilder();

            // ── Identify fields that will form the GROUP BY clause ─────────────
            // An explicit GroupBy field → always goes into GROUP BY.
            // A non-aggregate field when any aggregates exist → also must be in GROUP BY.
            bool hasAnyAggregate = vm.Fields.Any(f => f.Aggregate != AggregateFunction.None);

            var groupByFields = vm.Fields
                .Where(f => f.IsGroupBy ||
                            (hasAnyAggregate && f.Aggregate == AggregateFunction.None && !f.IsGroupBy))
                .ToList();

            bool hasGroupBy = groupByFields.Any();

            sb.AppendLine("CREATE OR ALTER PROCEDURE dbo." +
                          (string.IsNullOrWhiteSpace(vm.StoredProcName) ? "[ProcedureName]" : vm.StoredProcName));
            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");
            sb.AppendLine();
            sb.AppendLine("    SET NOCOUNT ON;");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(vm.PreQueryLogic))
            {
                sb.AppendLine(vm.PreQueryLogic.Trim());
                sb.AppendLine();
            }

            sb.AppendLine("    SELECT");

            // ── Build SELECT list — skip any field with a null-safe bracket ────
            var selectItems = vm.Fields
                .OrderBy(f => f.DisplayOrder)
                .Select(f =>
                {
                    string? safeName = SafeBracket(f.Name);
                    if (safeName is null) return null;           // ← skip empty-name fields entirely

                    string expr = f.GetSelectExpression();
                    if (!expr.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
                        return $"        {expr} AS {safeName}";

                    return $"        {expr}";
                })
                .Where(item => item is not null)
                .ToList();

            if (selectItems.Count == 0)
            {
                sb.AppendLine("        *");
            }
            else
            {
                for (int i = 0; i < selectItems.Count; i++)
                {
                    string comma = i < selectItems.Count - 1 ? "," : "";
                    sb.AppendLine($"{selectItems[i]}{comma}");
                }
            }

            string schemaClause = string.IsNullOrEmpty(vm.SchemaName) ? "dbo" : vm.SchemaName;
            sb.AppendLine($"    FROM [{vm.DatabaseName}].[{schemaClause}].[{vm.TableOrViewName}] AS [{vm.TableOrViewName}]");

            if (vm.ConfiguredJoins.Any())
            {
                foreach (var join in vm.ConfiguredJoins)
                {
                    sb.AppendLine($"    INNER JOIN [{vm.DatabaseName}].[{schemaClause}].[{join.JoinedTable}] AS [{join.JoinedTable}]" +
                                  $" ON [{join.PrimaryTable}].[{join.PrimaryColumn}] = [{join.JoinedTable}].[{join.JoinedColumn}]");
                }
            }

            if (!string.IsNullOrWhiteSpace(vm.CustomWhereClause))
            {
                sb.AppendLine($"    WHERE {vm.CustomWhereClause.Trim()}");
            }

            if (hasGroupBy)
            {
                // Only include fields that have a valid (non-null) safe bracket
                var groupByTokens = groupByFields
                    .Select(f => SafeBracket(f.Name))
                    .Where(b => b is not null)
                    .ToList();

                if (groupByTokens.Count > 0)
                    sb.AppendLine("    GROUP BY " + string.Join(", ", groupByTokens));
            }

            sb.AppendLine("    OPTION (RECOMPILE);");
            sb.AppendLine("END");

            vm.CustomSql = sb.ToString();
        }

        // ── ParseCustomSql_Click ───────────────────────────────────────────────
        private async void ParseCustomSql_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || string.IsNullOrWhiteSpace(vm.CustomSql)) return;

            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var dbService = new Infrastructure.DatabaseService();
                var pureQuery = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(vm.PreQueryLogic))
                    pureQuery.AppendLine(vm.PreQueryLogic);

                pureQuery.AppendLine("SELECT");

                // ── Build SELECT list with SafeBracket guard ───────────────────
                var selectItems = vm.Fields
                    .OrderBy(f => f.DisplayOrder)
                    .Select(f =>
                    {
                        string? safeName = SafeBracket(f.Name);
                        if (safeName is null) return null;      // ← skip empty-name fields

                        string expr = f.GetSelectExpression();
                        if (!expr.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
                            return $"    {expr} AS {safeName}";

                        return $"    {expr}";
                    })
                    .Where(item => item is not null)
                    .ToList();

                if (selectItems.Count == 0)
                    pureQuery.AppendLine("    *");
                else
                    pureQuery.AppendLine(string.Join(",\n", selectItems));

                string schemaClause = string.IsNullOrEmpty(vm.SchemaName) ? "dbo" : vm.SchemaName;
                pureQuery.AppendLine($"FROM [{vm.DatabaseName}].[{schemaClause}].[{vm.TableOrViewName}] AS [{vm.TableOrViewName}]");

                if (vm.ConfiguredJoins.Any())
                {
                    foreach (var join in vm.ConfiguredJoins)
                        pureQuery.AppendLine($"INNER JOIN [{vm.DatabaseName}].[{schemaClause}].[{join.JoinedTable}] AS [{join.JoinedTable}]" +
                                             $" ON [{join.PrimaryTable}].[{join.PrimaryColumn}] = [{join.JoinedTable}].[{join.JoinedColumn}]");
                }

                if (!string.IsNullOrWhiteSpace(vm.CustomWhereClause))
                    pureQuery.AppendLine($"WHERE {vm.CustomWhereClause.Trim()}");

                // ── GROUP BY with SafeBracket guard ───────────────────────────
                bool hasAnyAggregate = vm.Fields.Any(f => f.Aggregate != AggregateFunction.None);

                var groupByFields = vm.Fields
                    .Where(f => f.IsGroupBy ||
                                (hasAnyAggregate && f.Aggregate == AggregateFunction.None && !f.IsGroupBy))
                    .ToList();

                if (groupByFields.Any())
                {
                    var groupByTokens = groupByFields
                        .Select(f => SafeBracket(f.Name))
                        .Where(b => b is not null)
                        .ToList();

                    if (groupByTokens.Count > 0)
                        pureQuery.AppendLine("GROUP BY " + string.Join(", ", groupByTokens));
                }

                var tempReport = new ReportDefinition
                {
                    ServerName  = vm.Report.ServerName,
                    DatabaseName = vm.Report.DatabaseName,
                    AuthType    = vm.Report.AuthType,
                    Username    = vm.Report.Username,
                    Password    = vm.Report.Password,
                    CustomSql   = pureQuery.ToString()
                };

                var customFields = await dbService.GetSchemaFromCustomSqlAsync(tempReport);

                vm.Fields.Clear();
                vm.AvailableFields.Clear();

                foreach (var field in customFields)
                {
                    field.IsDetailField = true;
                    vm.Fields.Add(field);
                }

                MessageBox.Show(
                    $"Successfully parsed {customFields.Count} fields from your custom SQL. They are now available in the Layout tab.",
                    "SQL Parsed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to parse custom SQL.\n\nError: {ex.Message}",
                                "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
    }
}