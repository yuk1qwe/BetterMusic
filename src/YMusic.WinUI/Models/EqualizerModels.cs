#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;

namespace YMusic.WinUI.Models;

public sealed partial class EqualizerBand : ObservableObject
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
    public double Frequency { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedGain))]
    private double _gain; // In decibels: -12.0 to +12.0

    public string FormattedGain => Math.Abs(Gain) < 0.05 ? "0 dB" : (Gain > 0 ? $"+{Gain:F1} dB" : $"{Gain:F1} dB");
}

public sealed record EqualizerPreset(string Name, IReadOnlyList<double> Gains);

public static class DefaultEqualizerPresets
{
    public static readonly IReadOnlyList<EqualizerPreset> Presets = new List<EqualizerPreset>
    {
        new("Flat", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        new("Rock", new double[] { 4.5, 3.5, 1.5, 0, -1.0, -0.5, 1.5, 3.0, 4.0, 4.5 }),
        new("Pop", new double[] { -1.5, -0.5, 1.5, 3.5, 4.0, 3.5, 1.5, 0.5, -0.5, -1.0 }),
        new("Bass Boost", new double[] { 6.0, 5.5, 4.0, 2.0, 0.5, 0, 0, 0, 0, 0 }),
        new("Vocal", new double[] { -2.0, -2.0, -1.0, 1.5, 3.5, 4.0, 3.5, 2.0, 1.0, 0 }),
        new("Jazz", new double[] { 3.0, 2.0, 1.0, 1.5, -1.0, -1.0, 0, 1.5, 2.5, 3.0 }),
        new("Electronic", new double[] { 5.0, 4.5, 2.0, 0, -1.5, 1.5, 0.5, 2.0, 4.0, 4.5 }),
        new("Classical", new double[] { 4.0, 3.0, 2.0, 1.5, -1.0, -1.0, 0, 2.0, 3.0, 3.5 }),
        new("Custom", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
    };

    public static readonly IReadOnlyList<(string Label, double Freq)> Standard10Bands = new List<(string, double)>
    {
        ("32", 32),
        ("64", 64),
        ("125", 125),
        ("250", 250),
        ("500", 500),
        ("1k", 1000),
        ("2k", 2000),
        ("4k", 4000),
        ("8k", 8000),
        ("16k", 16000)
    };
}
