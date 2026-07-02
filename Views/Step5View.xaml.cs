using System.Linq;
using System.Windows;
using System.Windows.Controls;

using AutoReportWizard.Models;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step5View : UserControl
    {
        public Step5View()
        {
            InitializeComponent();
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || LayoutGrid.SelectedItem is not ReportField selectedField) return;
            int idx = vm.Fields.IndexOf(selectedField);
            if (idx <= 0) return;

            vm.Fields.Move(idx, idx - 1);
            RefreshDisplayOrder(vm);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || LayoutGrid.SelectedItem is not ReportField selectedField) return;
            int idx = vm.Fields.IndexOf(selectedField);
            if (idx < 0 || idx >= vm.Fields.Count - 1) return;

            vm.Fields.Move(idx, idx + 1);
            RefreshDisplayOrder(vm);
        }

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || LayoutGrid.SelectedItem is not ReportField selectedField) return;
            vm.Fields.Remove(selectedField);
            RefreshDisplayOrder(vm);
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            // Generate a unique placeholder name
            string baseName = "NewField";
            string uniqueName = baseName;
            int counter = 1;
            while (vm.Fields.Any(f => f.Name == uniqueName))
            {
                uniqueName = $"{baseName}{counter++}";
            }

            vm.Fields.Add(new ReportField
            {
                Name = uniqueName,
                SqlDataType = "nvarchar",
                DotNetType = "System.String",
                IsDetailField = true
            });

            RefreshDisplayOrder(vm);
        }

        private void RefreshDisplayOrder(WizardViewModel vm)
        {
            for (int i = 0; i < vm.Fields.Count; i++)
            {
                vm.Fields[i].DisplayOrder = i;
            }
            vm.SyncFieldsToReport();
        }
    }
}