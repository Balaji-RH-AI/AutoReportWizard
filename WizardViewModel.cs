using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AutoReportWizard.Infrastructure;
using AutoReportWizard.Models;

namespace AutoReportWizard
{
    /// <summary>
    /// Central MVVM hub for the wizard. Holds the single ReportDefinition state
    /// object and exposes all properties needed by every step view.
    /// No JSON payloads — strongly typed objects are passed directly to services.
    /// </summary>
    public class WizardViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _databaseService = new();

        // ── Core State ────────────────────────────────────────────────────────
        public ReportDefinition Report { get; } = new ReportDefinition();

        /// <summary>Observable list of fields — bound to all step views.</summary>
        public ObservableCollection<ReportField> Fields { get; } = new();

        /// <summary>Fields available in the selected table (Left Box)</summary>
        public ObservableCollection<ReportField> AvailableFields { get; } = new();

        /// <summary>Observable list of parameters — bound to Step 5.</summary>
        public ObservableCollection<ReportParameter> Parameters { get; } = new();

        public ICommand RunPreviewCommand { get; }

        public WizardViewModel()
        {
            foreach (var parameter in Report.Parameters)
                Parameters.Add(parameter);

            RunPreviewCommand = new RelayCommand(
                async _ => await RunPreviewAsync(),
                _ => !IsPreviewRunning);
        }

        // ── Step 1 Bindings (Target Environment & Credentials) ────────────────
        public string ServerName
        {
            get => Report.ServerName;
            set { Report.ServerName = value; OnPropertyChanged(); }
        }

        public string DatabaseName
        {
            get => Report.DatabaseName;
            set { Report.DatabaseName = value; OnPropertyChanged(); }
        }

