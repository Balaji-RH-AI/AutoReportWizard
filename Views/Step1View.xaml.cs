using System.Windows;
using System.Windows.Controls;

using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step1View : UserControl
    {
        public Step1View()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Captures securely masked passwords as changes occur and writes them to the model context.
        /// </summary>
        private void UserPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is WizardViewModel viewModel && sender is PasswordBox passwordBox)
            {
                viewModel.Password = passwordBox.Password;
            }
        }
    }
}