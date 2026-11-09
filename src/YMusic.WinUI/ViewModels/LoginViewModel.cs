#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.ViewModels;

public sealed partial class LoginViewModel : BaseViewModel
{
    private readonly IYandexMusicService _musicService;
    private readonly ICredentialStorageService _credentialStorage;
    private readonly IAudioPlayerService _audioPlayer;

    [ObservableProperty]
    private string _tokenInput = string.Empty;

    [ObservableProperty]
    private UserProfile? _currentUser;

    [ObservableProperty]
    private bool _isLoggedIn;

    public event EventHandler? LoginSucceeded;
    public event EventHandler? LoggedOut;

    public LoginViewModel(
        IYandexMusicService musicService,
        ICredentialStorageService credentialStorage,
        IAudioPlayerService audioPlayer)
    {
        _musicService = musicService;
        _credentialStorage = credentialStorage;
        _audioPlayer = audioPlayer;

        _musicService.UnauthorizedResponseReceived += (_, _) =>
        {
            HandleUnauthorized();
        };
    }

    public async Task CheckExistingLoginAsync()
    {
        string? storedToken = _credentialStorage.GetToken();
        if (string.IsNullOrWhiteSpace(storedToken))
        {
            RunOnUi(() => IsLoggedIn = false);
            return;
        }

        SetBusy(true);
        ClearError();

        try
        {
            _musicService.SetOAuthToken(storedToken);
            var profile = await _musicService.GetAccountStatusAsync();

            if (profile != null && !string.IsNullOrWhiteSpace(profile.UserId))
            {
                _credentialStorage.SaveUserId(profile.UserId);
                RunOnUi(() =>
                {
                    CurrentUser = profile;
                    IsLoggedIn = true;
                });
            }
            else
            {
                RunOnUi(() =>
                {
                    IsLoggedIn = false;
                    ErrorMessage = "Unable to connect to Yandex Music. Please check your internet connection.";
                });
            }
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                ErrorMessage = $"Authentication check failed: {ex.Message}";
                IsLoggedIn = false;
            });
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    public async Task<bool> LoginWithOAuthTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            SetError("Token cannot be empty.");
            return false;
        }

        SetBusy(true);
        ClearError();

        try
        {
            string trimmedToken = token.Trim();
            _musicService.SetOAuthToken(trimmedToken);

            var (profile, error) = await _musicService.GetAccountStatusDetailedAsync();
            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId))
            {
                SetError(error ?? "Invalid or expired token. Could not retrieve profile.");
                return false;
            }

            _credentialStorage.SaveToken(trimmedToken, profile.UserId);

            RunOnUi(() =>
            {
                CurrentUser = profile;
                IsLoggedIn = true;
                TokenInput = string.Empty;
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            });

            return true;
        }
        catch (Exception ex)
        {
            SetError($"Login error: {ex.Message}");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task LoginWithTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(TokenInput))
        {
            SetError("Please enter your OAuth token.");
            return;
        }

        await LoginWithOAuthTokenAsync(TokenInput);
    }

    [RelayCommand]
    private void Logout()
    {
        _credentialStorage.RemoveToken();
        _audioPlayer.StopAndClear();
        RunOnUi(() =>
        {
            CurrentUser = null;
            IsLoggedIn = false;
            LoggedOut?.Invoke(this, EventArgs.Empty);
        });
    }

    private void HandleUnauthorized()
    {
        _credentialStorage.RemoveToken();
        _audioPlayer.StopAndClear();
        RunOnUi(() =>
        {
            CurrentUser = null;
            IsLoggedIn = false;
            ErrorMessage = "Session expired. Please log in again.";
            LoggedOut?.Invoke(this, EventArgs.Empty);
        });
    }
}
