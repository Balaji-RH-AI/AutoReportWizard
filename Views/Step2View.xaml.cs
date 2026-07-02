using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoReportWizard.Models;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step2View : UserControl
    {
        public Step2View()
        {
            InitializeComponent();
        }

        // Trigger the ViewModel's load method when the "↻ Load" text is clicked
        private async void RefreshDatabases_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is WizardViewModel vm)
            {
                await vm.LoadDatabaseOptionsAsync();
            }
        }

        // ── Relational Joins UI Logic ─────────────────────────────────────────

        private void AddJoin_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            var join = new TableJoin
            {
                PrimaryTable = string.IsNullOrWhiteSpace(vm.SelectedTable) ? vm.TableOrViewName : vm.SelectedTable
            };

            vm.ConfiguredJoins.Add(join);
        }

        private void DeleteJoin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TableJoin join)
                return;

            if (DataContext is not WizardViewModel vm) return;

            vm.ConfiguredJoins.Remove(join);
        }

        // ── Transfer Mechanics (Access-Style Field Selection) ─────────────────

        private void MoveRight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;
            var selected = AvailableList.SelectedItems.Cast<ReportField>().ToList();
            foreach (var item in selected)
            {
                vm.AvailableFields.Remove(item);
                vm.Fields.Add(item);
            }
        }

        private void MoveAllRight_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;
            foreach (var item in vm.AvailableFields.ToList())
            {
                vm.AvailableFields.Remove(item);
                vm.Fields.Add(item);
            }
        }

        private void MoveLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;
            var selected = SelectedList.SelectedItems.Cast<ReportField>().ToList();
            foreach (var item in selected)
            {
                vm.Fields.Remove(item);
                vm.AvailableFields.Add(item);
            }
        }

        private void MoveAllLeft_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;
            foreach (var item in vm.Fields.ToList())
            {
                vm.Fields.Remove(item);
                vm.AvailableFields.Add(item);
            }
        }
    }
}