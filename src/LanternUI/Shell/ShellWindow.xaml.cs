using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Lantern.UI;

/// <summary>
/// The window: a slim title bar (window buttons, the settings search, the title), the page tabs or
/// the sidebar, the page host, a folding activity log and a one-line status bar. It owns the theme
/// and the accent; pages ask it through IShell, never through a static window instance.
/// </summary>
public partial class ShellWindow : Window, IShell
{
    private readonly List<(string Key, string Title, Func<PageBase> Create)> _factories = new();
    private readonly Dictionary<string, PageBase> _pages = new();
    private readonly ObservableCollection<string> _log = new();
    private readonly List<RadioButton> _tabs = new();
    private readonly List<RadioButton> _sideItems = new();

    private bool _logOpen;
    private bool _switching;
    private bool _sidebar;
    private string? _current;

    private List<SearchHit> _hits = new();
    private int _hit;
    private DispatcherTimer? _searchDelay;
    private Task? _pagesReady;

    public ShellWindow()
    {
        InitializeComponent();

        // Caption height 34 hands the top strip to Windows as a real caption (drag, double click,
        // Aero snap); the resize border is wider than the default 4 px so grabbing is easy.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 34,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        LogList.ItemsSource = _log;
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        Deactivated += (_, _) => HideHits();

        ApplyWindowButtons();
        ApplyNavigation();
    }

    private IAppState? _app;

    /// <summary>
    /// The state arrives after the constructor (an object initializer), so the window applies what
    /// depends on it here — the classic layout used to wait for the next theme change to show its
    /// background picture.
    /// </summary>
    public IAppState? App
    {
        get => _app;
        set
        {
            _app = value;
            ApplyBackdrop();
            ApplyWindowButtons();
            ApplyNavigation();
        }
    }

    /// <summary>The strings the window owns; set it, then call ApplyLanguage (a language needs a restart).</summary>
    public ILocalizer Localizer { get; set; } = NullLocalizer.Instance;

    public UiLayout Layout { get; private set; } = UiLayout.Modern;

    /// <summary>Puts the window's own text in the chosen language.</summary>
    public void ApplyLanguage()
    {
        SearchHint.Text = Localizer.T("Search settings  (Ctrl+F)");
        LogTitle.Text = Localizer.T("Activity log");
        LogClear.Content = Localizer.T("Clear");
        LogToggle.Content = Localizer.T("Log");
        if (StatusText.Text.Length == 0) StatusText.Text = Localizer.T("Ready");
        ApplyButtonNames();
    }

    /// <summary>The window buttons are drawn paths: their name and tooltip come from here.</summary>
    private void ApplyButtonNames()
    {
        void Name(Button button, string key)
        {
            var text = Localizer.T(key);
            AutomationProperties.SetName(button, text);
            button.ToolTip = text;
        }

        Name(MacClose, "Close");
        Name(MacMin, "Minimize");
        Name(MacMax, "Maximize");
        Name(WinClose, "Close");
        Name(WinMin, "Minimize");
        Name(WinMax, "Maximize");
    }

    public void SetLayout(UiLayout layout) => Layout = layout;

    /// <summary>
    /// Mirrors the window for a right-to-left language. The layout containers follow FlowDirection on
    /// their own; what has to be told by hand is the title bar, where the two groups are pinned to a
    /// physical side instead of to the reading order.
    /// </summary>
    public void ApplyFlowDirection(bool rightToLeft)
    {
        FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyWindowButtons();
    }

    /// <summary>
    /// HorizontalAlignment is read in the reading direction, so in a right-to-left window "Right"
    /// means the left of the screen. This turns a side you can see into the alignment that gets there.
    /// </summary>
    private HorizontalAlignment SideOf(bool visualRight) =>
        visualRight != (FlowDirection == FlowDirection.RightToLeft) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>Which side of the title bar the caption buttons are on, as seen on screen.</summary>
    public bool ButtonsOnTheRight => WinButtons.Visibility == Visibility.Visible
        ? WinButtons.HorizontalAlignment == SideOf(true)
        : MacButtons.HorizontalAlignment == SideOf(true);

    /// <summary>Which side the search box is on, as seen on screen.</summary>
    public bool SearchOnTheRight => SearchArea.HorizontalAlignment == SideOf(true);

