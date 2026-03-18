using System;
using System.Collections.Generic;

namespace ContextMenuProfiler.UI.Core
{
    public static class BenchmarkSemantics
    {
        private static readonly string[] FolderLocationHints =
        {
            CategoryLocationHint.Directory,
            CategoryLocationHint.Folder
        };

        private static readonly string[] FileLocationHints =
        {
            CategoryLocationHint.AllFiles,
            CategoryLocationHint.Extension,
            CategoryLocationHint.AllFileSystemObjects
        };

        private static class CategoryPriority
        {
            public const int Unknown = 0;
            public const int File = 1;
            public const int Folder = 2;
            public const int Drive = 3;
            public const int Background = 4;
        }

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
            public const string Timeout = "Timeout";
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

        public static class InterfaceType
        {
            public const string StaticVerb = "Static Verb";
            public const string Skipped = "Skipped";
        }

        public static class LocationSummary
        {
            public const string ModernShellUwp = "Modern Shell (UWP)";
            public const string StaticVerbRegistryShellPrefix = "Registry (Shell) - ";
        }

        public static class RegistryLocationToken
        {
            public const string Disabled = "[Disabled]";
            public const string StaticVerbDisabledKeyPrefix = "-";
        }

        public static class Runtime
        {
            public const int MaxParallelProbeTasks = 8;
            public const int IpcTimeoutLikeRoundtripThresholdMs = 1900;
            public const int HookReconnectStabilizationDelayMs = 1000;
            public const int ClipboardRetryAttempts = 5;
            public const int ClipboardRetryDelayMs = 100;
            public const uint ClipboardCantOpenHResult = 0x800401D0;
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

        public static bool IsRegistryManagedExtensionType(string? type)
        {
            return !IsPackagedExtensionType(type);
        }

        public static bool IsDisabledRegistryLocation(string? location)
        {
            return !string.IsNullOrWhiteSpace(location)
                && location.Contains(RegistryLocationToken.Disabled, StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildStaticVerbRegistryLocation(string? registryPath)
        {
            string hive = ExtractRegistryHive(registryPath);
            return LocationSummary.StaticVerbRegistryShellPrefix + hive;
        }

        public static bool IsStaticVerbRegistryPathDisabled(string? registryPath)
        {
            if (string.IsNullOrWhiteSpace(registryPath))
            {
                return false;
            }

            int lastSeparatorIndex = registryPath.LastIndexOf('\\');
            string terminalSegment = lastSeparatorIndex >= 0
                ? registryPath[(lastSeparatorIndex + 1)..]
                : registryPath;

            return terminalSegment.StartsWith(RegistryLocationToken.StaticVerbDisabledKeyPrefix, StringComparison.Ordinal);
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

        public static bool IsTimeoutLikeError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.Contains(StatusToken.Timeout, StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveCategoryFromLocations(IEnumerable<string> locations)
        {
            if (locations == null)
            {
                return Category.File;
            }

            int resolvedPriority = CategoryPriority.Unknown;

            foreach (var location in locations)
            {
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                int locationPriority = ResolveLocationPriority(location);
                if (locationPriority == CategoryPriority.Background)
                {
                    return Category.Background;
                }

                if (locationPriority > resolvedPriority)
                {
                    resolvedPriority = locationPriority;
                }
            }

            return resolvedPriority switch
            {
                CategoryPriority.Drive => Category.Drive,
                CategoryPriority.Folder => Category.Folder,
                CategoryPriority.File => Category.File,
                _ => Category.File
            };
        }

        private static int ResolveLocationPriority(string location)
        {
            if (location.Contains(CategoryLocationHint.Background, StringComparison.OrdinalIgnoreCase))
            {
                return CategoryPriority.Background;
            }

            if (location.Contains(CategoryLocationHint.Drive, StringComparison.OrdinalIgnoreCase))
            {
                return CategoryPriority.Drive;
            }

            if (ContainsAnyLocationHint(location, FolderLocationHints))
            {
                return CategoryPriority.Folder;
            }

            if (ContainsAnyLocationHint(location, FileLocationHints))
            {
                return CategoryPriority.File;
            }

            return CategoryPriority.Unknown;
        }

        private static bool ContainsAnyLocationHint(string location, IEnumerable<string> hints)
        {
            foreach (var hint in hints)
            {
                if (location.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExtractRegistryHive(string? registryPath)
        {
            if (string.IsNullOrWhiteSpace(registryPath))
            {
                return string.Empty;
            }

            int firstSeparatorIndex = registryPath.IndexOf('\\');
            if (firstSeparatorIndex <= 0)
            {
                return registryPath;
            }

            return registryPath[..firstSeparatorIndex];
        }
    }
}
