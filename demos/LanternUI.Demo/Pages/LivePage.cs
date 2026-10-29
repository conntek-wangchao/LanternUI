using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lantern.UI;

namespace LanternUI.Demo.Pages;

/// <summary>
/// The chart piece: three charts fed by a synthetic source. One source runs on the dispatcher, one on a
/// worker thread (to show that pushing from anywhere is safe), and one keeps a fixed range so the bars
/// do not jump. The charts themselves own no timer and hold no data of their own.
/// </summary>
public sealed class LivePage : PageBase
{
    private readonly Chart _throughput;
    private readonly Chart _latency;
    private readonly Chart _distribution;
    private readonly DispatcherTimer _pump;
    private System.Threading.Timer? _worker;
    private int _tick;
    private double _phase;

    public LivePage(IAppState app, IShell shell, ILocalizer loc)
        : base(app, loc, loc.T("Live"),
            loc.T("A chart draws what a source pushes: no timer, no data of its own."), shell.Layout)
    {
        Group(loc.T("Streaming"));
        ToggleRow(loc.T("Stream"),
            loc.T("A synthetic source at twenty samples a second; the chart only redraws."),
            () => S.Bool("chart_stream"), Stream, glyph: Glyphs.Speed);

        _throughput = new Chart
        {
            Kind = ChartStyle.Area,
            Capacity = 120,
            IncludeZero = true,
            SmoothScale = S.Bool("chart_smooth"),
            Caption = loc.T("Samples per second"),
            EmptyText = loc.T("No data yet."),
        };
        ChartRow(loc.T("Throughput"), loc.T("Pushed from a dispatcher timer, one sample per tick."),
            _throughput, out _, Glyphs.Speed);

        _latency = new Chart
        {
            Kind = ChartStyle.Line,
            Capacity = 60,
            SmoothScale = S.Bool("chart_smooth"),
            ValueFormat = "{0:0}",
            Caption = loc.T("Milliseconds"),
            EmptyText = loc.T("No data yet."),
        };
        ChartRow(loc.T("Latency"), loc.T("Pushed from a worker thread: the control marshals its own repaint."),
            _latency, out _, Glyphs.Lab);

        _distribution = new Chart
        {
            Kind = ChartStyle.Bars,
            Capacity = 40,
            AutoScale = false,
            Minimum = 0,
            Maximum = 100,
            ValueFormat = "{0:0}%",
            Caption = loc.T("Percent"),
            EmptyText = loc.T("No data yet."),
        };
        ChartRow(loc.T("Distribution"), loc.T("A fixed 0-100 range, so the scale does not jump."),
            _distribution, out _, Glyphs.Monitor);

        Group(loc.T("Window"));
        SliderRow(loc.T("Samples in the window"), loc.T("How many samples stay in the rolling window."),
            20, 300, 10, () => _throughput.Capacity, value =>
            {
                _throughput.Capacity = (int)Math.Round(value);
                _latency.Capacity = Math.Max(20, _throughput.Capacity / 2);
            }, glyph: Glyphs.Chart);

        ToggleRow(loc.T("Smooth scale"),
            loc.T("Grow the axis at once when a sample leaves it, and shrink it only when the data clearly settles."),
            () => S.Bool("chart_smooth"), value =>
            {
                _throughput.SmoothScale = value;
                _latency.SmoothScale = value;
                _throughput.InvalidateVisual();
                _latency.InvalidateVisual();
            }, glyph: Glyphs.Speed);

        // A little history, so the page is not blank before the first tick arrives.
        for (var index = 0; index < 40; index++) Warm(0.35 + 0.1 * Math.Sin(index / 3.0));

        _pump = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _pump.Tick += (_, _) => Pulse();

        Loaded += (_, _) => { if (S.Bool("chart_stream")) Stream(true); };
        Unloaded += (_, _) => Stream(false);
    }

    private void Stream(bool running)
    {
        if (running)
        {
            _pump.Start();
            _worker ??= new System.Threading.Timer(_ => _latency.Push(120 + 60 * Random.Shared.NextDouble()), null,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(120));
        }
        else
        {
            _pump.Stop();
            _worker?.Dispose();
            _worker = null;
        }
    }

    private void Pulse()
    {
        _tick++;
        _phase += 0.21;
        var wave = Math.Sin(_phase);
        var noise = Random.Shared.NextDouble() - 0.5;
        _throughput.Push(20 + 14 * wave + 3 * noise);
        _distribution.Push(Math.Clamp(50 + 36 * Math.Cos(_phase / 1.7) + 8 * noise, 0, 100));
        if (_tick % 3 == 0) _latency.Push(150 + 40 * Math.Sin(_phase / 2) + 25 * noise);
    }

    private void Warm(double fraction)
    {
        _throughput.Push(20 + 14 * fraction + 3 * (Random.Shared.NextDouble() - 0.5));
        _distribution.Push(Math.Clamp(50 + 36 * fraction, 0, 100));
        _latency.Push(150 + 30 * fraction);
    }
}
