using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ContextMenuProfiler.UI.Core;

namespace ContextMenuProfiler.UI.Core.Helpers
{
    public static class ShellUtils
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

        /// <summary>
        /// Resolves MUI resource strings (e.g., @shell32.dll,-1234) to their localized text.
        /// </summary>
        public static string ResolveMuiString(string? muiString)
        {
            if (string.IsNullOrEmpty(muiString)
                || !muiString.StartsWith(BenchmarkSemantics.IconLocation.IndirectStringPrefix, StringComparison.Ordinal))
            {
                return muiString ?? "";
            }
            
            var sb = new StringBuilder(BenchmarkSemantics.IconLocation.IndirectStringBufferSize);
            if (SHLoadIndirectString(muiString, sb, (uint)sb.Capacity, IntPtr.Zero) == 0)
            {
                return sb.ToString();
            }
            return muiString;
        }

        /// <summary>
        /// Tries to open a CLSID registry key from common locations.
        /// </summary>
        public static RegistryKey? OpenClsidKey(string clsidB)
        {
            // HKCR is a merged view of HKLM\SOFTWARE\Classes and HKCU\SOFTWARE\Classes.
            // We only need to check HKCR and then WOW6432Node specifically if not found.
            return Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildClsidPath(clsidB))
                ?? Registry.ClassesRoot.OpenSubKey(ComRegistrySemantics.BuildWow6432NodeClsidPath(clsidB));
        }
    }
}
