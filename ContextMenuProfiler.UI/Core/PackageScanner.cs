using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;
using Windows.Management.Deployment;
using Windows.ApplicationModel;

namespace ContextMenuProfiler.UI.Core
{
    public class PackageScanner
    {
        private static readonly XNamespace NS_DEFAULT = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        private static readonly XNamespace NS_UAP = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        private static readonly XNamespace NS_DESKTOP4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
        private static readonly XNamespace NS_DESKTOP5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
        private static readonly XNamespace NS_COM = "http://schemas.microsoft.com/appx/manifest/com/windows10";

        public static IEnumerable<BenchmarkResult> ScanPackagedExtensions(string? targetPath)
        {
            var packageManager = new PackageManager();
            var packages = SafeFindPackages(packageManager);

            string targetExt = DetermineTargetExtension(targetPath);
            bool scanAll = (targetPath == null);

            var seenClsids = new HashSet<Guid>();

            foreach (var package in packages)
            {
                List<BenchmarkResult> packageResults = new List<BenchmarkResult>();
                try
                {
                    ProcessPackage(package, packageResults, targetExt, scanAll);
                }
                catch { /* Individual package failure shouldn't stop the whole scan */ }

                foreach (var res in packageResults)
                {
                    if (res.Clsid.HasValue && seenClsids.Add(res.Clsid.Value))
                    {
                        yield return res;
                    }
                }
            }
        }

        private static void ProcessPackage(Package package, List<BenchmarkResult> results, string targetExt, bool scanAll)
        {
            string? installPath = package.InstalledLocation?.Path;
            if (string.IsNullOrEmpty(installPath)) return;

            string manifestPath = Path.Combine(installPath, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return;

            string manifestContent = File.ReadAllText(manifestPath);
            if (!manifestContent.Contains("fileExplorerContextMenus")) return;    

            // Handle Sparse Packages (like VS Code)
            // Deferred: only resolve EffectiveLocation for packages that actually have context menu extensions
            string effectivePath = installPath;
            try {
                if (package.EffectiveLocation != null) effectivePath = package.EffectiveLocation.Path;
            } catch { }

            XDocument doc = XDocument.Parse(manifestContent);
            var clsidToPath = MapClsidToBinaryPath(doc, effectivePath);
            
            var extensions = doc.Descendants().Where(e => e.Name.LocalName == "Extension" && 
                             e.Attribute("Category")?.Value == "windows.fileExplorerContextMenus");

            foreach (var extElement in extensions)
            {
                ProcessExtensionElement(package, extElement, results, clsidToPath, effectivePath, targetExt, scanAll);
            }
        }

        private static void ProcessExtensionElement(Package package, XElement extElement, List<BenchmarkResult> results, 
            Dictionary<Guid, string> clsidToPath, string installPath, string targetExt, bool scanAll)
        {
            var itemTypes = extElement.Descendants().Where(e => e.Name.LocalName == "ItemType");

            foreach (var itemType in itemTypes)
            {
                string? type = itemType.Attribute("Type")?.Value?.ToLower();
                if (string.IsNullOrEmpty(type)) continue;

                if (!scanAll && !IsTypeMatch(type, targetExt)) continue;

                var verbs = itemType.Descendants().Where(e => e.Name.LocalName == "Verb");
                foreach (var verb in verbs)
                {
                    if (TryParseVerb(package, verb, clsidToPath, installPath, out var result) && result != null)
                    {
                        if (!results.Any(r => r.Clsid == result.Clsid))
                            results.Add(result);
                    }
                }
            }
        }

        private static bool TryParseVerb(Package package, XElement verb, Dictionary<Guid, string> clsidToPath, 
            string installPath, out BenchmarkResult? result)
        {
            result = null;
            string? clsidStr = verb.Attribute("Clsid")?.Value;
            if (!Guid.TryParse(clsidStr, out Guid clsid)) return false;

            string name = package.DisplayName;
            if (string.IsNullOrEmpty(name)) name = package.Id.Name;
            
            string? verbId = verb.Attribute("Id")?.Value;
            if (!string.IsNullOrEmpty(verbId)) name += $" ({verbId})";

            string? logoPath = ResolveBestLogo(package, verb.Document, installPath);
            string binaryPath = clsidToPath.TryGetValue(clsid, out var relPath) ? 
                                ResolveBinaryPath(installPath, relPath) : installPath;

            result = new BenchmarkResult
            {
                Name = name,
                Clsid = clsid,
                Status = "OK",
                Type = "UWP",
                Path = package.Id.FullName,
                BinaryPath = binaryPath,
                // 简洁且必要：将 DLL 路径作为 Hint 传给 Converter，这是已知事实，不是猜测
                IconLocation = !string.IsNullOrEmpty(logoPath) ? $"{logoPath}|{binaryPath}" : logoPath,
                PackageName = package.Id.Name,
                Version = $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}",
                IconSource = !string.IsNullOrEmpty(logoPath) ? "Manifest (App Logo)" : "None"
            };

            return true;
        }

