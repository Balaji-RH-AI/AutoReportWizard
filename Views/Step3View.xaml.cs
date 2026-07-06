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

        private static void BuildPreviewSql(WizardViewModel vm)
        {
            var sb = new StringBuilder();
            var groupByFields = vm.Fields.Where(f => f.IsGroupBy).ToList();
            bool hasGroupBy = groupByFields.Any();

            sb.AppendLine("CREATE OR ALTER PROCEDURE dbo." + (string.IsNullOrWhiteSpace(vm.StoredProcName) ? "[ProcedureName]" : vm.StoredProcName));
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

            var selectItems = vm.Fields
                .OrderBy(f => f.DisplayOrder)
                .Select(f =>
                {
                    string expr = f.GetSelectExpression();
                    if (!expr.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"        {expr} AS [{f.Name}]";
                    }
                    return $"        {expr}";
                })
                .ToList();

            if (!selectItems.Any())
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
                    sb.AppendLine($"    INNER JOIN [{vm.DatabaseName}].[{schemaClause}].[{join.JoinedTable}] AS [{join.JoinedTable}] ON [{join.PrimaryTable}].[{join.PrimaryColumn}] = [{join.JoinedTable}].[{join.JoinedColumn}]");
                }
            }

            if (!string.IsNullOrWhiteSpace(vm.CustomWhereClause))
            {
                sb.AppendLine($"    WHERE {vm.CustomWhereClause.Trim()}");
            }

            if (hasGroupBy)
            {
                sb.AppendLine("    GROUP BY " + string.Join(", ", groupByFields.Select(f => $"[{f.Name}]")));
            }

            sb.AppendLine("    OPTION (RECOMPILE);");
            sb.AppendLine("END");

            vm.CustomSql = sb.ToString();
        }

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

                var selectItems = vm.Fields.OrderBy(f => f.DisplayOrder).Select(f =>
                {
                    string expr = f.GetSelectExpression();
                    if (!expr.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"    {expr} AS [{f.Name}]";
                    }
                    return $"    {expr}";
                }).ToList();

                if (!selectItems.Any())
                    pureQuery.AppendLine("    *");
                else
                    pureQuery.AppendLine(string.Join(",\n", selectItems));

                string schemaClause = string.IsNullOrEmpty(vm.SchemaName) ? "dbo" : vm.SchemaName;
                pureQuery.AppendLine($"FROM [{vm.DatabaseName}].[{schemaClause}].[{vm.TableOrViewName}] AS [{vm.TableOrViewName}]");

                if (vm.ConfiguredJoins.Any())
                {
                    foreach (var join in vm.ConfiguredJoins)
                        pureQuery.AppendLine($"INNER JOIN [{vm.DatabaseName}].[{schemaClause}].[{join.JoinedTable}] AS [{join.JoinedTable}] ON [{join.PrimaryTable}].[{join.PrimaryColumn}] = [{join.JoinedTable}].[{join.JoinedColumn}]");
                }

                if (!string.IsNullOrWhiteSpace(vm.CustomWhereClause))
                    pureQuery.AppendLine($"WHERE {vm.CustomWhereClause.Trim()}");

                var groupByFields = vm.Fields.Where(f => f.IsGroupBy).ToList();
                if (groupByFields.Any())
                    pureQuery.AppendLine("GROUP BY " + string.Join(", ", groupByFields.Select(f => $"[{f.Name}]")));

                var tempReport = new ReportDefinition
                {
                    ServerName = vm.Report.ServerName,
                    DatabaseName = vm.Report.DatabaseName,
                    AuthType = vm.Report.AuthType,
                    Username = vm.Report.Username,
                    Password = vm.Report.Password,
                    CustomSql = pureQuery.ToString()
                };

                var customFields = await dbService.GetSchemaFromCustomSqlAsync(tempReport);

                vm.Fields.Clear();
                vm.AvailableFields.Clear();

                foreach (var field in customFields)
                {
                    field.IsDetailField = true;
                    vm.Fields.Add(field);
                }

                MessageBox.Show($"Successfully parsed {customFields.Count} fields from your custom SQL. They are now available in the Layout tab.",
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