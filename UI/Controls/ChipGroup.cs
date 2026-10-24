namespace HzSwitcher.UI.Controls;

/// <summary>A wrapping row of single-choice chips; arrow keys move between them.</summary>
internal sealed class ChipGroup : FlowLayoutPanel
{
    public ChipGroup()
    {
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0);
    }

    public void MoveFocus(Control from, int direction)
    {
        int i = Controls.IndexOf(from) + direction;
        if (i >= 0 && i < Controls.Count) Controls[i].Focus();
    }
}