        public AuthenticationType AuthType
        {
            get => Report.AuthType;
            set
            {
                Report.AuthType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSqlAuth));
                OnPropertyChanged(nameof(IsWindowsAuth));
            }
        }

        public string Username
        {
            get => Report.Username;
            set { Report.Username = value; OnPropertyChanged(); }
        }

        public string Password
        {
            set { Report.Password = value; }
        }

        public bool IsSqlAuth => AuthType == AuthenticationType.SqlServer;
        public bool IsWindowsAuth => AuthType == AuthenticationType.Windows;


        // ── Step 2 Bindings (Dataset Definition) ──────────────────────────────
        public string ReportName
        {
            get => Report.ReportName;
            set { Report.ReportName = value; OnPropertyChanged(); OnPropertyChanged(nameof(StoredProcName)); }
        }

        public string SchemaName
        {
            get => Report.SchemaName;
            set { Report.SchemaName = value; OnPropertyChanged(); }
        }

        public string TableOrViewName
        {
            get => Report.TableOrViewName;
            set
            {
                Report.TableOrViewName = value;
                OnPropertyChanged();

                // Auto-generate SQL based on selection
                if (string.IsNullOrWhiteSpace(value))
                    CustomSql = string.Empty;
                else
                {
                    // Detect schema prefix
                    string tableOrView = value;
                    string? schema = null;

                    if (value.Contains('.'))
                    {
                        var parts = value.Split('.');
                        if (parts.Length == 2)
                        {
                            schema = parts[0];
                            tableOrView = parts[1];
                        }
                    }

                    string schemaClause = string.IsNullOrEmpty(schema) ? "dbo" : schema;
                    string sanitizedName = tableOrView.Replace("[dbo].", "").Replace("dbo.", "").Trim();
                    CustomSql = $"SELECT * FROM [{schemaClause}].[{sanitizedName}]";
                }
            }
        }

        // ── Cascading Dropdown Collections ────────────────────────────────────
        public ObservableCollection<string> AvailableDatabases { get; } = new();
        public ObservableCollection<string> AvailableSchemas { get; } = new();
        public ObservableCollection<string> AvailableTables { get; } = new();

        private string _selectedDatabase = string.Empty;
        public string SelectedDatabase
        {
            get => _selectedDatabase;
            set { _selectedDatabase = value; OnPropertyChanged(); }
        }

        private string _selectedSchema = string.Empty;
        public string SelectedSchema
        {
            get => _selectedSchema;
            set { _selectedSchema = value; OnPropertyChanged(); }
        }

        private string _selectedTable = string.Empty;
        public string SelectedTable
        {
            get => _selectedTable;
            set { _selectedTable = value; OnPropertyChanged(); }
        }

        public string StoredProcName => Report.StoredProcName;

        // ── Step 3 Bindings (Live SQL Editor) ─────────────────────────────────
        public string CustomSql
        {
            get => Report.CustomSql;
            set { Report.CustomSql = value; OnPropertyChanged(); }
        }

        // ── Step 4 Bindings (Header & Footer) ─────────────────────────────────
        public string ReportTitle
        {
            get => Report.ReportTitle;
            set { Report.ReportTitle = value; OnPropertyChanged(); }
        }

        public string ReportSubtitle
        {
            get => Report.ReportSubtitle;
            set { Report.ReportSubtitle = value; OnPropertyChanged(); }
        }

        public bool IncludeExecutionTime
        {
            get => Report.IncludeExecutionTime;
            set { Report.IncludeExecutionTime = value; OnPropertyChanged(); }
        }

        public bool IncludePageNumbers
        {
            get => Report.IncludePageNumbers;
            set { Report.IncludePageNumbers = value; OnPropertyChanged(); }
        }

        public bool IncludeGrandTotals
        {
            get => Report.IncludeGrandTotals;
            set { Report.IncludeGrandTotals = value; OnPropertyChanged(); }
        }

        public string? DynamicHeaderFieldName
        {
            get => Report.DynamicHeaderFieldName;
            set { Report.DynamicHeaderFieldName = value; OnPropertyChanged(); }
        }

        public string StaticHeaderLeftLine1
        {
            get => Report.StaticHeaderLeftLine1;
            set { Report.StaticHeaderLeftLine1 = value; OnPropertyChanged(); }
        }

        public string StaticHeaderLeftLine2
        {
            get => Report.StaticHeaderLeftLine2;
            set { Report.StaticHeaderLeftLine2 = value; OnPropertyChanged(); }
        }

        // Real-time clock for the Step 5 preview pane
        public string CurrentPreviewTime => DateTime.Now.ToString("g");

        // ── Step 6 Bindings (Layout & Output) ─────────────────────────────────
        public string OutputDirectory
        {
            get => Report.OutputDirectory;
            set { Report.OutputDirectory = value; OnPropertyChanged(); }
        }

        private DataTable? _previewData;
        public DataTable? PreviewData
        {
            get => _previewData;
            set
            {
                _previewData = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPreviewData));
            }
        }

        private bool _isPreviewRunning;
        public bool IsPreviewRunning
        {
            get => _isPreviewRunning;
            set
            {
                _isPreviewRunning = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _previewError = string.Empty;
        public string PreviewError
        {
            get => _previewError;
            set
            {
                _previewError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasPreviewError));
            }
        }

        public bool HasPreviewData => PreviewData?.Rows.Count > 0;
        public bool HasPreviewError => !string.IsNullOrWhiteSpace(PreviewError);

        // ── Schema Discovery State ────────────────────────────────────────────
        private bool _isDiscovering;
        public bool IsDiscovering
        {
            get => _isDiscovering;
            set { _isDiscovering = value; OnPropertyChanged(); }
        }

        private string _discoveryError = string.Empty;
        public string DiscoveryError
        {
            get => _discoveryError;
            set { _discoveryError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDiscoveryError)); }
        }

        public bool HasDiscoveryError => !string.IsNullOrEmpty(_discoveryError);

        // ── Generation State ──────────────────────────────────────────────────
        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); }
        }

        private string _generationLog = string.Empty;
        public string GenerationLog
        {
            get => _generationLog;
            set { _generationLog = value; OnPropertyChanged(); }
        }

        public void AppendLog(string message)
        {
            GenerationLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        }

        // ── Sync Fields → Report.Fields ───────────────────────────────────────
        public void SyncFieldsToReport()
        {
            Report.Fields.Clear();
            for (int i = 0; i < Fields.Count; i++)
            {
                Fields[i].DisplayOrder = i;
                Report.Fields.Add(Fields[i]);
            }
        }

        // ── MVVM Boilerplate ──────────────────────────────────────────────────
        public void SyncParametersToReport()
        {
            Report.Parameters.Clear();
            foreach (var parameter in Parameters)
                Report.Parameters.Add(parameter);
        }

        private async Task RunPreviewAsync()
        {
            if (IsPreviewRunning)
                return;

            PreviewError = string.Empty;
            IsPreviewRunning = true;

            try
            {
                SyncFieldsToReport();
                SyncParametersToReport();

                PreviewData = await _databaseService.ExecuteStoredProcedurePreviewAsync(Report, Report.Parameters);
                AppendLog($"Preview returned {PreviewData.Rows.Count} row(s).");
            }
            catch (Exception ex)
            {
                PreviewData = null;
                PreviewError = ex.Message;
                AppendLog($"Preview failed: {ex.Message}");
            }
            finally
            {
                IsPreviewRunning = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RelayCommand — lightweight ICommand for MVVM command binding
    // ─────────────────────────────────────────────────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void RaiseCanExecuteChanged() =>
            System.Windows.Application.Current.Dispatcher.Invoke(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
    }
}
