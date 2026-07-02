using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoReportWizard.Models;

namespace AutoReportWizard
{
    // ── InverseBoolConverter ──────────────────────────────────────────────
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    public partial class Step3View : UserControl
    {
        private static readonly AggregateFunction[] AggregateOptions =
            (AggregateFunction[])Enum.GetValues(typeof(AggregateFunction));

        public Step3View()
        {
            InitializeComponent();
        }

        // ── ComboBox bootstrapping ────────────────────────────────────────────
        private void AggCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox combo || combo.Tag is not ReportField field) return;
            combo.ItemsSource = AggregateOptions;
            combo.SelectedItem = field.Aggregate;
        }

        // ── Event handlers ────────────────────────────────────────────────────
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
            if (DataContext is not WizardViewModel vm) return;

            BuildPreviewSql(vm);
        }

        private void ScaffoldQuery_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            BuildPreviewSql(vm);
        }

        private static void BuildPreviewSql(WizardViewModel vm)
        {
            var sb = new StringBuilder();
            var groupByFields = vm.Fields.Where(f => f.IsGroupBy).ToList();
            bool hasGroupBy = groupByFields.Any();

            sb.AppendLine("CREATE OR ALTER PROCEDURE dbo." + vm.StoredProcName);
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
                .Select(f => "        " + f.GetSelectExpression())
                .ToList();

            for (int i = 0; i < selectItems.Count; i++)
            {
                string comma = i < selectItems.Count - 1 ? "," : "";
                sb.AppendLine($"{selectItems[i]}{comma}");
            }

            string schemaClause = string.IsNullOrEmpty(vm.SchemaName) ? "dbo" : vm.SchemaName;
            sb.AppendLine($"    FROM [{vm.DatabaseName}].[{schemaClause}].[{vm.TableOrViewName}]");

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

        // ── Parse Custom SQL to Layout ────────────────────────────────────────
        private async void ParseCustomSql_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || string.IsNullOrWhiteSpace(vm.CustomSql)) return;

            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                var dbService = new Infrastructure.DatabaseService();

                // Extract fields using SQL Server's native discovery
                var customFields = await dbService.GetSchemaFromCustomSqlAsync(vm.Report);

                // Clear existing layout fields
                vm.Fields.Clear();
                vm.AvailableFields.Clear();

                foreach (var field in customFields)
                {
                    // Ensure the layout grid sees them as active detail fields
                    field.IsDetailField = true;
                    vm.Fields.Add(field);
                }

                MessageBox.Show($"Successfully parsed {customFields.Count} fields from your custom SQL. They are now available in the Layout tab.",
                                "SQL Parsed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to parse custom SQL. Ensure your syntax is correct and does not rely on complex temp tables (#) that sp_describe cannot resolve.\n\nError: {ex.Message}",
                                "Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
    }
}