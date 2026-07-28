using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutoReportWizard.Models;
using AutoReportWizard.Services;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views;

/// <summary>
/// Interaction logic for ReportDesignerControl.xaml.
/// Implements drag-and-drop, mouse dragging, resizing, property synchronization,
/// and (Phase 2) the integrated live RDLC preview via Microsoft ReportViewer.
/// </summary>
public partial class ReportDesignerControl : UserControl
{
    // ── Canvas drag state ────────────────────────────────────────────────────
    private bool _isDragging;
    private Point _dragStartMousePos;
    private double _dragStartComponentX;
    private double _dragStartComponentY;
    private ReportComponent? _draggedComponent;

    // ── Live Preview (ReportViewer) state ────────────────────────────────────
    /// <summary>
    /// The WinForms ReportViewer control embedded in the Live Preview tab.
    /// Created lazily on first load so WindowsFormsHost is ready.
    /// </summary>
    private Microsoft.Reporting.WinForms.ReportViewer? _reportViewer;

    /// <summary>Cached reference to the current ViewModel for property subscriptions.</summary>
    private WizardViewModel? _designerVm;

    // ── Constructor ──────────────────────────────────────────────────────────
    public ReportDesignerControl()
    {
        InitializeComponent();

        // Subscribe to DataContext changes to wire up ViewModel listeners
        this.DataContextChanged += ReportDesignerControl_DataContextChanged;

        // Initialize ReportViewer once the WPF visual tree (and WFH) is ready
        this.Loaded += ReportDesignerControl_Loaded;

        // Clean up WinForms resources when this control is unloaded
        this.Unloaded += ReportDesignerControl_Unloaded;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void ReportDesignerControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeReportViewer();
    }

    private void ReportDesignerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unhook ViewModel listener to prevent memory leaks
        if (_designerVm is not null)
            _designerVm.PropertyChanged -= ViewModel_PropertyChanged;

