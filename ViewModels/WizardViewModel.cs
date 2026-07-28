using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.IO;
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
    private System.Threading.CancellationTokenSource? _currentCts;

    private System.Threading.CancellationToken RefreshCancellationToken()
    {
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = new System.Threading.CancellationTokenSource();
        return _currentCts.Token;
    }

    // ── Core State ────────────────────────────────────────────────────────
    private int _currentStep = 1;
    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            if (_currentStep == value) return;
            _currentStep = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep4));
            OnPropertyChanged(nameof(IsStep5));
            OnPropertyChanged(nameof(IsStep6));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    // Step 3 intentionally skipped/removed
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;
    public bool IsStep6 => CurrentStep == 6;

    public ReportDefinition Report { get; } = new ReportDefinition();

    /// <summary>Observable list of fields — bound to all step views.</summary>
    public ObservableCollection<ReportField> Fields { get; } = new();

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
    public static IReadOnlyList<HeaderZone> HeaderZones { get; } = Enum.GetValues<HeaderZone>().ToList();

    // ── Step 5 Designer Dropdown Lists ───────────────────────────────

    public static IReadOnlyList<AggregateFunction> AggregateFunctions { get; } = Enum.GetValues<AggregateFunction>().ToList();
    public static IReadOnlyList<string> TextAlignOptions { get; } = new List<string> { "Default", "Left", "Center", "Right" };
    public static IReadOnlyList<string> FontWeightOptions { get; } = new List<string> { "Normal", "Bold" };

    // ──────────────────────────────────────────────────────────────────────

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
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

    public ICommand NextStepCommand { get; }
    public ICommand PreviousStepCommand { get; }
    public ICommand GenerateReportCommand { get; }
    public ICommand ChangeConnectionCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand LoadDatabasesCommand { get; }
    
    private readonly SqlGeneratorService _sqlGen = new();
    private readonly SqlVerificationService _sqlVerify = new();
    private readonly RdlcValidationService _rdlcVal = new();

    public WizardViewModel()
    {
        foreach (var parameter in Report.DynamicParameters)
            DynamicParameters.Add(parameter);

        DynamicParameters.CollectionChanged += (_, _) => SyncDynamicParametersToReport();

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

        // 🔥 STEP NAVIGATION: Skip Step 3 logic applied here
        NextStepCommand = new RelayCommand(
            _ => {
                if (CurrentStep == 2) CurrentStep = 4;
                else if (CurrentStep < 6) CurrentStep++;
            },
            _ => CurrentStep < 6 && !IsBusy);

        PreviousStepCommand = new RelayCommand(
            _ => {
                if (CurrentStep == 4) CurrentStep = 2;
                else if (CurrentStep > 1) CurrentStep--;
            },
            _ => CurrentStep > 1 && !IsBusy);

        GenerateReportCommand = new RelayCommand(
            async _ => await GenerateReportAsync(),
            _ => !IsGenerating && !IsBusy && !string.IsNullOrWhiteSpace(ReportName));

        ChangeConnectionCommand = new RelayCommand(
            _ => { CurrentStep = 1; },
            _ => !IsBusy);

        FinishCommand = new RelayCommand(
            _ => { System.Windows.Application.Current.Shutdown(); },
            _ => !IsBusy);
            
        LoadDatabasesCommand = new RelayCommand(
            async _ => await LoadDatabaseOptionsAsync(),
            _ => !IsBusy);
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


    // ── Step 2 Bindings (Stored Procedure Selection) ──────────────────────────────
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

    public string StoredProcedureName
    {
        get => Report.StoredProcedureName;
        set { Report.StoredProcedureName = value; OnPropertyChanged(); }
    }

    // ── Cascading Dropdown Collections & Caching ──────────────────────────
    private readonly Dictionary<string, List<string>> _schemaCache = new();
    private readonly Dictionary<string, List<string>> _spCache = new();

    public ObservableCollection<string> AvailableDatabases { get; } = new();
    public ObservableCollection<string> AvailableSchemas { get; } = new();
    public ObservableCollection<string> AvailableStoredProcedures { get; } = new();

    // ── Step 2 Search Properties ──────────────────────────────────────────
    private string _databaseSearchText = string.Empty;
    public string DatabaseSearchText
    {
        get => _databaseSearchText;
        set
        {
            _databaseSearchText = value;
            OnPropertyChanged();
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableDatabases);
            if (view != null) view.Filter = item => string.IsNullOrWhiteSpace(value) || (item?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private string _schemaSearchText = string.Empty;
    public string SchemaSearchText
    {
        get => _schemaSearchText;
        set
        {
            _schemaSearchText = value;
            OnPropertyChanged();
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableSchemas);
            if (view != null) view.Filter = item => string.IsNullOrWhiteSpace(value) || (item?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private string _spSearchText = string.Empty;
    public string SpSearchText
    {
        get => _spSearchText;
        set
        {
            _spSearchText = value;
            OnPropertyChanged();
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AvailableStoredProcedures);
            if (view != null) view.Filter = item => string.IsNullOrWhiteSpace(value) || (item?.ToString()?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private string _selectedDatabase = string.Empty;
    public string SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (_selectedDatabase == value) return;
            _selectedDatabase = value;
            Report.DatabaseName = value;
            OnPropertyChanged();

            AvailableSchemas.Clear();
            AvailableStoredProcedures.Clear();
            SelectedSchema = string.Empty;
            SelectedStoredProcedure = string.Empty;

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
            if (_selectedSchema == value) return;
            _selectedSchema = value;
            Report.SchemaName = value;
            OnPropertyChanged();

            AvailableStoredProcedures.Clear();
            SelectedStoredProcedure = string.Empty;

            if (!string.IsNullOrWhiteSpace(value))
                _ = LoadStoredProceduresAsync();
        }
    }

    private string _selectedStoredProcedure = string.Empty;
    public string SelectedStoredProcedure
    {
        get => _selectedStoredProcedure;
        set
        {
            if (_selectedStoredProcedure == value) return;
            _selectedStoredProcedure = value;
            Report.StoredProcedureName = value;
            OnPropertyChanged();

            if (!string.IsNullOrWhiteSpace(value))
                _ = LoadStoredProcedureMetadataAsync();
        }
    }

    public string StoredProcName => Report.StoredProcName;

    // ── Async Data Loading Methods ────────────────────────────────────────

    public async Task LoadDatabaseOptionsAsync()
    {
        var ct = RefreshCancellationToken();
        AvailableDatabases.Clear();
        AvailableSchemas.Clear();
        AvailableStoredProcedures.Clear();
        _schemaCache.Clear();
        _spCache.Clear();
        IsBusy = true;
        DiscoveryError = string.Empty;

        try
        {
            string originalDb = Report.DatabaseName;
            if (string.IsNullOrWhiteSpace(Report.DatabaseName))
                Report.DatabaseName = "master";

            var dbs = await Task.Run(async () => await _databaseService.GetDatabasesAsync(Report, ct), ct);
            Report.DatabaseName = originalDb;

            foreach (var db in dbs)
                AvailableDatabases.Add(db);

            if (!string.IsNullOrWhiteSpace(DatabaseName) && AvailableDatabases.Contains(DatabaseName))
                SelectedDatabase = DatabaseName;
            else if (AvailableDatabases.Count > 0)
                SelectedDatabase = AvailableDatabases[0];
        }
        catch (Exception ex)
        {
            DiscoveryError = $"Failed to load databases: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadStoredProcedureMetadataAsync()
    {
        var ct = RefreshCancellationToken();
        IsBusy = true;
        DiscoveryError = string.Empty;

        try
        {
            // Step 1: Extract output fields
            var outputFields = await _databaseService.GetStoredProcedureOutputFieldsAsync(Report, ct);
            
            Report.OutputFields.Clear();
            Fields.Clear();

            foreach (var field in outputFields)
            {
                field.SourceDatabase = SelectedDatabase;
                field.SourceSchema = SelectedSchema;
                field.SourceTable = SelectedStoredProcedure;
                
                Report.OutputFields.Add(field);
                Fields.Add(field);
            }

            // Step 2: Extract input parameters
            var spParameters = await _databaseService.GetStoredProcedureParametersAsync(Report, ct);
            
            Report.ProcedureParameters.Clear();
            DynamicParameters.Clear();

            foreach (var param in spParameters)
            {
                Report.ProcedureParameters.Add(param);
                
                var dynParam = new DynamicParameter
                {
                    ParameterName = param.Name,
                    DataType = param.SqlDataType,
                    PromptText = param.Name.TrimStart('@'),
                    Value = string.Empty,
                    MapsToHeader = false
                };
                DynamicParameters.Add(dynParam);
            }

            SyncDynamicParametersToReport();

            if (string.IsNullOrWhiteSpace(ReportName))
            {
                ReportName = Report.StoredProcedureName.Replace("sp_", "").Replace("_", " ");
            }

            CanvasComponents.Clear();
            InitializeCanvasFromConfig();

            AppendLog($"Loaded SP metadata: {outputFields.Count} output fields, {spParameters.Count} input parameters");
        }
        catch (Exception ex)
        {
            DiscoveryError = $"SP Metadata Load Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadSchemasAsync()
    {
        AvailableSchemas.Clear();
        AvailableStoredProcedures.Clear();

        if (string.IsNullOrWhiteSpace(SelectedDatabase)) return;

        var ct = RefreshCancellationToken();

        try
        {
            if (_schemaCache.TryGetValue(SelectedDatabase, out var cachedSchemas))
            {
                foreach (var schema in cachedSchemas) AvailableSchemas.Add(schema);
                return;
            }

            IsBusy = true;
            var schemas = await _databaseService.GetSchemasAsync(Report, ct);
            var fetchedSchemas = schemas.ToList();
            _schemaCache[SelectedDatabase] = fetchedSchemas;

            foreach (var schema in fetchedSchemas) AvailableSchemas.Add(schema);
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

    public async Task LoadStoredProceduresAsync()
    {
        AvailableStoredProcedures.Clear();

        if (string.IsNullOrWhiteSpace(SelectedSchema)) return;

        string cacheKey = $"{SelectedDatabase}.{SelectedSchema}";
        var ct = RefreshCancellationToken();

        try
        {
            if (_spCache.TryGetValue(cacheKey, out var cachedSPs))
            {
                foreach (var sp in cachedSPs) AvailableStoredProcedures.Add(sp);
                return;
            }

            IsBusy = true;
            var sps = await _databaseService.GetStoredProceduresAsync(Report, SelectedSchema, ct);
            var fetchedSPs = sps.ToList();
            _spCache[cacheKey] = fetchedSPs;

            foreach (var sp in fetchedSPs) AvailableStoredProcedures.Add(sp);
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

    private Stream? _previewRdlcStream;
    public Stream? PreviewRdlcStream
    {
        get => _previewRdlcStream;
        set
        {
            if (ReferenceEquals(_previewRdlcStream, value)) return;

            try { _previewRdlcStream?.Dispose(); }
            catch { }

            _previewRdlcStream = value;
            OnPropertyChanged();
        }
    }

    private DynamicParameter? _selectedDynamicParameter;
    public DynamicParameter? SelectedDynamicParameter
    {
        get => _selectedDynamicParameter;
        set { _selectedDynamicParameter = value; OnPropertyChanged(); }
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

    public void SyncFieldsToReport()
    {
        Report.Fields.Clear();
        var sortedFields = Fields.OrderBy(f => f.CanvasX).ToList();
        for (int i = 0; i < sortedFields.Count; i++)
        {
            sortedFields[i].DisplayOrder = i;
            Report.Fields.Add(sortedFields[i]);
        }
    }

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
        if (parameter is null || !DynamicParameters.Contains(parameter)) return;
        DynamicParameters.Remove(parameter);
        SyncDynamicParametersToReport();
    }

    // ── Designer Helpers ──────────────────────────────────────────────────
    private void AddComponent(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;

        ReportComponent newComp = type.ToLower() switch
        {
            "column" => new TabularColumnComponent { X = 50, Y = 200 },
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
    
    // ── Designer Initialization ───────────────────────────────────────────
    public void InitializeCanvasFromConfig()
    {
        if (CanvasComponents.Count > 0) return;

        // 1. LEFT ZONE (Site, Process, Julian)
        double currentLeftY = 24;
        if (!string.IsNullOrWhiteSpace(HeaderSiteField)) 
        { 
            CanvasComponents.Add(new TextComponent { Text = $"Site : {HeaderSiteField}", X = 24, Y = currentLeftY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Left" }); 
            currentLeftY += 20; 
        }
        if (!string.IsNullOrWhiteSpace(HeaderProcessDateField)) 
        { 
            CanvasComponents.Add(new TextComponent { Text = $"Process : {HeaderProcessDateField}", X = 24, Y = currentLeftY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Left" }); 
            currentLeftY += 20; 
        }
        if (!string.IsNullOrWhiteSpace(HeaderJulianField)) 
        { 
            CanvasComponents.Add(new TextComponent { Text = $"Julian : {HeaderJulianField}", X = 24, Y = currentLeftY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Left" }); 
        }

        // 2. CENTER ZONE (Title, Subtitles)
        double currentCenterY = 24;
        string displayTitle = !string.IsNullOrWhiteSpace(ReportTitle) ? ReportTitle : (ReportName ?? "DYNAMIC REPORT");
        CanvasComponents.Add(new TextComponent { Text = displayTitle.ToUpper(), X = 224, Y = currentCenterY, Width = 350, Height = 24, FontSize = 14, FontWeight = "Bold", TextAlign = "Center" });
        currentCenterY += 24;

        if (!string.IsNullOrWhiteSpace(ReportSubtitle))
        {
            CanvasComponents.Add(new TextComponent { Text = ReportSubtitle, X = 224, Y = currentCenterY, Width = 350, Height = 20, FontSize = 10, FontWeight = "Bold", TextAlign = "Center" });
            currentCenterY += 20;
        }
        if (!string.IsNullOrWhiteSpace(StaticHeaderLeftLine1))
        {
            CanvasComponents.Add(new TextComponent { Text = StaticHeaderLeftLine1, X = 224, Y = currentCenterY, Width = 350, Height = 20, FontSize = 10, FontWeight = "Bold", TextAlign = "Center" });
        }

        // 3. RIGHT ZONE (Worksource, Load, Page Number)
        double currentRightY = 24;
        if (!string.IsNullOrWhiteSpace(HeaderWorksourceField)) 
        { 
            CanvasComponents.Add(new TextComponent { Text = $"Worksource : {HeaderWorksourceField}", X = 570, Y = currentRightY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Right" }); 
            currentRightY += 20; 
        }
        if (!string.IsNullOrWhiteSpace(HeaderLoadField)) 
        { 
            CanvasComponents.Add(new TextComponent { Text = $"Load : {HeaderLoadField}", X = 570, Y = currentRightY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Right" }); 
            currentRightY += 20; 
        }
        if (IncludePageNumbers)
        {
            CanvasComponents.Add(new TextComponent { Text = "Page : [=Globals!PageNumber] / [=Globals!TotalPages]", X = 570, Y = currentRightY, Width = 200, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Right" });
        }

        CanvasComponents.Add(new LineComponent { X = 24, Y = 96, Length = 746, Orientation = "Horizontal" });

        InjectCanvasColumns();

        CanvasComponents.Add(new LineComponent { X = 24, Y = 1050, Length = 746, Orientation = "Horizontal" });

        if (IncludeExecutionTime) 
        {
            CanvasComponents.Add(new TextComponent { Text = "[=Globals!ExecutionTime]", X = 24, Y = 1060, Width = 250, Height = 20, FontSize = 9, FontWeight = "Bold", TextAlign = "Left" });
        }
    }

    private void InjectCanvasColumns()
    {
        if (CanvasComponents.Any(c => c is TabularColumnComponent)) return;

        if (!Fields.Any()) return;

        double totalAvailableWidth = 746.0;
        double colWidth = Math.Max(40, totalAvailableWidth / Fields.Count);

        double currentColumnX = 24;
        foreach (var field in Fields)
        {
            if (currentColumnX + colWidth > 770) break;
            
            CanvasComponents.Add(new TabularColumnComponent
            {
                HeaderString = string.IsNullOrWhiteSpace(field.CustomHeaderLabel) ? field.Name : (field.CustomHeaderLabel ?? string.Empty),
                BoundField = field.Name,
                X = currentColumnX,
                Y = 120,
                Width = colWidth,
                Height = 40
            });
            
            currentColumnX += colWidth;
        }
    }
    
    private async Task RunPreviewAsync()
    {
        if (IsPreviewRunning) return;

        var ct = RefreshCancellationToken();
        PreviewError = string.Empty;
        IsPreviewRunning = true;
        IsBusy = true;

        try
        {
            SyncFieldsToReport();
            SyncDynamicParametersToReport();

            var dataTable = await Task.Run(() => _databaseService.ExecuteStoredProcedurePreviewAsync(Report, Report.Parameters, ct), ct);

            if (string.IsNullOrWhiteSpace(dataTable.TableName) || dataTable.TableName == "Table")
                dataTable.TableName = "DataSet1";

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (Fields.Count == 0 && dataTable.Columns.Count > 0)
                {
                    int order = 0;
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        var field = new ReportField
                        {
                            Name = col.ColumnName,
                            SqlDataType = col.DataType.Name,
                            DotNetType = col.DataType.FullName,
                            IsDetailField = true,
                            DisplayOrder = order++
                        };
                        Report.OutputFields.Add(field);
                        Report.Fields.Add(field);
                        Fields.Add(field);
                    }
                }

                if (CanvasComponents.Count == 0) InitializeCanvasFromConfig();
                InjectCanvasColumns();
                Report.CanvasItems = CanvasComponents.ToList();
            });

            MemoryStream rdlcStream = await Task.Run(() => ReportPreviewService.SerializeToStreamAsync(Report, ct), ct);

            PreviewData = dataTable;
            PreviewRdlcStream = rdlcStream;
            AppendLog($"Preview returned {PreviewData.Rows.Count} row(s). RDLC serialized in-memory ({rdlcStream.Length:N0} bytes).");
        }
        catch (Exception ex)
        {
            PreviewData = null;
            PreviewRdlcStream = null;
            
            string errorMessage = ex.Message;
            if (ex.InnerException != null)
            {
                errorMessage += "\nDetails: " + ex.InnerException.Message;
                if (ex.InnerException.InnerException != null)
                    errorMessage += "\n" + ex.InnerException.InnerException.Message;
            }
            
            PreviewError = errorMessage;
            AppendLog($"Preview failed: {errorMessage}");
        }
        finally
        {
            IsPreviewRunning = false;
            IsBusy = false;
        }
    }

    private async Task GenerateReportAsync()
    {
        SyncFieldsToReport();
        SyncParametersToReport();

        if (string.IsNullOrWhiteSpace(ReportName))
        {
            AppendLog("❌ Aborted — Report Name is required.");
            return;
        }

        IsGenerating = true;
        IsBusy = true;
        GenerationLog = string.Empty;

        try
        {
            Directory.CreateDirectory(OutputDirectory);

            AppendLog("── Phase A: T-SQL Generation ──────────────────");
            string generatedSql = await Task.Run(() => _sqlGen.Generate(Report));
            AppendLog("  ✔ T-SQL script generated successfully.");

            AppendLog("  Verifying syntax via SET PARSEONLY ON…");
            var verifyResult = await _sqlVerify.VerifyAsync(Report, generatedSql);

            if (!verifyResult.IsValid) AppendLog($"  ⚠️  SQL Verification Warning (Line {verifyResult.ErrorLine}): {verifyResult.ErrorMessage}");
            else AppendLog("  ✔ Syntax verified.");

            string sqlPath = Path.Combine(OutputDirectory, $"{Report.StoredProcName}.sql");
            await File.WriteAllTextAsync(sqlPath, generatedSql);
            AppendLog($"  📄 Saved: {sqlPath}\n");

            AppendLog("── Phase B: RDLC XML Generation ───────────────");
            var rdlcDoc = await Task.Run(() => RdlcXmlEngine.GenerateRdlcXml(Report));
            AppendLog("  ✔ XDocument built successfully.");

            string rdlcPath = Path.Combine(OutputDirectory, $"{Report.ReportName}.rdlc");
            await Task.Run(() => rdlcDoc.Save(rdlcPath));
            AppendLog($"  📄 Saved: {rdlcPath}\n");

            AppendLog("══ Generation Complete ══════════════════════════");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ FATAL ERROR: {ex.Message}");
            TelemetryService.RecordFailure(null, ex, Report.ReportName);
        }
        finally
        {
            IsGenerating = false;
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