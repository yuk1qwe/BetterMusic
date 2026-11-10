#nullable enable

using System;
using Microsoft.UI.Xaml.Controls;

namespace YMusic.WinUI.Views.Controls;

public sealed class ObservableListView : ListView
{
    public ScrollViewer? InnerScrollViewer { get; private set; }
    public event EventHandler<double>? VerticalOffsetChanged;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("ScrollViewer") is ScrollViewer sv)
        {
            InnerScrollViewer = sv;
            sv.ViewChanged -= OnScrollViewChanged;
            sv.ViewChanged += OnScrollViewChanged;
        }
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            VerticalOffsetChanged?.Invoke(this, sv.VerticalOffset);
        }
    }

    public void ScrollByDelta(double delta)
    {
        if (InnerScrollViewer != null)
        {
            InnerScrollViewer.ChangeView(null, InnerScrollViewer.VerticalOffset - delta, null, false);
        }
    }

    public void ScrollToTop()
    {
        InnerScrollViewer?.ChangeView(null, 0, null, true);
    }
}
