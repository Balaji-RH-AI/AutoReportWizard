using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutoReportWizard.Models;

namespace AutoReportWizard.Views
{
    public partial class DesignerFieldItem : UserControl
    {
        private const int GRID_SNAP = 8;

        public DesignerFieldItem() => InitializeComponent();

        private void MoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is ReportField field)
            {
                field.CanvasX = Math.Max(0, Math.Round((field.CanvasX + e.HorizontalChange) / GRID_SNAP) * GRID_SNAP);
                field.CanvasY = Math.Max(0, Math.Round((field.CanvasY + e.VerticalChange) / GRID_SNAP) * GRID_SNAP);
            }
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is ReportField field)
            {
                field.ItemWidth = Math.Max(24, Math.Round((field.ItemWidth + e.HorizontalChange) / GRID_SNAP) * GRID_SNAP);
                field.ItemHeight = Math.Max(16, Math.Round((field.ItemHeight + e.VerticalChange) / GRID_SNAP) * GRID_SNAP);
            }
        }
    }
}