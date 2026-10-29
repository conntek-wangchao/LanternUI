using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

/// <summary>
/// The chart piece: the rolling window's arithmetic, the worker-thread path, and that it actually puts
/// ink on the surface. No timer is involved anywhere — the control never owns one.
/// </summary>
public class ChartTests
{
    [Fact]
    public void A_full_window_drops_the_oldest_sample() => Sta.Run(() =>
    {
        var chart = new Chart { Capacity = 5 };
        for (var value = 1; value <= 9; value++) chart.Push(value);

        Assert.Equal(5, chart.Count);
        Assert.Equal(new double[] { 5, 6, 7, 8, 9 }, chart.Values);
        Assert.Equal(9, chart.Last);
    });

    [Fact]
    public void Resizing_the_window_keeps_the_newest_samples() => Sta.Run(() =>
    {
        var chart = new Chart { Capacity = 10 };
        for (var value = 1; value <= 10; value++) chart.Push(value);

        chart.Capacity = 4;

        Assert.Equal(4, chart.Count);
        Assert.Equal(new double[] { 7, 8, 9, 10 }, chart.Values);

        chart.Capacity = 8;
        Assert.Equal(4, chart.Count);
        Assert.Equal(new double[] { 7, 8, 9, 10 }, chart.Values);
    });

    [Fact]
    public void Statistics_describe_the_window() => Sta.Run(() =>
    {
        var chart = new Chart();
        chart.Push(new[] { 4d, 8d, 6d });

        var (low, high, mean) = chart.Statistics;
        Assert.Equal(4, low);
        Assert.Equal(8, high);
        Assert.Equal(6, mean, 6);
    });

    [Fact]
    public void Clear_and_non_finite_samples() => Sta.Run(() =>
    {
        var chart = new Chart { Capacity = 4 };
        chart.Push(double.NaN);
        chart.Push(double.PositiveInfinity);
        chart.Push(3);
        chart.Push(double.NaN);

        Assert.Equal(new[] { 3d }, chart.Values);

        chart.Clear();
        Assert.Equal(0, chart.Count);
        Assert.True(double.IsNaN(chart.Last));
    });

