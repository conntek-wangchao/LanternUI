using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The picker's entries used to be drawn, so a client found their text and nothing else. They are a
/// real ListBox now: a list, five items, each named after its label and selectable through UIA.
/// This test is the reason that stays true.
///
/// The query walks the desktop's children of this process, not just the owner window: a Popup is its
/// own top-level window, and the list inside it is not reachable from the owner's tree.
/// </summary>
public class PickerAutomationTests
{
    [Fact]
    public void The_open_picker_is_a_list_of_selectable_items()
    {
        ChoicePicker? picker = null;
        var window = Sta.Run(() =>
        {
            picker = new ChoicePicker(Enumerable.Range(0, 5)
                .Select(index => new Choice("Item " + index, "i" + index)).ToArray());
            AutomationProperties.SetName(picker, "Language");
            var panel = new StackPanel();
            panel.Children.Add(picker);
            var shell = new Window
            {
                Width = 480, Height = 320, Left = -9000, Top = -9000,
                ShowInTaskbar = false, Content = panel,
            };
            shell.Show();
            shell.UpdateLayout();
            return shell;
        });

        try
        {
            Sta.Run(() => picker!.Open());

            var items = new List<AutomationElement>();
            for (var attempt = 0; attempt < 60 && items.Count == 0; attempt++)
            {
                Sta.Run(() =>
                {
                    picker!.UpdateLayout();
                    picker.Panel.UpdateLayout();
                });
                items = Query(ControlType.ListItem, null);
                if (items.Count == 0) Thread.Sleep(100);
            }

            Assert.Equal(5, items.Count);
            Assert.Single(Query(ControlType.List, "Language"));
            Assert.Single(Query(ControlType.ListItem, "Item 2"));

            // Selectable through UIA, which is what a screen reader and an automation script need.
            var second = Query(ControlType.ListItem, "Item 2").Single();
            Assert.True(second.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern));
            ((SelectionItemPattern)pattern).Select();
            Assert.Equal("Item 2", Sta.Run(() => picker!.SelectedLabel));
        }
        finally
        {
            Sta.Run(() => window.Close());
        }
    }

    /// <summary>Every match in this process, found on a thread of its own so the dispatcher can answer.</summary>
    private static List<AutomationElement> Query(ControlType type, string? name)
    {
        var found = new List<AutomationElement>();
        var thread = new Thread(() =>
        {
            System.Windows.Automation.Condition condition = new PropertyCondition(AutomationElement.ControlTypeProperty, type);
            if (name != null)
                condition = new AndCondition(condition, new PropertyCondition(AutomationElement.NameProperty, name));

            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId));
            foreach (AutomationElement window in windows)
                found.AddRange(window.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>());
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(20));
        return found;
    }
}
