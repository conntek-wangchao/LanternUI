using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

public class PageTests
{
    [Fact]
    public void A_page_indexes_its_rows_and_the_scorer_ranks_them()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
            var localizer = new Localizer("en", null);
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var page = new Demo.Pages.AppearancePage(state, shell, localizer, catalog.Languages);

            var rows = page.SearchRows().ToList();
            Assert.True(rows.Count >= 10, "rows: " + rows.Count);
            Assert.Contains(rows, row => row.Title == "Shader cache");   // a folded section still counts

            var places = new[] { new Place("Appearance", page) };
            var hits = SearchScorer.Find(places, "shader", localizer);
            Assert.Single(hits);
            Assert.Equal("Shader cache", hits[0].Row.Title);

            Assert.Empty(SearchScorer.Find(places, "zzz", localizer));
        });
    }

    [Fact]
    public void A_refused_register_value_shows_the_kept_at_line()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
            var localizer = new Localizer("zh-CN", catalog.For("zh-CN"));
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var recorder = new Recorder();
            state.Sink = recorder;
            var page = new Demo.Pages.RegisterPage(state, shell, localizer);

            // The tree exists only once the page has been laid out: a content presenter builds its
            // child on the first measure pass.
            page.Measure(new Size(1020, 760));
            page.Arrange(new Rect(0, 0, 1020, 760));
            page.UpdateLayout();

            var table = Visuals.Descendants<RegisterTable>(page).First();
            var map = new RegisterMap();
            map.Add(new RegisterEntry(0, 0x12));
            table.Map = map;
            page.Measure(new Size(1020, 760));
            page.Arrange(new Rect(0, 0, 1020, 760));
            page.UpdateLayout();

            // The first box of a row is the register's own; the field boxes come after it.
            var box = Visuals.Descendants<System.Windows.Controls.TextBox>(table).First();
            box.Text = "300";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            Assert.Equal("18", box.Text);

            var hint = Visuals.Descendants<System.Windows.Controls.TextBlock>(page)
                .Single(block => block.Text.StartsWith("已保持在", StringComparison.Ordinal));
            Assert.Contains("255", hint.Text);
            Assert.Equal(Visibility.Visible, hint.Visibility);

            // A note, not a log line, and the next commit takes it away — like the slider.
            Assert.DoesNotContain(recorder.Lines, logged => logged.Contains("已保持在", StringComparison.Ordinal));
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal(Visibility.Collapsed, hint.Visibility);
        });
    }

    [Fact]
    public void A_clamped_slider_note_is_not_logged_and_is_gone_on_the_next_commit()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var recorder = new Recorder();
            state.Sink = recorder;

            var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
            var localizer = new Localizer("en", catalog.For("en"));
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var page = new Demo.Pages.AppearancePage(state, shell, localizer, catalog.Languages);
            page.Measure(new Size(1020, 760));
            page.Arrange(new Rect(0, 0, 1020, 760));
            page.UpdateLayout();

            // The row names the control it was given, which is the slider-and-box strip.
            var line = Visuals.Descendants<Panel>(page)
                .First(candidate => (AutomationProperties.GetName(candidate) ?? "") == "Overlay opacity");
            var slider = (Slider)line.Children[0];
            var box = (TextBox)line.Children[1];
            Assert.NotNull(slider);
            var row = FindRow(line)!;

            box.Text = "400";        // the row goes from 0 to 100
            Blur(box);

            Assert.Contains("0 to 100", row.Hint.Text);
            Assert.Equal(Visibility.Visible, row.Hint.Visibility);
            // A note on the row, never a line in the log.
            Assert.DoesNotContain(recorder.Lines, logged => logged.Contains("Kept at", StringComparison.Ordinal));

            // The next commit is what takes it away, and leaving the box is such a commit.
            Blur(box);
            Assert.Equal(Visibility.Collapsed, row.Hint.Visibility);
            Assert.DoesNotContain(recorder.Lines, logged => logged.Contains("Kept at", StringComparison.Ordinal));
        });
    }

    private static SettingRow? FindRow(DependencyObject node)
    {
        while (node is not null and not SettingRow)
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)!;
        return node as SettingRow;
    }

    private static void Blur(FrameworkElement element) =>
        element.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, element, null)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent,
        });

    private sealed class Recorder : ILogSink
    {
        public List<string> Lines { get; } = new();

        public void Write(DateTime at, LogLevel level, string message) => Lines.Add(level + " " + message);

        public void Flush() { }
    }

[Fact]
    public void The_register_page_offers_its_file_card()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var localizer = new Localizer("en", null);
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var page = new Demo.Pages.RegisterPage(state, shell, localizer);

            var titles = page.SearchRows().Select(row => row.Title).ToList();

            // A card that is built but never added to the page is invisible; the row list proves it is there.
            Assert.Contains("Map file", titles);
            Assert.Contains("Register map", titles);
            Assert.Contains("Simulated device", titles);
        });
    }

    [Fact]
    public void A_title_prefix_outranks_a_description_match()
    {
        Sta.Run(() =>
        {
            var state = Sta.NewState();
            var catalog = JsonCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Localization"));
            var localizer = new Localizer("en", null);
            var shell = new ShellWindow { App = state, Localizer = localizer };
            var page = new Demo.Pages.AppearancePage(state, shell, localizer, catalog.Languages);

            var hits = SearchScorer.Find(new[] { new Place("Appearance", page) }, "window", localizer);
            Assert.NotEmpty(hits);
            Assert.Equal("Window buttons", hits[0].Row.Title);   // 100: the title starts with the query
            Assert.All(hits.Zip(hits.Skip(1)), pair => Assert.True(pair.First.Score >= pair.Second.Score));
        });
    }
}