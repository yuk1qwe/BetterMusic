#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using YMusic.WinUI.Helpers;
using YMusic.WinUI.Models;
using YMusic.WinUI.Services.Interfaces;

namespace YMusic.WinUI.Views.Navigation;

public sealed class PinnedNavManager : IDisposable
{
    private sealed class PinnedItemHolder
    {
        public NavigationViewItem NavItem { get; set; } = null!;
        public Grid ContentGrid { get; set; } = null!;
        public PinnedItemRecord Record { get; set; } = null!;
        public CompositeTransform Transform { get; set; } = null!;
        public double CurrentTargetShiftY { get; set; }
        public Storyboard? ActiveStoryboard { get; set; }
    }

    private readonly NavigationView _navView;
    private readonly IPinnedItemsService _pinnedItemsService;
    private readonly Func<bool> _isAuthenticated;
    private readonly Action<string> _onNavigateToTag;
    private readonly DispatcherQueue _dispatcherQueue;

    private readonly List<object> _pinnedNavItems = new();
    private readonly List<PinnedItemHolder> _pinnedHolders = new();

    private ScrollViewer? _navScrollViewer;
    private DispatcherQueueTimer? _autoScrollTimer;
    private double _autoScrollVelocity;
    private Point _startMousePosInNav;
    private Point _lastKnownMousePosInNav;
    private double _startScrollOffset;
    private bool _isPointerDown;
    private bool _isDraggingPinned;
    private int _dragSourceIndex = -1;
    private int _currentTargetIndex = -1;
    private PinnedItemHolder? _draggedHolder;
    private PinnedItemRecord? _potentialClickRecord;
    private DateTime _lastDragEndTime = DateTime.MinValue;
    private bool _isRebuilding;
    private bool _isRebuildingPinnedDrag;

    public bool IsDragging => _isDraggingPinned;
    public bool IsRebuilding => _isRebuilding;
    public DateTime LastDragEndTime => _lastDragEndTime;

    public void CancelDrag()
    {
        _lastDragEndTime = DateTime.UtcNow;
        ResetDragState();
    }

    public PinnedNavManager(
        NavigationView navView,
        IPinnedItemsService pinnedItemsService,
        Func<bool> isAuthenticated,
        Action<string> onNavigateToTag,
        DispatcherQueue dispatcherQueue)
    {
        _navView = navView;
        _pinnedItemsService = pinnedItemsService;
        _isAuthenticated = isAuthenticated;
        _onNavigateToTag = onNavigateToTag;
        _dispatcherQueue = dispatcherQueue;
    }

    public void Initialize()
    {
        _pinnedItemsService.PinStateChanged += PinnedItemsService_PinStateChanged;
        App.Loc.LanguageChanged += Loc_LanguageChanged;
        _navView.PaneOpened += NavView_PaneOpened;
        _navView.PaneClosed += NavView_PaneClosed;
    }

    private void PinnedItemsService_PinStateChanged(object? sender, string? e)
    {
        if (_isRebuildingPinnedDrag) return;
        _dispatcherQueue.TryEnqueue(RefreshPinnedMenuItems);
    }

    private void Loc_LanguageChanged(object? sender, string? e) =>
        _dispatcherQueue.TryEnqueue(RefreshPinnedMenuItems);

    private void NavView_PaneOpened(NavigationView sender, object args)
    {
        _navScrollViewer = null;
        RefreshPinnedMenuItems();
    }

    private void NavView_PaneClosed(NavigationView sender, object args)
    {
        _navScrollViewer = null;
        RefreshPinnedMenuItems();
    }

