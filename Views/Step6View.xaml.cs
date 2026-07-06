using System.ComponentModel;
using System.Windows;
using AutoReportWizard.Services;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views;

public partial class Step6View : System.Windows.Controls.UserControl
{
    private Microsoft.Reporting.WinForms.ReportViewer? _reportViewer;
    private WizardViewModel? _viewModel;

    public Step6View()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeReportViewer();
        TryRenderPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as WizardViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WizardViewModel.PreviewData)
            or nameof(WizardViewModel.PreviewRdlcPath))
        {
            Dispatcher.BeginInvoke(TryRenderPreview);
        }
    }

    private void InitializeReportViewer()
    {
        if (_reportViewer is not null)
            return;

        _reportViewer = new Microsoft.Reporting.WinForms.ReportViewer
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            ShowExportButton = false,
            ShowPrintButton = false,
            ShowRefreshButton = false,
            ShowZoomControl = true,
            ShowFindControls = false,
            ShowPageNavigationControls = true,
            ShowBackButton = false,
            ShowDocumentMapButton = false,
            ShowParameterPrompts = false,
            ShowPromptAreaButton = false,
            ProcessingMode = Microsoft.Reporting.WinForms.ProcessingMode.Local
        };

        ReportHost.Child = _reportViewer;
    }

    private void TryRenderPreview()
    {
        if (_reportViewer is null || _viewModel is null)
            return;

        if (_viewModel.PreviewData is null ||
            string.IsNullOrWhiteSpace(_viewModel.PreviewRdlcPath))
            return;

        try
        {
            ReportPreviewService.RenderLocalReport(
                _reportViewer,
                _viewModel.PreviewRdlcPath,
                _viewModel.PreviewData,
                _viewModel.DynamicParameters);

            ReportHost.Visibility = System.Windows.Visibility.Visible;
        }
        catch (Exception ex)
        {
            _viewModel.PreviewError = $"Report render failed: {ex.Message}";
        }
    }
}
