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

        // ── Custom Dropdown Logic (Search & Select) ───────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Tag is ListBox targetList)
            {
                var searchText = textBox.Text;

                // FIX: If this is the repeating "Joined Table" list, do NOT filter the shared default view.
                // Doing so causes previously configured rows to lose their SelectedItem!
                if (targetList.Name == "JoinTableList" && DataContext is WizardViewModel vm)
                {
                    // Preserve the selection so WPF doesn't drop it during the swap
                    var currentSelection = targetList.SelectedItem;

                    if (string.IsNullOrWhiteSpace(searchText))
                    {
                        // Reset to the full shared collection
                        targetList.ItemsSource = vm.AvailableTables;
                    }
                    else
                    {
                        // Create a temporary local list just for this specific dropdown
                        targetList.ItemsSource = vm.AvailableTables
                            .Where(t => t.Contains(searchText, System.StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }

                    if (currentSelection != null)
                        targetList.SelectedItem = currentSelection;

                    return;
                }

                // Standard collection view filter for the single-instance dropdowns (Database, Schema, Base Table)
                if (targetList.ItemsSource != null)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(targetList.ItemsSource);
                    view.Filter = item =>
                    {
                        if (string.IsNullOrWhiteSpace(searchText)) return true;
                        return item?.ToString().Contains(searchText, System.StringComparison.OrdinalIgnoreCase) ?? false;
                    };
                }
            }
        }

        private void DropdownList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // The ListBox Tag is bound to its parent ToggleButton. 
            if (sender is ListBox listBox && listBox.Tag is System.Windows.Controls.Primitives.ToggleButton toggle)
            {
                if (listBox.Parent is StackPanel panel)
                {
                    var activeSearchBox = panel.Children.OfType<TextBox>().FirstOrDefault();
                    if (activeSearchBox != null && activeSearchBox.IsKeyboardFocusWithin)
                    {
                        return;
                    }
                }

                if (e.AddedItems.Count > 0)
                {
                    toggle.IsChecked = false; // Close the popup

                    if (listBox.Parent is StackPanel p)
                    {
                        var searchBox = p.Children.OfType<TextBox>().FirstOrDefault();
                        if (searchBox != null)
                        {
                            searchBox.Text = string.Empty;
                        }
                    }
                }
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