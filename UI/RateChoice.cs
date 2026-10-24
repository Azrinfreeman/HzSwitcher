namespace HzSwitcher.UI;

/// <summary>Helper for rate dropdowns: first item is "Highest" (0), then the supported rates.</summary>
internal static class RateChoice
{
    public static void Fill(ComboBox box, IEnumerable<int> supported, int selectedHz)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.Clear();
        box.Items.Add("Highest");
        var rates = supported.ToList();
        if (selectedHz > 0 && !rates.Contains(selectedHz)) rates.Add(selectedHz);   // keep a saved rate the monitor no longer offers
        foreach (int hz in rates.Distinct().OrderBy(x => x)) box.Items.Add(hz == selectedHz && !supported.Contains(hz) ? $"{hz} Hz (unsupported)" : $"{hz} Hz");
        box.SelectedIndex = selectedHz <= 0 ? 0 : Math.Max(0, box.Items.IndexOf(box.Items.Cast<string>().First(i => i.StartsWith(selectedHz + " Hz"))));
    }

    public static int Value(ComboBox box)
    {
        string text = box.SelectedItem as string ?? "Highest";
        return text == "Highest" ? 0 : int.Parse(text.Split(' ')[0]);
    }
}
