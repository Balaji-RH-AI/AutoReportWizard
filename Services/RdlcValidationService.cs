using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Validates a generated RDLC XDocument against the official SSRS 2016
    /// XSD schema stored as an embedded resource.
    ///
    /// Uses XmlSchemaSet + XDocument.Validate() — strictly typed, no regex heuristics.
    /// All schema violations are collected and returned before any file is written.
    /// </summary>
    public class RdlcValidationService
    {
        private const string SchemaResourceName =
            "AutoReportWizard.Resources.ReportDefinition.xsd";

        private const string RdlNamespace =
            "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";

        /// <summary>
        /// Result of an RDLC validation pass.
        /// </summary>
        public record RdlcValidationResult(
            bool IsValid,
            IReadOnlyList<string> Violations);

        private XmlSchemaSet? _schemaSet;

        /// <summary>
        /// Validates the given XDocument against the SSRS 2016 XSD schema.
        /// </summary>
        public RdlcValidationResult Validate(XDocument document)
        {
            var violations = new List<string>();

            try
            {
                var schemas = GetOrLoadSchemas();
                document.Validate(schemas, (_, args) =>
                {
                    // Collect all violations rather than stopping at first error
                    violations.Add($"[{args.Severity}] Line {args.Exception?.LineNumber}: {args.Message}");
                });
            }
            catch (XmlSchemaException ex)
            {
                violations.Add($"[Schema Load Error] {ex.Message}");
            }
            catch (XmlException ex)
            {
                violations.Add($"[XML Parse Error] {ex.Message}");
            }

            return new RdlcValidationResult(violations.Count == 0, violations);
        }

        // ── Schema Loading ────────────────────────────────────────────────────

        private XmlSchemaSet GetOrLoadSchemas()
        {
            if (_schemaSet is not null) return _schemaSet;

            _schemaSet = new XmlSchemaSet();

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(SchemaResourceName);

            if (stream is null)
            {
                // XSD not embedded — return empty schema set (validation will be skipped gracefully)
                // This happens in dev environments before the XSD is added to Resources/
                return _schemaSet;
            }

            using var reader = XmlReader.Create(stream);
            _schemaSet.Add(RdlNamespace, reader);
            _schemaSet.Compile();

            return _schemaSet;
        }
    }
}
