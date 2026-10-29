using Lantern.UI;
using Xunit;

namespace LanternUI.Tests;

public class ChoicePickerTests
{
    private static ChoicePicker Picker() =>
        new(Enumerable.Range(0, 12).Select(index => new Choice("Item " + index, "i" + index)).ToArray());

    [Fact]
    public void Lists_every_entry_and_filters_by_text()
    {
        Sta.Run(() =>
        {
            var picker = Picker();
            Assert.Equal(12, picker.VisibleCount);

            picker.SetFilter("Item 1");
            Assert.Equal(3, picker.VisibleCount);   // Item 1, Item 10, Item 11

            picker.SetFilter("zzz");
            Assert.Equal(0, picker.VisibleCount);
        });
    }

    [Fact]
    public void Filtering_ignores_case()
    {
        Sta.Run(() =>
        {
            var picker = Picker();
            picker.SetFilter("ITEM 1");
            Assert.Equal(3, picker.VisibleCount);   // Item 1, Item 10, Item 11
        });
    }

    [Fact]
    public void Selecting_reports_only_when_asked()
    {
        Sta.Run(() =>
        {
            var picker = Picker();
            var reported = new List<int>();
            picker.Changed += reported.Add;

            picker.Select(4, notify: false);
            Assert.Equal(4, picker.SelectedIndex);
            Assert.Empty(reported);

            picker.Select(5, notify: true);
            Assert.Equal(5, picker.SelectedIndex);
            Assert.Equal(new[] { 5 }, reported);
        });
    }
}
