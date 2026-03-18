using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ContextMenuProfiler.UI.Core.Services;

namespace ContextMenuProfiler.UI.Core
{
    public class ExtensionManager
    {
        private const string BLOCKED_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

        public static bool IsExtensionBlocked(Guid clsid)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(BLOCKED_KEY_PATH))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(clsid.ToString("B"));
                        return val != null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"Failed to check block status for {clsid}", ex);
            }
            return false;
        }

        public static void SetExtensionBlockStatus(Guid clsid, string name, bool block)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(BLOCKED_KEY_PATH))
            {
                if (key != null)
                {
                    string clsidStr = clsid.ToString("B");
                    if (block)
                    {
                        key.SetValue(clsidStr, name);
                    }
                    else
                    {
                        key.DeleteValue(clsidStr, false);
                    }
                }
            }
            NotifyShell();
        }

        public static void DisableRegistryKey(string registryPath)
        {
            // Rename key: "Name" -> "-Name"
            // We need to parse parent and key name
            int lastSlash = registryPath.LastIndexOf('\\');
            if (lastSlash < 0) return;

            string parentPath = registryPath.Substring(0, lastSlash);
            string keyName = registryPath.Substring(lastSlash + 1);

            if (keyName.StartsWith(BenchmarkSemantics.RegistryLocationToken.StaticVerbDisabledKeyPrefix, StringComparison.Ordinal)) return; // Already disabled

            RenameRegistryKey(parentPath, keyName, BenchmarkSemantics.RegistryLocationToken.StaticVerbDisabledKeyPrefix + keyName);
        }

        public static void EnableRegistryKey(string registryPath)
        {
            // Rename key: "-Name" -> "Name"
            int lastSlash = registryPath.LastIndexOf('\\');
            if (lastSlash < 0) return;

            string parentPath = registryPath.Substring(0, lastSlash);
            string keyName = registryPath.Substring(lastSlash + 1);

            if (!keyName.StartsWith(BenchmarkSemantics.RegistryLocationToken.StaticVerbDisabledKeyPrefix, StringComparison.Ordinal)) return; // Already enabled

            RenameRegistryKey(parentPath, keyName, keyName.Substring(BenchmarkSemantics.RegistryLocationToken.StaticVerbDisabledKeyPrefix.Length));
        }

        public static void DeleteRegistryKey(string registryPath)
        {
             int lastSlash = registryPath.LastIndexOf('\\');
            if (lastSlash < 0) throw new ArgumentException($"Invalid registry path: {registryPath}");

            string parentPath = registryPath.Substring(0, lastSlash);
            string keyName = registryPath.Substring(lastSlash + 1);

            using (var parent = Registry.ClassesRoot.OpenSubKey(parentPath, true))
            {
                if (parent == null) throw new InvalidOperationException($"Parent key not found or not writable: {parentPath}");
                
                // Check if subkey exists before trying to delete
                using (var subKey = parent.OpenSubKey(keyName))
                {
                    if (subKey == null) return; // Already deleted
                }

                parent.DeleteSubKeyTree(keyName, false);
            }
            NotifyShell();
        }

        private static void RenameRegistryKey(string parentPath, string oldName, string newName)
        {
            // Shell extensions can be in HKLM or HKCU. 
            // Registry.ClassesRoot is a merged view, but writing to it can be tricky.
            // We try to open the parent key with write access.
            
            RegistryKey? parent = null;
            try
            {
                // Try HKLM first (common for system-wide extensions)
                parent = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{parentPath}", true);
                
                // If not found or not writable, try HKCU (per-user extensions)
                if (parent == null)
                {
                    parent = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{parentPath}", true);
                }

                // If still null, fallback to the merged view (might require elevation)
                if (parent == null)
                {
                    parent = Registry.ClassesRoot.OpenSubKey(parentPath, true);
                }

                if (parent == null) 
                    throw new UnauthorizedAccessException($"Access denied or path not found: {parentPath}. Try running as Administrator.");

                using (var source = parent.OpenSubKey(oldName))
                {
                    if (source == null) return; // Key already gone?

                    using (var dest = parent.CreateSubKey(newName))
                    {
                        if (dest == null) throw new InvalidOperationException($"Failed to create destination key: {newName}");
                        CopyRegistryKey(source, dest);
                    }
                }
                parent.DeleteSubKeyTree(oldName, false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"Failed to rename registry key from {oldName} to {newName}", ex);
                throw; // Re-throw to let UI handle the error (e.g. show "Need Admin")
            }
            finally
            {
                parent?.Dispose();
            }
            
            NotifyShell();
        }

        private static void CopyRegistryKey(RegistryKey source, RegistryKey dest)
        {
            // Copy values
            foreach (var valueName in source.GetValueNames())
            {
                var value = source.GetValue(valueName);
                if (value != null)
                {
                    dest.SetValue(valueName, value, source.GetValueKind(valueName));
                }
            }

            // Copy subkeys
            foreach (var subKeyName in source.GetSubKeyNames())
            {
                using (var srcSub = source.OpenSubKey(subKeyName))
                using (var destSub = dest.CreateSubKey(subKeyName))
                {
                    if (srcSub != null && destSub != null)
                    {
                        CopyRegistryKey(srcSub, destSub);
                    }
                }
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static void NotifyShell()
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000;
            const int SHCNF_IDLIST = 0x0000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
