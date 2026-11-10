#nullable enable

using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using YMusic.WinUI.ViewModels;

namespace YMusic.WinUI.Views.Pages;

public sealed partial class ProfilePage : Page
{
    public ProfileViewModel ViewModel { get; }
    private Storyboard? _categoryTransitionStoryboard;

    public ProfilePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ViewModel = App.GetService<ProfileViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (ViewModel.ShouldScrollToUpdates)
            {
                ViewModel.ShouldScrollToUpdates = false;
                ScrollToUpdates();
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Bindings.Update();
        _ = ViewModel.InitializeAsync();

        if (ViewModel.ShouldScrollToUpdates || (e.Parameter is string s && s == "Updates"))
        {
            ViewModel.ShouldScrollToUpdates = false;
            ViewModel.SelectedCategoryIndex = 7;
            ScrollToUpdates();
        }
        else
        {
            PlayCategoryTransition();
        }
    }

    public void ScrollToUpdates()
    {
        ViewModel.SelectedCategoryIndex = 7;
        if (!ViewModel.IsUpdateAvailable && !ViewModel.IsCheckingUpdates && !ViewModel.IsDownloadingUpdate)
        {
            _ = ViewModel.CheckForUpdatesAsync();
        }
        DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150);
            PageScrollViewer?.ChangeView(null, PageScrollViewer.ScrollableHeight, null, false);
            AboutAndUpdatesSection?.StartBringIntoView(new Microsoft.UI.Xaml.BringIntoViewOptions { AnimationDesired = true });
        });
    }

    private void CategoryPill_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string s && int.TryParse(s, out int idx))
        {
            if (ViewModel.SelectedCategoryIndex == idx)
                return;

            ViewModel.SelectedCategoryIndex = idx;
            PageScrollViewer?.ChangeView(null, 0, null, false);
            AnimatePillClick(b);
            PlayCategoryTransition();
        }
    }

    private void PlayCategoryTransition()
    {
        if (SettingsContentContainer == null || ContentTranslateTransform == null)
            return;

        _categoryTransitionStoryboard?.Stop();

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1. Subtle upward rise (Y: 16 -> 0)
        var slideAnim = new DoubleAnimation
        {
            From = 16.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideAnim, ContentTranslateTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        sb.Children.Add(slideAnim);

        // 2. Smooth opacity fade-in (0.0 -> 1.0)
        var fadeAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = ease
        };
        Storyboard.SetTarget(fadeAnim, SettingsContentContainer);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");
        sb.Children.Add(fadeAnim);

        _categoryTransitionStoryboard = sb;
        sb.Begin();
    }

    private void AnimatePillClick(Button button)
    {
        try
        {
            var scale = new ScaleTransform
            {
                CenterX = button.ActualWidth > 0 ? button.ActualWidth / 2 : 40,
                CenterY = button.ActualHeight > 0 ? button.ActualHeight / 2 : 18
            };
            button.RenderTransform = scale;

            var sb = new Storyboard();
            var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(180);

            var scaleX = new DoubleAnimation { From = 0.93, To = 1.0, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation { From = 0.93, To = 1.0, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");
            sb.Children.Add(scaleY);

            sb.Begin();
        }
        catch
        {
            // Ignore animation errors if visual tree is detached
        }
    }

    public string FormatLogin(string? login)
    {
        return string.IsNullOrWhiteSpace(login) ? string.Empty : $"@{login}";
    }

    public string FormatUid(string? uid)
    {
        return string.IsNullOrWhiteSpace(uid) ? string.Empty : $"UID: {uid}";
    }

    public Microsoft.UI.Xaml.Visibility HasEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void NotificationInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.DismissNotification();
    }

    private void AccentPreset_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button b)
        {
            var preset = (b.Tag as YMusic.WinUI.Services.Interfaces.AccentPreset) 
                      ?? (b.DataContext as YMusic.WinUI.Services.Interfaces.AccentPreset);
            if (preset != null)
            {
                ViewModel.SelectAccentCommand.Execute(preset);
            }
        }
    }
}
