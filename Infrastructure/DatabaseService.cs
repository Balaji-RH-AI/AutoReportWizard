using System.Diagnostics;
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;
using AutoReportWizard.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Linq;

namespace AutoReportWizard.Infrastructure
{
    /// <summary>
    /// Provides all database interactions using Microsoft.Data.SqlClient
    /// with Integrated Windows Authentication, connection pooling, and a
    /// Polly 8 resilience pipeline (exponential backoff, 3 retries).
    ///
    /// STORED PROCEDURE FIRST ARCHITECTURE:
    ///   - Extracts metadata from existing Stored Procedures via sys.parameters
    ///   - Hybrid schema discovery (sys.dm_exec_describe_first_result_set + SchemaOnly fallback)
    ///   - No dynamic SQL generation - all queries are strictly parameterized
    /// </summary>
    public class DatabaseService
    {
        // ── SQL-type → .NET System.Type mapping (deterministic, no reflection) ──
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

        // ── Polly 8 resilience pipeline ─────────────────────────────────────────
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
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    Debug.WriteLine(
                        $"[DatabaseService] Retry {args.AttemptNumber} after " +
                        $"{args.RetryDelay.TotalSeconds:F1}s — {args.Outcome.Exception?.Message}");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Discovers output fields from a Stored Procedure using a hybrid approach.
        /// Attempts sys.dm_exec_describe_first_result_set, falls back to CommandBehavior.SchemaOnly
        /// if temporary tables prevent static analysis.
        /// </summary>
        public async Task<List<ReportField>> GetStoredProcedureOutputFieldsAsync(
            ReportDefinition def,
            CancellationToken cancellationToken = default)
        {
            var fields = new List<ReportField>();
            string spFullName = $"[{def.SchemaName}].[{def.StoredProcedureName}]";

            await _resilience.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(ct);

                // Attempt 1: Static analysis (Fast, but fails on #temp tables)
                try
                {
                    const string sql = @"
                        SELECT name, system_type_name, column_ordinal, error_message
                        FROM sys.dm_exec_describe_first_result_set(@StoredProcedureName, NULL, 0)
                        WHERE is_hidden = 0 OR error_message IS NOT NULL
                        ORDER BY column_ordinal;";

                    await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = ReportDefinition.SchemaTimeoutSeconds };
                    cmd.Parameters.AddWithValue("@StoredProcedureName", spFullName);

                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        if (!reader.IsDBNull(3)) throw new InvalidOperationException("Temp table detected, falling back.");
                        if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                        fields.Add(MapToReportField(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) - 1));
                    }
                    
