using System.Collections.Generic;

namespace UEVR {
    // Serialized backend IDs, not the ComboBox's ordinal indices.
    public static class RenderingModeOptions {
        public const int Mono = 5;
        public const int NativeStateAfw = 6;
        public const int LegacyAfw = 7;
        // Keep AFW hidden until the matching backend integration is enabled.
        public static Dictionary<string, string> Create(bool supportsDibr, bool supportsAfw = false) {
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
            if (supportsAfw) {
                result.Add(NativeStateAfw.ToString(), "AFW - Native-State Source (Experimental)");
                result.Add(LegacyAfw.ToString(), "AFW - Legacy AFR/Synced Source (Experimental)");
            }
            return result;
        }
        public static Dictionary<string, string> PreserveValue(Dictionary<string, string> choices, string value) {
            var result = new Dictionary<string, string>(choices);
            if (!result.ContainsKey(value)) { result.Add(value, $"Unsupported method ({value}, preserved)"); }
            return result;
        }
    }
}
