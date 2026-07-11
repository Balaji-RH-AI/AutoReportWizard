using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoReportWizard.Models;

/// <summary>
/// Abstract base class for all report elements.
/// Implements INotifyPropertyChanged to support real-time WPF Canvas binding and property editing.
/// </summary>
public abstract class ReportComponent : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private double _x = 10;
    private double _y = 10;
    private double _width = 120;
    private double _height = 40;
    private int _zIndex = 0;

    public Guid Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value;
                OnPropertyChanged();
            }
        }
    }

    public double X
    {
        get => _x;
        set
        {
            if (Math.Abs(_x - value) > 0.001)
            {
                _x = value;
                OnPropertyChanged();
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (Math.Abs(_y - value) > 0.001)
            {
                _y = value;
                OnPropertyChanged();
            }
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            if (Math.Abs(_width - value) > 0.001)
            {
                _width = value;
                OnPropertyChanged();
            }
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (Math.Abs(_height - value) > 0.001)
            {
                _height = value;
                OnPropertyChanged();
            }
        }
    }

    public int ZIndex
    {
        get => _zIndex;
        set
        {
            if (_zIndex != value)
            {
                _zIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a customizable text label element.
/// </summary>
public class TextComponent : ReportComponent
{
    private string _text = "TextBlock Text";
    private double _fontSize = 12;
    private string _fontFamily = "Segoe UI";

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) > 0.001)
            {
                _fontSize = value;
                OnPropertyChanged();
            }
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily != value)
            {
                _fontFamily = value;
                OnPropertyChanged();
            }
        }
    }

    public TextComponent()
    {
        Width = 150;
        Height = 30;
    }
}

/// <summary>
/// Represents an image element inside the report layout.
/// </summary>
public class ImageComponent : ReportComponent
{
    private string _sourcePath = "C:\\Assets\\logo.png";

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (_sourcePath != value)
            {
                _sourcePath = value;
                OnPropertyChanged();
            }
        }
    }

    public ImageComponent()
    {
        Width = 100;
        Height = 100;
    }
}

/// <summary>
/// Represents a horizontal or vertical structural line separator.
/// </summary>
public class LineComponent : ReportComponent
{
    private double _length = 150;
    private string _orientation = "Horizontal"; // "Horizontal" or "Vertical"

    public double Length
    {
        get => _length;
        set
        {
            if (Math.Abs(_length - value) > 0.001)
            {
                _length = value;
                OnPropertyChanged();
                UpdateLineBounds();
            }
        }
    }

    public string Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                OnPropertyChanged();
                UpdateLineBounds();
            }
        }
    }

    public LineComponent()
    {
        Width = 150;
        Height = 10;
    }

    private void UpdateLineBounds()
    {
        // Automatically sync the bounding dimensions with the line's orientation and length
        if (Orientation.Equals("Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            Width = Length;
            Height = 10;
        }
        else
        {
            Width = 10;
            Height = Length;
        }
    }
}
