using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoReportWizard.Models;

/// <summary>
/// User-defined report parameter with prompt text and optional header mapping.
/// Flows through the wizard and into T-SQL, RDLC ReportParameters, and the preview UI.
/// </summary>
public class DynamicParameter : INotifyPropertyChanged
{
    private string _parameterName = string.Empty;
    private string _dataType = "varchar(50)";
    private string _promptText = string.Empty;
    private string _value = string.Empty;
    private bool _mapsToHeader;
    private HeaderZone _headerZone = HeaderZone.Left;
    private int _headerOrder;

    /// <summary>SQL parameter name including @ prefix (e.g. @ProcessDate).</summary>
    public string ParameterName
    {
        get => _parameterName;
        set { _parameterName = value; OnPropertyChanged(); OnPropertyChanged(nameof(RdlcParameterName)); }
    }

    /// <summary>SQL data type declaration (e.g. char(8), int, varchar(max)).</summary>
    public string DataType
    {
        get => _dataType;
        set { _dataType = value; OnPropertyChanged(); OnPropertyChanged(nameof(RdlcDataType)); }
    }

    /// <summary>User-facing label shown in the preview parameter bar.</summary>
    public string PromptText
    {
        get => _promptText;
        set { _promptText = value; OnPropertyChanged(); }
    }

    /// <summary>Runtime value entered by the user before running preview.</summary>
    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    /// <summary>When true, this parameter appears in the RDLC page header.</summary>
    public bool MapsToHeader
    {
        get => _mapsToHeader;
        set { _mapsToHeader = value; OnPropertyChanged(); }
    }

    /// <summary>Header zone placement (left, center, or right).</summary>
    public HeaderZone HeaderZone
    {
        get => _headerZone;
        set { _headerZone = value; OnPropertyChanged(); }
    }

    /// <summary>Sort order within the header zone.</summary>
    public int HeaderOrder
    {
        get => _headerOrder;
        set { _headerOrder = value; OnPropertyChanged(); }
    }

    /// <summary>RDLC parameter name without the @ prefix.</summary>
    public string RdlcParameterName =>
        ParameterName.TrimStart('@');

    /// <summary>SSRS ReportParameter DataType derived from SQL type.</summary>
    public string RdlcDataType => MapSqlTypeToRdlc(DataType);

    public static string MapSqlTypeToRdlc(string sqlDataType)
    {
        string typeName = sqlDataType;
        int paren = typeName.IndexOf('(');
        if (paren >= 0)
            typeName = typeName[..paren];

        return typeName.Trim().ToLowerInvariant() switch
        {
            "int" or "bigint" or "smallint" or "tinyint" => "Integer",
            "bit" => "Boolean",
            "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
            "decimal" or "money" or "numeric" or "smallmoney" or "float" or "real" => "Float",
            _ => "String"
        };
    }

    /// <summary>Converts to legacy ReportParameter for database execution.</summary>
    public ReportParameter ToReportParameter() => new()
    {
        Name = ParameterName.StartsWith('@') ? ParameterName : $"@{ParameterName}",
        SqlDataType = DataType,
        RdlcDataType = RdlcDataType,
        Value = Value == " " ? null : (string.IsNullOrWhiteSpace(Value) ? null : Value.Trim()),
        AllowBlank = RdlcDataType == "String"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum HeaderZone
{
    Left,
    Center,
    Right
}
