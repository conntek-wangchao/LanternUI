using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lantern.UI;

/// <summary>How the samples are drawn. All three share the same ring buffer and the same scale.</summary>
public enum ChartStyle { Line, Area, Bars }

/// <summary>
/// A rolling window of numbers, drawn as a line, an area or bars. It owns no timer and generates no
/// data: the caller pushes samples, and the control redraws. Pushing from a background thread is safe,
/// and a burst of pushes collapses into one repaint because WPF coalesces invalidations anyway.
/// A chart is not a table: a screen reader gets its name and whatever help text the page sets.
/// </summary>
public sealed class Chart : FrameworkElement
{
    private readonly object _gate = new();
    private double[] _values;
    private double[] _snapshot;
    private int _count;
    private int _start;
    private int _repaintQueued;

    private double _minimum;
    private double _maximum = 100;
    private (double Low, double High) _smoothed;
    private bool _smoothedValid;
    private int _shrinkFrames;
    private int _capacity = 120;
    private bool _autoScale = true;

    public Chart()
    {
        _values = new double[_capacity];
        _snapshot = new double[_capacity];
        MinHeight = 96;
        // A sample outside a fixed range maps outside the plot; without this the line would be drawn
        // over the row's own title, because WPF does not clip an element to its bounds by default.
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = false;
    }

    /// <summary>Optional line above the plot; the page passes a localized string.</summary>
    public string Caption { get; set; } = "";

    /// <summary>Shown instead of the plot while there are fewer than two samples.</summary>
    public string EmptyText { get; set; } = "";

    /// <summary>Format of the latest-value label; numbers only, no words.</summary>
    public string ValueFormat { get; set; } = "{0:0.0}";

    public bool ShowValue { get; set; } = true;

    public ChartStyle Kind { get; set; } = ChartStyle.Area;

    public int Capacity
    {
        get => _capacity;
        set
        {
            var next = Math.Max(2, value);
            lock (_gate)
            {
                if (next == _capacity) return;
                var keep = Math.Min(_count, next);
                var resized = new double[next];
                for (var index = 0; index < keep; index++) resized[keep - 1 - index] = this[_count - 1 - index];
                _values = resized;
                _snapshot = new double[next];
                _capacity = next;
                _count = keep;
                _start = 0;
                _smoothedValid = false;
            }
            Repaint();
        }
    }

    public bool AutoScale
    {
        get => _autoScale;
        set { _autoScale = value; Repaint(); }
    }

