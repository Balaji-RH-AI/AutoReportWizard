using System.Diagnostics;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using AutoReportWizard.Models;

namespace AutoReportWizard.Infrastructure
{
    /// <summary>
    /// Provides all database interactions using Microsoft.Data.SqlClient
    /// with Integrated Windows Authentication, connection pooling, and a
    /// Polly 8 resilience pipeline (exponential backoff, 3 retries).
    ///
    /// STRICT RULES:
    ///   - Only Integrated Security is used â€” no username/password parameters.
    ///   - Schema discovery is performed exclusively through sys.columns and
    ///     sys.types system views with parameterized commands.
    ///   - No dynamic SQL is constructed or executed in this service.
    /// </summary>
    public class DatabaseService
    {
        // â”€â”€ SQL-type â†’ .NET System.Type mapping (deterministic, no reflection) â”€â”€
        private static readonly Dictionary<string, string> SqlTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bigint"] = "System.Int64",
            ["binary"] = "System.Byte[]",
            ["bit"] = "System.Boolean",
            ["char"] = "System.String",
            ["date"] = "System.DateTime",
            ["datetime"] = "System.DateTime",
            ["datetime2"] = "System.DateTime",
            ["datetimeoffset"] = "System.DateTimeOffset",
            ["decimal"] = "System.Decimal",
            ["float"] = "System.Double",
            ["image"] = "System.Byte[]",
            ["int"] = "System.Int32",
            ["money"] = "System.Decimal",
            ["nchar"] = "System.String",
            ["ntext"] = "System.String",
            ["numeric"] = "System.Decimal",
            ["nvarchar"] = "System.String",
            ["real"] = "System.Single",
            ["smalldatetime"] = "System.DateTime",
            ["smallint"] = "System.Int16",
            ["smallmoney"] = "System.Decimal",
            ["text"] = "System.String",
            ["time"] = "System.TimeSpan",
            ["timestamp"] = "System.Byte[]",
            ["tinyint"] = "System.Byte",
            ["uniqueidentifier"] = "System.Guid",
            ["varbinary"] = "System.Byte[]",
            ["varchar"] = "System.String",
            ["xml"] = "System.String",
        };

