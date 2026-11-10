#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;

namespace YMusic.WinUI.Services.Interfaces;

public sealed class AccentPreset
{
    private SolidColorBrush? _brush;

    public string Name { get; init; }
    public string NameEn { get; init; }
    public string Hex { get; init; }
    public string Key { get; init; }

    public AccentPreset(string name, string nameEn, string hex, string key)
    {
        Name = name;
        NameEn = nameEn;
        Hex = hex;
        Key = key;
    }

    public SolidColorBrush Brush
    {
        get
        {
            if (_brush == null)
            {
                var color = ParseHex(Hex);
                _brush = new SolidColorBrush(color);
            }
            return _brush;
        }
    }

    private static Windows.UI.Color ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Windows.UI.Color.FromArgb(255, 255, 211, 0);
        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
        {
            return Windows.UI.Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }
        if (clean.Length == 8 && uint.TryParse(clean, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint argb))
        {
            return Windows.UI.Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
        }
        return Windows.UI.Color.FromArgb(255, 255, 211, 0);
    }
}

public sealed record ThemePreset(string Name, string NameEn, string Key, string BackgroundHex, string CardHex, string SecondaryCardHex);

public interface IThemeService
{
    string CurrentTheme { get; }
    string CurrentAccentHex { get; }
    string CurrentBackdrop { get; }

    IReadOnlyList<AccentPreset> AvailableAccents { get; }
    IReadOnlyList<ThemePreset> AvailableThemes { get; }

    void ApplyTheme(string themeKey);
    void ApplyAccentColor(string hexColor);
    void ApplyBackdrop(string backdropKey);
    void PreInitializeResources();
    void Initialize();

    event EventHandler? ThemeChanged;
}
