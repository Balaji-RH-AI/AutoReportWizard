using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using AutoReportWizard.Models;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step5View : UserControl
    {
        private Point _dragStartPoint;
        private ReportField? _draggedItem;

        public Step5View()
        {
            InitializeComponent();
        }

        // ── Drag-and-Drop ────────────────────────────────────────────────────
        private void LayoutGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void LayoutGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var diff = _dragStartPoint - e.GetPosition(null);

            if (System.Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // Only initiate drag on a DataGridRow
            var row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (row?.Item is not ReportField field) return;

            // Don't initiate drag if user is editing a cell
            if (LayoutGrid.CurrentColumn != null && LayoutGrid.IsEditing()) return;

            _draggedItem = field;
            DragDrop.DoDragDrop(row, new DataObject(typeof(ReportField), field), DragDropEffects.Move);
            _draggedItem = null;
        }

        private void LayoutGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ReportField)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void LayoutGrid_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;
            if (!e.Data.GetDataPresent(typeof(ReportField))) return;

            var droppedData = (ReportField)e.Data.GetData(typeof(ReportField));

            // Find the target row under the cursor
            var targetRow = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (targetRow?.Item is not ReportField targetField || droppedData == targetField)
                return;

            int oldIdx = vm.Fields.IndexOf(droppedData);
            int newIdx = vm.Fields.IndexOf(targetField);

            if (oldIdx < 0 || newIdx < 0) return;

            vm.Fields.Move(oldIdx, newIdx);
            RefreshDisplayOrder(vm);
        }

        // ── Other Actions ────────────────────────────────────────────────────
        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || LayoutGrid.SelectedItem is not ReportField selectedField) return;
            vm.Fields.Remove(selectedField);
            RefreshDisplayOrder(vm);
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

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

        // ── Helpers ──────────────────────────────────────────────────────────
        private void RefreshDisplayOrder(WizardViewModel vm)
        {
            for (int i = 0; i < vm.Fields.Count; i++)
            {
                vm.Fields[i].DisplayOrder = i;
            }
            vm.SyncFieldsToReport();
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }

    // Extension for checking DataGrid editing state
    internal static class DataGridExtensions
    {
        public static bool IsEditing(this DataGrid grid)
        {
            return grid.CommitEdit(DataGridEditingUnit.Row, true) == false;
        }
    }
}