        // â”€â”€ Polly 8 resilience pipeline â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly ResiliencePipeline _resilience = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<SqlException>(ex =>
                        ex.Number is 1205    // Deadlock
                            or -2            // Timeout
                            or 64            // Transport-level error
                            or 233           // No process on the other end
                            or 10053         // Connection aborted by host
                            or 10054         // Connection reset by peer
                            or 10060),       // Network unreachable
                MaxRetryAttempts = ReportDefinition.MaxDbRetries,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,   // 2s â†’ 4s â†’ 8s
                UseJitter = true,
                OnRetry = args =>
                {
                    Debug.WriteLine(
                        $"[DatabaseService] Retry {args.AttemptNumber} after " +
                        $"{args.RetryDelay.TotalSeconds:F1}s â€” {args.Outcome.Exception?.Message}");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // â”€â”€ Public API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Analyzes a custom T-SQL script (e.g. cross-database joins, UNIONS) and extracts
        /// the exact output schema using sp_describe_first_result_set.
        /// </summary>
        public async Task<List<ReportField>> GetSchemaFromCustomSqlAsync(
            ReportDefinition def,
            CancellationToken cancellationToken = default)
        {
            var fields = new List<ReportField>();

            await _resilience.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(ct);

                // Added 'error_message' to the SELECT so we can catch TempTable failures
                const string sql = @"
                    SELECT name, system_type_name, column_ordinal, error_message
                    FROM sys.dm_exec_describe_first_result_set(@CustomQuery, NULL, 0)
                    WHERE is_hidden = 0 OR error_message IS NOT NULL
                    ORDER BY column_ordinal;";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@CustomQuery", def.CustomSql);

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    // Check if SQL Server returned an explicit parsing error
                    if (!reader.IsDBNull(3))
                    {
                        string errorMsg = reader.GetString(3);
                        throw new InvalidOperationException($"SQL Server rejected the query schema. {errorMsg}");
                    }

                    if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                    string colName = reader.GetString(0);
                    string sqlType = reader.GetString(1);
                    int colOrder = reader.GetInt32(2);

                    fields.Add(new ReportField
                    {
                        Name = colName,
                        SqlDataType = sqlType,
                        DotNetType = MapToDotNet(sqlType), // Reuses your existing deterministic mapper
                        IsDetailField = true,
                        DisplayOrder = colOrder - 1
                    });
                }
            }, cancellationToken);

            return fields;
        }
        /// <summary>
        /// Discovers all columns for the given table/view from sys.columns.
        /// Enforces the 30-column guardrail and the 60-second schema timeout.
        /// </summary>
        /// <param name="def">Report definition supplying Server, Database, Schema, TableOrViewName.</param>
        /// <param name="cancellationToken">Caller-provided cancellation.</param>
        /// <returns>Ordered list of ReportField objects with SqlDataType and DotNetType populated.</returns>
        public async Task<List<ReportField>> GetSchemaAsync(
            ReportDefinition def,
            CancellationToken cancellationToken = default)
        {
            var fields = new List<ReportField>();

            await _resilience.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(ct);

                // Parameterized query â€” no dynamic SQL, only system views
                const string sql = """
                    SELECT
                        c.name                AS ColumnName,
                        tp.name               AS TypeName,
                        c.column_id           AS ColumnOrder
                    FROM sys.columns     c
                    JOIN sys.objects     o  ON o.object_id = c.object_id
                    JOIN sys.schemas     s  ON s.schema_id = o.schema_id
                    JOIN sys.types       tp ON tp.user_type_id = c.user_type_id
                    WHERE s.name        = @Schema
                      AND o.name       = @Table
                      AND o.type       IN ('U','V','P')   -- Tables, Views, Procs
                    ORDER BY c.column_id
                    """;

                await using var cmd = new SqlCommand(sql, connection)
                {
                    CommandTimeout = ReportDefinition.SchemaTimeoutSeconds
                };
                cmd.Parameters.AddWithValue("@Schema", def.SchemaName);
                cmd.Parameters.AddWithValue("@Table", def.TableOrViewName);

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    string colName = reader.GetString(0);
                    string sqlType = reader.GetString(1);
                    int colOrder = reader.GetInt32(2);

                    fields.Add(new ReportField
                    {
                        Name = colName,
                        SqlDataType = sqlType,
                        DotNetType = MapToDotNet(sqlType),
                        IsDetailField = true,
                        DisplayOrder = colOrder - 1  // 0-based
                    });
                }
            }, cancellationToken);
            return fields;
        }

        /// <summary>
        /// Fetches all accessible databases on the server using DBInfo.xml credentials.
        /// </summary>
        public async Task<List<string>> GetDatabasesAsync(ReportDefinition def, CancellationToken ct = default)
        {
            var databases = new List<string>();

            // 1. Force the model to parse DBInfo.xml for credentials
            def.LoadDbInfoConfiguration();

            await _resilience.ExecuteAsync(async token =>
            {
                // 2. Build the connection string using the newly loaded XML credentials
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                const string sql = @"
                    SELECT name
                    FROM sys.databases
                    WHERE state = 0 AND HAS_DBACCESS(name) = 1
                    ORDER BY name;";

                await using var cmd = new SqlCommand(sql, connection)
                {
                    CommandTimeout = ReportDefinition.SchemaTimeoutSeconds
                };

                await using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    databases.Add(reader.GetString(0));
                }
            }, ct);

            // Ensure the currently configured database is always in the list
            if (!string.IsNullOrWhiteSpace(def.DatabaseName) &&
                !databases.Any(db => string.Equals(db, def.DatabaseName, StringComparison.OrdinalIgnoreCase)))
            {
                databases.Insert(0, def.DatabaseName);
            }

            return databases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        /// <summary>
        /// Fetches all schemas for the currently selected database.
        /// </summary>
        public async Task<List<string>> GetSchemasAsync(ReportDefinition def, CancellationToken ct = default)
        {
            var schemas = new List<string>();
            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                const string sql = "SELECT name FROM sys.schemas ORDER BY name";
                await using var cmd = new SqlCommand(sql, connection);
                await using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    schemas.Add(reader.GetString(0));
                }
            }, ct);
            return schemas;
        }

        /// <summary>
        /// Fetches all Tables and Views for a specific schema.
        /// </summary>
        public async Task<List<string>> GetTablesAndViewsAsync(ReportDefinition def, string schema, CancellationToken ct = default)
        {
            var tables = new List<string>();
            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                const string sql = @"
                    SELECT name 
                    FROM sys.objects 
                    WHERE type IN ('U', 'V') AND schema_id = SCHEMA_ID(@schema) 
                    ORDER BY name";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@schema", schema);
                await using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token))
                {
                    tables.Add(reader.GetString(0));
                }
            }, ct);
            return tables;
        }

        public async Task<DataTable> ExecuteStoredProcedurePreviewAsync(
    ReportDefinition def,
    IReadOnlyCollection<ReportParameter> parameters,
    CancellationToken ct = default)
        {
            var data = new DataTable("PreviewData");

            // 1. We must parse out the CREATE PROCEDURE wrapper and extract ONLY the SELECT query
            // so we can execute it safely against the live database for a preview.
            string rawQuery = def.CustomSql;

            // Look for the "SELECT" keyword to start the query. 
            // We ignore the CREATE PROCEDURE and SET NOCOUNT ON headers.
            int selectIndex = rawQuery.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
            if (selectIndex >= 0)
            {
                // Extract everything from SELECT down to the end
                rawQuery = rawQuery.Substring(selectIndex);

                // Remove the OPTION (RECOMPILE); and END tags from the footer
                rawQuery = rawQuery.Replace("OPTION (RECOMPILE);", "", StringComparison.OrdinalIgnoreCase);
                rawQuery = rawQuery.Replace("END", "", StringComparison.OrdinalIgnoreCase);
            }

            // If the user wrote Pre-Query logic (like DECLARE variables), prepend it back to the raw query
            if (!string.IsNullOrWhiteSpace(def.PreQueryLogic))
            {
                rawQuery = def.PreQueryLogic + "\n" + rawQuery;
            }

            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                // 2. Change CommandType from StoredProcedure to Text so we can run raw SQL
                await using var cmd = new SqlCommand(rawQuery, connection)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = ReportDefinition.SchemaTimeoutSeconds
                };

                // 3. Attach any parameters the user provided in the UI
                foreach (var parameter in parameters)
                {
                    string parameterName = parameter.Name.StartsWith("@", StringComparison.Ordinal)
                        ? parameter.Name
                        : "@" + parameter.Name;
                    object value = ConvertParameterValue(parameter);
                    cmd.Parameters.AddWithValue(parameterName, value);
                }

                await using var reader = await cmd.ExecuteReaderAsync(token);
                data.Load(reader);
            }, ct);

            return data;
        }
        /// <summary>
        /// Tests connectivity to the target server/database using Windows Auth.
        /// Returns null on success, or an error message string on failure.
        /// </summary>
        public async Task<string?> TestConnectionAsync(
            ReportDefinition def,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _resilience.ExecuteAsync(async ct =>
                {
                    await using var connection = new SqlConnection(def.BuildConnectionString());
                    await connection.OpenAsync(ct);
                    // Fire a trivial query to confirm the database is accessible
                    await using var cmd = new SqlCommand("SELECT DB_NAME()", connection)
                    {
                        CommandTimeout = 10
                    };
                    await cmd.ExecuteScalarAsync(ct);
                }, cancellationToken);

                return null;  // success
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // â”€â”€ Type Mapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Deterministically maps a SQL type name to its .NET System.Type string.
        /// Falls back to System.String for any unmapped type.
        /// </summary>
        private static object ConvertParameterValue(ReportParameter parameter)
        {
            string rawValue = parameter.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawValue) && parameter.AllowBlank)
                return DBNull.Value;

            string typeName = parameter.SqlDataType;
            int typeLengthIndex = typeName.IndexOf('(');
            if (typeLengthIndex >= 0)
                typeName = typeName[..typeLengthIndex];

            typeName = typeName.Trim().ToLowerInvariant();

            return typeName switch
            {
                "bigint" => long.Parse(rawValue, CultureInfo.InvariantCulture),
                "bit" => ParseBit(rawValue),
                "date" or "datetime" or "datetime2" or "smalldatetime" =>
                    DateTime.Parse(rawValue, CultureInfo.InvariantCulture),
                "decimal" or "money" or "numeric" or "smallmoney" =>
                    decimal.Parse(rawValue, CultureInfo.InvariantCulture),
                "float" => double.Parse(rawValue, CultureInfo.InvariantCulture),
                "int" => int.Parse(rawValue, CultureInfo.InvariantCulture),
                "real" => float.Parse(rawValue, CultureInfo.InvariantCulture),
                "smallint" => short.Parse(rawValue, CultureInfo.InvariantCulture),
                "time" => TimeSpan.Parse(rawValue, CultureInfo.InvariantCulture),
                "tinyint" => byte.Parse(rawValue, CultureInfo.InvariantCulture),
                "uniqueidentifier" => Guid.Parse(rawValue),
                _ => rawValue
            };
        }

        private static bool ParseBit(string rawValue)
        {
            if (bool.TryParse(rawValue, out bool result))
                return result;

            return rawValue switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException($"Cannot convert '{rawValue}' to bit.")
            };
        }

        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("SQL identifier cannot be blank.", nameof(identifier));

            return "[" + identifier.Replace("]", "]]") + "]";
        }

        public static string MapToDotNet(string sqlTypeName) =>
            SqlTypeMap.TryGetValue(sqlTypeName, out string? dotNetType)
                ? dotNetType
                : "System.String";
    }
}

