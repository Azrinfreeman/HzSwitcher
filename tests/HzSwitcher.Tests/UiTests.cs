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
        Assert.True(ratio >= 4.5, $"contrast was {ratio:0.0}");
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

    [Fact]
    public void TextAndInputBoundariesRemainReadableAcrossSurfaces()
    {
        foreach (Color surface in new[] { Theme.Bg, Theme.Surface, Theme.SurfaceRaised })
        {
            Assert.True(Theme.Contrast(Theme.Text, surface) >= 4.5);
            Assert.True(Theme.Contrast(Theme.TextMuted, surface) >= 4.5);
            Assert.True(Theme.Contrast(Theme.InputBorder, surface) >= 3.0);
        }
    }

    [Fact]
    public void AccentLabelContrastSurvivesHoverAndPress()
    {
        foreach (float shift in new[] { -0.12f, 0f, 0.12f })
        {
            Color fill = Theme.Shift(Theme.Accent, shift);
            Assert.True(Theme.Contrast(Theme.ReadableText(fill), fill) >= 4.5);
        }
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

    [Fact]
    public void AccessibleValueUsesTheUserChangeAndCommitPath()
    {
        using var slider = new ThemedSlider { Minimum = 20, Maximum = 100, Value = 50 };
        int changes = 0, commits = 0;
        slider.Scroll += (_, _) => changes++;
        slider.InteractionCompleted += (_, _) => commits++;
        slider.AccessibilityObject.Value = "5";
        Assert.Equal(20, slider.Value);
        Assert.Equal(1, changes);
        Assert.Equal(1, commits);
        slider.Enabled = false;
        slider.AccessibilityObject.Value = "80";
        Assert.Equal(20, slider.Value);
        Assert.Equal(1, commits);
    }
}

public class NavigationAndLayoutTests
{
    [Fact]
    public void RatesExposeSelectionSeparatelyFromButtonActions()
    {
        using var group = new ChipGroup();
        using var rate = new PillButton { Text = "120 Hz", Selected = true };
        group.Controls.Add(rate);
        int applied = 0;
        rate.Click += (_, _) => applied++;
        Assert.Equal(AccessibleRole.RadioButton, rate.AccessibilityObject.Role);
        Assert.True(rate.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        rate.AccessibilityObject.DoDefaultAction();
        Assert.Equal(1, applied);
        rate.Selected = false;
        Assert.False(rate.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        using var preset = new PillButton { Text = "Gaming 120 Hz" };
        Assert.Equal(AccessibleRole.PushButton, preset.AccessibilityObject.Role);
    }

    [Fact]
    public void NavigationExposesEachPageAndItsSelection()
    {
        using var rail = new NavRail();
        rail.Add("Display", "");
        rail.Add("Automation", "");
        var accessible = rail.AccessibilityObject;
        Assert.Equal(2, accessible.GetChildCount());
        var automation = accessible.GetChild(1);
        Assert.Equal("Automation", automation.Name);
        automation.DoDefaultAction();
        Assert.Equal(1, rail.SelectedIndex);
        Assert.True(automation.State.HasFlag(AccessibleStates.Selected));
        Assert.False(accessible.GetChild(0).State.HasFlag(AccessibleStates.Selected));
    }

    [Theory]
    [InlineData(580)]
    [InlineData(380)]
    public void StacksWrapLongLabelsAndKeepChildrenWithinTheirContentWidth(int width)
    {
        using var stack = new LayoutStack { AutoSize = false, Size = new Size(width, 220), Padding = new Padding(24), AutoScroll = true };
        var label = MainForm.TextLabel(new string('W', 160));
        var nested = new LayoutStack();
        nested.Controls.Add(MainForm.Setting("Restore brightness at startup", new Toggle { Text = "" }));
        stack.Controls.Add(label);
        stack.Controls.Add(nested);
        stack.PerformLayout();
        Assert.True(label.Height > label.Font.Height);
        foreach (Control child in stack.Controls)
            Assert.True(child.Right <= stack.ClientSize.Width - stack.Padding.Right, $"Child {child.GetType().Name} overflowed: {child.Bounds}");
    }
}

public class DialogLayoutTests
{
    private sealed class ProbeDialog : DialogShell
    {
        public ProbeDialog() : base("Editor", new Size(580, 360))
        {
            Body.Controls.Add(new Panel { Size = new Size(400, 1200) });
            LeftActions.Controls.Add(new PillButton { Text = "Add item" });
            Finish();
        }

        public Control Save => SaveButton;
        public Control Cancel => CancelButtonControl;
        public Control Content => Body;
        protected override bool OnSave() => false;
    }

    [Fact]
    public void DialogKeepsActionsOutsideTheScrollingBodyAndSaveOnTheRight()
    {
        using var dialog = new ProbeDialog();
        dialog.PerformLayout();
        var footer = dialog.Save.Parent.Parent;
        Assert.True(footer.Height < 90, $"Footer unexpectedly occupies {footer.Height} pixels");
        Assert.True(dialog.Content.Bottom <= footer.Top);
        Assert.True(dialog.Save.Left > dialog.Cancel.Left);
        Assert.True(dialog.Save.Right <= dialog.Save.Parent.ClientSize.Width);
        dialog.ClientSize = new Size(500, 240);
        dialog.PerformLayout();
        Assert.True(footer.Bottom <= dialog.ClientSize.Height);
        Assert.True(dialog.Content.Bottom <= footer.Top);
    }
}

public class ShortcutCaptureTests
{
    [Theory]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Shift | Keys.Tab)]
    [InlineData(Keys.Escape)]
    [InlineData(Keys.Enter)]
    public void RecordingDoesNotTrapDialogNavigation(Keys key)
    {
        using var field = new TextBox { Text = "Ctrl+Alt+1" };
        var input = new KeyEventArgs(key);
        HotkeysForm.CaptureKey(field, input);
        Assert.False(input.SuppressKeyPress);
        Assert.Equal("Ctrl+Alt+1", field.Text);
    }

    [Fact]
    public void RecordingStillCapturesModifiedKeysAndClearsWithBackspace()
    {
        using var field = new TextBox();
        var input = new KeyEventArgs(Keys.Control | Keys.Alt | Keys.D2);
        HotkeysForm.CaptureKey(field, input);
        Assert.True(input.SuppressKeyPress);
        Assert.Equal("Ctrl+Alt+2", field.Text);
        HotkeysForm.CaptureKey(field, new KeyEventArgs(Keys.Back));
        Assert.Empty(field.Text);
    }
}
