using System.Xml.Linq;

namespace ContextMenuProfiler.UI.Core
{
    public static class PackageManifestSemantics
    {
        public static class NamespaceUri
        {
            public const string Default = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            public const string Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
            public const string Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
            public const string Desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
            public const string Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
        }

        public static class Namespaces
        {
            public static readonly XNamespace Default = NamespaceUri.Default;
            public static readonly XNamespace Uap = NamespaceUri.Uap;
            public static readonly XNamespace Desktop4 = NamespaceUri.Desktop4;
            public static readonly XNamespace Desktop5 = NamespaceUri.Desktop5;
            public static readonly XNamespace Com = NamespaceUri.Com;
        }

        public static class Manifest
        {
            public const string FileName = "AppxManifest.xml";
            public const string ContextMenuCategoryToken = "fileExplorerContextMenus";
            public const string ContextMenuCategory = "windows.fileExplorerContextMenus";

            public const string ExtensionElement = "Extension";
            public const string ItemTypeElement = "ItemType";
            public const string VerbElement = "Verb";
            public const string VisualElementsElement = "VisualElements";
            public const string LogoElement = "Logo";
            public const string ClassElement = "Class";

            public const string CategoryAttribute = "Category";
            public const string TypeAttribute = "Type";
            public const string ClsidAttribute = "Clsid";
            public const string IdAttribute = "Id";
            public const string PathAttribute = "Path";
            public const string Square44LogoAttribute = "Square44x44Logo";
            public const string Square150LogoAttribute = "Square150x150Logo";
        }

    }
}
