using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class MainWindow : Window
    {
        private int _currentStep = 1;
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
                _currentStep = 2;
                await AppState.LoadDatabaseOptionsAsync();
            }

            UpdateUI();
        }

        // ── Navigation ────────────────────────────────────────────────────────
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // If they click Finish on the last step, close the application
            if (_currentStep == 6)
            {
                Application.Current.Shutdown();
                return;
            }

            if (_currentStep < 6)
            {
                _currentStep++;
                UpdateUI();
            }
        }
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1) { _currentStep--; UpdateUI(); }
        }

        private void ChangeConnection_Click(object sender, RoutedEventArgs e)
        {
            _currentStep = 1; UpdateUI(); // Escape hatch
        }

        // ── UI State ──────────────────────────────────────────────────────────
        private void UpdateUI()
        {
            switch (_currentStep)
            {
                case 1: this.Width = 950; this.Height = 650; break;
                case 2: this.Width = 1000; this.Height = 720; break;
                case 3:
                case 4: this.Width = 1150; this.Height = 760; break;
                case 5:
                case 6: this.Width = 1300; this.Height = 850; break;
            }

            BackButton.IsEnabled = _currentStep > 1;
            if (_currentStep == 6)
            {
                NextButton.Content = "Finish ✓";
                NextButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E8B57")); // Sea Green
                NextButton.Foreground = Brushes.White;
            }
            else
            {
                NextButton.Content = "Next →";
                NextButton.SetResourceReference(Control.BackgroundProperty, "AccentGoldBrush");
                NextButton.SetResourceReference(Control.ForegroundProperty, "WindowBackgroundBrush");
            }

            UpdateSidebarStep(1, Icon1, Text1); UpdateSidebarStep(2, Icon2, Text2);
            UpdateSidebarStep(3, Icon3, Text3); UpdateSidebarStep(4, Icon4, Text4);
            UpdateSidebarStep(5, Icon5, Text5); UpdateSidebarStep(6, Icon6, Text6);

            UserControl? view = _currentStep switch
            {
                1 => new Step1View { DataContext = AppState },
                2 => new Step2View { DataContext = AppState },
                3 => new Step3View { DataContext = AppState },
                4 => new Step4View { DataContext = AppState },
                5 => new Step5View { DataContext = AppState },
                6 => new Step6View { DataContext = AppState },
                _ => null
            };

            if (view != null) MainContentArea.Content = view;
        }

        private void UpdateSidebarStep(int targetStep, TextBlock icon, TextBlock text)
        {
            if (targetStep < _currentStep)
            {
                icon.Text = "✓";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
            else if (targetStep == _currentStep)
            {
                icon.Text = "■";
                icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentGoldBrush");
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            }
            else
            {
                icon.Text = "□";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                text.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            }
        }
    }
}