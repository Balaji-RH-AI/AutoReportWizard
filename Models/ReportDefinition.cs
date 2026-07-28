using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AutoReportWizard.Models
{
    public enum AuthenticationType
    {
        Windows,
        SqlServer
    }

    /// <summary>
    /// Root report definition - the single source of truth that flows through
    /// all 5 wizard steps and is consumed directly by RdlcXmlEngine.
    /// 
    /// STORED PROCEDURE FIRST ARCHITECTURE:
    /// This model strictly relies on ingesting existing Stored Procedures,
    /// extracting their metadata, and generating RDLC files with
    /// CommandType=StoredProcedure. No dynamic SQL generation.
    /// </summary>
    public class ReportDefinition
    {
        // -- Step 1: Target Environment --
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "master";
        public AuthenticationType AuthType { get; set; } = AuthenticationType.Windows;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // -- Step 2: Stored Procedure Selection --
        public string SchemaName { get; set; } = "dbo";
        public string StoredProcedureName { get; set; } = string.Empty;
        public string ReportName { get; set; } = string.Empty;

        /// <summary>
        /// Output fields extracted from the selected Stored Procedure.
        /// Populated via sys.dm_exec_describe_first_result_set during schema discovery.
        /// </summary>
        public List<ReportField> OutputFields { get; set; } = new();

        /// <summary>
        /// Input parameters extracted from the selected Stored Procedure.
        /// Populated via sys.parameters during schema discovery.
        /// </summary>
        public List<ReportParameter> ProcedureParameters { get; set; } = new();

        /// <summary>
        /// All fields in the report (maintained for backward compatibility with canvas).
        /// Synchronized from OutputFields after SP discovery.
        /// </summary>
        public List<ReportField> Fields { get; set; } = new();

        /// <summary>
        /// Captures the visual layout components from the WPF Designer (Step 5)
        /// to accurately synchronize the generated RDLC with the user's drag-and-drop design.
        /// </summary>
        public List<ReportComponent> CanvasItems { get; set; } = new();

        /// <summary>
        /// User-defined input parameters for the Stored Procedure and RDLC file.
        /// Synced from <see cref="DynamicParameters"/> at generation/preview time.
        /// </summary>
        public List<ReportParameter> Parameters { get; set; } = new();

        /// <summary>
        /// Fully dynamic, user-defined parameters with prompt text and header mapping.
        /// </summary>
        public List<DynamicParameter> DynamicParameters { get; set; } = new();

        // -- Step 4: Header & Footer Config --
        public string ReportTitle { get; set; } = string.Empty;
        public string ReportSubtitle { get; set; } = string.Empty;
        public bool IncludeExecutionTime { get; set; } = true;
        public bool IncludePageNumbers { get; set; } = true;
        public bool IncludeGrandTotals { get; set; }
        public string? DynamicHeaderFieldName { get; set; }
        public string? HeaderSiteField { get; set; }
        public string? HeaderProcessDateField { get; set; }
        public string? HeaderJulianField { get; set; }
        public string? HeaderWorksourceField { get; set; }
        public string? HeaderLoadField { get; set; }

        public string StaticHeaderLeftLine1 { get; set; } = string.Empty;
        public string StaticHeaderLeftLine2 { get; set; } = string.Empty;
        
        // -- Step 5: Layout & Output --
        public string OutputDirectory { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AutoReportWizard");

        private bool _dbInfoLoadAttempted;

        // -- Derived Properties --
        public string StoredProcName =>
            "sp_" + System.Text.RegularExpressions.Regex.Replace(ReportName, @"[^A-Za-z0-9_]", "_");

        /// <summary>
        /// Fully qualified connection string built dynamically based on selected AuthType.
        /// </summary>
        public string BuildConnectionString()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = ServerName,
                InitialCatalog = DatabaseName,
                TrustServerCertificate = true,
                ConnectTimeout = 15,     // Fail faster if the server is unreachable
                MinPoolSize = 1,
                MaxPoolSize = 300,       // Matches your DBInfo.xml setting to prevent pool exhaustion
                Pooling = true
            };

            if (AuthType == AuthenticationType.Windows)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = Username;
                builder.Password = Password;
            }

            return builder.ConnectionString;
        }

        // -- Guardrail Constants --
        public bool LoadDbInfoConfiguration(string? dbInfoPath = null)
        {
            if (_dbInfoLoadAttempted && string.IsNullOrWhiteSpace(dbInfoPath))
                return false;

            _dbInfoLoadAttempted = true;

            string? resolvedPath = ResolveDbInfoPath(dbInfoPath);
            if (resolvedPath is null)
                return false;

            var document = XDocument.Load(resolvedPath);
            var dbAccess = document.Descendants()
                .FirstOrDefault(e => IsElement(e, "DBACCESS"));
            var common = (dbAccess ?? document.Root)?.Descendants()
                .FirstOrDefault(e => IsElement(e, "COMMON"));

            if (common is null)
                return false;

            string? server = ReadSetting(common, dbAccess,
                "ServerName", "Server", "DataSource", "SqlServer", "DbServer", "DB_SERVER");
            string? database = ReadSetting(common, dbAccess,
                "DatabaseName", "Database", "InitialCatalog", "Catalog", "DbName", "DB_NAME");
            string? username = ReadSetting(common, dbAccess,
                "Username", "UserName", "UserId", "UserID", "Uid", "User", "DbUser");
            string? encryptedPassword = ReadSetting(common, dbAccess,
                "EncryptedPassword", "PasswordEncrypted", "PwdEncrypted", "DBPASSWORD");
            string? password = encryptedPassword is null
                ? ReadSetting(common, dbAccess, "Password", "Pwd")
                : DecryptPassword(encryptedPassword);
            string? auth = ReadSetting(common, dbAccess,
                "Authentication", "AuthType", "IntegratedSecurity", "TrustedConnection");

            if (!string.IsNullOrWhiteSpace(server))
                ServerName = server.Trim();
            if (!string.IsNullOrWhiteSpace(database))
                DatabaseName = database.Trim();
            if (!string.IsNullOrWhiteSpace(username))
                Username = username.Trim();
            if (password is not null)
                Password = password;

            AuthType = ShouldUseSqlAuthentication(auth, Username)
                ? AuthenticationType.SqlServer
                : AuthenticationType.Windows;

            return true;
        }

        public static string DecryptPassword(string encryptedPassword)
        {
            if (string.IsNullOrWhiteSpace(encryptedPassword))
                return string.Empty;

            try
            {
                byte[] data = Convert.FromBase64String(encryptedPassword);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch
            {
                return encryptedPassword;
            }
        }
        
        private static bool ShouldUseSqlAuthentication(string? auth, string username)
        {
            if (!string.IsNullOrWhiteSpace(auth))
            {
                string normalized = auth.Trim().ToLowerInvariant();
                if (normalized is "sql" or "sqlserver" or "sql server" or "false" or "0")
                    return true;
                if (normalized is "windows" or "integrated" or "integratedsecurity" or "true" or "1")
                    return false;
            }

            return !string.IsNullOrWhiteSpace(username);
        }

        private static string? ResolveDbInfoPath(string? dbInfoPath)
        {
            if (!string.IsNullOrWhiteSpace(dbInfoPath))
                return File.Exists(dbInfoPath) ? dbInfoPath : null;

            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "DBInfo.xml"),
                Path.Combine(Environment.CurrentDirectory, "DBInfo.xml")
            };

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && directory is not null; i++, directory = directory.Parent)
                candidates.Add(Path.Combine(directory.FullName, "DBInfo.xml"));

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? ReadSetting(XElement common, XElement? dbAccess, params string[] names)
        {
            foreach (string name in names)
            {
                string? value = common.Elements().FirstOrDefault(e => IsElement(e, name))?.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value;

                value = common.Attributes().FirstOrDefault(a => IsName(a.Name.LocalName, name))?.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value;

                value = dbAccess?.Attributes().FirstOrDefault(a => IsName(a.Name.LocalName, name))?.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        private static bool IsElement(XElement element, string name) => IsName(element.Name.LocalName, name);
        private static bool IsName(string actual, string expected) => string.Equals(actual.Replace("_", string.Empty), expected.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase);

        public const int SchemaTimeoutSeconds = 60;
        public const int MaxDbRetries = 3;
    }
}