    /// <summary>True while a background picture is on screen (the demo's snapshot asserts it).</summary>
    public bool BackdropVisible => BackdropLayer.Visibility == Visibility.Visible;

    /// <summary>The page on screen (null before the first ShowPage).</summary>
    public PageBase? CurrentPage => _current != null && _pages.TryGetValue(_current, out var page) ? page : null;

    /// <summary>
    /// Switches the layout without a restart: rows bake their look when a page is built, so the
    /// cached pages are dropped and the current one is made again with the new layout.
    /// </summary>
    public void ApplyLayout(UiLayout layout)
    {
        if (layout == Layout) return;
        Layout = layout;

        var shown = _current;
        _pages.Clear();
        _pagesReady = null;

        ApplyNavigation();
        ApplyBackdrop();
        ApplyWindowButtons();
        if (shown != null) ShowPage(shown);
    }

    // ---------------------------------------------------------------- pages

    public void SetPages(IEnumerable<PageEntry> pages)
    {
        foreach (var entry in pages)
        {
            _factories.Add((entry.Key, entry.Title, entry.Create));

            var tab = NavItem(entry, "TabItem");
            var side = NavItem(entry, "Segment");
            side.Margin = new Thickness(0, 1, 0, 1);
            side.HorizontalAlignment = HorizontalAlignment.Stretch;
            side.HorizontalContentAlignment = HorizontalAlignment.Left;
            _tabs.Add(tab);
            _sideItems.Add(side);
            Tabs.Children.Add(tab);
            SideNav.Children.Add(side);
        }

        ApplyNavigation();
        if (_factories.Count > 0) ShowPage(_factories[0].Key);
    }

    private RadioButton NavItem(PageEntry entry, string style)
    {
        var item = new RadioButton { Content = entry.Title, Tag = entry.Key, GroupName = "Pages_" + style };
        item.SetResourceReference(StyleProperty, style);
        item.Checked += (_, _) =>
        {
            if (!_switching && item.Tag is string key) ShowPage(key);
        };
        return item;
    }

    public void ShowPage(string key)
    {
        var entry = _factories.FirstOrDefault(f => f.Key == key);
        if (entry.Create == null) return;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = entry.Create();
            _pages[key] = page;
        }

        Host.Content = page;
        page.OnShown();
        _current = key;

        _switching = true;
        foreach (var item in _tabs.Concat(_sideItems))
            if (Equals(item.Tag, key) && item.IsChecked != true) item.IsChecked = true;
        _switching = false;

