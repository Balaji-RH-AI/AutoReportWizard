using Microsoft.Data.SqlClient;
using AutoReportWizard.Models;

namespace AutoReportWizard.Services
{
    /// <summary>
    /// Verifies generated T-SQL by executing SET PARSEONLY ON within a
    /// rolled-back transaction on the target database.
    ///
    /// This service performs NO data changes. It only validates syntax.
    /// On failure, it captures the SQL Server error details and returns
    /// them as a structured result before any file is written.
    /// </summary>
    public class SqlVerificationService
    {
        /// <summary>
        /// Result of a SQL syntax verification pass.
        /// </summary>
        public record SqlVerificationResult(
            bool   IsValid,
            string? ErrorMessage,
            int     ErrorLine,
            int     ErrorNumber);

        /// <summary>
        /// Parses the given SQL script against the target database.
        /// Uses SET PARSEONLY ON — the server checks syntax without executing.
        /// Wrapped in a transaction that is always rolled back.
        /// </summary>
        public async Task<SqlVerificationResult> VerifyAsync(
            ReportDefinition def,
            string generatedSql,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(cancellationToken);

                await using var transaction = connection.BeginTransaction();
                try
                {
                    // Tell the parser to only check syntax, not execute
                    await using var parseOnlyCmd = new SqlCommand(
                        "SET PARSEONLY ON", connection, transaction)
                    {
                        CommandTimeout = 30
                    };
                    await parseOnlyCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Execute the generated script — syntax errors surface here as SqlException
                    await using var scriptCmd = new SqlCommand(
                        generatedSql, connection, transaction)
                    {
                        CommandTimeout = 30
                    };
                    await scriptCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Turn PARSEONLY off before rolling back
                    await using var parseOffCmd = new SqlCommand(
                        "SET PARSEONLY OFF", connection, transaction)
                    {
                        CommandTimeout = 10
                    };
                    await parseOffCmd.ExecuteNonQueryAsync(cancellationToken);

                    return new SqlVerificationResult(true, null, 0, 0);
                }
                finally
                {
                    // Always roll back — this is a read-only verification pass
                    await transaction.RollbackAsync(cancellationToken);
                }
            }
            catch (SqlException ex)
            {
                // Surface the first error with its line number for the UI
                var first = ex.Errors.Count > 0 ? ex.Errors[0] : null;
                return new SqlVerificationResult(
                    false,
                    ex.Message,
                    first?.LineNumber ?? 0,
                    first?.Number    ?? 0);
            }
            catch (Exception ex)
            {
                return new SqlVerificationResult(false, ex.Message, 0, 0);
            }
        }
    }
}
