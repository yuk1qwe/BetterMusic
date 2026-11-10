#nullable enable

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using YMusic.WinUI.Services;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class LoginPage : Page
{
    private static string OAuthUrl =>
        $"https://oauth.yandex.ru/authorize?response_type=token&client_id={Uri.EscapeDataString(EnvironmentConfig.YandexClientId)}";

    private bool _isLoggingIn;

    public LoginViewModel ViewModel { get; }

    public LoginPage()
    {
        ViewModel = App.GetService<LoginViewModel>();
        InitializeComponent();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
    }

    private void SignInWithBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        StartBrowserOAuth();
    }

    private void StartBrowserOAuth()
    {
        _isLoggingIn = false;
        ViewModel.ClearError();
        InitialLoginPanel.Visibility = Visibility.Collapsed;
        BrowserWaitingPanel.Visibility = Visibility.Visible;

        OpenBrowser();
    }

    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(OAuthUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ViewModel.SetError($"Не удалось открыть браузер: {ex.Message}");
        }
    }

    private void ReopenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        OpenBrowser();
    }

    private void CancelBrowserAuthButton_Click(object sender, RoutedEventArgs e)
    {
        BrowserWaitingPanel.Visibility = Visibility.Collapsed;
        InitialLoginPanel.Visibility = Visibility.Visible;
    }

    private async void PasteFromClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        bool success = await CheckClipboardForTokenAsync(showErrorIfNotFound: true);
        if (!success && !ViewModel.HasError)
        {
            ViewModel.SetError("В буфере обмена пока не найдена ссылка с токеном. Скопируйте адресную строку в браузере после входа (Ctrl+C).");
        }
    }

    private async Task<bool> CheckClipboardForTokenAsync(bool showErrorIfNotFound)
    {
        if (_isLoggingIn)
        {
            return false;
        }

        try
        {
            var dataPackage = Clipboard.GetContent();
            if (dataPackage != null && dataPackage.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackage.GetTextAsync();
                string? token = ExtractToken(text);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _isLoggingIn = true;
            
                    bool success = await ViewModel.LoginWithOAuthTokenAsync(token);
                    if (!success)
                    {
                        _isLoggingIn = false;
                    }
                    return success;
                }
            }
        }
        catch
        {
            // Ignore transient clipboard access errors
        }

        return false;
    }

    private static string? ExtractToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = Regex.Match(input, @"(?:^|[#&?])access_token=([^&]+)");
        if (match.Success)
        {
            return Uri.UnescapeDataString(match.Groups[1].Value);
        }

        string trimmed = input.Trim();
        if (trimmed.StartsWith("y0_") && !trimmed.Contains(" ") && trimmed.Length >= 20)
        {
            return trimmed;
        }

        return null;
    }

    private async void SignInWithEmbeddedButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoggingIn = false;
        OAuthOverlay.Visibility = Visibility.Visible;
        WebViewProgressRing.IsActive = true;

        try
        {
            await OAuthWebView.EnsureCoreWebView2Async();
            OAuthWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            OAuthWebView.CoreWebView2.SourceChanged -= OnWebViewSourceChanged;
            OAuthWebView.CoreWebView2.SourceChanged += OnWebViewSourceChanged;
            OAuthWebView.Source = new Uri(OAuthUrl);
        }
        catch (Exception ex)
        {
            OAuthOverlay.Visibility = Visibility.Collapsed;
            ViewModel.SetError($"Не удалось инициализировать браузер: {ex.Message}");
        }
    }

    private void CancelOAuthButton_Click(object sender, RoutedEventArgs e)
    {
        OAuthOverlay.Visibility = Visibility.Collapsed;
        try
        {
            OAuthWebView.CoreWebView2?.Stop();
        }
        catch
        {
            // Ignore clean up errors
        }
    }

    private void OAuthWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        CheckForTokenAndLogin(args.Uri, () => args.Cancel = true);
    }

    private void OAuthWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        WebViewProgressRing.IsActive = false;
    }

    private void OnWebViewSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        WebViewProgressRing.IsActive = false;
        CheckForTokenAndLogin(sender.Source, null);
    }

    private void CheckForTokenAndLogin(string uriString, Action? cancelAction)
    {
        if (_isLoggingIn || string.IsNullOrWhiteSpace(uriString) || !uriString.Contains("access_token="))
        {
            return;
        }

        string? token = ExtractToken(uriString);
        if (!string.IsNullOrWhiteSpace(token))
        {
            _isLoggingIn = true;
            cancelAction?.Invoke();

            DispatcherQueue.TryEnqueue(async () =>
            {
                OAuthOverlay.Visibility = Visibility.Collapsed;
                try
                {
                    OAuthWebView.CoreWebView2?.Stop();
                }
                catch
                {
                    // Ignore
                }

                bool success = await ViewModel.LoginWithOAuthTokenAsync(token);
                if (!success)
                {
                    _isLoggingIn = false;
                }
            });
        }
    }
}
