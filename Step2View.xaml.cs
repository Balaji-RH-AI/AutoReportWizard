using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoReportWizard.Infrastructure;
using AutoReportWizard.Models;

namespace AutoReportWizard
{
    public partial class Step2View : UserControl
    {
        private readonly DatabaseService _dbService = new();

        public Step2View()
        {
            InitializeComponent();
        }

        // â”€â”€ Database Fetching â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // Add a flag to prevent multiple rapid clicks
        private bool _isConnecting = false;

        private async void RefreshDatabases_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isConnecting || DataContext is not WizardViewModel vm) return;

            _isConnecting = true;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            vm.AvailableDatabases.Clear();
            vm.AvailableSchemas.Clear();
            vm.AvailableTables.Clear();

            try
            {
                var dbs = await _dbService.GetDatabasesAsync(vm.Report);
                foreach (var db in dbs) vm.AvailableDatabases.Add(db);

                if (!string.IsNullOrWhiteSpace(vm.DatabaseName) && vm.AvailableDatabases.Contains(vm.DatabaseName))
                    vm.SelectedDatabase = vm.DatabaseName;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Connection failed. Please verify your VPN is connected and {vm.ServerName} is reachable.\n\nError: {ex.Message}",
                                "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isConnecting = false;
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        private async void Database_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || string.IsNullOrEmpty(vm.SelectedDatabase)) return;

            // Set context for future queries
            vm.Report.DatabaseName = vm.SelectedDatabase;
            vm.AvailableSchemas.Clear();
            vm.AvailableTables.Clear();

            try
            {
                var schemas = await _dbService.GetSchemasAsync(vm.Report);
                foreach (var schema in schemas) vm.AvailableSchemas.Add(schema);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Schema Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Schema_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || string.IsNullOrEmpty(vm.SelectedSchema)) return;

            vm.Report.SchemaName = vm.SelectedSchema;
            vm.AvailableTables.Clear();

            try
            {
                var tables = await _dbService.GetTablesAndViewsAsync(vm.Report, vm.SelectedSchema);
                foreach (var table in tables) vm.AvailableTables.Add(table);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Table Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Table_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm || string.IsNullOrEmpty(vm.SelectedTable)) return;

            vm.Report.TableOrViewName = vm.SelectedTable;
            vm.AvailableFields.Clear();

            try
            {
                var fields = await _dbService.GetSchemaAsync(vm.Report);
                foreach (var field in fields)
                {
                    // Tag the field with its source so the UI shows where it came from
                    field.SourceDatabase = vm.SelectedDatabase;
                    field.SourceSchema = vm.SelectedSchema;
                    field.SourceTable = vm.SelectedTable;

                    vm.AvailableFields.Add(field);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Column Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // â”€â”€ Transfer Mechanics (Access-Style) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
