using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoReportWizard
{
    public partial class MainWindow : Window
    {
        private int _currentStep = 1;

        // Single ViewModel instance shared across ALL step views via DataContext
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
            UpdateUI();
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 6)
            {
                _currentStep++;
                UpdateUI();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateUI();
            }
        }

        // ── UI State ──────────────────────────────────────────────────────────

        private void UpdateUI()
        {
            // ── Dynamic Window Sizing Strategy ────────────────────────────────
            switch (_currentStep)
            {
                case 1: // Target Environment
                    this.Width = 950; this.Height = 650;
                    break;
                case 2: // Dataset Definition
                    this.Width = 1000; this.Height = 720;
                    break;
                case 3: // Group By & Editor
                case 4: // Header & Footer
                    this.Width = 1150; this.Height = 760;
                    break;
                case 5: // Visual Layout
                case 6: // Generation Terminal
                    this.Width = 1300; this.Height = 850;
                    break;
            }

            // ── Bottom Bar Text & Buttons ─────────────────────────────────────
            StepHintText.Text = StepHints[_currentStep];
            BackButton.IsEnabled = _currentStep > 1;
            NextButton.Content = _currentStep == 6 ? "Generate Output ⚡" : "Next →";
            NextButton.IsEnabled = _currentStep < 6;  // Step 6 uses its own Generate button

            // ── Sidebar Step Icons ────────────────────────────────────────────
            UpdateSidebarStep(1, Icon1, Text1);
            UpdateSidebarStep(2, Icon2, Text2);
            UpdateSidebarStep(3, Icon3, Text3);
            UpdateSidebarStep(4, Icon4, Text4);
            UpdateSidebarStep(5, Icon5, Text5);
            UpdateSidebarStep(6, Icon6, Text6);

            // ── Swap step content ─────────────────────────────────────────────
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

            if (view != null)
            {
                MainContentArea.Content = view;
            }
        }

        private void UpdateSidebarStep(int targetStep, TextBlock icon, TextBlock text)
        {
            if (targetStep < _currentStep)
            {
                // Completed step — Green Checkmark
                icon.Text = "✓";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                text.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            }
            else if (targetStep == _currentStep)
            {
                // Current step — Gold Dot
                icon.Text = "●";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37));
                text.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // Future step — Hollow Gray Dot
                icon.Text = "○";
                icon.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                text.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            }
        }
    }
}