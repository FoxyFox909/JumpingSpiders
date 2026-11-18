using System.Collections.Generic;
using System.Globalization;

namespace JumpingSpiders;

/// <summary>
/// Corrects settings read from a file that a player can edit (JumpingSpidersConfig.Validate uses it on
/// ModConfig/jumpingspiders.json). Each method takes the setting's name and value and returns the value to use:
/// the value itself when it is fine, else the nearest allowed value (or the default, for one that must be above
/// zero), with one sentence about the correction added to Warnings. Pure C#, covered by unit tests.
///
/// A value that is not a number (NaN) is never "fine": every test is written so that NaN fails it.
/// </summary>
public class SettingCorrector
{
    /// <summary>One sentence per corrected setting, for the server log.</summary>
    public readonly List<string> Warnings = new List<string>();

    /// <summary>The value, or min when it is lower.</summary>
    public double AtLeast(string name, double value, double min)
    {
        if (value >= min)
        {
            return value;
        }
        Warn(name, Text(value), "must be at least " + Text(min), Text(min));
        return min;
    }

    /// <summary>The value, or min when it is lower.</summary>
    public int AtLeast(string name, int value, int min)
    {
        if (value >= min)
        {
            return value;
        }
        Warn(name, Text(value), "must be at least " + Text(min), Text(min));
        return min;
    }

    /// <summary>The value, or the nearer of min and max when it is outside them.</summary>
    public double Between(string name, double value, double min, double max)
    {
        if (value >= min && value <= max)
        {
            return value;
        }
        double corrected = min;
        if (value > max)
        {
            corrected = max;
        }
        Warn(name, Text(value), "must be from " + Text(min) + " to " + Text(max), Text(corrected));
        return corrected;
    }

    /// <summary>The value, or fallback (the default) when it is not above zero.</summary>
    public double AboveZero(string name, double value, double fallback)
    {
        if (value > 0)
        {
            return value;
        }
        Warn(name, Text(value), "must be above 0", Text(fallback));
        return fallback;
    }

    /// <summary>
    /// The upper end of a range: high, or low when high is below it (the range then holds just that one value).
    /// </summary>
    public double NotBelow(string highName, double high, string lowName, double low)
    {
        if (high >= low)
        {
            return high;
        }
        Warn(highName, Text(high), "must not be below " + lowName + " (" + Text(low) + ")", Text(low));
        return low;
    }

    /// <summary>The same for whole numbers.</summary>
    public int NotBelow(string highName, int high, string lowName, int low)
    {
        if (high >= low)
        {
            return high;
        }
        Warn(highName, Text(high), "must not be below " + lowName + " (" + Text(low) + ")", Text(low));
        return low;
    }

    // Numbers as the JSON file writes them (a point, never a comma, whatever the server's language).
    private static string Text(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void Warn(string name, string value, string rule, string corrected)
    {
        Warnings.Add(name + " is " + value + " but " + rule + "; using " + corrected + ".");
    }
}
