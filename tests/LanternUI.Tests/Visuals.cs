using System.Windows;
using System.Windows.Media;

namespace LanternUI.Tests;

/// <summary>Walks a visual tree: the register table is built in code, so tests read it the same way.</summary>
internal static class Visuals
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
