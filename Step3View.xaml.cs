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
            combo.ItemsSource    = AggregateOptions;
            combo.SelectedItem   = field.Aggregate;
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

        // ── Scaffold Custom Query to Editor ───────────────────────────────────
        private void ScaffoldQuery_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            var sb = new StringBuilder();
            var groupByFields = vm.Fields.Where(f => f.IsGroupBy).ToList();
            bool hasGroupBy = groupByFields.Any();

            sb.AppendLine("SELECT");

            var selectItems = vm.Fields
                .OrderBy(f => f.DisplayOrder)
                .Select(f => "    " + f.GetSelectExpression())
                .ToList();

            for (int i = 0; i < selectItems.Count; i++)
            {
                string comma = i < selectItems.Count - 1 ? "," : "";
                sb.AppendLine($"{selectItems[i]}{comma}");
            }

            // Using standard bracket syntax for scaffolding
            string schemaClause = string.IsNullOrEmpty(vm.SchemaName) ? "dbo" : vm.SchemaName;
            sb.AppendLine($"FROM [{vm.DatabaseName}].[{schemaClause}].[{vm.TableOrViewName}]");

            if (hasGroupBy)
            {
                sb.Append("GROUP BY ");
                sb.AppendLine(string.Join(", ", groupByFields.Select(f => $"[{f.Name}]")));
            }

            sb.AppendLine("OPTION (RECOMPILE);");

            // Write straight to the ViewModel so the UI editor updates instantly
            vm.CustomSql = sb.ToString();
        }
    }
}