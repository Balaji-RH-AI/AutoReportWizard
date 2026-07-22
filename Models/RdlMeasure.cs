using System.Globalization;

namespace AutoReportWizard.Models;

/// <summary>
/// Converts WPF canvas measurements (96 DPI device-independent pixels) into
/// RDL 2016-compliant dimension strings (e.g. "1.25in").
///
/// At 96 DPI: 1 inch = 96 pixels, so the conversion formula is simply:
///   inches = pixels / 96.0
///
/// All output values are formatted as "0.####in" (max 4 decimal places,
/// trailing zeros stripped) to match Visual Studio RDLC designer output.
/// </summary>
public static class RdlMeasure
{
    /// <summary>The WPF standard DPI used for pixel-to-inch conversion.</summary>
    private const double DPI = 96.0;

    /// <summary>Minimum allowable output value in inches (zero).</summary>
    private const double MinInches = 0.0;

    /// <summary>
    /// Converts a WPF device-independent pixel value to an RDL inch string.
    /// </summary>
    /// <param name="pixels">
    /// The pixel measurement from the WPF canvas (X, Y, Width, or Height).
    /// </param>
    /// <returns>
    /// A valid RDL measurement string such as <c>"1.25in"</c>.
    /// Returns <c>"0in"</c> if the input is NaN, Infinity, or negative.
    /// </returns>
    public static string PixelsToIn(double pixels)
    {
        // Sanitize: NaN, ±Infinity, or any negative dimension is meaningless in RDL.
        if (double.IsNaN(pixels) || double.IsInfinity(pixels) || pixels < 0.0)
            return "0in";

        double inches = pixels / DPI;

        // Clamp to non-negative to guard against floating-point epsilon underflow.
        if (inches < MinInches)
            inches = MinInches;

        // "G4" gives up to 4 significant figures but still produces short strings
        // like "1in" rather than "1.0000in". We append the unit suffix manually
        // to match the RDL schema string format exactly.
        string formatted = inches.ToString("0.####", CultureInfo.InvariantCulture);
        return formatted + "in";
    }

    /// <summary>
    /// Converts an inch value directly to an RDL inch string, applying the same
    /// safety clamping as <see cref="PixelsToIn"/>. Useful when the caller already
    /// has an inch measurement (e.g. from <c>RdlcXmlEngine</c> constants).
    /// </summary>
    /// <param name="inches">The inch value to format.</param>
    /// <returns>A valid RDL measurement string such as <c>"0.25in"</c>.</returns>
    public static string InchesToIn(double inches)
    {
        if (double.IsNaN(inches) || double.IsInfinity(inches) || inches < 0.0)
            return "0in";

        string formatted = inches.ToString("0.####", CultureInfo.InvariantCulture);
        return formatted + "in";
    }
}