        private static string? ResolveBestLogo(Package package, XDocument? doc, string installPath)
        {
            try
            {
                if (doc == null) return null;
                // 1. 尝试从 XML 提取相对路径
                string? relativeLogo = ExtractRelativeLogoFromManifest(doc);
                if (string.IsNullOrEmpty(relativeLogo)) return null;
                
                // 2. 统一返回 ms-appx 协议，让 Converter 的智能逻辑去处理缩放和稀疏包路径
                // 这是最稳健的做法，因为 Converter 已经具备了处理 scale-xxx 和 EffectiveLocation 的能力
                return $"ms-appx://{package.Id.Name}/{relativeLogo.TrimStart('\\', '/')}";
            }
            catch { return null; }
        }

        private static string? ExtractRelativeLogoFromManifest(XDocument doc)
        {
            var visualElements = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            if (visualElements != null)
            {
                return visualElements.Attribute("Square44x44Logo")?.Value
                    ?? visualElements.Attribute("Square150x150Logo")?.Value
                    ?? visualElements.Attribute("Logo")?.Value;
            }

            return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
        }

        private static Dictionary<Guid, string> MapClsidToBinaryPath(XDocument doc, string installPath)
        {
            var map = new Dictionary<Guid, string>();
            var classes = doc.Descendants().Where(e => e.Name.LocalName == "Class" && e.Name.Namespace == NS_COM);

            foreach (var cls in classes)
            {
                string? idStr = cls.Attribute("Id")?.Value;
                string? path = cls.Attribute("Path")?.Value;
                if (Guid.TryParse(idStr, out Guid guid) && !string.IsNullOrEmpty(path))
                {
                    map[guid] = path;
                }
            }
            return map;
        }

        private static string ResolveBinaryPath(string installPath, string relPath)
        {
            try
            {
                string combined = Path.Combine(installPath, relPath);
                return File.Exists(combined) ? combined : (File.Exists(relPath) ? relPath : installPath);
            }
            catch { return installPath; }
        }

        private static IEnumerable<Package> SafeFindPackages(PackageManager pm)
        {
            try { return pm.FindPackagesForUser(""); }
            catch { return Enumerable.Empty<Package>(); }
        }

        private static string DetermineTargetExtension(string? path)
        {
            if (path == null) return "";
            if (Directory.Exists(path)) return "directory";
            return Path.GetExtension(path).ToLower();
        }

        private static bool IsTypeMatch(string type, string targetExt)
        {
            if (type == "*") return true;
            if (type == "directory" || type == "folder") return targetExt == "directory";
            if (type == "directory\\background") return targetExt == "directory";
            return type == targetExt;
        }

        public static string? GetPackageNameForClsid(Guid clsid)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey($@"PackagedCom\ClassIndex\{clsid:B}");
                return key?.GetValue("") as string;
            }
            catch { return null; }
        }
    }
}
