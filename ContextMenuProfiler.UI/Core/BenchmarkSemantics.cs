using System;

namespace ContextMenuProfiler.UI.Core
{
    public static class BenchmarkSemantics
    {
        public const string StatusRegistryFallback = Status.RegistryFallback;
        public const string StatusHookLoadedNoMenu = Status.HookLoadedNoMenu;
        public const string StatusLoadError = Status.LoadError;
        public const string StatusOrphanedMissingDll = Status.OrphanedMissingDll;

        public static class Type
        {
            public const string Com = "COM";
            public const string Uwp = "UWP";
            public const string Static = "Static";
            public const string PackagedExtension = "Packaged Extension";
            public const string PackagedCom = "Packaged COM";
            public const string UwpPackagedCom = "UWP / Packaged COM";
        }

        public static class Category
        {
            public const string File = "File";
            public const string Folder = "Folder";
            public const string Background = "Background";
            public const string Drive = "Drive";
            public const string Uwp = "UWP";
            public const string Static = "Static";
        }

        public static class Status
        {
            public const string Unknown = "Unknown";
            public const string Ok = "OK";
            public const string VerifiedViaHook = "Verified via Hook";
            public const string HookLoadedNoMenu = "Hook Loaded (No Menu)";
            public const string OrphanedMissingDll = "Orphaned / Missing DLL";
            public const string IpcTimeout = "IPC Timeout";
            public const string LoadError = "Load Error";
            public const string RegistryFallback = "Registry Fallback";
            public const string StaticNotMeasured = "Static (Not Measured)";
            public const string SkippedKnownUnstable = "Skipped (Known Unstable)";
            public const string DisabledPendingRestart = "Disabled (Pending Restart)";
            public const string EnabledPendingRestart = "Enabled (Pending Restart)";
        }

        public static bool IsPackagedExtensionType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            return string.Equals(type, Type.Uwp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, Type.PackagedExtension, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, Type.PackagedCom, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, Type.UwpPackagedCom, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFallbackLikeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Contains("Fallback", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Error", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Orphaned", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Missing", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWarningLikeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.StartsWith(Status.LoadError, StringComparison.OrdinalIgnoreCase)
                || status.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Not Registered", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Fallback", StringComparison.OrdinalIgnoreCase)
                || status.Contains("No Menu", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Orphaned", StringComparison.OrdinalIgnoreCase)
                || status.Contains("Missing", StringComparison.OrdinalIgnoreCase);
        }
    }
}
