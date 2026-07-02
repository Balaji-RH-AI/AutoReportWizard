using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoReportWizard
{
    public partial class MainWindow : Window
    {
        private int _currentStep = 1;
        public WizardViewModel AppState { get; } = new WizardViewModel();

        private static readonly string[] StepHints =
        {
            "",
            "Enter the target database server, authentication type, and credentials.",
            "Select the database, schema, and table to define your dataset.",
            "Choose grouping options and configure field-level transformations.",
            "Set the report title, subtitle, and configure header/footer options.",
            "Reorder columns and configure spatial header/footer zones.",
            "Review your configuration and generate the final output files."
        };

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = AppState;
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Force the XML to load right now
            AppState.Report.LoadDbInfoConfiguration();

            // 2. Sync the loaded data into the ViewModel so the UI can see it
            AppState.ServerName = AppState.Report.ServerName;
            AppState.DatabaseName = AppState.Report.DatabaseName;
            AppState.Username = AppState.Report.Username;
            AppState.AuthType = AppState.Report.AuthType;

            // 3. Evaluate the skip
            if (!string.IsNullOrWhiteSpace(AppState.ServerName))
            {
                _currentStep = 2; // Jump directly to Dataset Definition
            }

            UpdateUI();
        }

        // ── Navigation ────────────────────────────────────────────────────────
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 6) { _currentStep++; UpdateUI(); }
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

            StepHintText.Text = StepHints[_currentStep];
            BackButton.IsEnabled = _currentStep > 1;
            NextButton.Content = _currentStep == 6 ? "Generate Output ⚡" : "Next →";
            NextButton.IsEnabled = _currentStep < 6;

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
                text.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            }
            else if (targetStep == _currentStep)
            {
                icon.Text = "■";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37));
                text.Foreground = new SolidColorBrush(Colors.White);
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