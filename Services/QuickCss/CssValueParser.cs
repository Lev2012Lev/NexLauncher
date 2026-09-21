using System;
using System.Globalization;

namespace NexLauncher.Services.QuickCss;

/// <summary>Pure CSS value conversions; no Avalonia or current-culture dependencies.</summary>
public static class CssValueParser
{
    public static bool TryColor(string value, out CssColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        value = value.Trim();
        if (value[0] == '#')
        {
            var hex = value.AsSpan(1);
            if (hex.Length is not (3 or 4 or 6 or 8)) return false;
            if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)) return false;
            color = hex.Length switch
            {
                3 => new CssColor((byte)(((number >> 8) & 15) * 17), (byte)(((number >> 4) & 15) * 17), (byte)((number & 15) * 17)),
                4 => new CssColor((byte)(((number >> 12) & 15) * 17), (byte)(((number >> 8) & 15) * 17), (byte)(((number >> 4) & 15) * 17), (byte)((number & 15) * 17)),
                6 => new CssColor((byte)(number >> 16), (byte)(number >> 8), (byte)number),
                _ => new CssColor((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number)
            };
            return true;
        }
        var name = value.ToLowerInvariant();
        switch (name)
        {
            case "transparent": color = new CssColor(0, 0, 0, 0); return true;
            case "black": color = new CssColor(0, 0, 0); return true;
            case "white": color = new CssColor(255, 255, 255); return true;
            case "red": color = new CssColor(255, 0, 0); return true;
            case "green": color = new CssColor(0, 128, 0); return true;
            case "blue": color = new CssColor(0, 0, 255); return true;
            case "gray": case "grey": color = new CssColor(128, 128, 128); return true;
        }
        var rgba = name.StartsWith("rgba(", StringComparison.Ordinal);
        if ((!rgba && !name.StartsWith("rgb(", StringComparison.Ordinal)) || !name.EndsWith(')')) return false;
        var components = name[(rgba ? 5 : 4)..^1].Split(',');
        if (components.Length != (rgba ? 4 : 3)) return false;
        if (!TryChannel(components[0], out var r) || !TryChannel(components[1], out var g) || !TryChannel(components[2], out var b)) return false;
        var alpha = 1d;
        if (rgba && !TryUnitless(components[3], 0, 1, out alpha)) return false;
        color = new CssColor(r, g, b, (byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero));
        return true;
    }

    public static bool TryNumber(string value, double minimum, double maximum, out double number)
    {
        number = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
        value = value.Trim();
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return TryUnitless(value, minimum, maximum, out number);
    }

    public static bool TryBox(string value, double maximum, out CssBox box)
    {
        box = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) return false;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return false;
        Span<double> sides = stackalloc double[4];
        for (var index = 0; index < parts.Length; index++)
            if (!TryNumber(parts[index], 0, maximum, out sides[index])) return false;
        box = parts.Length switch
        {
            1 => new CssBox(sides[0], sides[0], sides[0], sides[0]),
            2 => new CssBox(sides[0], sides[1], sides[0], sides[1]),
            3 => new CssBox(sides[0], sides[1], sides[2], sides[1]),
            _ => new CssBox(sides[0], sides[1], sides[2], sides[3])
        };
        return true;
    }

    public static bool TryFontWeight(string value, out int weight)
    {
        weight = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase)) { weight = 400; return true; }
        if (value.Equals("bold", StringComparison.OrdinalIgnoreCase)) { weight = 700; return true; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out weight)
            && weight is >= 100 and <= 900 && weight % 100 == 0;
    }

    private static bool TryChannel(string value, out byte channel) =>
        byte.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out channel);

    private static bool TryUnitless(string value, double minimum, double maximum, out double number)
    {
        number = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Decimal notation only: no percentages, exponents, NaN/Infinity or thousands separators.
        foreach (var character in value.Trim())
            if (!(character is >= '0' and <= '9' or '.' or '+' or '-')) return false;
        return double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                   CultureInfo.InvariantCulture, out number)
               && double.IsFinite(number) && number >= minimum && number <= maximum;
    }
}
