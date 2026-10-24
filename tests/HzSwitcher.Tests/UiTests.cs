using HzSwitcher.UI;
using HzSwitcher.UI.Controls;
using Xunit;

namespace HzSwitcher.Tests;

public class ThemeTests
{
    [Fact] public void ContrastOfBlackAndWhiteIs21() => Assert.Equal(21.0, Theme.Contrast(Color.White, Color.Black), 1);
    [Fact] public void ContrastOfSameColorIs1() => Assert.Equal(1.0, Theme.Contrast(Color.SteelBlue, Color.SteelBlue), 3);

    [Fact]
    public void AccentTextIsReadableOnAccent()
    {
        double ratio = Theme.Contrast(Theme.Accent, Theme.AccentText);
        Assert.True(ratio >= 3.0, $"contrast was {ratio:0.0}");
    }

    [Fact]
    public void AccentIsVisibleOnCards()
    {
        Assert.True(Theme.Contrast(Theme.Accent, Theme.Surface) >= 3.0);
    }

    [Fact]
    public void ShiftLightensAndDarkens()
    {
        var c = Color.FromArgb(100, 100, 100);
        Assert.True(Theme.Shift(c, 0.2f).R > c.R);
        Assert.True(Theme.Shift(c, -0.2f).R < c.R);
    }
}

public class NumberBoxTests
{
    [Fact]
    public void ValueIsClampedToRange()
    {
        var box = new NumberBox { Minimum = 1, Maximum = 10 };
        box.Value = 99;
        Assert.Equal(10, box.Value);
        box.Value = -5;
        Assert.Equal(1, box.Value);
    }

    [Fact]
    public void ShrinkingTheRangeClampsTheValue()
    {
        var box = new NumberBox { Minimum = 0, Maximum = 100 };
        box.Value = 80;
        box.Maximum = 50;
        Assert.Equal(50, box.Value);
    }

    [Fact]
    public void ValueChangedFiresOnlyWhenTheValueChanges()
    {
        var box = new NumberBox { Minimum = 0, Maximum = 100 };
        int fired = 0;
        box.ValueChanged += (_, _) => fired++;
        box.Value = 5;
        box.Value = 5;
        box.Value = 6;
        Assert.Equal(2, fired);
    }
}

public class ThemedSliderTests
{
    [Fact]
    public void ValueIsClampedToRange()
    {
        var s = new ThemedSlider { Minimum = 20, Maximum = 100 };
        s.Value = 5;
        Assert.Equal(20, s.Value);
        s.Value = 500;
        Assert.Equal(100, s.Value);
    }

    [Fact]
    public void RaisingTheMinimumPullsTheValueUp()
    {
        var s = new ThemedSlider { Minimum = 0, Maximum = 100 };
        s.Value = 10;
        s.Minimum = 20;
        Assert.Equal(20, s.Value);
    }
}
