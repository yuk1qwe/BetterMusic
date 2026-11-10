#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    private DispatcherQueue? _dispatcherQueue;

    public ILocalizationService? Loc => App.Services != null ? App.Loc : null;

    protected BaseViewModel()
    {
        try
        {
            if (App.Services != null)
            {
                App.Loc.LanguageChanged += OnLocalizationLanguageChanged;
            }
        }
        catch { }
    }

    private void OnLocalizationLanguageChanged(object? sender, string lang)
    {
        RunOnUi(() =>
        {
            OnPropertyChanged(nameof(Loc));
            OnLanguageChanged();
        });
    }

    protected virtual void OnLanguageChanged()
    {
    }

    protected DispatcherQueue GetDispatcherQueue()
    {
        return _dispatcherQueue ??= (DispatcherQueue.GetForCurrentThread()
            ?? DispatcherQueueController.CreateOnCurrentThread().DispatcherQueue);
    }

    public void RunOnUi(Action action)
    {
        var dq = App.MainDispatcherQueue ?? GetDispatcherQueue();
        if (dq.HasThreadAccess)
        {
            action();
        }
        else
        {
            dq.TryEnqueue(() => action());
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void ClearError()
    {
        RunOnUi(() => ErrorMessage = null);
    }

    public void SetBusy(bool busy)
    {
        RunOnUi(() => IsBusy = busy);
    }

    public void SetError(string? message)
    {
        RunOnUi(() => ErrorMessage = message);
    }
}
