#nullable enable

using System.ComponentModel;

namespace YMusic.WinUI.Services.Interfaces;

public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Current language code: "ru" or "en".
    /// </summary>
    string CurrentLanguage { get; set; }

    /// <summary>
    /// Indexer for XAML and code bindings: loc["NavExplore"].
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Get localized string by resource key.
    /// </summary>
    string GetString(string key);

    /// <summary>
    /// Get formatted localized string with arguments.
    /// </summary>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Event raised when the active language changes.
    /// </summary>
    event EventHandler<string>? LanguageChanged;
}
