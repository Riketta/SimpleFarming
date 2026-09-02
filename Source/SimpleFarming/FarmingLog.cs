using System.Collections.Generic;
using Verse;

namespace SimpleFarming
{
    /// <summary>Central logging for the mod. Debug output is opt-in via the mod settings;
    /// errors always surface so broken def data from other mods gets reported.</summary>
    public static class FarmingLog
    {
        private const string Prefix = "[SimpleFarming] ";

        private static readonly HashSet<string> onceKeys = new HashSet<string>();

        public static void Debug(string message)
        {
            if (SimpleFarmingMod.DebugLogging)
            {
                Log.Message(Prefix + message);
            }
        }

        /// <summary>Debug log that fires only once per key (e.g. per defName) so opening an
        /// info card repeatedly does not spam the log.</summary>
        public static void DebugOnce(string key, string message)
        {
            if (!onceKeys.Add(key))
            {
                return;
            }
            Debug(message);
        }

        /// <summary>Error that fires only once per key, for conditions that would otherwise
        /// repeat on every info card open (e.g. a broken def from another mod).</summary>
        public static void ErrorOnce(string key, string message)
        {
            if (onceKeys.Add("error:" + key))
            {
                Log.Error(Prefix + message);
            }
        }

        /// <summary>Forgets which keys were already logged; used when cached models are
        /// dropped so debug output can legitimately repeat after a settings change.</summary>
        public static void ClearOnceKeys()
        {
            onceKeys.Clear();
        }

        public static void Error(string message)
        {
            Log.Error(Prefix + message);
        }
    }
}