                    if (fields.Count > 0) return; // Success!
                }
                catch
                {
                    fields.Clear(); // Clear partials and prepare for Fallback
                }
            }, cancellationToken);

            return fields;
        }

        /// <summary>
        /// Discovers input parameters from a Stored Procedure using sys.parameters.
        /// Returns a list of ReportParameter objects ready for RDLC generation.
        /// </summary>
        public async Task<List<ReportParameter>> GetStoredProcedureParametersAsync(
            ReportDefinition def,
            CancellationToken cancellationToken = default)
        {
            var parameters = new List<ReportParameter>();

            await _resilience.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(ct);

                const string sql = @"
                    SELECT 
                        p.name AS ParameterName,
                        t.name AS DataType,
                        p.max_length AS MaxLength,
                        p.precision AS Precision,
                        p.scale AS Scale,
                        p.is_output AS IsOutput
                    FROM sys.parameters p
                    INNER JOIN sys.objects o ON o.object_id = p.object_id
                    INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                    INNER JOIN sys.types t ON t.user_type_id = p.user_type_id
                    WHERE s.name = @SchemaName
                      AND o.name = @ProcedureName
                      AND o.type = 'P'
                      AND p.is_output = 0
                    ORDER BY p.parameter_id;";

                await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = ReportDefinition.SchemaTimeoutSeconds };
                cmd.Parameters.AddWithValue("@SchemaName", def.SchemaName);
                cmd.Parameters.AddWithValue("@ProcedureName", def.StoredProcedureName);

                await using var reader = await cmd.ExecuteReaderAsync(ct);

                while (await reader.ReadAsync(ct))
                {
                    string paramName = reader.GetString(0);
                    string dataType = reader.GetString(1);
                    int maxLength = reader.GetInt16(2);
                    byte precision = reader.GetByte(3);
                    byte scale = reader.GetByte(4);

                    string sqlDataType = BuildSqlDataType(dataType, maxLength, precision, scale);

                    parameters.Add(new ReportParameter
                    {
                        Name = paramName.Replace("@", ""), // Strip @ for UI display
                        SqlDataType = sqlDataType,
                        RdlcDataType = MapSqlTypeToRdlc(sqlDataType),
                        Value = string.Empty,
                        AllowBlank = true,
                        IsHidden = false
                    });
                }
            }, cancellationToken);

            return parameters;
        }

        public async Task<List<string>> GetStoredProceduresAsync(ReportDefinition def, string schema, CancellationToken ct = default)
        {
            var procedures = new List<string>();
            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                const string sql = @"
                    SELECT name 
                    FROM sys.objects 
                    WHERE type = 'P' 
                      AND schema_id = SCHEMA_ID(@schema)
                      AND is_ms_shipped = 0
                    ORDER BY name";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@schema", schema);
                await using var reader = await cmd.ExecuteReaderAsync(token);

                while (await reader.ReadAsync(token)) procedures.Add(reader.GetString(0));
            }, ct);
            return procedures;
        }

        public async Task<List<string>> GetDatabasesAsync(ReportDefinition def, CancellationToken ct = default)
        {
            var databases = new List<string>();
            def.LoadDbInfoConfiguration();

            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                const string sql = "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name;";
                await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = ReportDefinition.SchemaTimeoutSeconds };
                await using var reader = await cmd.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) databases.Add(reader.GetString(0));
            }, ct);

            if (!string.IsNullOrWhiteSpace(def.DatabaseName) && !databases.Any(db => string.Equals(db, def.DatabaseName, StringComparison.OrdinalIgnoreCase)))
            {
                databases.Insert(0, def.DatabaseName);
            }
            return databases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<List<string>> GetSchemasAsync(ReportDefinition def, CancellationToken ct = default)
        {
            var schemas = new List<string>();
            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);
                await using var cmd = new SqlCommand("SELECT name FROM sys.schemas ORDER BY name", connection);
                await using var reader = await cmd.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) schemas.Add(reader.GetString(0));
            }, ct);
            return schemas;
        }

        public async Task<DataTable> ExecuteStoredProcedurePreviewAsync(ReportDefinition def, IReadOnlyCollection<ReportParameter> parameters, CancellationToken ct = default)
        {
            var data = new DataTable("PreviewData");

            await _resilience.ExecuteAsync(async token =>
            {
                await using var connection = new SqlConnection(def.BuildConnectionString());
                await connection.OpenAsync(token);

                string spFullName = $"[{def.SchemaName}].[{def.StoredProcedureName}]";
                await using var cmd = new SqlCommand(spFullName, connection)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = ReportDefinition.SchemaTimeoutSeconds
                };

                foreach (var parameter in parameters)
                {
                    string parameterName = parameter.Name.StartsWith("@", StringComparison.Ordinal) ? parameter.Name : "@" + parameter.Name;
                    object value = ConvertParameterValue(parameter);
                    cmd.Parameters.AddWithValue(parameterName, value);
                }

                await using var reader = await cmd.ExecuteReaderAsync(token);
                data.Load(reader);
            }, ct);

            return data;
        }

        public async Task<string?> TestConnectionAsync(ReportDefinition def, CancellationToken cancellationToken = default)
        {
            try
            {
                await _resilience.ExecuteAsync(async ct =>
                {
                    await using var connection = new SqlConnection(def.BuildConnectionString());
                    await connection.OpenAsync(ct);
                    await using var cmd = new SqlCommand("SELECT DB_NAME()", connection) { CommandTimeout = 10 };
                    await cmd.ExecuteScalarAsync(ct);
                }, cancellationToken);

                return null; 
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ── Type Mapping & Helpers ──────────────────────────────────────────────

        private static ReportField MapToReportField(string colName, string sqlType, int order)
        {
            return new ReportField
            {
                Name = colName,
                SqlDataType = sqlType,
                DotNetType = MapToDotNet(sqlType),
                IsDetailField = true,
                DisplayOrder = order,
                ItemWidth = 120,
                ItemHeight = 32,
                CanvasX = 16,
                CanvasY = 16,
                TextAlign = "Default",
                FontWeight = "Normal",
                BorderColor = "LightGrey"
            };
        }

        private static string BuildSqlDataType(string baseType, int maxLength, byte precision, byte scale)
        {
            string normalized = baseType.ToLowerInvariant();
            return normalized switch
            {
                "char" or "varchar" or "binary" or "varbinary" => maxLength == -1 ? $"{baseType}(max)" : $"{baseType}({maxLength})",
                "nchar" or "nvarchar" => maxLength == -1 ? $"{baseType}(max)" : $"{baseType}({maxLength / 2})",
                "decimal" or "numeric" => $"{baseType}({precision},{scale})",
                _ => baseType
            };
        }

        private static string MapSqlTypeToRdlc(string sqlDataType)
        {
            string normalized = sqlDataType.ToLowerInvariant();
            if (normalized.Contains("char") || normalized.Contains("text") || normalized.Contains("xml")) return "String";
            if (normalized.Contains("int")) return "Integer";
            if (normalized.Contains("date") || normalized.Contains("time")) return "DateTime";
            if (normalized.Contains("decimal") || normalized.Contains("money") || normalized.Contains("numeric") || normalized.Contains("float") || normalized.Contains("real")) return "Float";
            if (normalized.Contains("bit")) return "Boolean";
            return "String";
        }

        private static object ConvertParameterValue(ReportParameter parameter)
        {
            string rawValue = parameter.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawValue) && parameter.AllowBlank) return DBNull.Value;

            string typeName = parameter.SqlDataType;
            int typeLengthIndex = typeName.IndexOf('(');
            if (typeLengthIndex >= 0) typeName = typeName[..typeLengthIndex];
            typeName = typeName.Trim().ToLowerInvariant();

            return typeName switch
            {
                "bigint" => long.Parse(rawValue, CultureInfo.InvariantCulture),
                "bit" => ParseBit(rawValue),
                "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(rawValue, CultureInfo.InvariantCulture),
                "decimal" or "money" or "numeric" or "smallmoney" => decimal.Parse(rawValue, CultureInfo.InvariantCulture),
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
            if (bool.TryParse(rawValue, out bool result)) return result;
            return rawValue switch { "1" => true, "0" => false, _ => throw new FormatException($"Cannot convert '{rawValue}' to bit.") };
        }

        public static string MapToDotNet(string sqlTypeName) => SqlTypeMap.TryGetValue(sqlTypeName, out string? dotNetType) ? dotNetType : "System.String";
    }
}