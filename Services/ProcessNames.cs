using System;
using System.Collections.Generic;
using System.Linq;

namespace MovaCore.Services
{
    /// <summary>Matching of process names typed by the user ("Code", "code.exe", " devenv.EXE ").</summary>
    public static class ProcessNames
    {
        public static string Normalize(string name)
        {
            string trimmed = name.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            return trimmed.ToLowerInvariant();
        }

        public static HashSet<string> NormalizeAll(IEnumerable<string> names) =>
            names.Select(Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
    }
}
