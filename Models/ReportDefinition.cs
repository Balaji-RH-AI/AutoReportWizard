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
    /// Root report definition â€” the single source of truth that flows through
    /// all 5 wizard steps and is consumed directly by SqlGeneratorService
    /// and RdlcXmlEngine. No JSON serialization at any boundary.
    /// </summary>
    public class ReportDefinition
    {
        // â”€â”€ Step 1: Target Environment â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string ServerName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "master";
        public AuthenticationType AuthType { get; set; } = AuthenticationType.Windows;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        // â”€â”€ Step 2: Dataset Definition â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string SchemaName { get; set; } = "dbo";
        public string TableOrViewName { get; set; } = string.Empty;
        public string ReportName { get; set; } = string.Empty;

        /// <summary>
        /// All fields in the report. Each field carries its SQL type,
        /// .NET type, aggregation settings, and layout flags.
        /// </summary>
        public List<ReportField> Fields { get; set; } = new();

        /// <summary>
        /// Stores the raw, user-edited T-SQL from the Step 3 Live Editor.
        /// </summary>
        public string CustomSql { get; set; } = string.Empty;

        /// <summary>
        /// User-defined input parameters for the Stored Procedure and RDLC file.
        /// </summary>
        public List<ReportParameter> Parameters { get; set; } = new()
        {
            new ReportParameter { Name = "@ProcessDate", SqlDataType = "char(8)", RdlcDataType = "String" },
            new ReportParameter { Name = "@Siteid", SqlDataType = "int", RdlcDataType = "Integer" },
            new ReportParameter { Name = "@BatchNo", SqlDataType = "varchar(max)", RdlcDataType = "String" },
            new ReportParameter { Name = "@WorkSource", SqlDataType = "varchar(max)", RdlcDataType = "String" }
        };

        // â”€â”€ Step 4: Header & Footer Config â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string ReportTitle { get; set; } = string.Empty;
        public string ReportSubtitle { get; set; } = string.Empty;
        public bool IncludeExecutionTime { get; set; } = true;
        public bool IncludePageNumbers { get; set; } = true;
        public bool IncludeGrandTotals { get; set; }
        public string? DynamicHeaderFieldName { get; set; }
        public string HeaderSiteValue { get; set; } = "70 LouisvilleKY";
        public string HeaderProcessDateValue { get; set; } = "05/06/2026";
        public string HeaderJulianValue { get; set; } = "126";
        public string HeaderWorksourceValue { get; set; } = "7700428";
        public string HeaderLoadValue { get; set; } = "21";
        public string HeaderPageValue { get; set; } = "1 / 58";
        public string HeaderBatchNumber { get; set; } = "3292";
        public string StaticHeaderLeftLine1 { get; set; } = "Site : [Expr]";
        public string StaticHeaderLeftLine2 { get; set; } = "Process : [Expr]";

        // â”€â”€ Step 5: Layout & Output â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string OutputDirectory { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AutoReportWizard");

        private bool _dbInfoLoadAttempted;

        // â”€â”€ Derived Properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string StoredProcName =>
            "sp_" + System.Text.RegularExpressions.Regex.Replace(ReportName, @"[^A-Za-z0-9_]", "_");

        /// <summary>
        /// Fully qualified connection string built dynamically based on selected AuthType.
        /// </summary>
        public string BuildConnectionString()
        {
            LoadDbInfoConfiguration();

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = ServerName,
                InitialCatalog = DatabaseName,
                TrustServerCertificate = true, // Required for self-signed certs on corp networks
                ConnectTimeout = 30,
                MinPoolSize = 1,
                MaxPoolSize = 10,
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

        // â”€â”€ Guardrail Constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                // Attempt to decode the Base64 password from DBInfo.xml
                byte[] data = Convert.FromBase64String(encryptedPassword);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch
            {
                // If it fails to parse (e.g., it's plain text or a different encryption), fallback to raw
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
                string? value = common.Elements()
                    .FirstOrDefault(e => IsElement(e, name))
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = common.Attributes()
                    .FirstOrDefault(a => IsName(a.Name.LocalName, name))
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                value = dbAccess?.Attributes()
                    .FirstOrDefault(a => IsName(a.Name.LocalName, name))
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static bool IsElement(XElement element, string name) =>
            IsName(element.Name.LocalName, name);

        private static bool IsName(string actual, string expected) =>
            string.Equals(
                actual.Replace("_", string.Empty),
                expected.Replace("_", string.Empty),
                StringComparison.OrdinalIgnoreCase);

        public const int SchemaTimeoutSeconds = 60;
        public const int MaxDbRetries = 3;
    }
}

