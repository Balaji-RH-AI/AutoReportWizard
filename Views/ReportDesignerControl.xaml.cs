using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutoReportWizard.Models;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views;

/// <summary>
/// Interaction logic for ReportDesignerControl.xaml.
/// Implements drag-and-drop, mouse dragging, resizing, and property synchronization.
/// </summary>
public partial class ReportDesignerControl : UserControl
{
    private bool _isDragging;
    private Point _dragStartMousePos;
    private double _dragStartComponentX;
    private double _dragStartComponentY;
    private ReportComponent? _draggedComponent;

    public ReportDesignerControl()
    {
        InitializeComponent();
        
        // FIX: Subscribe to the event here instead of trying to override a non-existent method
        this.DataContextChanged += ReportDesignerControl_DataContextChanged;
    }

    private void ReportDesignerControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WizardViewModel oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is WizardViewModel newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        }

        UpdatePropertyGridVisibility();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardViewModel.SelectedComponent))
        {
            UpdatePropertyGridVisibility();
        }
    }

    /// <summary>
    /// Synchronizes the visibility of properties panels and updates the Type Badge indicator.
    /// </summary>
    private void UpdatePropertyGridVisibility()
    {
        var vm = DataContext as WizardViewModel;
        if (vm != null && vm.SelectedComponent != null)
        {
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

    // ================= DRAG-AND-DROP TOOLBOX INITIATION =================

    private void ToolboxItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBoxItem item)
        {
            string componentType = item.Tag?.ToString() ?? "Text";
            DataObject dragData = new DataObject("ReportComponentType", componentType);
            DragDrop.DoDragDrop(item, dragData, DragDropEffects.Copy);
        }
    }

    // ================= INTERACTIVE CANVAS DROP EVENT HANDLERS =================

    private void Canvas_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ReportComponentType"))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ReportComponentType"))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
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
                "line" => new LineComponent 
                { 
                    X = Math.Round(dropPoint.X / 4.0) * 4.0, 
                    Y = Math.Round(dropPoint.Y / 4.0) * 4.0, 
                    Length = 150, 
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

    // ================= INTERACTIVE CANVAS MOUSE MOVE EVENT HANDLERS =================

    private void CanvasItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ReportComponent component)
        {
            // Set Selection in MVVM state
            if (DataContext is WizardViewModel vm)
            {
                vm.SelectedComponent = component;
            }

            _isDragging = true;
            _draggedComponent = component;
            _dragStartMousePos = e.GetPosition(DesignerCanvas);
            _dragStartComponentX = component.X;
            _dragStartComponentY = component.Y;

            fe.CaptureMouse();
            fe.Focus(); // Force keyboard focus to the clicked element so Delete/Arrows work
            
            // Hook move and release events dynamically
            fe.MouseMove += CanvasItem_MouseMove;
            fe.MouseUp += CanvasItem_MouseUp;

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

            // Clamp positions to positive canvas workspace boundaries
            _draggedComponent.X = Math.Max(0, targetX);
            _draggedComponent.Y = Math.Max(0, targetY);

            e.Handled = true;
        }
    }

    private void CanvasItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && sender is FrameworkElement fe)
        {
            fe.MouseMove -= CanvasItem_MouseMove;
            fe.MouseUp -= CanvasItem_MouseUp;

            fe.ReleaseMouseCapture();
            _isDragging = false;
            _draggedComponent = null;

            e.Handled = true;
        }
    }

    // ================= RESIZING EVENT HANDLING =================

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb thumb && thumb.DataContext is ReportComponent component)
        {
            double newWidth = component.Width + e.HorizontalChange;
            double newHeight = component.Height + e.VerticalChange;

            // Apply 4px grid snapping to resizing actions
            newWidth = Math.Round(newWidth / 4.0) * 4.0;
            newHeight = Math.Round(newHeight / 4.0) * 4.0;

            // Enforce minimum dimension boundaries to prevent collapse
            component.Width = Math.Max(12, newWidth);
            component.Height = Math.Max(12, newHeight);

            // Special handling for visual lines: length syncs with its current layout axis
            if (component is LineComponent line)
            {
                if (line.Orientation.Equals("Horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    line.Length = component.Width;
                    component.Height = 10; // Lock perpendicular thickness axis
                }
                else
                {
                    line.Length = component.Height;
                    component.Width = 10; // Lock perpendicular thickness axis
                }
            }

            e.Handled = true;
        }
    }

    // ================= SHORTCUT KEYBOARD INPUT EVENT HANDLERS =================

    private void Canvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is WizardViewModel vm && vm.SelectedComponent != null)
        {
            // 1. Handle Deletion
            if (e.Key == Key.Delete)
            {
                vm.DeleteComponentCommand.Execute(null);
                e.Handled = true;
            }
            // 2. Handle Nudging (Arrow Keys)
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

    // ================= EXPORT PRINT LAYOUT EVENT HANDLERS =================

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