using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lantern.UI;

/// <summary>
/// Makes the wheel scroll the page by the wheel's own delta. Without it a row's drop-down or text
/// box swallows the wheel even when it has nothing to scroll, and the page jumps a screen at a time.
/// The wheel is taken in the tunnelling pass unless the control under the pointer can really scroll
/// further that way (an inner list with room, a slider, a text box), or an open pop-up owns it.
/// </summary>
public static class PageScroll
{
    public static void Attach(ScrollViewer page)
    {
        page.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled || InPopup(e.OriginalSource, page) || Owned(e.OriginalSource as DependencyObject, page, e.Delta))
                return;

            page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta);
            e.Handled = true;
        };
    }

    /// <summary>An open pop-up is a window of its own: the wheel there belongs to the list.</summary>
    private static bool InPopup(object source, ScrollViewer page) =>
        source is Visual visual &&
        PresentationSource.FromVisual(visual) is { } window &&
        !ReferenceEquals(window, PresentationSource.FromVisual(page));

    private static bool Owned(DependencyObject? source, ScrollViewer page, int delta)
    {
        while (source != null && !ReferenceEquals(source, page))
        {
            if (source is ScrollViewer inner && inner.ScrollableHeight > 0)
            {
                var room = delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
                if (room) return true;
            }

            if (source is Slider or TextBox) return true;

            source = source is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }
}
