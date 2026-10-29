using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// Accessibility is testable, so it is tested: the peer layer without a window, and the real UIA
/// tree of a shown window. UIA calls run off the dispatcher thread on purpose — an in-process UIA
/// client blocks on the provider, which is that same dispatcher, so doing it inline would deadlock.
/// </summary>
public class AutomationTests
{
    [Fact]
    public void A_row_and_its_control_are_named_after_the_setting()
    {
        // Show and close on the dispatcher; ask UIA from this thread, which lets the dispatcher answer.
        var (shell, handle) = Sta.Run(ShowShell);
        try
        {
            Assert.NotNull(Find(handle, ControlType.Group, "Show the log at start"));
            Assert.NotNull(Find(handle, ControlType.CheckBox, "Show the log at start"));
            Assert.NotNull(Find(handle, ControlType.Button, "Close"));
            Assert.NotNull(Find(handle, ControlType.RadioButton, "Deutsch"));
        }
        finally
        {
            Sta.Run(() => shell.Close());
        }
    }

    [Fact]
    public void The_picker_reports_itself_as_a_combo_box()
    {
        Sta.Run(() =>
        {
            var picker = new ChoicePicker(Enumerable.Range(0, 12)
                .Select(index => new Choice("Item " + index, "i" + index)).ToArray());
            picker.Select(3, notify: false);
            AutomationProperties.SetName(picker, "Language");

            var peer = UIElementAutomationPeer.CreatePeerForElement(picker);
            Assert.IsType<ChoicePickerAutomationPeer>(peer);
            Assert.Equal(AutomationControlType.ComboBox, peer!.GetAutomationControlType());
            Assert.Equal("Language: Item 3", peer.GetName());

            var expand = (System.Windows.Automation.Provider.IExpandCollapseProvider)peer;
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            expand.Expand();
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            expand.Collapse();
        });
    }

    [Fact]
    public void A_list_row_is_read_as_its_title()
    {
        Assert.Equal("Row 42", new ListItem("Row 42", "detail", "new").ToString());
    }

    private static (ShellWindow Shell, IntPtr Handle) ShowShell()
    {
        var state = Sta.NewState();
        var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
        var localizer = new Localizer("en", null);

        var window = new ShellWindow { App = state, Localizer = localizer, Left = -8000, Top = -8000, ShowInTaskbar = false };
        window.ApplyLanguage();
        window.SetPages(new[]
        {
            new PageEntry("appearance", "Appearance",
                () => new Demo.Pages.AppearancePage(state, window, localizer, catalog.Languages)),
        });
        window.ShowPage("appearance");
        window.Show();
        window.UpdateLayout();
        return (window, new WindowInteropHelper(window).Handle);
    }

    /// <summary>Looks the element up from a thread of its own so the provider (the dispatcher) can answer.</summary>
    private static AutomationElement? Find(IntPtr handle, ControlType type, string name)
    {
        AutomationElement? found = null;
        var thread = new Thread(() =>
        {
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, type),
                new PropertyCondition(AutomationElement.NameProperty, name));
            found = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants, condition);
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(20));
        return found;
    }
}
