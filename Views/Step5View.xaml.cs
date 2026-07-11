using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // ── Toolbar Actions ───────────────────────────────────────────────────

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is WizardViewModel vm)
            {
                var newField = new ReportField
                {
                    Name = $"NewField{vm.Fields.Count + 1}",
                    CustomHeaderLabel = "New Field",
                    SqlDataType = "varchar",
                    DotNetType = "System.String",
                    CanvasX = 16,
                    CanvasY = 16,
                    ItemWidth = 120,
                    ItemHeight = 32,
                    IsDetailField = true
                };

                vm.Fields.Add(newField);

                // Auto-select the newly added field
                foreach (var f in vm.Fields) f.IsSelected = false;
                newField.IsSelected = true;
                vm.SelectedField = newField;
            }
        }

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is WizardViewModel vm && vm.SelectedField != null)
            {
                vm.Fields.Remove(vm.SelectedField);
                vm.SelectedField = null;
            }
        }

        // ── Canvas Interaction ────────────────────────────────────────────────

        private void DesignCanvasPaper_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Clear selection if the user clicks the empty white paper area
            if (DataContext is WizardViewModel vm)
            {
                foreach (var f in vm.Fields) f.IsSelected = false;
                vm.SelectedField = null;
            }
        }

        // Fixed Method Name to match the XAML
        private void DesignerFieldItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ReportField field)
            {
                if (DataContext is WizardViewModel vm)
                {
                    // Deselect all others
                    foreach (var f in vm.Fields) f.IsSelected = false;

                    // Select the clicked item
                    field.IsSelected = true;
                    vm.SelectedField = field;
                }
                // Do NOT set e.Handled = true here, allowing the drag thumb to work
            }
        }
    }
}