        // Explicitly dispose the WinForms viewer to free memory and prevent airspace leaks
        _reportViewer?.Dispose();
    }

    // ── DataContext wiring ───────────────────────────────────────────────────

    private void ReportDesignerControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from the old ViewModel
        if (e.OldValue is WizardViewModel oldVm)
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;

        // Subscribe to the new ViewModel
        _designerVm = e.NewValue as WizardViewModel;
        if (_designerVm is not null)
            _designerVm.PropertyChanged += ViewModel_PropertyChanged;

        UpdatePropertyGridVisibility();
    }

    private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ReportComponent component)
        {
            const int GRID_SNAP = 8;
            component.X = Math.Max(0, Math.Round((component.X + e.HorizontalChange) / GRID_SNAP) * GRID_SNAP);
            component.Y = Math.Max(0, Math.Round((component.Y + e.VerticalChange) / GRID_SNAP) * GRID_SNAP);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // ── Canvas designer ─────────────────────────────────────────────
            case nameof(WizardViewModel.SelectedComponent):
                UpdatePropertyGridVisibility();
                break;

            // ── Live Preview tab ────────────────────────────────────────────
            // When the ViewModel raises either PreviewData or PreviewRdlcStream,
            // dispatch to the UI thread and re-render the ReportViewer.
            case nameof(WizardViewModel.PreviewData):
            case nameof(WizardViewModel.PreviewRdlcStream):
                Dispatcher.BeginInvoke(TryRenderPreview);
                break;
        }
    }

    // ── Properties panel visibility ──────────────────────────────────────────

    /// <summary>
    /// Synchronizes the visibility of properties panels and updates the Type Badge indicator.
    /// </summary>
    private void UpdatePropertyGridVisibility()
    {
        var vm = DataContext as WizardViewModel;
        if (vm != null && vm.SelectedComponent != null)
        {
            // Auto-open the properties sidebar if an element is selected
            if (PropertiesToggle != null && PropertiesToggle.IsChecked != true)
            {
                PropertiesToggle.IsChecked = true;
            }

            NoSelectionPlaceholder.Visibility = Visibility.Collapsed;
            PropertiesForm.Visibility = Visibility.Visible;
            TypeBadgeBorder.Visibility = Visibility.Visible;
            FooterKeyboardGuide.Visibility = Visibility.Visible;

            string typeName = vm.SelectedComponent.GetType().Name;
            TypeBadgeText.Text = typeName.Replace("Component", "").ToUpper() + " ELEMENT";
        }
        else
        {
            NoSelectionPlaceholder.Visibility = Visibility.Visible;
            PropertiesForm.Visibility = Visibility.Collapsed;
            TypeBadgeBorder.Visibility = Visibility.Collapsed;
            FooterKeyboardGuide.Visibility = Visibility.Collapsed;
        }
    }

    // ── Live Preview: ReportViewer initialization & rendering ─────────────────

    /// <summary>
    /// Creates the Microsoft ReportViewer control and assigns it to the
    /// <c>ReportHost</c> WindowsFormsHost in the "▶ Live Preview" tab.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    private void InitializeReportViewer()
    {
        if (_reportViewer is not null)
            return;

        _reportViewer = new Microsoft.Reporting.WinForms.ReportViewer
        {
            Dock                   = System.Windows.Forms.DockStyle.Fill,
            ShowExportButton       = false,
            ShowPrintButton        = false,
            ShowRefreshButton      = false,
            ShowZoomControl        = true,
            ShowFindControls       = false,
            ShowPageNavigationControls = true,
            ShowBackButton         = false,
            ShowDocumentMapButton  = false,
            ShowParameterPrompts   = false,
            ShowPromptAreaButton   = false,
            ProcessingMode         = Microsoft.Reporting.WinForms.ProcessingMode.Local
        };

        // Assign to the WindowsFormsHost declared in the XAML "▶ Live Preview" tab
        ReportHost.Child = _reportViewer;
    }

    /// <summary>
    /// Renders the RDLC report inside the ReportViewer using the latest
    /// <see cref="WizardViewModel.PreviewData"/> and <see cref="WizardViewModel.PreviewRdlcStream"/>.
    /// No-ops if either value is missing, preventing partial-render crashes.
    /// The stream is deliberately not disposed here — it is owned by the ViewModel and
    /// will be disposed when the next render cycle replaces it via the property setter.
    /// </summary>
    private void TryRenderPreview()
    {
        // Guard: viewer and VM must both be ready
        if (_reportViewer is null || _designerVm is null)
            return;

        // Guard: both data and stream must be populated before rendering
        if (_designerVm.PreviewData is null ||
            _designerVm.PreviewRdlcStream is null)
            return;

        try
        {
            ReportPreviewService.RenderLocalReportFromStream(
                _reportViewer,
                _designerVm.PreviewRdlcStream,
                _designerVm.PreviewData,
                _designerVm.DynamicParameters);

            // Ensure the host is visible now that content is loaded
            ReportHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            // Surface render errors back to the ViewModel so the UI can display them
            _designerVm.PreviewError = $"Report render failed: {ex.Message}";
        }
    }

    // ── Drag-and-drop toolbox initiation ─────────────────────────────────────

    private void ToolboxItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBoxItem item)
        {
            string componentType = item.Tag?.ToString() ?? "Text";
            DataObject dragData = new DataObject("ReportComponentType", componentType);
            DragDrop.DoDragDrop(item, dragData, DragDropEffects.Copy);
        }
    }

    /// <summary>
    /// Handles drag initiation from the Data Source tree (Parameters and Fields).
    /// Creates appropriate drag data payloads for canvas drop handling.
    /// </summary>
    private void DataSourceItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
        {
            string itemType = element.Tag?.ToString() ?? "Text";
            object? dataContext = element.DataContext;

            if (itemType == "Parameter" && dataContext is DynamicParameter param)
            {
                // Drag parameter - will create a TextBlock with parameter expression
                DataObject dragData = new DataObject();
                dragData.SetData("ReportComponentType", "ParameterText");
                dragData.SetData("ParameterData", param);
                DragDrop.DoDragDrop(element, dragData, DragDropEffects.Copy);
            }
            else if (itemType == "Field" && dataContext is ReportField field)
            {
                // Drag field - will create a TabularColumn bound to this field
                DataObject dragData = new DataObject();
                dragData.SetData("ReportComponentType", "FieldColumn");
                dragData.SetData("FieldData", field);
                DragDrop.DoDragDrop(element, dragData, DragDropEffects.Copy);
            }
        }
    }

    // ── Interactive canvas drop event handlers ────────────────────────────────

    private void Canvas_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("ReportComponentType")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("ReportComponentType")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Canvas_Drop(object sender, DragEventArgs e)
    {
        var vm = DataContext as WizardViewModel;
        if (vm != null && e.Data.GetDataPresent("ReportComponentType"))
        {
            string type = e.Data.GetData("ReportComponentType") as string ?? "Text";

            // Resolve correct drop coordinates relative to the DesignerCanvas layout
            Point dropPoint = e.GetPosition(DesignerCanvas);

            // Create appropriate strongly-typed model based on selection tag
            ReportComponent? newComp = type.ToLower() switch
            {
                "column" => new TabularColumnComponent
                {
                    X = Math.Round(dropPoint.X / 4.0) * 4.0,
                    Y = Math.Round(dropPoint.Y / 4.0) * 4.0
                },
                "fieldcolumn" => CreateFieldColumn(e, dropPoint),
                "parametertext" => CreateParameterText(e, dropPoint),
                "text" => new TextComponent
                {
                    X = Math.Round(dropPoint.X / 4.0) * 4.0,
                    Y = Math.Round(dropPoint.Y / 4.0) * 4.0,
                    Text = "TextBlock Text"
                },
                "image" => new ImageComponent
                {
                    X = Math.Round(dropPoint.X / 4.0) * 4.0,
                    Y = Math.Round(dropPoint.Y / 4.0) * 4.0
                },
                "line"  => new LineComponent
                {
                    X           = Math.Round(dropPoint.X / 4.0) * 4.0,
                    Y           = Math.Round(dropPoint.Y / 4.0) * 4.0,
                    Length      = 150,
                    Orientation = "Horizontal"
                },
                _ => null
            };

            if (newComp != null)
            {
                vm.CanvasComponents.Add(newComp);
                vm.SelectedComponent = newComp;
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// Creates a TabularColumnComponent from a dragged field with automatic binding.
    /// </summary>
    private TabularColumnComponent? CreateFieldColumn(DragEventArgs e, Point dropPoint)
    {
        if (e.Data.GetDataPresent("FieldData") && e.Data.GetData("FieldData") is ReportField field)
        {
            return new TabularColumnComponent
            {
                X = Math.Round(dropPoint.X / 4.0) * 4.0,
                Y = Math.Round(dropPoint.Y / 4.0) * 4.0,
                HeaderString = field.Name,
                BoundField = field.Name,
                Width = 120,
                Height = 200
            };
        }
        return null;
    }

    /// <summary>
    /// Creates a TextComponent with parameter expression from a dragged parameter.
    /// </summary>
    private TextComponent? CreateParameterText(DragEventArgs e, Point dropPoint)
    {
        if (e.Data.GetDataPresent("ParameterData") && e.Data.GetData("ParameterData") is ReportParameter param)
        {
            string paramName = param.Name.TrimStart('@');
            return new TextComponent
            {
                X = Math.Round(dropPoint.X / 4.0) * 4.0,
                Y = Math.Round(dropPoint.Y / 4.0) * 4.0,
                Text = $"=Parameters!{paramName}.Value",
                Width = 150,
                Height = 25
            };
        }
        return null;
    }

    // ── Interactive canvas mouse move event handlers ──────────────────────────

    private void CanvasItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ReportComponent component)
        {
            // Set selection in MVVM state
            if (DataContext is WizardViewModel vm)
                vm.SelectedComponent = component;

            _isDragging = true;
            _draggedComponent  = component;
            _dragStartMousePos = e.GetPosition(DesignerCanvas);
            _dragStartComponentX = component.X;
            _dragStartComponentY = component.Y;

            fe.CaptureMouse();
            fe.Focus(); // Force keyboard focus so Delete/Arrows work

            // Hook move and release events dynamically
            fe.MouseMove += CanvasItem_MouseMove;
            fe.MouseUp   += CanvasItem_MouseUp;

            e.Handled = true;
        }
    }

    private void CanvasItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && _draggedComponent != null && sender is FrameworkElement fe)
        {
            Point currentMousePos = e.GetPosition(DesignerCanvas);
            double deltaX = currentMousePos.X - _dragStartMousePos.X;
            double deltaY = currentMousePos.Y - _dragStartMousePos.Y;

            double targetX = _dragStartComponentX + deltaX;
            double targetY = _dragStartComponentY + deltaY;

            // Apply 4px grid snapping for precise visual layout alignment
            targetX = Math.Round(targetX / 4.0) * 4.0;
            targetY = Math.Round(targetY / 4.0) * 4.0;

            // BOUNDING BOX MATH: A4 Sheet width is roughly 794px, height is 1123px
            double maxRight = 794 - _draggedComponent.Width;
            double maxBottom = 1123 - _draggedComponent.Height;

            // Clamp positions so they cannot exceed canvas boundaries
            _draggedComponent.X = Math.Clamp(targetX, 0, maxRight);
            _draggedComponent.Y = Math.Clamp(targetY, 0, maxBottom);

            e.Handled = true;
        }
    }

    private void CanvasItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && sender is FrameworkElement fe)
        {
            fe.MouseMove -= CanvasItem_MouseMove;
            fe.MouseUp   -= CanvasItem_MouseUp;

            fe.ReleaseMouseCapture();
            _isDragging       = false;
            _draggedComponent = null;

            e.Handled = true;
        }
    }

    // ── Resizing event handling ───────────────────────────────────────────────

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb thumb && thumb.DataContext is ReportComponent component)
        {
            double newWidth  = component.Width  + e.HorizontalChange;
            double newHeight = component.Height + e.VerticalChange;

            newWidth  = Math.Round(newWidth  / 4.0) * 4.0;
            newHeight = Math.Round(newHeight / 4.0) * 4.0;

            // BOUNDING BOX MATH: Prevent resizing beyond the right/bottom canvas edges
            double maxWidth = 794 - component.X;
            double maxHeight = 1123 - component.Y;

            component.Width  = Math.Clamp(newWidth, 12, maxWidth);
            component.Height = Math.Clamp(newHeight, 12, maxHeight);

            // Special handling for visual lines
            if (component is LineComponent line)
            {
                if (line.Orientation.Equals("Horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    line.Length      = component.Width;
                    component.Height = 10;
                }
                else
                {
                    line.Length     = component.Height;
                    component.Width = 10; 
                }
            }

            e.Handled = true;
        }
    }

    // ── Shortcut keyboard input event handlers ────────────────────────────────

    private void Canvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is WizardViewModel vm && vm.SelectedComponent != null)
        {
            // 1. Handle deletion
            if (e.Key == Key.Delete)
            {
                vm.DeleteComponentCommand.Execute(null);
                e.Handled = true;
            }
            // 2. Handle nudging (arrow keys, 4px steps)
            else if (e.Key == Key.Left)
            {
                vm.SelectedComponent.X = Math.Max(0, vm.SelectedComponent.X - 4);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                vm.SelectedComponent.X += 4;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                vm.SelectedComponent.Y = Math.Max(0, vm.SelectedComponent.Y - 4);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                vm.SelectedComponent.Y += 4;
                e.Handled = true;
            }
        }
    }

    // ── Export / print layout event handlers ─────────────────────────────────

    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        PrintDialog printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            // Print the entire UserControl or specific sub-grid
            printDialog.PrintVisual(this, "AutoReport Wizard print layout job");
        }
    }
}