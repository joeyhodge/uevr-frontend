using System.Collections.Generic;

namespace UEVR {
    // Serialized backend IDs, not the ComboBox's ordinal indices.
    public static class RenderingModeOptions {
        public const int Mono = 5;
        public static Dictionary<string, string> Create(bool supportsDibr) {
            var result = new Dictionary<string, string> {
                { "0", "Native Stereo" },
                { "1", "Synced Sequential" },
                { "2", "Alternating/AFR" },
            };
            if (supportsDibr) {
                result.Add("3", "Synthetic Stereo (DIBR, Experimental)");
                result.Add("4", "Synthetic Stereo (DIBR Single View, Experimental)");
            }
            result.Add(Mono.ToString(), "Mono (Experimental)");
            return result;
        }
        public static Dictionary<string, string> PreserveValue(Dictionary<string, string> choices, string value) {
            var result = new Dictionary<string, string>(choices);
            if (!result.ContainsKey(value)) { result.Add(value, $"Unsupported method ({value}, preserved)"); }
            return result;
        }
    }
}
