namespace HzSwitcher.UI.Controls;

/// <summary>A vertical stack whose children follow the available width, including when a scrollbar appears.</summary>
internal class LayoutStack : FlowLayoutPanel
{
    private bool _arranging;

    public LayoutStack()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Margin = Padding.Empty;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (!_arranging)
        {
            _arranging = true;
            try
            {
                int width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                foreach (Control child in Controls)
                {
                    int available = Math.Max(1, width - child.Margin.Horizontal);
                    if (child is Label) child.MaximumSize = new Size(available, 0);
                    child.Width = available;
                }
            }
            finally { _arranging = false; }
        }
        base.OnLayout(e);
    }
}
