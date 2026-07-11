using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AutoReportWizard.Infrastructure;
using AutoReportWizard.Models;
using AutoReportWizard.Services;

namespace AutoReportWizard.ViewModels;

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

    /// <summary>Observable list of parameters — bound to Step 6 preview bar.</summary>
    public ObservableCollection<ReportParameter> Parameters { get; } = new();

    /// <summary>User-defined dynamic parameters with prompt text and header mapping.</summary>
    public ObservableCollection<DynamicParameter> DynamicParameters { get; } = new();

    // ── Design Surface State (Report Designer) ─────────────────────────────
    /// <summary>Observable collection of report elements placed on the visual designer canvas.</summary>
    public ObservableCollection<ReportComponent> CanvasComponents { get; } = new();

    private ReportComponent? _selectedComponent;
    /// <summary>The currently active/selected component on the report canvas.</summary>
    public ReportComponent? SelectedComponent
    {
        get => _selectedComponent;
        set
        {
            if (_selectedComponent == value) return;
            _selectedComponent = value;
            OnPropertyChanged();
        }
    }

    private ReportField? _selectedField;
    /// <summary>
    /// The field currently selected on the Step 5 drag-and-drop canvas.
    /// The right-side Property Grid binds to this.
    /// </summary>
    public ReportField? SelectedField
    {
        get => _selectedField;
        set
        {
            if (_selectedField == value) return;
            _selectedField = value;
            OnPropertyChanged();
        }
    }

    /// <summary>SQL data types available in the parameter builder dropdown.</summary>
    public static IReadOnlyList<string> SqlDataTypes { get; } = new[]
    {
        "int", "bigint", "smallint", "tinyint", "bit",
        "char(8)", "varchar(50)", "varchar(max)", "nvarchar(50)", "nvarchar(max)",
        "date", "datetime", "datetime2", "decimal(18,4)", "money", "float", "uniqueidentifier"
    };

    /// <summary>Header zone options for parameter-to-header mapping.</summary>
    public static IReadOnlyList<HeaderZone> HeaderZones { get; } =
        Enum.GetValues<HeaderZone>().ToList();

    // ── Step 5 Designer Dropdown Lists ───────────────────────────────

    /// <summary>Options for Aggregate dropdown.</summary>
    public static IReadOnlyList<AggregateFunction> AggregateFunctions { get; } =
        Enum.GetValues<AggregateFunction>().ToList();

    /// <summary>Options for Text Align dropdown.</summary>
    public static IReadOnlyList<string> TextAlignOptions { get; } =
        new List<string> { "Default", "Left", "Center", "Right" };

    /// <summary>Options for Font Weight dropdown.</summary>
    public static IReadOnlyList<string> FontWeightOptions { get; } =
        new List<string> { "Normal", "Bold" };

    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Join mappings configured for the current report.</summary>
    public ObservableCollection<TableJoin> ConfiguredJoins { get; } = new();

    /// <summary>Columns available in the currently selected base table.</summary>
    public ObservableCollection<string> AvailableColumns { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    // Commands
    public ICommand RunPreviewCommand { get; }
    public ICommand AddParameterCommand { get; }
    public ICommand RemoveParameterCommand { get; }
    public ICommand AddComponentCommand { get; }
    public ICommand DeleteComponentCommand { get; }

    public WizardViewModel()
    {
        foreach (var parameter in Report.DynamicParameters)
            DynamicParameters.Add(parameter);

        foreach (var join in Report.Joins)
            ConfiguredJoins.Add(join);

        ConfiguredJoins.CollectionChanged += (_, _) => SyncJoinsToReport();
        DynamicParameters.CollectionChanged += (_, _) => SyncDynamicParametersToReport();

        ImportSpCommand = new RelayCommand(
            async _ => await ImportSpSchemaAsync(),
            _ => !string.IsNullOrWhiteSpace(ExistingSpName) && !IsBusy);

        RunPreviewCommand = new RelayCommand(
            async _ => await RunPreviewAsync(),
            _ => !IsPreviewRunning && !IsBusy);

        AddParameterCommand = new RelayCommand(
            _ => AddParameter(),
            _ => !IsBusy);

        RemoveParameterCommand = new RelayCommand(
            p => RemoveParameter(p as DynamicParameter),
            p => p is DynamicParameter && !IsBusy);

        AddComponentCommand = new RelayCommand(
            type => AddComponent(type?.ToString()),
            _ => !IsBusy);

        DeleteComponentCommand = new RelayCommand(
            _ => DeleteComponent(),
            _ => SelectedComponent != null && !IsBusy);
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

    private string _existingSpName = string.Empty;
    public string ExistingSpName
    {
        get => _existingSpName;
        set
        {
            _existingSpName = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested(); // Refresh button state
        }
    }

    public ICommand ImportSpCommand { get; }

    // ── Cascading Dropdown Collections & Caching ──────────────────────────
    private readonly Dictionary<string, List<string>> _schemaCache = new();
    private readonly Dictionary<string, List<string>> _tableCache = new();

    public ObservableCollection<string> AvailableDatabases { get; } = new();
    public ObservableCollection<string> AvailableSchemas { get; } = new();
    public ObservableCollection<string> AvailableTables { get; } = new();

    private string _selectedDatabase = string.Empty;
    public string SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (_selectedDatabase == value)
                return;

            _selectedDatabase = value;
            Report.DatabaseName = value;
            OnPropertyChanged();

            AvailableSchemas.Clear();
            AvailableTables.Clear();
            SelectedSchema = string.Empty;
            SelectedTable = string.Empty;

            if (!string.IsNullOrWhiteSpace(value))
                _ = LoadSchemasAsync();
        }
    }

    private string _selectedSchema = string.Empty;
    public string SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            if (_selectedSchema == value)
                return;

            _selectedSchema = value;
            Report.SchemaName = value;
            OnPropertyChanged();

            AvailableTables.Clear();
            SelectedTable = string.Empty;

            if (!string.IsNullOrWhiteSpace(value))
                _ = LoadTablesAsync();
        }
    }

    private string _selectedTable = string.Empty;
    public string SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (_selectedTable == value)
                return;

            _selectedTable = value;
            Report.TableOrViewName = value;
            OnPropertyChanged();

            if (!string.IsNullOrWhiteSpace(value))
                _ = LoadFieldsAsync();
        }
    }

    public string StoredProcName => Report.StoredProcName;

    // ── Async Data Loading Methods ────────────────────────────────────────

    public async Task LoadDatabaseOptionsAsync()
    {
        AvailableDatabases.Clear();
        AvailableSchemas.Clear();
        AvailableTables.Clear();
        _schemaCache.Clear();
        _tableCache.Clear();
        IsBusy = true;

        try
        {
            var dbs = await _databaseService.GetDatabasesAsync(Report);
            foreach (var db in dbs)
                AvailableDatabases.Add(db);

            if (!string.IsNullOrWhiteSpace(DatabaseName) && AvailableDatabases.Contains(DatabaseName))
            {
                SelectedDatabase = DatabaseName;
            }
            else if (AvailableDatabases.Count > 0)
            {
                SelectedDatabase = AvailableDatabases[0];
            }
        }
        catch (Exception ex)
        {
            DiscoveryError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportSpSchemaAsync()
    {
        IsBusy = true;
        DiscoveryError = string.Empty;

        try
        {
            string spName = ExistingSpName.Trim();

            var testParams = new Dictionary<string, object>();

            var fields = await _databaseService.ImportStoredProcedureSchemaAsync(
                Report.BuildConnectionString(),
                spName,
                testParams);

            AvailableFields.Clear();
            Fields.Clear();
            ClearAvailableColumns();

            foreach (var f in fields)
            {
                f.SourceDatabase = SelectedDatabase;
                Fields.Add(f);
                AvailableColumns.Add(f.Name);
            }

            if (string.IsNullOrWhiteSpace(ReportName))
            {
                ReportName = spName.Replace("[", "").Replace("]", "").Split('.').Last();
            }
        }
        catch (Exception ex)
        {
            DiscoveryError = $"SP Import Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadSchemasAsync()
    {
        AvailableSchemas.Clear();
        AvailableTables.Clear();
        AvailableColumns.Clear();
        AvailableFields.Clear();

        if (string.IsNullOrWhiteSpace(SelectedDatabase))
            return;

        try
        {
            if (_schemaCache.TryGetValue(SelectedDatabase, out var cachedSchemas))
            {
                foreach (var schema in cachedSchemas)
                    AvailableSchemas.Add(schema);
                return;
            }

            IsBusy = true;
            var schemas = await _databaseService.GetSchemasAsync(Report);

            var fetchedSchemas = schemas.ToList();
            _schemaCache[SelectedDatabase] = fetchedSchemas;

            foreach (var schema in fetchedSchemas)
                AvailableSchemas.Add(schema);
        }
        catch (Exception ex)
        {
            DiscoveryError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadTablesAsync()
    {
        AvailableTables.Clear();
        AvailableColumns.Clear();
        AvailableFields.Clear();

        if (string.IsNullOrWhiteSpace(SelectedSchema))
            return;

        string cacheKey = $"{SelectedDatabase}.{SelectedSchema}";

        try
        {
            if (_tableCache.TryGetValue(cacheKey, out var cachedTables))
            {
                foreach (var t in cachedTables) AvailableTables.Add(t);
                return;
            }

            IsBusy = true;
            var tables = await _databaseService.GetTablesAndViewsAsync(Report, SelectedSchema);
            var fetchedTables = tables.ToList();
            _tableCache[cacheKey] = fetchedTables;

            foreach (var t in fetchedTables) AvailableTables.Add(t);
        }
        catch (Exception ex)
        {
            DiscoveryError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadFieldsAsync()
    {
        AvailableFields.Clear();
        ClearAvailableColumns();

        if (string.IsNullOrWhiteSpace(SelectedTable))
            return;

        try
        {
            IsBusy = true;
            var fields = await _databaseService.GetSchemaAsync(Report);
            foreach (var f in fields)
            {
                f.SourceDatabase = SelectedDatabase;
                f.SourceSchema = SelectedSchema;
                f.SourceTable = SelectedTable;
                AvailableFields.Add(f);
                AvailableColumns.Add(f.Name);
            }
            UpdateJoinBaseTable(SelectedTable);
        }
        catch (Exception ex)
        {
            DiscoveryError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    public string PreQueryLogic
    {
        get => Report.PreQueryLogic;
        set { Report.PreQueryLogic = value; OnPropertyChanged(); }
    }

    public string CustomWhereClause
    {
        get => Report.CustomWhereClause;
        set { Report.CustomWhereClause = value; OnPropertyChanged(); }
    }

    public string? HeaderSiteField
    {
        get => Report.HeaderSiteField;
        set { Report.HeaderSiteField = value; OnPropertyChanged(); }
    }

    public string? HeaderProcessDateField
    {
        get => Report.HeaderProcessDateField;
        set { Report.HeaderProcessDateField = value; OnPropertyChanged(); }
    }

    public string? HeaderJulianField
    {
        get => Report.HeaderJulianField;
        set { Report.HeaderJulianField = value; OnPropertyChanged(); }
    }

    public string? HeaderWorksourceField
    {
        get => Report.HeaderWorksourceField;
        set { Report.HeaderWorksourceField = value; OnPropertyChanged(); }
    }

    public string? HeaderLoadField
    {
        get => Report.HeaderLoadField;
        set { Report.HeaderLoadField = value; OnPropertyChanged(); }
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

    private string? _previewRdlcPath;
    /// <summary>Path to the temp RDLC file generated for Step 6 ReportViewer preview.</summary>
    public string? PreviewRdlcPath
    {
        get => _previewRdlcPath;
        set
        {
            _previewRdlcPath = value;
            OnPropertyChanged();
        }
    }

    private DynamicParameter? _selectedDynamicParameter;
    public DynamicParameter? SelectedDynamicParameter
    {
        get => _selectedDynamicParameter;
        set
        {
            _selectedDynamicParameter = value;
            OnPropertyChanged();
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

        // SORT FIX: Order the fields from Left to Right based on their X coordinate on the canvas
        var sortedFields = Fields.OrderBy(f => f.CanvasX).ToList();

        for (int i = 0; i < sortedFields.Count; i++)
        {
            sortedFields[i].DisplayOrder = i;
            Report.Fields.Add(sortedFields[i]);
        }
    }

    // ── MVVM Boilerplate ──────────────────────────────────────────────────
    public void SyncParametersToReport()
    {
        Report.Parameters.Clear();
        foreach (var parameter in DynamicParameters)
            Report.Parameters.Add(parameter.ToReportParameter());
    }

    public void SyncDynamicParametersToReport()
    {
        Report.DynamicParameters.Clear();
        for (int i = 0; i < DynamicParameters.Count; i++)
        {
            DynamicParameters[i].HeaderOrder = i;
            Report.DynamicParameters.Add(DynamicParameters[i]);
        }

        SyncParametersToReport();
    }

    public void AddParameter()
    {
        string baseName = "@Param";
        string uniqueName = baseName;
        int counter = 1;
        while (DynamicParameters.Any(p => p.ParameterName.Equals(uniqueName, StringComparison.OrdinalIgnoreCase)))
        {
            uniqueName = $"{baseName}{counter++}";
        }

        DynamicParameters.Add(new DynamicParameter
        {
            ParameterName = uniqueName,
            DataType = "varchar(50)",
            PromptText = "New Parameter"
        });

        SyncDynamicParametersToReport();
    }

    public void RemoveParameter(DynamicParameter? parameter)
    {
        if (parameter is null || !DynamicParameters.Contains(parameter))
            return;

        DynamicParameters.Remove(parameter);
        SyncDynamicParametersToReport();
    }

    public void SyncJoinsToReport()
    {
        Report.Joins.Clear();
        foreach (var join in ConfiguredJoins)
            Report.Joins.Add(join);
    }

    public void UpdateJoinBaseTable(string baseTable)
    {
        foreach (var join in ConfiguredJoins.Where(j => string.IsNullOrWhiteSpace(j.PrimaryTable)))
            join.PrimaryTable = baseTable;
    }

    public void ClearAvailableColumns()
    {
        AvailableColumns.Clear();
    }

    // ── Designer Helpers ──────────────────────────────────────────────────
    private void AddComponent(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;

        ReportComponent newComp = type.ToLower() switch
        {
            "text" => new TextComponent { X = 50, Y = 50, Text = "TextBlock Text" },
            "image" => new ImageComponent { X = 50, Y = 50 },
            "line" => new LineComponent { X = 50, Y = 50, Length = 150, Orientation = "Horizontal" },
            _ => throw new ArgumentException($"Unknown component type: {type}")
        };

        CanvasComponents.Add(newComp);
        SelectedComponent = newComp;
    }

    private void DeleteComponent()
    {
        if (SelectedComponent != null)
        {
            CanvasComponents.Remove(SelectedComponent);
            SelectedComponent = null;
        }
    }

    private async Task RunPreviewAsync()
    {
        if (IsPreviewRunning)
            return;

        PreviewError = string.Empty;
        IsPreviewRunning = true;
        IsBusy = true;

        string? previousRdlcPath = PreviewRdlcPath;

        try
        {
            SyncFieldsToReport();
            SyncDynamicParametersToReport();

            var (dataTable, rdlcPath) = await Task.Run(async () =>
            {
                var table = await _databaseService.ExecuteStoredProcedurePreviewAsync(
                    Report, Report.Parameters);

                string path = await ReportPreviewService.ScaffoldRdlcToTempAsync(Report);
                return (table, path);
            });

            PreviewData = dataTable;
            PreviewRdlcPath = rdlcPath;
            AppendLog($"Preview returned {PreviewData.Rows.Count} row(s) with RDLC at {rdlcPath}.");
            ReportPreviewService.TryDeleteTempFile(previousRdlcPath);
        }
        catch (Exception ex)
        {
            PreviewData = null;
            PreviewRdlcPath = null;
            PreviewError = ex.Message;
            AppendLog($"Preview failed: {ex.Message}");
        }
        finally
        {
            IsPreviewRunning = false;
            IsBusy = false;
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