    public void RefreshPinnedMenuItems()
    {
        _isRebuilding = true;
        try
        {
            string? selectedTag = (_navView.SelectedItem as NavigationViewItem)?.Tag?.ToString();

            ResetDragState();
            _pinnedHolders.Clear();
            _navScrollViewer = null;

            foreach (var item in _pinnedNavItems)
            {
                _navView.MenuItems.Remove(item);
            }
            _pinnedNavItems.Clear();

            if (!_isAuthenticated()) return;

            var pinned = _pinnedItemsService.GetPinnedItems();
            if (pinned.Count == 0) return;

            var separator = new NavigationViewItemSeparator();
            var header = new NavigationViewItemHeader
            {
                Content = App.Loc?.CurrentLanguage == "en" ? "Pinned" : "Закреплено",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 11,
                Opacity = 0.7
            };

            _navView.MenuItems.Add(separator);
            _pinnedNavItems.Add(separator);

            _navView.MenuItems.Add(header);
            _pinnedNavItems.Add(header);

            bool isExpanded = _navView.IsPaneOpen;
            int totalCount = pinned.Count;

            for (int i = 0; i < totalCount; i++)
            {
                var record = pinned[i];
                int currentIndex = i;

                var transform = new CompositeTransform();
                var navItem = new NavigationViewItem
                {
                    Tag = record.Key,
                    MinHeight = 44,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    SelectsOnInvoked = true,
                    ManipulationMode = ManipulationModes.None,
                    RenderTransform = transform,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };
                navItem.RegisterPropertyChangedCallback(NavigationViewItem.IsSelectedProperty, (s, dp) =>
                {
                    if (_isDraggingPinned && s is NavigationViewItem nvi && nvi.IsSelected)
                    {
                        nvi.IsSelected = false;
                    }
                });
                CursorHelper.SetUseHandCursor(navItem, true);

                var grid = CreatePinnedNavItemContent(navItem, record, isExpanded);

                var holder = new PinnedItemHolder
                {
                    NavItem = navItem,
                    ContentGrid = grid,
                    Record = record,
                    Transform = transform
                };
                _pinnedHolders.Add(holder);

                ToolTipService.SetToolTip(navItem, record.Title);

                navItem.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Card_PointerPressed), handledEventsToo: true);
                navItem.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Card_PointerMoved), handledEventsToo: true);
                navItem.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Card_PointerReleased), handledEventsToo: true);
                navItem.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Card_PointerCaptureLost), handledEventsToo: true);
                navItem.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(Card_PointerCanceled), handledEventsToo: true);

                var flyout = new MenuFlyout();

                if (currentIndex > 0)
                {
                    var moveToTopItem = new MenuFlyoutItem
                    {
                        Text = App.Loc?.CurrentLanguage == "en" ? "Move to top" : "В самое начало",
                        Icon = new FontIcon { Glyph = "\uE898" },
                        Tag = record.Key
                    };
                    moveToTopItem.Click += (s, e) =>
                    {
                        if (s is FrameworkElement fe && fe.Tag is string key)
                        {
                            _pinnedItemsService.MovePinnedItem(key, 0);
                        }
                    };
                    flyout.Items.Add(moveToTopItem);

                    var moveUpItem = new MenuFlyoutItem
                    {
                        Text = App.Loc?.CurrentLanguage == "en" ? "Move up" : "Переместить вверх",
                        Icon = new FontIcon { Glyph = "\uE74A" },
                        Tag = record.Key
                    };
                    moveUpItem.Click += (s, e) =>
                    {
                        if (s is FrameworkElement fe && fe.Tag is string key)
                        {
                            _pinnedItemsService.MovePinnedItemUp(key);
                        }
                    };
                    flyout.Items.Add(moveUpItem);
                }

                if (currentIndex < totalCount - 1)
                {
                    var moveDownItem = new MenuFlyoutItem
                    {
                        Text = App.Loc?.CurrentLanguage == "en" ? "Move down" : "Переместить вниз",
                        Icon = new FontIcon { Glyph = "\uE74B" },
                        Tag = record.Key
                    };
                    moveDownItem.Click += (s, e) =>
                    {
                        if (s is FrameworkElement fe && fe.Tag is string key)
                        {
                            _pinnedItemsService.MovePinnedItemDown(key);
                        }
                    };
                    flyout.Items.Add(moveDownItem);

                    var moveToBottomItem = new MenuFlyoutItem
                    {
                        Text = App.Loc?.CurrentLanguage == "en" ? "Move to bottom" : "В самый конец",
                        Icon = new FontIcon { Glyph = "\uE896" },
                        Tag = record.Key
                    };
                    moveToBottomItem.Click += (s, e) =>
                    {
                        if (s is FrameworkElement fe && fe.Tag is string key)
                        {
                            _pinnedItemsService.MovePinnedItem(key, totalCount - 1);
                        }
                    };
                    flyout.Items.Add(moveToBottomItem);
                }

                if (totalCount > 1)
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                }

                var unpinItem = new MenuFlyoutItem
                {
                    Text = App.Loc?.CurrentLanguage == "en" ? "Unpin" : "Открепить",
                    Icon = new FontIcon { Glyph = "\uE77A" },
                    Tag = record
                };
                unpinItem.Click += (s, e) =>
                {
                    if (s is FrameworkElement fe && fe.Tag is PinnedItemRecord r)
                    {
                        if (r.Type == "album")
                            _pinnedItemsService.UnpinAlbum(r.AlbumId);
                        else
                            _pinnedItemsService.UnpinPlaylist(r.Uid, r.Kind);
                    }
                };
                flyout.Items.Add(unpinItem);
                navItem.ContextFlyout = flyout;

                _navView.MenuItems.Add(navItem);
                _pinnedNavItems.Add(navItem);
            }

            if (!string.IsNullOrEmpty(selectedTag) && !selectedTag.StartsWith("playlist:") && !selectedTag.StartsWith("album:"))
            {
                var matching = _navView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == selectedTag);
                if (matching != null)
                {
                    _navView.SelectedItem = matching;
                }
            }
            else if (!string.IsNullOrEmpty(selectedTag))
            {
                _navView.SelectedItem = null;
            }
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private Grid CreatePinnedNavItemContent(NavigationViewItem navItem, PinnedItemRecord record, bool isExpanded)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 44,
            ManipulationMode = ManipulationModes.None,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) // hit-testable
        };

        CursorHelper.SetUseHandCursor(grid, true);

        if (isExpanded)
        {
            navItem.Icon = null;

            grid.ColumnSpacing = 10;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var coverBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(5),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            if (!string.IsNullOrWhiteSpace(record.CoverUrl) && Uri.TryCreate(record.CoverUrl, UriKind.Absolute, out var uri))
            {
                coverBorder.Child = new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri)
                    {
                        DecodePixelWidth = 64,
                        DecodePixelHeight = 64
                    },
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false
                };
            }
            else
            {
                coverBorder.Child = new FontIcon
                {
                    Glyph = record.Type == "album" ? "\uE93C" : "\uE142",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
            }

            var textBlock = new TextBlock
            {
                Text = record.Title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13,
                IsHitTestVisible = false
            };

            var gripIcon = new FontIcon
            {
                Glyph = "\uE76F",
                FontSize = 11,
                Opacity = 0.35,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            };

            var gripHandle = new Border
            {
                Width = 26,
                Height = 36,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 2, 0),
                Child = gripIcon,
                IsHitTestVisible = false
            };

            grid.PointerEntered += (s, e) => gripIcon.Opacity = 0.85;
            grid.PointerExited += (s, e) =>
            {
                if (!_isDraggingPinned)
                {
                    gripIcon.Opacity = 0.35;
                }
            };

            Grid.SetColumn(coverBorder, 0);
            Grid.SetColumn(textBlock, 1);
            Grid.SetColumn(gripHandle, 2);

            grid.Children.Add(coverBorder);
            grid.Children.Add(textBlock);
            grid.Children.Add(gripHandle);

            navItem.Content = grid;
            return grid;
        }
        else
        {
            navItem.Content = null;
            if (!string.IsNullOrWhiteSpace(record.CoverUrl) && Uri.TryCreate(record.CoverUrl, UriKind.Absolute, out var uri))
            {
                navItem.Icon = new ImageIcon
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri)
                    {
                        DecodePixelWidth = 64,
                        DecodePixelHeight = 64
                    }
                };
            }
            else
            {
                navItem.Icon = new FontIcon
                {
                    Glyph = record.Type == "album" ? "\uE93C" : "\uE142",
                    FontSize = 14
                };
            }
            return grid;
        }
    }

    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var ptInNav = e.GetCurrentPoint(_navView);
        if (!ptInNav.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_isPointerDown) return;

        NavigationViewItem? navItem = sender as NavigationViewItem ?? FindParent<NavigationViewItem>(sender as DependencyObject);
        if (navItem == null || navItem.Tag is not string key) return;

        int index = _pinnedHolders.FindIndex(h => string.Equals(h.Record.Key, key, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        var holder = _pinnedHolders[index];

        _isPointerDown = true;
        _isDraggingPinned = false;
        _dragSourceIndex = index;
        _currentTargetIndex = index;
        _draggedHolder = holder;
        _potentialClickRecord = holder.Record;
        _startMousePosInNav = ptInNav.Position;
        _lastKnownMousePosInNav = ptInNav.Position;

        var sv = GetNavScrollViewer();
        _startScrollOffset = sv?.VerticalOffset ?? 0;

        try { navItem.CapturePointer(e.Pointer); } catch { }
        e.Handled = true;
    }

    private void Card_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDown || _draggedHolder == null) return;

        var currentPoint = e.GetCurrentPoint(_navView);
        _lastKnownMousePosInNav = currentPoint.Position;
        double deltaYFromPress = Math.Abs(currentPoint.Position.Y - _startMousePosInNav.Y);
        double deltaXFromPress = Math.Abs(currentPoint.Position.X - _startMousePosInNav.X);

        if (!_isDraggingPinned)
        {
            if (deltaYFromPress >= 4 || deltaXFromPress >= 4)
            {
                _isDraggingPinned = true;
                _draggedHolder.ActiveStoryboard?.Stop();
                _draggedHolder.ActiveStoryboard = null;
                Canvas.SetZIndex(_draggedHolder.NavItem, 1000);
                _draggedHolder.Transform.ScaleX = 1.02;
                _draggedHolder.Transform.ScaleY = 1.02;
                _draggedHolder.NavItem.Opacity = 0.94;
                CursorHelper.SetCursor(_navView, CursorHelper.HandCursor);
            }
        }

        if (_isDraggingPinned)
        {
            var sv = GetNavScrollViewer();
            double currentScroll = sv?.VerticalOffset ?? 0;
            double totalDeltaY = (currentPoint.Position.Y - _startMousePosInNav.Y) + (currentScroll - _startScrollOffset);

            _draggedHolder.Transform.TranslateY = totalDeltaY;

            double itemHeight = _draggedHolder.NavItem.ActualHeight > 0 ? _draggedHolder.NavItem.ActualHeight : 44.0;
            int slotOffset = (int)Math.Round(totalDeltaY / itemHeight);
            int targetIndex = Math.Clamp(_dragSourceIndex + slotOffset, 0, _pinnedHolders.Count - 1);
            _currentTargetIndex = targetIndex;

            UpdateItemShifts(_dragSourceIndex, targetIndex, itemHeight);

            // Auto-scroll check
            if (sv != null)
            {
                try
                {
                    var svTransform = sv.TransformToVisual(_navView);
                    var svBounds = svTransform.TransformBounds(new Rect(0, 0, sv.ActualWidth, sv.ActualHeight));
                    double mouseYInSv = currentPoint.Position.Y - svBounds.Top;
                    double svHeight = svBounds.Height;

                    if (mouseYInSv < 45)
                    {
                        double dist = Math.Max(0, 45 - mouseYInSv);
                        _autoScrollVelocity = -Math.Clamp(Math.Max(4, dist * 0.45), 4, 22);
                    }
                    else if (mouseYInSv > svHeight - 45)
                    {
                        double dist = Math.Max(0, mouseYInSv - (svHeight - 45));
                        _autoScrollVelocity = Math.Clamp(Math.Max(4, dist * 0.45), 4, 22);
                    }
                    else
                    {
                        _autoScrollVelocity = 0;
                    }
                }
                catch
                {
                    _autoScrollVelocity = 0;
                }

                if (_autoScrollVelocity != 0)
                {
                    StartAutoScrollTimer();
                }
                else
                {
                    _autoScrollTimer?.Stop();
                }
            }

            e.Handled = true;
        }
    }

    private void StartAutoScrollTimer()
    {
        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = _dispatcherQueue.CreateTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        }
        if (!_autoScrollTimer.IsRunning)
        {
            _autoScrollTimer.Start();
        }
    }

    private void AutoScrollTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_isDraggingPinned || _navScrollViewer == null || _autoScrollVelocity == 0 || _draggedHolder == null)
        {
            _autoScrollTimer?.Stop();
            return;
        }

        double currentOffset = _navScrollViewer.VerticalOffset;
        double newOffset = Math.Clamp(currentOffset + _autoScrollVelocity, 0, _navScrollViewer.ScrollableHeight);
        if (Math.Abs(newOffset - currentOffset) > 0.05)
        {
            _navScrollViewer.ChangeView(null, newOffset, null, true);

            double mouseDeltaY = _lastKnownMousePosInNav.Y - _startMousePosInNav.Y;
            double scrollDeltaY = newOffset - _startScrollOffset;
            double totalDeltaY = mouseDeltaY + scrollDeltaY;

            _draggedHolder.Transform.TranslateY = totalDeltaY;

            double itemHeight = _draggedHolder.NavItem.ActualHeight > 0 ? _draggedHolder.NavItem.ActualHeight : 44.0;
            int slotOffset = (int)Math.Round(totalDeltaY / itemHeight);
            int targetIndex = Math.Clamp(_dragSourceIndex + slotOffset, 0, _pinnedHolders.Count - 1);
            _currentTargetIndex = targetIndex;

            UpdateItemShifts(_dragSourceIndex, targetIndex, itemHeight);
        }
    }

    private void UpdateItemShifts(int sourceIndex, int targetIndex, double itemHeight)
    {
        for (int i = 0; i < _pinnedHolders.Count; i++)
        {
            if (i == sourceIndex) continue;

            var holder = _pinnedHolders[i];
            double targetShift = 0;

            if (targetIndex < sourceIndex)
            {
                if (i >= targetIndex && i < sourceIndex)
                {
                    targetShift = itemHeight;
                }
            }
            else if (targetIndex > sourceIndex)
            {
                if (i > sourceIndex && i <= targetIndex)
                {
                    targetShift = -itemHeight;
                }
            }

            if (Math.Abs(holder.CurrentTargetShiftY - targetShift) > 0.5)
            {
                holder.CurrentTargetShiftY = targetShift;
                AnimateHolderShift(holder, targetShift);
            }
        }
    }

    private void AnimateHolderShift(PinnedItemHolder holder, double toY)
    {
        var anim = new DoubleAnimation
        {
            To = toY,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, holder.Transform);
        Storyboard.SetTargetProperty(anim, "TranslateY");

        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            holder.Transform.TranslateY = toY;
            if (ReferenceEquals(holder.ActiveStoryboard, sb))
            {
                holder.ActiveStoryboard = null;
            }
        };
        holder.ActiveStoryboard = sb;
        sb.Begin();
    }

    private void CompleteDropOrClick(UIElement? releasingElement, Pointer? pointer)
    {
        if (!_isPointerDown) return;

        bool wasDragging = _isDraggingPinned;
        var dragged = _draggedHolder;
        int sourceIndex = _dragSourceIndex;
        int targetIndex = _currentTargetIndex;
        var clickRecord = _potentialClickRecord;

        _autoScrollTimer?.Stop();
        _autoScrollVelocity = 0;
        _isPointerDown = false;
        _isDraggingPinned = false;
        _draggedHolder = null;
        _dragSourceIndex = -1;
        _currentTargetIndex = -1;
        _potentialClickRecord = null;

        CursorHelper.SetCursor(_navView, null);

        if (pointer != null && releasingElement != null)
        {
            try { releasingElement.ReleasePointerCapture(pointer); } catch { }
        }

        if (!wasDragging)
        {
            ResetDragState();

            if (clickRecord != null)
            {
                _onNavigateToTag(clickRecord.Key);
            }
            return;
        }

        _lastDragEndTime = DateTime.UtcNow;

        if (dragged == null || sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            ResetDragState();
            return;
        }

        string sourceKey = dragged.Record.Key;

        // Perform move and atomic synchronous rebuild so the list smoothly transitions into place without jumping
        _isRebuildingPinnedDrag = true;
        try
        {
            _pinnedItemsService.MovePinnedItem(sourceKey, targetIndex);
            RefreshPinnedMenuItems();
        }
        finally
        {
            _isRebuildingPinnedDrag = false;
        }
    }

    private void Card_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        CompleteDropOrClick(sender as UIElement, e.Pointer);
        e.Handled = true;
    }

    private void Card_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CompleteDropOrClick(sender as UIElement, e.Pointer);
        e.Handled = true;
    }

    private void Card_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _lastDragEndTime = DateTime.UtcNow;
        ResetDragState();
    }

    private void ResetDragState()
    {
        _autoScrollTimer?.Stop();
        _autoScrollVelocity = 0;
        _isPointerDown = false;
        _isDraggingPinned = false;
        _dragSourceIndex = -1;
        _currentTargetIndex = -1;
        _potentialClickRecord = null;
        _draggedHolder = null;

        CursorHelper.SetCursor(_navView, null);

        foreach (var holder in _pinnedHolders)
        {
            holder.ActiveStoryboard?.Stop();
            holder.ActiveStoryboard = null;
            holder.CurrentTargetShiftY = 0;
            holder.Transform.TranslateY = 0;
            holder.Transform.ScaleX = 1.0;
            holder.Transform.ScaleY = 1.0;
            holder.NavItem.Opacity = 1.0;
            Canvas.SetZIndex(holder.NavItem, 0);
        }
    }

    private ScrollViewer? GetNavScrollViewer()
    {
        if (_navScrollViewer != null) return _navScrollViewer;

        foreach (var holder in _pinnedHolders)
        {
            var sv = FindParent<ScrollViewer>(holder.NavItem);
            if (sv != null)
            {
                _navScrollViewer = sv;
                return sv;
            }
        }

        _navScrollViewer = FindVisualChild<ScrollViewer>(_navView);
        return _navScrollViewer;
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T typed)
                return typed;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }

    public void Dispose()
    {
        _autoScrollTimer?.Stop();
        _pinnedItemsService.PinStateChanged -= PinnedItemsService_PinStateChanged;
        App.Loc.LanguageChanged -= Loc_LanguageChanged;
        _navView.PaneOpened -= NavView_PaneOpened;
        _navView.PaneClosed -= NavView_PaneClosed;
    }
}
