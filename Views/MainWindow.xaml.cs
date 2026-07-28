using System;
using System.Windows;
using System.Windows.Threading;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class MainWindow : Window
    {
        public WizardViewModel AppState { get; } = new WizardViewModel();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = AppState;
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(InitializeWizardAsync), DispatcherPriority.Loaded);
        }

        private async void InitializeWizardAsync()
        {
            AppState.Report.LoadDbInfoConfiguration();

            AppState.ServerName = AppState.Report.ServerName;
            AppState.DatabaseName = AppState.Report.DatabaseName;
            AppState.Username = AppState.Report.Username;
            AppState.AuthType = AppState.Report.AuthType;

            if (!string.IsNullOrWhiteSpace(AppState.ServerName))
            {
                AppState.CurrentStep = 2;
                await AppState.LoadDatabaseOptionsAsync();
            }
        }
    }
}