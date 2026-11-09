#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using YMusic.WinUI.Services.Interfaces;
using YMusic.WinUI.Views;

namespace YMusic.WinUI.Services.Implementations;

public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;

    public event EventHandler? ThemeChanged;

    public string CurrentTheme => _settingsService.AppTheme;
    public string CurrentAccentHex => _settingsService.AccentColorHex;
    public string CurrentBackdrop => _settingsService.BackdropKind;

    // Persistent Accent Brushes (mutated in place so active controls redraw immediately)
    private readonly SolidColorBrush _accentFillColorDefaultBrush = new(ParseHex("#FFEAB308"));
    private readonly SolidColorBrush _accentFillColorSecondaryBrush = new(Darken(ParseHex("#FFEAB308"), 0.15));
    private readonly SolidColorBrush _accentFillColorTertiaryBrush = new(Darken(ParseHex("#FFEAB308"), 0.30));
    private readonly SolidColorBrush _accentTextFillColorPrimaryBrush = new(Lighten(ParseHex("#FFEAB308"), 0.15));
    private readonly SolidColorBrush _textOnAccentFillColorPrimaryBrush = new(Colors.Black);
    private readonly SolidColorBrush _textOnAccentFillColorSecondaryBrush = new(Color.FromArgb(200, 30, 30, 30));

    // Persistent Theme Surface Brushes
    private readonly SolidColorBrush _cardBackgroundFillColorDefaultBrush = new(ParseHex("#FF1C1C21"));
    private readonly SolidColorBrush _cardBackgroundFillColorSecondaryBrush = new(ParseHex("#FF26262D"));
    private readonly SolidColorBrush _solidBackgroundFillColorBaseBrush = new(ParseHex("#FF121215"));
    private readonly SolidColorBrush _applicationPageBackgroundThemeBrush = new(ParseHex("#FF121215"));
    private readonly SolidColorBrush _layerFillColorDefaultBrush = new(ParseHex("#FF16161A"));

    public IReadOnlyList<AccentPreset> AvailableAccents { get; } = new List<AccentPreset>
    {
        new("Янтарный мёд", "Warm Amber", "#FFEAB308", "amber"),
        new("Шалфей", "Nordic Sage", "#FF34D399", "sage"),
        new("Нежная лаванда", "Dusty Lavender", "#FF818CF8", "lavender"),
        new("Северный лазурный", "Nordic Sky", "#FF60A5FA", "sky"),
        new("Пастельный коралл", "Soft Coral", "#FFFB7185", "coral"),
        new("Тёплый персик", "Muted Peach", "#FFFB923C", "peach"),
        new("Морской эвкалипт", "Muted Eucalyptus", "#FF2DD4BF", "eucalyptus"),
        new("Пепельная роза", "Dusty Rose", "#FFF472B6", "rose"),
        new("Платиновый монохром", "Platinum Minimal", "#FFE2E8F0", "platinum")
    };

    public IReadOnlyList<ThemePreset> AvailableThemes { get; } = new List<ThemePreset>
    {
        new("Тёмная классическая", "Classic Dark", "Dark", "#FF121215", "#FF1C1C21", "#FF26262D"),
        new("OLED Абсолютно чёрный", "OLED Pure Black", "OLED", "#FF000000", "#FF0B0B0E", "#FF141418"),
        new("Глубокая полночь", "Deep Midnight", "Midnight", "#FF070B14", "#FF0E1627", "#FF152038"),
        new("Тёплый графит", "Warm Sepia", "Sepia", "#FF141210", "#FF1C1814", "#FF27221D"),
        new("Светлая тема", "Light Mode", "Light", "#FFF3F3F3", "#FFFFFFFF", "#FFF0F0F0"),
        new("Как в системе", "System Default", "Default", "#FF121215", "#FF1C1C21", "#FF26262D")
    };

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Injects persistent brushes into Application.Current.Resources and ThemeDictionaries
    /// BEFORE any window or control is instantiated, ensuring WinUI uses them from the first render.
    /// </summary>
    public void PreInitializeResources()
    {
        try
        {
            ApplyAccentColorInternal(CurrentAccentHex, saveSetting: false);
            ApplyThemeInternal(CurrentTheme, saveSetting: false);

            var res = Application.Current.Resources;
            RegisterBrushesInDictionary(res);

            foreach (string themeName in new[] { "Dark", "Light", "Default" })
            {
                if (!res.ThemeDictionaries.TryGetValue(themeName, out var dObj) || dObj is not ResourceDictionary td)
                {
                    td = new ResourceDictionary();
                    res.ThemeDictionaries[themeName] = td;
                }
                RegisterBrushesInDictionary(td);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] PreInitializeResources error: {ex.Message}");
        }
    }

    public void Initialize()
    {
        try
        {
            PreInitializeResources();

            if (App.MainWindow is ShellWindow shell)
            {
                RegisterBrushesInDictionary(shell.WindowRootGrid.Resources);
                foreach (string themeName in new[] { "Dark", "Light", "Default" })
                {
                    if (!shell.WindowRootGrid.Resources.ThemeDictionaries.TryGetValue(themeName, out var dObj) || dObj is not ResourceDictionary td)
                    {
                        td = new ResourceDictionary();
                        shell.WindowRootGrid.Resources.ThemeDictionaries[themeName] = td;
                    }
                    RegisterBrushesInDictionary(td);
                }
            }

            ApplyAccentColor(CurrentAccentHex);
            ApplyTheme(CurrentTheme);
            ApplyBackdrop(CurrentBackdrop);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] Initialize error: {ex.Message}");
        }
    }

    public void ApplyAccentColor(string hexColor)
    {
        ApplyAccentColorInternal(hexColor, saveSetting: true);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAccentColorInternal(string hexColor, bool saveSetting)
    {
        try
        {
            Color baseColor = ParseHex(hexColor);
            if (saveSetting)
            {
                _settingsService.AccentColorHex = FormatHex(baseColor);
            }

            double luminance = (0.299 * baseColor.R) + (0.587 * baseColor.G) + (0.114 * baseColor.B);
            Color textOnAccent = luminance > 140 ? Colors.Black : Colors.White;
            Color textOnAccentSec = luminance > 140
                ? Color.FromArgb(200, 30, 30, 30)
                : Color.FromArgb(210, 240, 240, 240);

            Color light1 = Lighten(baseColor, 0.15);
            Color light2 = Lighten(baseColor, 0.30);
            Color light3 = Lighten(baseColor, 0.45);
            Color dark1 = Darken(baseColor, 0.15);
            Color dark2 = Darken(baseColor, 0.30);
            Color dark3 = Darken(baseColor, 0.45);

            // Mutate persistent brushes in place so active UI controls update immediately on GPU
            _accentFillColorDefaultBrush.Color = baseColor;
            _accentFillColorSecondaryBrush.Color = dark1;
            _accentFillColorTertiaryBrush.Color = dark2;
            _accentTextFillColorPrimaryBrush.Color = light1;
            _textOnAccentFillColorPrimaryBrush.Color = textOnAccent;
            _textOnAccentFillColorSecondaryBrush.Color = textOnAccentSec;

            // Update Color values in Application resources & ThemeDictionaries
            var res = Application.Current.Resources;
            UpdateColorsInDict(res, baseColor, light1, light2, light3, dark1, dark2, dark3);
            RegisterBrushesInDictionary(res);

            foreach (string themeName in new[] { "Dark", "Light", "Default" })
            {
                if (res.ThemeDictionaries.TryGetValue(themeName, out var dObj) && dObj is ResourceDictionary td)
                {
                    UpdateColorsInDict(td, baseColor, light1, light2, light3, dark1, dark2, dark3);
                    RegisterBrushesInDictionary(td);
                }
            }

            if (App.MainWindow is ShellWindow shell)
            {
                shell.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateColorsInDict(shell.WindowRootGrid.Resources, baseColor, light1, light2, light3, dark1, dark2, dark3);
                    RegisterBrushesInDictionary(shell.WindowRootGrid.Resources);
                    foreach (string themeName in new[] { "Dark", "Light", "Default" })
                    {
                        if (shell.WindowRootGrid.Resources.ThemeDictionaries.TryGetValue(themeName, out var dObj) && dObj is ResourceDictionary td)
                        {
                            UpdateColorsInDict(td, baseColor, light1, light2, light3, dark1, dark2, dark3);
                            RegisterBrushesInDictionary(td);
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] ApplyAccentColor error: {ex.Message}");
        }
    }

    public void ApplyTheme(string themeKey)
    {
        ApplyThemeInternal(themeKey, saveSetting: true);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyThemeInternal(string themeKey, bool saveSetting)
    {
        try
        {
            if (saveSetting)
            {
                _settingsService.AppTheme = themeKey;
            }

            var themePreset = AvailableThemes.FirstOrDefault(t => t.Key.Equals(themeKey, StringComparison.OrdinalIgnoreCase))
                ?? AvailableThemes[0];

            ElementTheme elemTheme = themeKey switch
            {
                "Light" => ElementTheme.Light,
                "Default" => ElementTheme.Default,
                _ => ElementTheme.Dark
            };

            Color bgCol = ParseHex(themePreset.BackgroundHex);
            Color cardCol = ParseHex(themePreset.CardHex);
            Color secCardCol = ParseHex(themePreset.SecondaryCardHex);

            // Mutate surface brushes in place so cards and backgrounds update live
            _cardBackgroundFillColorDefaultBrush.Color = cardCol;
            _cardBackgroundFillColorSecondaryBrush.Color = secCardCol;
            _solidBackgroundFillColorBaseBrush.Color = bgCol;
            _applicationPageBackgroundThemeBrush.Color = bgCol;
            _layerFillColorDefaultBrush.Color = cardCol;

            var res = Application.Current.Resources;
            RegisterBrushesInDictionary(res);

            foreach (string themeName in new[] { "Dark", "Light", "Default" })
            {
                if (res.ThemeDictionaries.TryGetValue(themeName, out var dObj) && dObj is ResourceDictionary td)
                {
                    RegisterBrushesInDictionary(td);
                }
            }

            if (App.MainWindow is ShellWindow shell)
            {
                shell.DispatcherQueue.TryEnqueue(() =>
                {
                    shell.WindowRootGrid.RequestedTheme = elemTheme;
                    RegisterBrushesInDictionary(shell.WindowRootGrid.Resources);
                    UpdateBackdropAndWindowBackground(shell, themePreset);
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] ApplyTheme error: {ex.Message}");
        }
    }

    public void ApplyBackdrop(string backdropKey)
    {
        try
        {
            _settingsService.BackdropKind = backdropKey;

            var themePreset = AvailableThemes.FirstOrDefault(t => t.Key.Equals(CurrentTheme, StringComparison.OrdinalIgnoreCase))
                ?? AvailableThemes[0];

            if (App.MainWindow is ShellWindow shell)
            {
                shell.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateBackdropAndWindowBackground(shell, themePreset);
                });
            }

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] ApplyBackdrop error: {ex.Message}");
        }
    }

    private void UpdateBackdropAndWindowBackground(ShellWindow shell, ThemePreset themePreset)
    {
        Color bgCol = ParseHex(themePreset.BackgroundHex);

        switch (CurrentBackdrop)
        {
            case "Solid":
                shell.SystemBackdrop = null;
                shell.WindowRootGrid.Background = new SolidColorBrush(bgCol);
                break;

            case "MicaAlt":
                shell.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                if (themePreset.Key == "OLED")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0));
                }
                else if (themePreset.Key == "Midnight")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(200, 7, 11, 20));
                }
                else if (themePreset.Key == "Sepia")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(200, 20, 18, 16));
                }
                else
                {
                    shell.WindowRootGrid.Background = null;
                }
                break;

            case "Acrylic":
                shell.SystemBackdrop = new DesktopAcrylicBackdrop();
                if (themePreset.Key == "OLED")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
                }
                else if (themePreset.Key == "Midnight")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(180, 7, 11, 20));
                }
                else if (themePreset.Key == "Sepia")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(180, 20, 18, 16));
                }
                else
                {
                    shell.WindowRootGrid.Background = null;
                }
                break;

            case "Mica":
            default:
                shell.SystemBackdrop = new MicaBackdrop();
                if (themePreset.Key == "OLED")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0));
                }
                else if (themePreset.Key == "Midnight")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(200, 7, 11, 20));
                }
                else if (themePreset.Key == "Sepia")
                {
                    shell.WindowRootGrid.Background = new SolidColorBrush(Color.FromArgb(200, 20, 18, 16));
                }
                else
                {
                    shell.WindowRootGrid.Background = null;
                }
                break;
        }
    }

    private void RegisterBrushesInDictionary(ResourceDictionary dict)
    {
        dict["AccentFillColorDefaultBrush"] = _accentFillColorDefaultBrush;
        dict["AccentFillColorSecondaryBrush"] = _accentFillColorSecondaryBrush;
        dict["AccentFillColorTertiaryBrush"] = _accentFillColorTertiaryBrush;
        dict["AccentTextFillColorPrimaryBrush"] = _accentTextFillColorPrimaryBrush;
        dict["TextOnAccentFillColorPrimaryBrush"] = _textOnAccentFillColorPrimaryBrush;
        dict["TextOnAccentFillColorSecondaryBrush"] = _textOnAccentFillColorSecondaryBrush;
        dict["SystemControlHighlightAccentBrush"] = _accentFillColorDefaultBrush;
        dict["SystemControlHighlightAltAccentBrush"] = _accentFillColorSecondaryBrush;

        // ToggleSwitch:
        dict["ToggleSwitchFillOn"] = _accentFillColorDefaultBrush;
        dict["ToggleSwitchFillOnPointerOver"] = _accentFillColorSecondaryBrush;
        dict["ToggleSwitchFillOnPressed"] = _accentFillColorTertiaryBrush;
        dict["ToggleSwitchStrokeOn"] = _accentFillColorDefaultBrush;
        dict["ToggleSwitchStrokeOnPointerOver"] = _accentFillColorSecondaryBrush;
        dict["ToggleSwitchStrokeOnPressed"] = _accentFillColorTertiaryBrush;
        dict["ToggleSwitchKnobFillOn"] = _textOnAccentFillColorPrimaryBrush;

        // Slider:
        dict["SliderTrackValueFill"] = _accentFillColorDefaultBrush;
        dict["SliderTrackValueFillPointerOver"] = _accentFillColorSecondaryBrush;
        dict["SliderTrackValueFillPressed"] = _accentFillColorTertiaryBrush;
        dict["SliderThumbBackground"] = _accentFillColorDefaultBrush;
        dict["SliderThumbBackgroundPointerOver"] = _accentFillColorSecondaryBrush;
        dict["SliderThumbBackgroundPressed"] = _accentFillColorTertiaryBrush;

        // Button (AccentButtonStyle):
        dict["AccentButtonBackground"] = _accentFillColorDefaultBrush;
        dict["AccentButtonBackgroundPointerOver"] = _accentFillColorSecondaryBrush;
        dict["AccentButtonBackgroundPressed"] = _accentFillColorTertiaryBrush;
        dict["AccentButtonForeground"] = _textOnAccentFillColorPrimaryBrush;
        dict["AccentButtonForegroundPointerOver"] = _textOnAccentFillColorPrimaryBrush;
        dict["AccentButtonForegroundPressed"] = _textOnAccentFillColorSecondaryBrush;

        // Navigation & Selection:
        dict["NavigationViewSelectionIndicatorForeground"] = _accentFillColorDefaultBrush;
        dict["NavigationViewItemForegroundSelected"] = _accentFillColorDefaultBrush;
        dict["NavigationViewItemForegroundSelectedPointerOver"] = _accentFillColorSecondaryBrush;

        // Surface / Cards:
        dict["CardBackgroundFillColorDefaultBrush"] = _cardBackgroundFillColorDefaultBrush;
        dict["CardBackgroundFillColorSecondaryBrush"] = _cardBackgroundFillColorSecondaryBrush;
        dict["SolidBackgroundFillColorBaseBrush"] = _solidBackgroundFillColorBaseBrush;
        dict["ApplicationPageBackgroundThemeBrush"] = _applicationPageBackgroundThemeBrush;
        dict["LayerFillColorDefaultBrush"] = _layerFillColorDefaultBrush;
    }

    private static void UpdateColorsInDict(ResourceDictionary dict, Color baseColor, Color light1, Color light2, Color light3, Color dark1, Color dark2, Color dark3)
    {
        dict["SystemAccentColor"] = baseColor;
        dict["SystemAccentColorLight1"] = light1;
        dict["SystemAccentColorLight2"] = light2;
        dict["SystemAccentColorLight3"] = light3;
        dict["SystemAccentColorDark1"] = dark1;
        dict["SystemAccentColorDark2"] = dark2;
        dict["SystemAccentColorDark3"] = dark3;
    }

    #region Color Utilities

    public static Color ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.FromArgb(255, 255, 211, 0);

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6)
        {
            if (uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return Color.FromArgb(
                    255,
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
            }
        }
        else if (clean.Length == 8)
        {
            if (uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return Color.FromArgb(
                    (byte)((argb >> 24) & 0xFF),
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));
            }
        }

        return Color.FromArgb(255, 255, 211, 0);
    }

    public static string FormatHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Lighten(Color c, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);
        return Color.FromArgb(
            c.A,
            (byte)Math.Clamp((int)(c.R + (255 - c.R) * factor), 0, 255),
            (byte)Math.Clamp((int)(c.G + (255 - c.G) * factor), 0, 255),
            (byte)Math.Clamp((int)(c.B + (255 - c.B) * factor), 0, 255));
    }

    public static Color Darken(Color c, double factor)
    {
        factor = Math.Clamp(factor, 0.0, 1.0);
        return Color.FromArgb(
            c.A,
            (byte)Math.Clamp((int)(c.R * (1.0 - factor)), 0, 255),
            (byte)Math.Clamp((int)(c.G * (1.0 - factor)), 0, 255),
            (byte)Math.Clamp((int)(c.B * (1.0 - factor)), 0, 255));
    }

    #endregion
}
