using System;

namespace ContextMenuProfiler.UI.Core
{
    public static class ComRegistrySemantics
    {
        public const string FriendlyNameValueName = "FriendlyName";
        public const string InprocServer32SubKeyName = "InprocServer32";
        public const string ThreadingModelValueName = "ThreadingModel";
        public const string TreatAsSubKeyName = "TreatAs";
        public const string AppIdValueName = "AppID";
        public const string AppIdSubKeyPrefix = "AppID";
        public const string DllSurrogateValueName = "DllSurrogate";
        public const string DllHostExecutableName = "dllhost.exe";
        public const string DisplayNameValueName = "DisplayName";
        public const string PackageInstallPathValueName = "Path";
        public const string DllPathValueName = "DllPath";
        public const string ClsidPrefix = "CLSID";
        public const string Wow6432NodeClsidPrefix = @"WOW6432Node\CLSID";

        public const string PackagedComClassIndexPrefix = @"PackagedCom\ClassIndex";
        public const string PackagedComPackagePrefix = @"PackagedCom\Package";
        public const string PackagedComClassSubKeyName = "Class";
        public const string PackageRepositoryPrefix = @"Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";
        public const char PackageNameSeparator = '_';

        public static string BuildPackagedComClassIndexPath(string clsidB)
        {
            return $@"{PackagedComClassIndexPrefix}\{clsidB}";
        }

        public static string BuildClsidPath(string clsidB)
        {
            return $@"{ClsidPrefix}\{clsidB}";
        }

        public static string BuildWow6432NodeClsidPath(string clsidB)
        {
            return $@"{Wow6432NodeClsidPrefix}\{clsidB}";
        }

        public static string BuildPackagedComPackageClassPath(string packageName, string clsidB)
        {
            return $@"{packageName}\{PackagedComClassSubKeyName}\{clsidB}";
        }

        public static string BuildAppIdPath(string appId)
        {
            return $@"{AppIdSubKeyPrefix}\{appId}";
        }

        public static string BuildPackageRepositoryPath(string packageFullName)
        {
            return $@"{PackageRepositoryPrefix}\{packageFullName}";
        }

        public static string ExtractPackageIdPrefix(string packageFullName)
        {
            int separatorIndex = packageFullName.IndexOf(PackageNameSeparator);
            if (separatorIndex <= 0)
            {
                return packageFullName;
            }

            return packageFullName[..separatorIndex];
        }
    }
}
