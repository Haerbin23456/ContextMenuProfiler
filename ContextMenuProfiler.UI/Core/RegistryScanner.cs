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
        public static Dictionary<Guid, List<RegistryHandlerInfo>> ScanHandlers(ScanMode mode = ScanMode.Targeted)
        {
            var handlers = new ConcurrentDictionary<Guid, List<RegistryHandlerInfo>>();

            // 1. Scan Global Locations (Fast & Essential)
            var commonLocations = new[]
            {
                (BenchmarkSemantics.RegistryPathPattern.AllFilesHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFiles),
                (BenchmarkSemantics.RegistryPathPattern.AllFilesHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.AllFiles)),
                (BenchmarkSemantics.RegistryPathPattern.DirectoryHandlers, BenchmarkSemantics.RegistryLocationLabel.Directory),
                (BenchmarkSemantics.RegistryPathPattern.DirectoryHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.Directory)),
                (BenchmarkSemantics.RegistryPathPattern.FolderHandlers, BenchmarkSemantics.RegistryLocationLabel.Folder),
                (BenchmarkSemantics.RegistryPathPattern.DriveHandlers, BenchmarkSemantics.RegistryLocationLabel.Drive),
                (BenchmarkSemantics.RegistryPathPattern.AllFileSystemObjectsHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFileSystemObjects),
                (BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground),
                (BenchmarkSemantics.RegistryPathPattern.DesktopBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DesktopBackground)
            };

            foreach (var loc in commonLocations)
            {
                ScanLocation(handlers, loc.Item1, loc.Item2);
            }

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
                    if (keyName.StartsWith("."))
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
            bool isDirectory = Directory.Exists(targetPath);
            string ext = isDirectory
                ? BenchmarkSemantics.RegistryPathPattern.DirectoryAssociationType
                : Path.GetExtension(targetPath).ToLowerInvariant();

            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.AllFilesHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFiles);
            ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.AllFilesHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.AllFiles));

            if (isDirectory)
            {
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.DirectoryHandlers, BenchmarkSemantics.RegistryLocationLabel.Directory);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.DirectoryHandlersDisabled, BenchmarkSemantics.BuildDisabledRegistryLocationLabel(BenchmarkSemantics.RegistryLocationLabel.Directory));
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.FolderHandlers, BenchmarkSemantics.RegistryLocationLabel.Folder);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.DriveHandlers, BenchmarkSemantics.RegistryLocationLabel.Drive);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.AllFileSystemObjectsHandlers, BenchmarkSemantics.RegistryLocationLabel.AllFileSystemObjects);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.DesktopBackgroundHandlers, BenchmarkSemantics.RegistryLocationLabel.DesktopBackground);
                return new Dictionary<Guid, List<RegistryHandlerInfo>>(handlers);
            }

            if (!string.IsNullOrEmpty(ext))
            {
                string extensionLocation = BenchmarkSemantics.BuildExtensionRegistryLocationLabel(ext);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(ext, disabled: false), extensionLocation);
                ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationHandlers(ext, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(extensionLocation));

                string? progId = GetProgID(ext);
                if (!string.IsNullOrEmpty(progId))
                {
                    string progIdLocation = BenchmarkSemantics.BuildProgIdRegistryLocationLabel(progId, ext);
                    ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: false), progIdLocation);
                    ScanLocation(handlers, BenchmarkSemantics.RegistryPathPattern.BuildProgIdHandlers(progId, disabled: true), BenchmarkSemantics.BuildDisabledRegistryLocationLabel(progIdLocation));
                }
            }

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
                        if (trimmedName.StartsWith("{") && trimmedName.EndsWith("}") && Guid.TryParse(trimmedName, out clsid))
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
                                    if (trimmedGuid.StartsWith("{") && Guid.TryParse(trimmedGuid, out clsid))
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

            // 1. Scan Global Locations
            var shellLocations = new[]
            {
                (BenchmarkSemantics.RegistryPathPattern.AllFilesShell, BenchmarkSemantics.RegistryLocationLabel.AllFiles),
                (BenchmarkSemantics.RegistryPathPattern.DirectoryShell, BenchmarkSemantics.RegistryLocationLabel.Directory),
                (BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundShell, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground),
                (BenchmarkSemantics.RegistryPathPattern.DriveShell, BenchmarkSemantics.RegistryLocationLabel.Drive),
                (BenchmarkSemantics.RegistryPathPattern.FolderShell, BenchmarkSemantics.RegistryLocationLabel.Folder)
            };

            foreach (var loc in shellLocations)
            {
                ScanShellKey(verbs, loc.Item1, loc.Item2);
            }

            return new Dictionary<string, List<string>>(verbs);
        }

        public static Dictionary<string, List<string>> ScanStaticVerbsForPath(string targetPath)
        {
            var verbs = new ConcurrentDictionary<string, List<string>>();
            bool isDirectory = Directory.Exists(targetPath);
            string ext = isDirectory
                ? BenchmarkSemantics.RegistryPathPattern.DirectoryAssociationType
                : Path.GetExtension(targetPath).ToLowerInvariant();

            ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.AllFilesShell, BenchmarkSemantics.RegistryLocationLabel.AllFiles);

            if (isDirectory)
            {
                ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.DirectoryShell, BenchmarkSemantics.RegistryLocationLabel.Directory);
                ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.DirectoryBackgroundShell, BenchmarkSemantics.RegistryLocationLabel.DirectoryBackground);
                ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.DriveShell, BenchmarkSemantics.RegistryLocationLabel.Drive);
                ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.FolderShell, BenchmarkSemantics.RegistryLocationLabel.Folder);
                return new Dictionary<string, List<string>>(verbs);
            }

            if (!string.IsNullOrEmpty(ext))
            {
                ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.BuildSystemFileAssociationShell(ext), BenchmarkSemantics.BuildExtensionRegistryLocationLabel(ext));
                string? progId = GetProgID(ext);
                if (!string.IsNullOrEmpty(progId))
                {
                    ScanShellKey(verbs, BenchmarkSemantics.RegistryPathPattern.BuildProgIdShell(progId), BenchmarkSemantics.BuildProgIdRegistryLocationLabel(progId, ext));
                }
            }

            return new Dictionary<string, List<string>>(verbs);
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
                            if (!string.IsNullOrEmpty(displayName) && displayName.StartsWith("@"))
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
