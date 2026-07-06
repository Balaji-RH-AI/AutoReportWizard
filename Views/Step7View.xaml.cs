using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutoReportWizard.Services;
using AutoReportWizard.ViewModels;

namespace AutoReportWizard.Views
{
    public partial class Step7View : UserControl
    {
        private readonly SqlGeneratorService _sqlGen = new();
        private readonly SqlVerificationService _sqlVerify = new();
        private readonly RdlcValidationService _rdlcVal = new();

        public Step7View()
        {
            InitializeComponent();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            var dlg = new OpenFolderDialog
            {
                Title = "Select output folder for generated .sql and .rdlc files",
                InitialDirectory = vm.OutputDirectory
            };

            if (dlg.ShowDialog() == true)
                vm.OutputDirectory = dlg.FolderName;
        }

        private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WizardViewModel vm) return;

            // Ensure ViewModel internal lists are perfectly synced before generation
            vm.SyncFieldsToReport();
            vm.SyncParametersToReport();

            if (string.IsNullOrWhiteSpace(vm.ReportName))
            {
                vm.AppendLog("❌ Aborted — Report Name is required.");
                return;
            }

            vm.IsGenerating = true;
            vm.GenerationLog = string.Empty;

            PhaseALabel.Foreground = System.Windows.Media.Brushes.DimGray;
            PhaseBLabel.Foreground = System.Windows.Media.Brushes.DimGray;

            try
            {
                Directory.CreateDirectory(vm.OutputDirectory);

                // ── PHASE A ───────────────────────────────────────────────
                PhaseALabel.Text = "⏳ Phase A — T-SQL";
                PhaseALabel.Foreground = System.Windows.Media.Brushes.Gold;
                vm.AppendLog("── Phase A: T-SQL Generation ──────────────────");

                string generatedSql = await Task.Run(() => _sqlGen.Generate(vm.Report));
                vm.AppendLog("  ✔ T-SQL script generated successfully.");

                vm.AppendLog("  Verifying syntax via SET PARSEONLY ON…");
                var verifyResult = await _sqlVerify.VerifyAsync(vm.Report, generatedSql);

                if (!verifyResult.IsValid)
                {
                    vm.AppendLog($"  ⚠️  SQL Verification Warning (Line {verifyResult.ErrorLine}): {verifyResult.ErrorMessage}");
                }
                else
                {
                    vm.AppendLog("  ✔ Syntax verified.");
                }

                string sqlPath = Path.Combine(vm.OutputDirectory, $"{vm.Report.StoredProcName}.sql");
                await File.WriteAllTextAsync(sqlPath, generatedSql);
                vm.AppendLog($"  📄 Saved: {sqlPath}\n");

                PhaseALabel.Text = "✔ Phase A — T-SQL";
                PhaseALabel.Foreground = System.Windows.Media.Brushes.LightGreen;

                // ── PHASE B ───────────────────────────────────────────────
                PhaseBLabel.Text = "⏳ Phase B — RDLC XML";
                PhaseBLabel.Foreground = System.Windows.Media.Brushes.Gold;
                vm.AppendLog("── Phase B: RDLC XML Generation ───────────────");

                var rdlcDoc = await Task.Run(() => RdlcXmlEngine.GenerateRdlcXml(vm.Report));
                vm.AppendLog("  ✔ XDocument built successfully.");

                string rdlcPath = Path.Combine(vm.OutputDirectory, $"{vm.Report.ReportName}.rdlc");
                await Task.Run(() => rdlcDoc.Save(rdlcPath));
                vm.AppendLog($"  📄 Saved: {rdlcPath}\n");

                PhaseBLabel.Text = "✔ Phase B — RDLC XML";
                PhaseBLabel.Foreground = System.Windows.Media.Brushes.LightGreen;

                vm.AppendLog("══ Generation Complete ══════════════════════════");
                ScrollLogToBottom();
            }
            catch (Exception ex)
            {
                vm.AppendLog($"❌ FATAL ERROR: {ex.Message}");
                Infrastructure.TelemetryService.RecordFailure(null, ex, vm.Report.ReportName);
            }
            finally
            {
                vm.IsGenerating = false;
                ScrollLogToBottom();
            }
        }

        private void ScrollLogToBottom()
        {
            Dispatcher.BeginInvoke(() => LogScroller.ScrollToBottom(),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