        TitleText.Text = entry.Title;
    }

    /// <summary>Modern layout in tabs or in a sidebar, Classic always in the sidebar.</summary>
    public void ApplyNavigation()
    {
        _sidebar = Layout == UiLayout.Classic || App?.Settings.Str("ui_navigation") == "sidebar";
        TabStrip.Visibility = _sidebar || _factories.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Sidebar.Visibility = _sidebar ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- theme

    public void ApplyTheme() => ApplyAccent(App?.Settings.Str("ui_theme_colour") ?? "#4C9DFF");

    public void ApplyAccent(string hex)
    {
        var parsed = AccentTheme.Parse(hex);
        _accent = parsed ?? AccentTheme.Default;
        AccentTheme.Apply(parsed);
        ApplyWindowButtons();
        ApplyBackdrop();
    }

    private Color _accent = AccentTheme.Default;

    /// <summary>
    /// The Modern layout's background: a picture, blurred as much as asked, under a shade tinted by
    /// the accent. Classic has no backdrop; a missing picture leaves the plain window colour.
    /// </summary>
    public void ApplyBackdrop()
    {
        var settings = App?.Settings;
        var mode = settings?.Str("ui_background") ?? "none";
        var path = settings?.Str("ui_background_image") ?? "";
        var blur = settings?.Str("ui_background_blur") ?? "strong";
        var radius = blur switch { "off" => 0, "light" => 14, _ => 48 };

        if (Layout == UiLayout.Classic || mode == "none" || path.Length == 0 || !System.IO.File.Exists(path))
        {
            BackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        SetBackdrop(path, radius);
    }

    /// <summary>Shows a picture behind the page; radius 0 leaves it unblurred.</summary>
    public void SetBackdrop(string path, double radius)
    {
        // A photograph behind the page defeats the point of the high-contrast palette.
        if (ThemeBoot.HighContrast)
        {
            BackdropImage.Source = null;
            BackdropImage.Effect = null;
            BackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = radius > 20 ? 960 : 1600;   // blurred: less is enough
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            BackdropImage.Source = image;
        }
        catch (Exception)
        {
            BackdropImage.Source = null;
            BackdropLayer.Visibility = Visibility.Collapsed;
            return;
        }

        BackdropImage.Effect = radius <= 0
            ? null
            : new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        BackdropImage.Margin = new Thickness(radius <= 0 ? 0 : -60);

        var shade = AccentTheme.ShadeColour(_accent);
        var wash = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(AccentTheme.WithAlpha(shade, 0x59), 0),
                new(AccentTheme.WithAlpha(shade, 0x80), 0.5),
                new(AccentTheme.WithAlpha(shade, 0xB3), 1),
            },
            90);
        wash.Freeze();
        BackdropShade.Background = wash;

        BackdropLayer.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- notifications

    private readonly Queue<(string Title, string Body)> _toasts = new();
    private bool _toastShowing;

    /// <summary>
    /// Queues a notification card in the corner; they are shown one after another. A screen reader
    /// is told too — a card that appears silently is a message only people who watch the corner get.
    /// </summary>
    public void Notify(string title, string body)
    {
        _toasts.Enqueue((title, body));
        Announce(title + ". " + body);
        if (!_toastShowing) ShowNextToast();
    }

    private void Announce(string text)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.Notification)) return;
        var peer = UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent, text, "lantern.toast");
    }

    private void ShowNextToast()
    {
        if (_toasts.Count == 0)
        {
            _toastShowing = false;
            return;
        }

        _toastShowing = true;
        var (title, body) = _toasts.Dequeue();
        var toast = new Toast(title, body);
        var slide = new TranslateTransform(40, 0);
        toast.RenderTransform = slide;
        toast.Opacity = 0;
        ToastHost.Children.Add(toast);

        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(220)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) =>
            {
                ToastHost.Children.Remove(toast);
                ShowNextToast();
            };
            toast.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    /// <summary>macOS traffic lights or Windows caption buttons, on their usual side or the other.</summary>
    public void ApplyWindowButtons()
    {
        var settings = App?.Settings;
        var windows = settings?.Str("ui_window_buttons") == "windows";
        var side = settings?.Str("ui_window_buttons_side") ?? "usual";
        var right = side == "right" || (side != "left" && windows);

        MacButtons.Visibility = windows ? Visibility.Collapsed : Visibility.Visible;
        WinButtons.Visibility = windows ? Visibility.Visible : Visibility.Collapsed;

        // Sides are visual: the search box hugs the reading start of the title bar, and the caption
        // buttons go where the setting says. Under right-to-left "the start" is the right edge, and a
        // flow-relative alignment would have put both on top of each other.
        var rightToLeft = FlowDirection == FlowDirection.RightToLeft;
        MacButtons.HorizontalAlignment = SideOf(right);
        WinButtons.HorizontalAlignment = SideOf(right);
        SearchArea.HorizontalAlignment = SideOf(rightToLeft);
        MacButtons.Margin = right ? new Thickness(0, 0, 14, 0) : new Thickness(14, 0, 0, 0);
        WinButtons.Margin = right ? new Thickness(0) : new Thickness(14, 0, 0, 0);

        // The title stays centred and the tools give way by what the buttons take; the search box
        // starts after them when they sit on the left, and narrows beside the wider caption buttons.
        var taken = !right ? 0 : windows ? 3 * 46 : 14 + 3 * 20;
        // Margin is read in the reading direction too, so the title's free space is put on the side the
        // buttons are on: the flow's left is the screen's right in a mirrored window.
        TitleText.Margin = !right
            ? new Thickness(0)
            : rightToLeft ? new Thickness(taken, 0, 0, 0) : new Thickness(0, 0, taken, 0);

        // The box hugs the flow's start edge, so its gap is always the flow-left margin: the buttons
        // sit in that gap when they share the edge, and the box simply starts further in when they do.
        var shared = right == rightToLeft;
        var gap = shared ? (windows ? 3 * 46 + 10 : 14 + 3 * 20 + 4) : 12;
        SearchArea.Margin = new Thickness(gap, 0, 0, 0);
        // The box sizes itself to its placeholder (a longer language must not clip); the cap only
        // tightens it when the wider caption buttons sit on the same side.
        SearchArea.MaxWidth = !right && windows ? 220 : 340;

        if (SearchResults.Visibility == Visibility.Visible) PlaceResults();
    }

    // ---------------------------------------------------------------- log

    public void Log(string message, LogLevel level)
    {
        _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        if (_log.Count > 500) _log.RemoveAt(0);

        StatusText.Text = message;
        var key = level switch
        {
            LogLevel.Good => "StatusOkBrush",
            LogLevel.Warn => "StatusWarnBrush",
            LogLevel.Bad => "StatusBadBrush",
            LogLevel.Step => "AccentBrush",
            _ => "TextTertiaryBrush",
        };
        if (FindResource(key) is Brush brush) StatusDot.Fill = brush;
        if (_logOpen && _log.Count > 0) LogList.ScrollIntoView(_log[^1]);
    }

    /// <summary>"Saved 14:32:07", the reference's quiet confirmation that settings reached the disk.</summary>
    public void ShowSaved() => SavedText.Text = Localizer.T("Saved") + " " + DateTime.Now.ToString("HH:mm:ss");

    public void ShowLog()
    {
        if (_logOpen) return;
        ToggleLog();
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e) => ToggleLog();

    private void ToggleLog()
    {
        _logOpen = !_logOpen;
        LogPanel.Visibility = _logOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_logOpen && _log.Count > 0) LogList.ScrollIntoView(_log[^1]);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        StatusText.Text = Localizer.T("Ready");
        if (FindResource("TextTertiaryBrush") is Brush idle) StatusDot.Fill = idle;
    }

    // ---------------------------------------------------------------- search

    /// <summary>Scrolls the page to a fraction of its height (0 top, 1 bottom) — the screenshot pipeline.</summary>
    public void ScrollTo(double fraction)
    {
        if (Host.Content is not DependencyObject page) return;
        if (FindScrollViewer(page) is not { } scroller) return;
        scroller.ScrollToVerticalOffset(Math.Clamp(fraction, 0, 1) * scroller.ScrollableHeight);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer scroller) return scroller;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>Runs a search without the keyboard (the i18n self-check uses it to reach the empty result).</summary>
    public void RunSearch(string query)
    {
        SearchBox.Text = query;
        ShowHits();
    }

    /// <summary>Puts the keyboard in the search box (the demo and Ctrl+F use it).</summary>
    public void FocusSearch(string? query = null)
    {
        SearchBox.Focus();
        if (!string.IsNullOrEmpty(query)) SearchBox.Text = query;
        SearchBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusSearch();
            e.Handled = true;
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SearchResults.Visibility != Visibility.Visible) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (IsInside(source, SearchResults) || IsInside(source, SearchArea)) return;
        HideHits();
    }

    private static bool IsInside(DependencyObject? node, DependencyObject root)
    {
        for (; node != null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var empty = SearchBox.Text.Length == 0;
        SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (SearchBox.Text.Trim().Length == 0)
        {
            _searchDelay?.Stop();
            HideHits();
            return;
        }

        if (_searchDelay == null)
        {
            _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchDelay.Tick += (_, _) =>
            {
                _searchDelay!.Stop();
                ShowHits();
            };
        }
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Pages are made one at a time in the background the first time the box is used: typing
        // never waits for them, and the search re-runs over all of them once they are there.
        _pagesReady ??= BuildPagesAsync();
        ShowHits();
    }

    private async Task BuildPagesAsync()
    {
        foreach (var entry in _factories.ToList())
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (!_pages.ContainsKey(entry.Key)) _pages[entry.Key] = entry.Create();
        }

        if (SearchBox.IsKeyboardFocused && SearchBox.Text.Trim().Length > 0 && _searchDelay?.IsEnabled != true)
            ShowHits();
    }

    private void ShowHits()
    {
        var query = SearchBox.Text;
        if (query.Trim().Length == 0)
        {
            HideHits();
            return;
        }

        _hits = SearchScorer.Find(Places(), query, Localizer);
        _hit = 0;
        SearchList.Children.Clear();

        if (_hits.Count == 0)
        {
            var none = new TextBlock
            {
                Text = string.Format(Localizer.T("Nothing found for \u201c{0}\u201d"), query.Trim()),
                FontSize = 11.5,
                Margin = new Thickness(10, 8, 10, 8),
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            SearchList.Children.Add(none);
        }

        for (var i = 0; i < _hits.Count; i++)
        {
            var index = i;
            var view = ResultView(_hits[i]);
            view.MouseEnter += (_, _) =>
            {
                _hit = index;
                MarkHit();
            };
            view.MouseLeftButtonUp += (_, _) => PickHit(index);
            SearchList.Children.Add(view);
        }

        SearchResults.Visibility = Visibility.Visible;
        PlaceResults();
        MarkHit();
    }

    private void PlaceResults()
    {
        var offset = SearchArea.TranslatePoint(new Point(0, SearchArea.ActualHeight + 5), this);
        SearchResults.Margin = new Thickness(offset.X, offset.Y, 0, 0);
    }

    private void HideHits()
    {
        _hits.Clear();
        SearchResults.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Every place the search looks through: a page, or each section of a container page as a place
    /// of its own ("More › Mods"), the way the reference nests its sub-pages.
    /// </summary>
    private IEnumerable<Place> Places()
    {
        foreach (var entry in _factories)
        {
            if (!_pages.TryGetValue(entry.Key, out var page)) continue;

            if (page is IPlaceHost host)
            {
                var key = entry.Key;
                foreach (var place in host.Places())
                    yield return new Place(entry.Title + " › " + place.Name, place.Page,
                        () =>
                        {
                            ShowPage(key);
                            place.Show?.Invoke();
                        });
                continue;
            }

            var top = entry.Key;
            yield return new Place(entry.Title, page, () => ShowPage(top));
        }
    }

    private void MarkHit()
    {
        for (var i = 0; i < SearchList.Children.Count; i++)
            if (SearchList.Children[i] is Border { Tag: SearchHit } view)
                if (i == _hit) view.SetResourceReference(BackgroundProperty, "SelectedBrush");
                else view.Background = Brushes.Transparent;
    }

    private void PickHit(int index)
    {
        if (index < 0 || index >= _hits.Count) return;
        var hit = _hits[index];

        SearchBox.Text = "";
        HideHits();
        hit.Place.Show?.Invoke();
        if (Keyboard.FocusedElement is TextBox) Keyboard.Focus(Host);

        // The page has to be laid out and grouped into its cards before a row can be centred.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => hit.Place.Page.Reveal(hit.Row)));
    }

    private static Border ResultView(SearchHit hit)
    {
        var title = new TextBlock
        {
            Text = hit.Row.Title,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var where = new TextBlock
        {
            Text = hit.Row.Section is { Length: > 0 } section && !hit.Place.Name.EndsWith(section, StringComparison.Ordinal)
                ? hit.Place.Name + " \u203a " + section
                : hit.Place.Name,
            FontSize = 11,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        where.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(title);
        Grid.SetColumn(where, 1);
        top.Children.Add(where);

        var stack = new StackPanel();
        stack.Children.Add(top);

        if (hit.Snippet is { Length: > 0 } snippet)
        {
            var line = new TextBlock
            {
                Text = snippet.ReplaceLineEndings(" "),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
            stack.Children.Add(line);
        }

        return new Border
        {
            Child = stack,
            Tag = hit,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Clear();
                else Keyboard.Focus(Host);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_searchDelay?.IsEnabled == true)
                {
                    _searchDelay.Stop();
                    ShowHits();
                }
                if (_hits.Count > 0 && SearchResults.Visibility == Visibility.Visible) PickHit(_hit);
                e.Handled = true;
                break;

            case Key.Down or Key.Up when _hits.Count > 0:
                _hit = (_hit + (e.Key == Key.Down ? 1 : _hits.Count - 1)) % _hits.Count;
                MarkHit();
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}