    [Fact]
    public void Pushing_from_a_worker_thread_is_safe()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Capacity = 100 };
            Parallel.For(0, 5000, index => chart.Push(index % 250));

            Assert.Equal(100, chart.Count);
            Assert.All(chart.Values, value => Assert.False(double.IsNaN(value)));
        });
    }

    [Fact]
    public void Rendering_draws_ink_with_and_without_data()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Capacity = 40, Caption = "Samples", EmptyText = "No data yet." };
            var empty = Ink(chart);

            for (var index = 0; index < 30; index++) chart.Push(Math.Sin(index / 3.0) * 10);
            var filled = Ink(chart);

            Assert.True(empty > 0, "the empty state should draw its text");
            Assert.True(filled > empty, "a chart with samples should draw more than its header");
        });
    }

    [Fact]
    public void The_automation_peer_names_the_chart()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Caption = "Samples per second" };
            System.Windows.Automation.AutomationProperties.SetName(chart, "Throughput");

            var peer = UIElementAutomationPeer.CreatePeerForElement(chart);
            Assert.NotNull(peer);
            Assert.Equal("Throughput", peer!.GetName());
            Assert.Equal(AutomationControlType.Custom, peer.GetAutomationControlType());
        });
    }


    [Fact]
    public void Auto_scale_follows_data_that_leaves_the_old_range()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Capacity = 20 };
            for (var index = 0; index < 10; index++) chart.Push(index);
            chart.AdvanceScale();
            var before = chart.Range;

            chart.Push(1000);
            chart.AdvanceScale();
            var after = chart.Range;

            Assert.True(after.High >= 1000, "the axis has to take the spike in");
            Assert.True(after.High > before.High * 10, "and it has to move a lot, not a little");
            Assert.True(after.Low <= 0, "the low end follows the window too");
        });
    }

    [Fact]
    public void A_fixed_range_keeps_its_bounds_and_clips_the_overflow()
    {
        Sta.Run(() =>
        {
            var chart = new Chart
            {
                Capacity = 20,
                AutoScale = false,
                Minimum = 0,
                Maximum = 100,
                ShowValue = false,
            };

            chart.Push(50);
            chart.Push(500);
            var host = new System.Windows.Controls.Grid { Width = 400, Height = 200 };
            chart.Height = 80;
            chart.VerticalAlignment = VerticalAlignment.Bottom;
            host.Children.Add(chart);
            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var pixels = new byte[400 * 200 * 4];
            bitmap.CopyPixels(pixels, 400 * 4, 0);

            var above = 0;
            for (var row = 0; row < 100; row++)
                for (var column = 0; column < 400; column++)
                    if (pixels[(row * 400 + column) * 4 + 3] > 24) above++;

            Assert.Equal((0d, 100d), chart.Range);
            Assert.Equal(0, above);
            host.Children.Clear();
        });
    }


    [Fact]
    public void An_area_chart_anchors_at_zero_by_default()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Kind = ChartStyle.Area };
            for (var index = 0; index < 20; index++) chart.Push(20 + index % 10);
            chart.AdvanceScale();

            Assert.True(chart.Range.Low <= 0, "an area measured from 20 lies about its size: " + chart.Range.Low);
        });
    }

    [Fact]
    public void A_line_chart_does_not_include_zero()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Kind = ChartStyle.Line };
            for (var index = 0; index < 20; index++) chart.Push(20 + index % 10);
            chart.AdvanceScale();

            Assert.True(chart.Range.Low > 15, "a line should keep the range tight: " + chart.Range.Low);
        });
    }

    [Fact]
    public void Include_zero_can_be_forced_either_way()
    {
        Sta.Run(() =>
        {
            var tight = new Chart { Kind = ChartStyle.Area, IncludeZero = false };
            tight.Push(30);
            tight.Push(40);
            tight.AdvanceScale();
            Assert.True(tight.Range.Low > 20);

            var wide = new Chart { Kind = ChartStyle.Line, IncludeZero = true };
            wide.Push(30);
            wide.Push(40);
            wide.AdvanceScale();
            Assert.True(wide.Range.Low <= 0);
        });
    }

    [Fact]
    public void Smooth_scale_expands_at_once_and_shrinks_in_steps()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Capacity = 4, SmoothScale = true };
            chart.Push(50);
            chart.AdvanceScale();

            chart.Push(1000);
            chart.AdvanceScale();
            Assert.True(chart.Range.High >= 1000, "a spike must be visible at once");

            for (var index = 0; index < 4; index++) chart.Push(10);
            chart.AdvanceScale();
            Assert.True(chart.Range.High >= 1000, "one frame after the spike leaves, the axis still holds");

            for (var frame = 0; frame < 10; frame++) chart.AdvanceScale();
            Assert.True(chart.Range.High < 20, "and then it steps down: " + chart.Range.High);
        });
    }

    [Fact]
    public void Smooth_scale_moves_the_axis_far_less_on_live_data()
    {
        Sta.Run(() =>
        {
            // A noisy window: the extremes of a sliding window of ten jump around, which is the case
            // smoothing exists for — a trending series should still move its axis.
            static HashSet<(double, double)> Ranges(bool smooth)
            {
                var chart = new Chart { Capacity = 10, SmoothScale = smooth, Kind = ChartStyle.Line };
                var seen = new HashSet<(double, double)>();
                for (var index = 0; index < 120; index++)
                {
                    chart.Push(50 + 30 * Math.Sin(index * 1.7));
                    chart.AdvanceScale();
                    seen.Add(chart.Range);
                }
                return seen;
            }

            var plain = Ranges(false).Count;
            var smooth = Ranges(true).Count;

            Assert.True(plain >= 10, "the plain axis is expected to move a lot: " + plain);
            Assert.True(smooth * 2 <= plain, "plain " + plain + " vs smooth " + smooth);
            Assert.True(smooth >= 1);
        });
    }

[Fact]
    public void A_brush_that_cannot_be_frozen_does_not_break_the_drawing()
    {
        Sta.Run(() =>
        {
            var chart = new Chart { Capacity = 20, Caption = "Samples" };
            for (var index = 0; index < 12; index++) chart.Push(Math.Sin(index / 2.0) * 5);

            // The high-contrast palette defines every brush with a DynamicResource on its colour, and
            // such a brush cannot be frozen — nor can its clone, which is what the old code tried.
            var source = new System.Windows.Media.SolidColorBrush(Colors.White);
            var bound = new System.Windows.Media.SolidColorBrush(Colors.White);
            BindingOperations.SetBinding(bound, System.Windows.Media.SolidColorBrush.ColorProperty,
                new Binding(nameof(System.Windows.Media.SolidColorBrush.Color)) { Source = source });
            chart.Resources["DividerBrush"] = bound;
            chart.Resources["AccentBrush"] = bound;
            chart.Resources["TextSecondaryBrush"] = bound;

            Assert.False(bound.CanFreeze);
            Assert.True(Ink(chart) > 0, "the chart should still draw");
        });
    }

    /// <summary>Puts the chart on a surface of its own and counts the pixels it painted.</summary>
    private static int Ink(Chart chart)
    {
        var host = new System.Windows.Controls.Grid { Width = 400, Height = 120 };
        host.Children.Add(chart);
        host.Measure(new Size(400, 120));
        host.Arrange(new Rect(0, 0, 400, 120));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap(400, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);

        var pixels = new byte[400 * 120 * 4];
        bitmap.CopyPixels(pixels, 400 * 4, 0);

        var painted = 0;
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] > 24) painted++;

        host.Children.Clear();
        return painted;
    }
}