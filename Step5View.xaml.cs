using System.Windows;
using System.Windows.Controls;
using AutoReportWizard.Models;

namespace AutoReportWizard
{
    public partial class Step5View : UserControl
    {
        private ReportField? _selectedField;

        public Step5View()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshDisplayOrder();
        }

        private void LayoutGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedField = LayoutGrid.SelectedItem as ReportField;
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || _selectedField is null) return;
            int idx = vm.Fields.IndexOf(_selectedField);
            if (idx <= 0) return;

            vm.Fields.Move(idx, idx - 1);
            RefreshDisplayOrder();
            LayoutGrid.SelectedItem = _selectedField;
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || _selectedField is null) return;
            int idx = vm.Fields.IndexOf(_selectedField);
            if (idx < 0 || idx >= vm.Fields.Count - 1) return;

            vm.Fields.Move(idx, idx + 1);
            RefreshDisplayOrder();
            LayoutGrid.SelectedItem = _selectedField;
        }

        private void RefreshDisplayOrder()
        {
            if (DataContext is not WizardViewModel vm) return;
            for (int i = 0; i < vm.Fields.Count; i++)
                vm.Fields[i].DisplayOrder = i;
        }
    }
}