using System;
using System.Collections.Generic;
using System.Linq;

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

        public static class FilterCategory
        {
            public const string All = "All";
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

        public static class StatusToken
        {
            public const string Fallback = "Fallback";
            public const string Error = "Error";
            public const string Orphaned = "Orphaned";
            public const string Missing = "Missing";
            public const string Exception = "Exception";
            public const string Failed = "Failed";
            public const string NotRegistered = "Not Registered";
            public const string Invalid = "Invalid";
            public const string NotFound = "Not Found";
            public const string NoMenu = "No Menu";
            public const string NotMeasured = "Not Measured";
            public const string Unsupported = "Unsupported";
        }

        public static class CategoryLocationHint
        {
            public const string Background = "Background";
            public const string Drive = "Drive";
            public const string Directory = "Directory";
            public const string Folder = "Folder";
            public const string AllFiles = "All Files";
            public const string Extension = "Extension";
            public const string AllFileSystemObjects = "All File System Objects";
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

        public static bool IsCategoryMatch(string? selectedCategory, string? resultCategory)
        {
            if (string.IsNullOrWhiteSpace(selectedCategory)
                || string.Equals(selectedCategory, FilterCategory.All, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(selectedCategory, resultCategory, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFallbackLikeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Contains(StatusToken.Fallback, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Error, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Orphaned, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Missing, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWarningLikeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.StartsWith(Status.LoadError, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Exception, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Failed, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.NotRegistered, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Invalid, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.NotFound, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Fallback, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.NoMenu, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Orphaned, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Missing, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNotMeasuredLikeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Contains(StatusToken.NotMeasured, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.Unsupported, StringComparison.OrdinalIgnoreCase)
                || status.Contains(StatusToken.NoMenu, StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveCategoryFromLocations(IEnumerable<string> locations)
        {
            var locs = locations?.ToList() ?? new List<string>();

            if (locs.Any(l => l.Contains(CategoryLocationHint.Background, StringComparison.OrdinalIgnoreCase))) return Category.Background;
            if (locs.Any(l => l.Contains(CategoryLocationHint.Drive, StringComparison.OrdinalIgnoreCase))) return Category.Drive;
            if (locs.Any(l => l.Contains(CategoryLocationHint.Directory, StringComparison.OrdinalIgnoreCase) || l.Contains(CategoryLocationHint.Folder, StringComparison.OrdinalIgnoreCase))) return Category.Folder;
            if (locs.Any(l => l.Contains(CategoryLocationHint.AllFiles, StringComparison.OrdinalIgnoreCase) || l.Contains(CategoryLocationHint.Extension, StringComparison.OrdinalIgnoreCase) || l.Contains(CategoryLocationHint.AllFileSystemObjects, StringComparison.OrdinalIgnoreCase))) return Category.File;

            return Category.File;
        }
    }
}
