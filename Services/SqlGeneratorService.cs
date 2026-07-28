using System;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Simplified SQL generation for Stored Procedure First architecture.
    /// Since we execute stored procedures directly via CommandType.StoredProcedure,
    /// this service is now obsolete except for generating simple EXEC statements
    /// for documentation or manual testing purposes.
    /// </summary>
    public class SqlGeneratorService
    {
        /// <summary>
        /// Generates a simple EXEC statement for the configured stored procedure.
        /// This is primarily used for documentation/testing, not actual execution.
        /// </summary>
        public string Generate(ReportDefinition def)
        {
            return GenerateSql(def);
        }

        /// <summary>
        /// Generates a simple EXEC statement for the stored procedure.
        /// Actual SP execution happens via CommandType.StoredProcedure in DatabaseService.
        /// </summary>
        public static string GenerateSql(ReportDefinition def)
        {
            if (string.IsNullOrWhiteSpace(def.StoredProcedureName))
                return string.Empty;

            return $"EXEC [{def.SchemaName}].[{def.StoredProcedureName}]";
        }

        /// <summary>
        /// Utility method for quoting SQL identifiers (maintained for compatibility).
        /// </summary>
        public static string QuoteName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            return "[" + identifier.Replace("]", "]]") + "]";
        }
    }
}
