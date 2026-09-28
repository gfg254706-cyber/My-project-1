using System.IO;
using UnityEditor;
using UnityEngine;

namespace DungeonCards.EditorTools
{
    /// <summary>
    /// Imports the TextMeshPro essential resources, which this project has never had. Without them
    /// TMP_Settings is missing and every TextMeshProUGUI renders with no font.
    ///
    /// In Unity 6 TMP ships inside com.unity.ugui, so the resources are imported from that package's
    /// own .unitypackage rather than through the TMP importer window (which needs a GUI context).
    /// </summary>
    public static class TmpEssentials
    {
        public const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public static bool IsImported()
        {
            return AssetDatabase.LoadAssetAtPath<Object>(SettingsPath) != null;
        }

        /// <summary>Locates "TMP Essential Resources.unitypackage" inside the installed TMP package.</summary>
        static string FindPackageFile()
        {
            UnityEditor.PackageManager.PackageInfo info =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_Settings).Assembly);

            if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;

            string path = Path.Combine(info.resolvedPath, "Package Resources");
            path = Path.Combine(path, "TMP Essential Resources.unitypackage");
            return File.Exists(path) ? path : null;
        }

        /// <summary>Idempotent: does nothing once the essentials are already in the project.</summary>
        public static void Ensure()
        {
            if (IsImported()) return;

            string packageFile = FindPackageFile();
            if (packageFile == null)
            {
                Debug.LogError("[TmpEssentials] Could not find 'TMP Essential Resources.unitypackage'. " +
                               "Import it from Window > TextMeshPro > Import TMP Essential Resources.");
                return;
            }

            Debug.Log("[TmpEssentials] Importing TMP essentials from " + packageFile);
            UnityEditor.AssetPackage.Package.Import(packageFile, false);
        }
    }
}
