using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
                (@"*\shellex\ContextMenuHandlers", "All Files (*)"),
                (@"*\shellex\-ContextMenuHandlers", "All Files (*) [Disabled]"),
                (@"Directory\shellex\ContextMenuHandlers", "Directory"),
                (@"Directory\shellex\-ContextMenuHandlers", "Directory [Disabled]"),
                (@"Folder\shellex\ContextMenuHandlers", "Folder"),
                (@"Drive\shellex\ContextMenuHandlers", "Drive"),
                (@"AllFileSystemObjects\shellex\ContextMenuHandlers", "All File System Objects"),
                (@"Directory\Background\shellex\ContextMenuHandlers", "Directory Background"),
                (@"DesktopBackground\shellex\ContextMenuHandlers", "Desktop Background")
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
                        ScanLocation(handlers, $"SystemFileAssociations\\{keyName}\\shellex\\ContextMenuHandlers", $"Extension ({keyName})");
                        ScanLocation(handlers, $"SystemFileAssociations\\{keyName}\\shellex\\-ContextMenuHandlers", $"Extension ({keyName}) [Disabled]");

                        // Get ProgID
                        string progId = GetProgID(keyName);
                        if (!string.IsNullOrEmpty(progId))
                        {
                            ScanLocation(handlers, $"{progId}\\shellex\\ContextMenuHandlers", $"ProgID ({progId} for {keyName})");
                            ScanLocation(handlers, $"{progId}\\shellex\\-ContextMenuHandlers", $"ProgID ({progId} for {keyName}) [Disabled]");
                        }
                    }
                });
            }

            // Convert back to regular Dictionary
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
                                object val = subKey?.GetValue("");
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
                                Location = $"{locationName} ({trimmedName})"
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

        private static string GetProgID(string ext)
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
                (@"*\shell", "All Files (*)"),
                (@"Directory\shell", "Directory"),
                (@"Directory\Background\shell", "Directory Background"),
                (@"Drive\shell", "Drive"),
                (@"Folder\shell", "Folder")
            };

            foreach (var loc in shellLocations)
            {
                ScanShellKey(verbs, loc.Item1, loc.Item2);
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
                        if (verbName.Equals("Attributes", StringComparison.OrdinalIgnoreCase) || 
                            verbName.Equals("AnyCode", StringComparison.OrdinalIgnoreCase)) continue;

                        using (var verbKey = key.OpenSubKey(verbName))
                        {
                            if (verbKey == null) continue;

                            // Get Command
                            string command = "";
                            using (var commandKey = verbKey.OpenSubKey("command"))
                            {
                                command = commandKey?.GetValue("") as string ?? "";
                            }

                            // If no command, it's likely a sub-menu or invalid, but we might still want to see it
                            // However, for "Static Verb" type, the command is the main identity
                            if (string.IsNullOrEmpty(command)) continue;

                            // Get Display Name (MUIVerb > Default)
                            string displayName = verbKey.GetValue("MUIVerb") as string;
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
                                displayName = verbName.TrimStart('-'); // Fallback to key name
                            }

                            // Use unique key: "Name|Command" to distinguish same name but different command
                            string uniqueKey = $"{displayName}|{command}";
                            
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
