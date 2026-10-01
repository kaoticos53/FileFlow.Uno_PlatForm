using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Platform;

/// <summary>
/// Habilita el desplazamiento por rueda del ratón en los diálogos modales ContentDialog en WinUI 3 desktop.
/// </summary>
internal static class ContentDialogWheelScroller
{
    public static void Enable(ContentDialog dialog)
    {
        dialog.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnDialogPointerWheelChanged), handledEventsToo: true);
    }

    private static void OnDialogPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ContentDialog dialog)
        {
            return;
        }

        var point = e.GetCurrentPoint(dialog);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        ScrollViewer? targetScrollViewer = FindScrollTarget(e.OriginalSource as DependencyObject, dialog);
        if (targetScrollViewer == null)
        {
            return;
        }

        if (point.Properties.IsHorizontalMouseWheel)
        {
            if (targetScrollViewer.ScrollableWidth > 0)
            {
                double linesStep = 48.0;
                double offsetDelta = (delta / 120.0) * linesStep;
                double currentOffset = targetScrollViewer.HorizontalOffset;
                double newOffset = Math.Clamp(currentOffset + offsetDelta, 0, targetScrollViewer.ScrollableWidth);
                if (Math.Abs(newOffset - currentOffset) > 0.1)
                {
                    targetScrollViewer.ChangeView(newOffset, null, null, disableAnimation: true);
                    e.Handled = true;
                }
            }
            return;
        }

        if (targetScrollViewer.ScrollableHeight > 0)
        {
            double linesStep = 48.0;
            double offsetDelta = -(delta / 120.0) * linesStep;
            double currentOffset = targetScrollViewer.VerticalOffset;
            double newOffset = Math.Clamp(currentOffset + offsetDelta, 0, targetScrollViewer.ScrollableHeight);

            if (Math.Abs(newOffset - currentOffset) > 0.1)
            {
                targetScrollViewer.ChangeView(null, newOffset, null, disableAnimation: true);
                e.Handled = true;
            }
        }
    }

    private static ScrollViewer? FindScrollTarget(DependencyObject? hit, ContentDialog dialog)
    {
        DependencyObject? current = hit;
        while (current != null && current != dialog)
        {
            if (current is ListViewBase lvb)
            {
                var sv = FindDescendantScrollViewer(lvb);
                if (sv != null && sv.ScrollableHeight > 0)
                {
                    return sv;
                }
            }
            else if (current is TextBox tb)
            {
                var sv = FindDescendantScrollViewer(tb);
                if (sv != null && sv.ScrollableHeight > 0)
                {
                    return sv;
                }
            }
            else if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                return sv;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        current = hit;
        while (current != null && current != dialog)
        {
            var sv = FindDescendantScrollViewer(current);
            if (sv != null && sv.ScrollableHeight > 0)
            {
                return sv;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        if (dialog.Content is DependencyObject content)
        {
            var sv = FindDescendantScrollViewer(content);
            if (sv != null && sv.ScrollableHeight > 0)
            {
                return sv;
            }
        }

        return null;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer sv)
        {
            return sv;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var found = FindDescendantScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
