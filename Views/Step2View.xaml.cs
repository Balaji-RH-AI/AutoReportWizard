using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AutoReportWizard.Views;

public sealed partial class Step2View : UserControl
{
    public Step2View()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the associated dropdown popup when an item is selected.
    /// This is purely a UI interaction and intentionally remains in the view.
    /// </summary>
    /// <param name="sender">The ListBox raising the event.</param>
    /// <param name="e">Selection change event arguments.</param>
    private void DropdownList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);

        if (sender is not ListBox listBox)
            return;

        if (listBox.Tag is not ToggleButton toggleButton)
            return;

        if (e.AddedItems.Count == 0)
            return;

        toggleButton.IsChecked = false;
    }
}