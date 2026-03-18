using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Management.Deployment;
using Windows.ApplicationModel;
using System.Linq;

using System.Collections.Concurrent;
using ContextMenuProfiler.UI.Core;

namespace ContextMenuProfiler.UI.Converters
{
    public class IconToImageConverter : IValueConverter
    {
        private static readonly PackageManager _packageManager = new PackageManager();
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new ConcurrentDictionary<string, ImageSource>();

        private string? ResolveMsAppxUri(string uriStr)
        {
            try
            {
                // 解析 URI 和可选的 Binary Hint
                string actualUri = uriStr;
                string? hintDllPath = null;
                int pipeIndex = uriStr.IndexOf(BenchmarkSemantics.IconLocation.HintSeparator);
                if (pipeIndex > 0) {
                    actualUri = uriStr.Substring(0, pipeIndex);
                    hintDllPath = uriStr.Substring(pipeIndex + 1);
                }

                var uri = new Uri(actualUri);
                string packageId = uri.Host;
                string relativePath = uri.AbsolutePath.TrimStart('/').Replace('/', '\\');

                var packages = _packageManager.FindPackagesForUser("");
                var package = packages.FirstOrDefault(p => 
                    p.Id.Name.Equals(packageId, StringComparison.OrdinalIgnoreCase) || 
                    p.Id.FamilyName.Equals(packageId, StringComparison.OrdinalIgnoreCase));

                if (package == null) return null;

                // 收集候选根目录
                var roots = new List<string>();
                if (!string.IsNullOrEmpty(hintDllPath)) {
                    string? dir = Path.GetDirectoryName(hintDllPath);
                    if (dir != null) roots.Add(dir);
                }
                if (package.EffectiveLocation != null) roots.Add(package.EffectiveLocation.Path);
                if (package.InstalledLocation != null) roots.Add(package.InstalledLocation.Path);

                // 核心逻辑：尝试每个根目录及其父目录（处理 VS Code 这种 appx 子文件夹结构）
                foreach (var baseRoot in roots.Distinct())
                {
                    var searchLevels = new[] { baseRoot, Directory.GetParent(baseRoot)?.FullName };
                    foreach (var root in searchLevels)
                    {
                        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                        
                        string fullPath = Path.Combine(root, relativePath);
                        if (File.Exists(fullPath)) return fullPath;

                        // MRT 变体匹配
                        string? dir = Path.GetDirectoryName(fullPath);
                        if (dir != null && Directory.Exists(dir)) {
                            string fileName = Path.GetFileNameWithoutExtension(fullPath);
                            string ext = Path.GetExtension(fullPath);
                            var files = Directory.GetFiles(dir, $"{fileName}*{ext}");
                            var best = files.OrderByDescending(f =>
                                                f.Contains(BenchmarkSemantics.IconLocation.MrtPreferredTargetSizeToken, StringComparison.OrdinalIgnoreCase))
                                           .ThenByDescending(f =>
                                                f.Contains(BenchmarkSemantics.IconLocation.MrtPreferredScaleToken, StringComparison.OrdinalIgnoreCase))
                                           .FirstOrDefault();
                            if (best != null) return best;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? path = value as string;
            
            if (string.IsNullOrEmpty(path)
                || string.Equals(path, HookIpcSemantics.Response.NoIconToken, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyProperty.UnsetValue;
            }

            if (_iconCache.TryGetValue(path, out var cached)) return cached;

            try
            {
                ImageSource? result = null;
                // ... (rest of the logic)
                // Note: We'll wrap the existing logic and store in cache at the end
                result = InnerConvert(path);
                if (result != null)
                {
                    _iconCache.TryAdd(path, result);
                }
                return result ?? DependencyProperty.UnsetValue;
            }
            catch { return DependencyProperty.UnsetValue; }
        }

        private ImageSource? InnerConvert(string path)
        {
            try
            {
                // Handle ms-appx:// URIs (UWP resources)
                if (path.StartsWith(BenchmarkSemantics.IconLocation.MsAppxUriPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var resolvedPath = ResolveMsAppxUri(path);
                    if (string.IsNullOrEmpty(resolvedPath)) return null;
                    path = resolvedPath;
                }

                // Expand environment variables
                path = Environment.ExpandEnvironmentVariables(path);
                
                // Handle MUI / UWP Resource strings (starts with @)
                if (path.StartsWith("@"))
                {
                    StringBuilder sb = new StringBuilder(BenchmarkSemantics.IconLocation.IndirectStringBufferSize);
                    int res = SHLoadIndirectString(path, sb, (uint)sb.Capacity, IntPtr.Zero);
                    if (res == 0) // S_OK
                    {
                        string resolved = sb.ToString();
                        if (File.Exists(resolved))
                        {
                            path = resolved;
                        }
                        else
                        {
                            // It might have resolved to a plain string or a path that still needs parsing
                            path = resolved;
                        }
                    }
                    else
                    {
                        // Remove @ prefix and try parsing as normal path
                        path = path.Substring(1);
                    }
                }

                int iconIndex = 0;
                string filePath = path;

                // Parse resource index (path,index or path,-id)
                int commaIndex = path.LastIndexOf(',');
                if (commaIndex > 0)
                {
                    string indexStr = path.Substring(commaIndex + 1);
                    if (int.TryParse(indexStr, out int idx))
                    {
                        iconIndex = idx;
                        filePath = path.Substring(0, commaIndex);
                    }
                }

                filePath = filePath.Trim('"', '\'');

                if (!File.Exists(filePath))
                {
                    // Try System32 fallback
                    string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string sys32Path = Path.Combine(sys32, filePath);
                    if (File.Exists(sys32Path)) filePath = sys32Path;
                }

                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    
                    if (ext == BenchmarkSemantics.IconFileExtension.Png
                        || ext == BenchmarkSemantics.IconFileExtension.Jpg
                        || ext == BenchmarkSemantics.IconFileExtension.Bmp)
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(filePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        return bitmap;
                    }

                    // Extract Icon
                    IntPtr[] phiconLarge = new IntPtr[1];
                    IntPtr[] phiconSmall = new IntPtr[1];
                    
                    uint readIconCount = ExtractIconEx(filePath, iconIndex, phiconLarge, phiconSmall, 1);
                    
                    if (readIconCount > 0 && phiconLarge[0] != IntPtr.Zero)
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            phiconLarge[0],
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        DestroyIcon(phiconLarge[0]);
                        if (phiconSmall[0] != IntPtr.Zero) DestroyIcon(phiconSmall[0]);
                        
                        bitmapSource.Freeze();
                        return bitmapSource;
                    }
                }
            }
            catch { }

            return null;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
        private static extern int DestroyIcon(IntPtr hIcon);
    }
}
