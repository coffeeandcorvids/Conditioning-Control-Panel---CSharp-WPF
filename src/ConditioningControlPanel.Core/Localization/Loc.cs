using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Models
{
    /// <summary>
    /// English-only shim for upstream's Localization.Loc (which fronts a
    /// LocalizationManager we don't port — our fork ships one language).
    /// Unknown keys return the key itself, matching upstream's miss behaviour,
    /// so LocalizedName-style accessors degrade to their raw properties upstreamside
    /// and to visible keys here. A table can be injected later if we ever localize.
    /// </summary>
    public static class Loc
    {
        public static IReadOnlyDictionary<string, string>? Table { get; set; }

        public static string Get(string key) =>
            Table != null && Table.TryGetValue(key, out var v) ? v : key;

        public static string GetF(string key, params object[] args)
        {
            var format = Get(key);
            try { return string.Format(format, args); }
            catch (FormatException) { return format; }
        }
    }
}
