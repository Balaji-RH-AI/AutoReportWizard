using System.Windows;
using System.Windows.Controls;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step5View : UserControl
    {
        public Step5View()
        {
            InitializeComponent();

            // Trigger the canvas hydration when this view appears on screen
            this.Loaded += Step5View_Loaded;
        }

        private void Step5View_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is WizardViewModel vm)
            {
                vm.InitializeCanvasFromConfig();
            }
        }
    }
}