#nullable enable

using YMusic.WinUI.Models;

namespace YMusic.WinUI.Services.Interfaces;

public interface IEqualizerService
{
    bool IsEnabled { get; set; }
    IReadOnlyList<EqualizerBand> Bands { get; }
    IReadOnlyList<EqualizerPreset> Presets { get; }
    string CurrentPresetName { get; }

    void ApplyPreset(string presetName);
    void SetBandGain(int index, double gainDb);
    void ResetToFlat();

    event EventHandler? EqualizerChanged;
}
