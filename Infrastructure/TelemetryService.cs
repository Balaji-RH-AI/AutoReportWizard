using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace AutoReportWizard.Infrastructure
{
    /// <summary>
    /// Centralized OpenTelemetry telemetry provider for AutoReportWizard.
    /// Configures an ActivitySource for structured tracing of all generation
    /// operations. Failures are recorded as span events with sanitized context
    /// (server/database names are excluded from spans for security).
    /// </summary>
    public static class TelemetryService
    {
        public const string SourceName = "AutoReportWizard.Generation";
        public const string Version    = "1.0.0";

        /// <summary>
        /// The shared ActivitySource used across all generation services.
        /// Spans are named "generation.sql", "generation.rdlc", "schema.discovery", etc.
        /// </summary>
        public static readonly ActivitySource Source = new(SourceName, Version);

        private static TracerProvider? _tracerProvider;

        /// <summary>
        /// Initializes the OpenTelemetry TracerProvider with the Console exporter.
        /// Call once from App startup. Safe to call multiple times (no-op after first call).
        /// </summary>
        public static void Initialize()
        {
            if (_tracerProvider is not null) return;

            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(SourceName)
                .AddConsoleExporter()
                .Build();
        }

        /// <summary>
        /// Shuts down the TracerProvider cleanly on application exit.
        /// </summary>
        public static void Shutdown()
        {
            _tracerProvider?.Dispose();
            _tracerProvider = null;
        }

        // ── Span Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Starts a generation span with standard report metadata tags.
        /// The caller is responsible for disposing the returned Activity.
        /// </summary>
        /// <param name="operationName">e.g. "generation.sql" or "generation.rdlc"</param>
        /// <param name="reportName">Report name (safe for telemetry).</param>
        /// <param name="fieldCount">Number of fields in the report.</param>
        public static Activity? StartGenerationSpan(
            string operationName,
            string reportName,
            int fieldCount)
        {
            var activity = Source.StartActivity(operationName, ActivityKind.Internal);
            activity?.SetTag("report.name",   reportName);
            activity?.SetTag("field.count",   fieldCount);
            activity?.SetTag("wizard.version", Version);
            return activity;
        }

        /// <summary>
        /// Records a generation failure on the current span.
        /// Logs the exception type and message; does NOT log raw SQL or credentials.
        /// </summary>
        public static void RecordFailure(Activity? activity, Exception ex, string partialAssetHint)
        {
            if (activity is null) return;

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddEvent(new ActivityEvent("generation.failure",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"]        = ex.GetType().Name,
                    ["exception.message"]     = ex.Message,
                    ["partial.asset.hint"]    = partialAssetHint,
                }));
        }

        /// <summary>
        /// Records a successful generation completion on the current span.
        /// </summary>
        public static void RecordSuccess(Activity? activity, string outputPath)
        {
            if (activity is null) return;

            activity.SetStatus(ActivityStatusCode.Ok);
            activity.SetTag("output.file", System.IO.Path.GetFileName(outputPath));
        }
    }
}