    /// <summary>The lower bound used when AutoScale is off.</summary>
    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; Repaint(); }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = value; Repaint(); }
    }

    /// <summary>
    /// Puts zero inside the vertical range. Null (the default) means "an area chart does, a line does
    /// not": an area measured from anywhere but zero shows how it wiggles as if it were how big it is.
    /// </summary>
    public bool? IncludeZero { get; set; }

    /// <summary>
    /// Takes the sting out of a live axis: a value outside the current range expands it at once, but a
    /// range only shrinks when the data has become clearly smaller — so noise cannot make it breathe.
    /// </summary>
    public bool SmoothScale { get; set; }

    /// <summary>The vertical bounds used by the last paint: auto-scaled, or the fixed Minimum/Maximum.</summary>
    public (double Low, double High) Range { get; private set; }

    /// <summary>
    /// Works out the bounds for one paint and moves the smoothing one step; the paint calls this for
    /// each frame, and so can a host that wants to know the axis before it is drawn.
    /// </summary>
    public (double Low, double High) AdvanceScale() => Range = ComputeRange();

    public int Count { get { lock (_gate) return _count; } }

    public double Last { get { lock (_gate) return _count == 0 ? double.NaN : this[_count - 1]; } }

    /// <summary>The samples oldest to newest; a copy, so the caller cannot pull the buffer apart.</summary>
    public IReadOnlyList<double> Values
    {
        get
        {
            lock (_gate)
            {
                var copy = new double[_count];
                for (var index = 0; index < _count; index++) copy[index] = this[index];
                return copy;
            }
        }
    }

    /// <summary>Adds one sample, dropping the oldest when the window is full.</summary>
    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;

        lock (_gate)
        {
            if (_count < _capacity)
            {
                _values[(_start + _count) % _capacity] = value;
                _count++;
            }
            else
            {
                _values[_start] = value;
                _start = (_start + 1) % _capacity;
            }
        }

        Repaint();
    }

    public void Push(IEnumerable<double> values)
    {
        foreach (var value in values) Push(value);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _count = 0;
            _start = 0;
        }
        _smoothedValid = false;
        _shrinkFrames = 0;
        Repaint();
    }

    /// <summary>Lowest, highest and mean of what is in the window right now.</summary>
    public (double Low, double High, double Mean) Statistics
    {
        get
        {
            lock (_gate)
            {
                if (_count == 0) return (0, 0, 0);
                var low = double.MaxValue;
                var high = double.MinValue;
                var sum = 0d;
                for (var index = 0; index < _count; index++)
                {
                    var value = this[index];
                    if (value < low) low = value;
                    if (value > high) high = value;
                    sum += value;
                }
                return (low, high, sum / _count);
            }
        }
    }

    private double this[int index] => _values[(_start + index) % _capacity];

    /// <summary>
    /// Redraws on the dispatcher, once per burst even if a worker pushes a thousand samples. Nothing
    /// dispatcher-affine is touched off the UI thread — not even IsLoaded, which would throw.
    /// </summary>
    private void Repaint()
    {
        if (Dispatcher.CheckAccess())
        {
            InvalidateVisual();
            return;
        }

        if (Interlocked.Exchange(ref _repaintQueued, 1) != 0) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _repaintQueued, 0);
            InvalidateVisual();
        }), DispatcherPriority.Render);
    }

    /// <summary>
    /// A bare FrameworkElement measures to nothing, and a wide row would then draw a zero-width plot.
    /// Ask for the space the row offers, and keep a usable width when nothing constrains it.
    /// </summary>
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 360 : available.Width;
        var height = double.IsInfinity(available.Height) ? Math.Max(MinHeight, 120) : available.Height;
        return new Size(width, Math.Max(height, MinHeight));
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var accent = Resource("AccentBrush", SystemColors.HighlightBrush);
        var secondary = Resource("TextSecondaryBrush", SystemColors.GrayTextBrush);
        var divider = Resource("DividerBrush", SystemColors.ControlDarkBrush);
        var dpi = VisualTreeHelper.GetDpi(this);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4) return;

        int count;
        lock (_gate)
        {
            if (_snapshot.Length != _capacity) _snapshot = new double[_capacity];
            count = _count;
            for (var index = 0; index < count; index++) _snapshot[index] = this[index];
        }

        // Header: the caption on the left, the latest value on the right, both above the plot.
        var header = 0d;
        var caption = Caption.Length == 0 ? null : Format(Caption, 11, secondary, dpi);
        var value = ShowValue && count > 0
            ? Format(string.Format(CultureInfo.CurrentCulture, ValueFormat, _snapshot[count - 1]), 12, accent, dpi)
            : null;
        if (caption != null) drawing.DrawText(caption, new Point(0, 0));
        if (value != null) drawing.DrawText(value, new Point(width - value.Width - 1, 0));
        if (caption != null) header = caption.Height + 4;
        if (value != null) header = Math.Max(header, value.Height + 4);

        var plot = new Rect(0, header, width, Math.Max(1, height - header));

        if (count < 2)
        {
            if (EmptyText.Length > 0)
            {
                var empty = Format(EmptyText, 11.5, secondary, dpi);
                drawing.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2,
                    plot.Top + (plot.Height - empty.Height) / 2));
            }
            return;
        }

        var (low, high) = AdvanceScale();

        // Two faint reference lines: the window's top and bottom.
        var gridPen = new Pen(AccentTheme.Copy(divider, 0.5), 1);
        drawing.DrawLine(gridPen, new Point(0, Align(plot.Top, dpi.DpiScaleY)), new Point(width, Align(plot.Top, dpi.DpiScaleY)));
        drawing.DrawLine(gridPen, new Point(0, Align(plot.Bottom, dpi.DpiScaleY)), new Point(width, Align(plot.Bottom, dpi.DpiScaleY)));

        var span = Capacity <= 1 ? 1 : Capacity - 1;
        double X(int index) => plot.Left + plot.Width * index / span;
        double Y(double sample) => plot.Bottom - plot.Height * (sample - low) / (high - low);

        if (Kind == ChartStyle.Bars)
        {
            var barPen = new Pen(accent, Math.Max(1, plot.Width / Capacity * 0.6))
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
            };
            for (var index = 0; index < count; index++)
                drawing.DrawLine(barPen, new Point(X(index), plot.Bottom), new Point(X(index), Y(_snapshot[index])));
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(X(0), Y(_snapshot[0])), Kind == ChartStyle.Area, Kind == ChartStyle.Area);
                var points = new List<Point>(count);
                for (var index = 1; index < count; index++) points.Add(new Point(X(index), Y(_snapshot[index])));
                context.PolyLineTo(points, true, false);
                if (Kind == ChartStyle.Area)
                {
                    context.LineTo(new Point(X(count - 1), plot.Bottom), true, false);
                    context.LineTo(new Point(X(0), plot.Bottom), true, false);
                }
            }
            geometry.Freeze();

            if (Kind == ChartStyle.Area)
            {
                drawing.DrawGeometry(AccentTheme.Copy(accent, 0.18), null, geometry);
            }

            var pen = new Pen(accent, 1.5)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            drawing.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>Text in the application's UI font, so a chart follows a font change like every row does.</summary>
    /// <summary>
    /// The bounds this paint should use: the data's own range (plus zero and 8% air) when auto-scaling,
    /// or exactly Minimum..Maximum when the caller fixed it — and, with SmoothScale, the range grows at
    /// once and only steps down when the data has clearly become smaller.
    /// </summary>
    private (double Low, double High) ComputeRange()
    {
        if (!AutoScale) return (Minimum, Maximum);

        double low, high;
        lock (_gate)
        {
            if (_count == 0) return (Minimum, Maximum);
            low = double.MaxValue;
            high = double.MinValue;
            for (var index = 0; index < _count; index++)
            {
                var value = this[index];
                if (value < low) low = value;
                if (value > high) high = value;
            }
        }

        if (IncludeZero ?? Kind == ChartStyle.Area)
        {
            if (low > 0) low = 0;
            if (high < 0) high = 0;
        }

        if (high - low < 1e-9)
        {
            low -= 1;
            high += 1;
        }
        else
        {
            var pad = (high - low) * 0.08;
            low -= pad;
            high += pad;
        }

        if (!SmoothScale) return (low, high);

        if (!_smoothedValid)
        {
            _smoothed = (low, high);
            _smoothedValid = true;
            return _smoothed;
        }

        var current = _smoothed;
        if (low < current.Low || high > current.High)
        {
            // Grow only the side that left the range: the other one would otherwise jump as well.
            _smoothed = (Math.Min(current.Low, low), Math.Max(current.High, high));
            _shrinkFrames = 0;
            return _smoothed;
        }

        // Shrinking waits for the data to stay clearly smaller for a while. Without that wait the axis
        // snaps back up on the next sample and the "smoothing" moves as often as the plain version.
        if (high - low <= (current.High - current.Low) * 0.9)
        {
            if (++_shrinkFrames >= 8)
            {
                _smoothed = (low, high);
                _shrinkFrames = 0;
            }
        }
        else
        {
            _shrinkFrames = 0;
        }

        return _smoothed;
    }

    private FormattedText Format(string text, double size, Brush brush, DpiScale dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(UiFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush,
            dpi.PixelsPerDip);

    private FontFamily UiFamily => TryFindResource("UiFont") as FontFamily ?? SystemFonts.MessageFontFamily;

    private static double Align(double value, double scale) => Math.Round(value * scale) / scale + 0.5 / scale;

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);

    private sealed class ChartAutomationPeer : FrameworkElementAutomationPeer
    {
        public ChartAutomationPeer(Chart owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override string GetClassNameCore() => nameof(Chart);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return name.Length > 0 ? name : ((Chart)Owner).Caption;
        }
    }
}