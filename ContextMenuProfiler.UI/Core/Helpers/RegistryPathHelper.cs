using System;

namespace ContextMenuProfiler.UI.Core.Helpers
{
    public static class RegistryPathHelper
    {
        public const string ClassesRootPrefix = "HKEY_CLASSES_ROOT\\";
        private const string AnyFileWildcardPrefix = "*\\";
        private const string HiveTokenPrefix = "HKEY_";

        public static string NormalizeForRegedit(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (path.StartsWith(AnyFileWildcardPrefix, StringComparison.Ordinal))
            {
                return ClassesRootPrefix + path;
            }

            if (!path.Contains(HiveTokenPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ClassesRootPrefix + path;
            }

            return path;
        }
    }
}
