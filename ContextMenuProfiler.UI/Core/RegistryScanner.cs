using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

using System.Runtime.InteropServices;
using System.Text;

using ContextMenuProfiler.UI.Core.Helpers;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Core
{
    public enum ScanMode
    {
        Targeted, // Fast, common locations
        Full      // Slow, all extensions
    }

    public class RegistryHandlerInfo
    {
        public string Path { get; set; } = "";
        public string Location { get; set; } = "";
    }

    public class RegistryScanner
    {
        private static readonly (string Path, string Location)[] GlobalHandlerLocations =
        {
            (BenchmarkSemantics.RegistryPathPattern.AllFilesHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFiles),
            (BenchmarkSemantics.RegistryPathPattern.AllFilesHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.AllFiles))
        };

        private static readonly (string Path, string Location)[] DirectoryScopedHandlerLocations =
        {
            (BenchmarkSemantics.RegistryPathPattern.DirectoryHandlers, BenchmarkSemantics.RegistryLocationLabel.Directory),
            (BenchmarkSemantics.RegistryPathPattern.DirectoryHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.Directory)),
            (BenchmarkSemantics.RegistryPathPattern.FolderHandlers, BenchmarkSemantics.RegistryLocationLabel.Folder),
            (BenchmarkSemantics.RegistryPathPattern.DriveHandlers, BenchmarkSemantics.RegistryLocationLabel.Drive),
            (BenchmarkSemantics.RegistryPathPattern.AllFileSystemObjectsHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFileSystemObjects),
            (BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground),
            (BenchmarkSemantics.RegistryPathPattern.DesktopBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DesktopBackground)
        };

        private static readonly (string Path, string Location)[] GlobalShellLocations =
        {
            (BenchmarkSemantics.RegistryPathPattern.AllFilesShell, BenchmarkSemantics.RegistryLocationLabel.AllFiles)
        };

        private static readonly (string Path, string Location)[] DirectoryScopedShellLocations =
        {
            (BenchmarkSemantics.RegistryPathPattern.DirectoryShell, BenchmarkSemantics.RegistryLocationLabel.Directory),
            (BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundShell, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground),
            (BenchmarkSemantics.RegistryPathPattern.DriveShell, BenchmarkSemantics.RegistryLocationLabel.Drive),
            (BenchmarkSemantics.RegistryPathPattern.FolderShell, BenchmarkSemantics.RegistryLocationLabel.Folder)
        };

        private readonly struct TargetAssociationContext
        {
            public TargetAssociationContext(bool isDirectory, string associationType)
            {
                IsDirectory = isDirectory;
                AssociationType = associationType;
            }

            public bool IsDirectory { get; }
            public string AssociationType { get; }
        }

        public static Dictionary<Guid, List<RegistryHandlerInfo>> ScanHandlers(ScanMode mode = ScanMode.Targeted)
        {
            var handlers = new ConcurrentDictionary<Guid, List<RegistryHandlerInfo>>();

            ScanHandlerLocations(handlers, GlobalHandlerLocations);
            ScanHandlerLocations(handlers, DirectoryScopedHandlerLocations);

            // 2. Scan Extensions (Only if Full mode)
            if (mode == ScanMode.Full)
            {
                string[] rootKeys = Registry.ClassesRoot.GetSubKeyNames();
                
                // Bound parallelism to avoid saturating CPU during deep scans.
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
                };

                Parallel.ForEach(rootKeys, options, keyName =>
                {
                    if (keyName.StartsWith(BenchmarkSemantics.RegistryToken.ExtensionPrefix, StringComparison.Ordinal))
                    {
                        // It's an extension
                        // Check SystemFileAssociations
                        string extensionLocation = BenchmarkSemantics.BuildExtensionRegistryLocationLabel(keyName);
                        ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(keyName, disabled: false), extensionLocation);
                        ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(keyName, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(extensionLocation));

                        // Get ProgID
                        string? progId = GetProgID(keyName);
                        if (!string.IsNullOrEmpty(progId))
                        {
                            string progIdLocation = BenchmarkSemantics.BuildProgIdRegistryLocationLabel(progId, keyName);
                            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: false), progIdLocation);
                            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(progIdLocation));
                        }
                    }
                });
            }

            // Convert back to regular Dictionary
            return new Dictionary<Guid, List<RegistryHandlerInfo>>(handlers);
        }

        public static Dictionary<Guid, List<RegistryHandlerInfo>> ScanHandlersForPath(string targetPath)
        {
            var handlers = new ConcurrentDictionary<Guid, List<RegistryHandlerInfo>>();
            var context = ResolveTargetAssociationContext(targetPath);

            ScanHandlerLocations(handlers, GlobalHandlerLocations);

            if (context.IsDirectory)
            {
                ScanHandlerLocations(handlers, DirectoryScopedHandlerLocations);
                return new Dictionary<Guid, List<RegistryHandlerInfo>>(handlers);
            }

            ScanFileAssociationHandlers(handlers, context.AssociationType);

            return new Dictionary<Guid, List<RegistryHandlerInfo>>(handlers);
        }

        // Updated signature to accept ConcurrentDictionary
        private static void ScanLocation(ConcurrentDictionary<Guid, List<RegistryHandlerInfo>> handlers, string subKeyPath, string locationName)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(subKeyPath))
                {
                    if (key == null) return;
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        string trimmedName = subKeyName.Trim();
                        Guid clsid = Guid.Empty;
                        bool found = false;

                        // Pattern 1: Key name is the CLSID (e.g. {GUID})
                        if (BenchmarkSemantics.LooksLikeBracedClsid(trimmedName) && Guid.TryParse(trimmedName, out clsid))
                        {
                            found = true;
                        }

                        // Pattern 2: Default value is the CLSID
                        if (!found)
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                object? val = subKey?.GetValue("");
                                if (val is string guidStr)
                                {
                                    string trimmedGuid = guidStr.Trim();
                                    if (BenchmarkSemantics.LooksLikeBracedClsid(trimmedGuid) && Guid.TryParse(trimmedGuid, out clsid))
                                    {
                                        found = true;
                                    }
                                }
                            }
                        }

                        if (found && clsid != Guid.Empty)
                        {
                            var info = new RegistryHandlerInfo { 
                                Path = $@"{subKeyPath}\{subKeyName}",
                                Location = BenchmarkSemantics.BuildRegistryHandlerLocation(locationName, trimmedName)
                            };

                            var list = handlers.GetOrAdd(clsid, _ => new List<RegistryHandlerInfo>());
                            lock (list)
                            {
                                if (!list.Any(i => i.Path == info.Path))
                                {
                                    list.Add(info);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"ScanLocation failed for {subKeyPath}", ex);
            }
        }

        private static string? GetProgID(string ext)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(ext))
                {
                    return key?.GetValue("") as string;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"GetProgID failed for {ext}", ex);
                return null;
            }
        }

        public static Dictionary<string, List<string>> ScanStaticVerbs()
        {
            var verbs = new ConcurrentDictionary<string, List<string>>();

            ScanShellLocations(verbs, GlobalShellLocations);
            ScanShellLocations(verbs, DirectoryScopedShellLocations);

            return new Dictionary<string, List<string>>(verbs);
        }

        public static Dictionary<string, List<string>> ScanStaticVerbsForPath(string targetPath)
        {
            var verbs = new ConcurrentDictionary<string, List<string>>();
            var context = ResolveTargetAssociationContext(targetPath);

            ScanShellLocations(verbs, GlobalShellLocations);

            if (context.IsDirectory)
            {
                ScanShellLocations(verbs, DirectoryScopedShellLocations);
                return new Dictionary<string, List<string>>(verbs);
            }

            ScanFileAssociationShellVerbs(verbs, context.AssociationType);

            return new Dictionary<string, List<string>>(verbs);
        }

        private static TargetAssociationContext ResolveTargetAssociationContext(string targetPath)
        {
            bool isDirectory = Directory.Exists(targetPath);
            string associationType = isDirectory
                ? BenchmarkSemantics.RegistryPathPattern.DirectoryAssociationType
                : Path.GetExtension(targetPath).ToLowerInvariant();

            return new TargetAssociationContext(isDirectory, associationType);
        }

        private static void ScanHandlerLocations(
            ConcurrentDictionary<Guid, List<RegistryHandlerInfo>> handlers,
            IEnumerable<(string Path, string Location)> locations)
        {
            foreach (var location in locations)
            {
                ScanLocation(handlers, location.Path, location.Location);
            }
        }

        private static void ScanShellLocations(
            ConcurrentDictionary<string, List<string>> verbs,
            IEnumerable<(string Path, string Location)> locations)
        {
            foreach (var location in locations)
            {
                ScanShellKey(verbs, location.Path, location.Location);
            }
        }

        private static void ScanFileAssociationHandlers(ConcurrentDictionary<Guid, List<RegistryHandlerInfo>> handlers, string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return;
            }

            string extensionLocation = BenchmarkSemantics.BuildExtensionRegistryLocationLabel(extension);
            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(extension, disabled: false), extensionLocation);
            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(extension, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(extensionLocation));

            string? progId = GetProgID(extension);
            if (string.IsNullOrEmpty(progId))
            {
                return;
            }

            string progIdLocation = BenchmarkSemantics.BuildProgIdRegistryLocationLabel(progId, extension);
            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: false), progIdLocation);
            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(progIdLocation));
        }

        private static void ScanFileAssociationShellVerbs(ConcurrentDictionary<string, List<string>> verbs, string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return;
            }

            ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationShell(extension), BenchmarkSemantics.BuildExtensionRegistryLocationLabel(extension));

            string? progId = GetProgID(extension);
            if (string.IsNullOrEmpty(progId))
            {
                return;
            }

            ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.BuildProgIdShell(progId), BenchmarkSemantics.BuildProgIdRegistryLocationLabel(progId, extension));
        }

        private static void ScanShellKey(ConcurrentDictionary<string, List<string>> verbs, string subKeyPath, string locationName)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(subKeyPath))
                {
                    if (key == null) return;
                    foreach (var verbName in key.GetSubKeyNames())
                    {
                        // Ignore some system defaults that are usually not interesting or dangerous to touch
                        if (BenchmarkSemantics.IsIgnoredStaticVerbName(verbName)) continue;

                        using (var verbKey = key.OpenSubKey(verbName))
                        {
                            if (verbKey == null) continue;

                            // Get Command
                            string command = "";
                            using (var commandKey = verbKey.OpenSubKey(BenchmarkSemantics.StaticVerb.CommandSubKeyName))
                            {
                                command = commandKey?.GetValue("") as string ?? "";
                            }

                            // If no command, it's likely a sub-menu or invalid, but we might still want to see it
                            // However, for "Static Verb" type, the command is the main identity
                            if (string.IsNullOrEmpty(command)) continue;

                            // Get Display Name (MUIVerb > Default)
                            string? displayName = verbKey.GetValue(BenchmarkSemantics.StaticVerb.MuiVerbValueName) as string;
                            if (string.IsNullOrEmpty(displayName))
                            {
                                displayName = verbKey.GetValue("") as string; // Default value
                            }

                            // Resolve MUI string if necessary
                            if (!string.IsNullOrEmpty(displayName)
                                && displayName.StartsWith(BenchmarkSemantics.IconLocation.IndirectStringPrefix, StringComparison.Ordinal))
                            {
                                displayName = ShellUtils.ResolveMuiString(displayName);
                            }

                            if (string.IsNullOrEmpty(displayName))
                            {
                                displayName = verbName.TrimStart(BenchmarkSemantics.RegistryLocationToken.StaticVerbDisabledKeyPrefixChar); // Fallback to key name
                            }

                            // Use unique key: "Name|Command" to distinguish same name but different command
                            string uniqueKey = BenchmarkSemantics.BuildStaticVerbUniqueKey(displayName, command);
                            
                            var list = verbs.GetOrAdd(uniqueKey, _ => new List<string>());
                            lock (list)
                            {
                                if (!list.Contains($"{subKeyPath}\\{verbName}"))
                                {
                                    list.Add($"{subKeyPath}\\{verbName}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"ScanShellKey failed for {subKeyPath}", ex);
            }
        }
    }
}
