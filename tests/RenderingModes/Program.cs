using System;
using UEVR;

foreach (bool dibr in new[] { false, true }) {
foreach (bool afw in new[] { false, true }) {
    var choices = RenderingModeOptions.Create(dibr, afw);
    for (int id = 0; id <= 5; ++id) {
        bool supported = id < 3 || id == 5 || dibr;
        if (choices.ContainsKey(id.ToString()) != supported) { throw new Exception("IDs were renumbered"); }
    }
    if (RenderingModeOptions.NativeStateAfw != 6 || RenderingModeOptions.LegacyAfw != 7 ||
        choices.ContainsKey("6") != afw || choices.ContainsKey("7") != afw) {
        throw new Exception("AFW IDs were renumbered or leaked into unsupported builds");
    }
    foreach (string saved in new[] { "0", "1", "2", "3", "4", "5", "6", "7", "99", "-1" }) {
        var mapped = RenderingModeOptions.PreserveValue(choices, saved);
        // XAML binds SelectedValuePath=Key, not SelectedIndex.
        if (!mapped.ContainsKey(saved) || (mapped.Count != choices.Count + (choices.ContainsKey(saved) ? 0 : 1))) {
            throw new Exception("Saved ID was clamped, lost, or added to the shared choices");
        }
    }
    if (choices.ContainsKey("99")) { throw new Exception("Unknown ID leaked into other profiles"); }
}
}
if (RenderingModeOptions.Create(true).ContainsKey("6")) { throw new Exception("Incomplete AFW became selectable by default"); }
Console.WriteLine("Rendering ID/unknown-profile checks passed with DIBR/AFW capabilities on and off.");
