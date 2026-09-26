using System;
using UEVR;

foreach (bool dibr in new[] { false, true }) {
    var choices = RenderingModeOptions.Create(dibr);
    for (int id = 0; id <= 5; ++id) {
        bool supported = id < 3 || id == 5 || dibr;
        if (choices.ContainsKey(id.ToString()) != supported) { throw new Exception("IDs were renumbered"); }
    }
    foreach (string saved in new[] { "0", "1", "2", "3", "4", "5", "99", "-1" }) {
        var mapped = RenderingModeOptions.PreserveValue(choices, saved);
        // XAML binds SelectedValuePath=Key, not SelectedIndex.
        if (!mapped.ContainsKey(saved) || (mapped.Count != choices.Count + (choices.ContainsKey(saved) ? 0 : 1))) {
            throw new Exception("Saved ID was clamped, lost, or added to the shared choices");
        }
    }
    if (choices.ContainsKey("99")) { throw new Exception("Unknown ID leaked into other profiles"); }
}
Console.WriteLine("Rendering method ID/unknown-profile checks passed for DIBR and non-DIBR frontends.");
