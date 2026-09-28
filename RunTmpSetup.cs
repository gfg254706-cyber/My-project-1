using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using DungeonCards.EditorTools;

public static class RunTmpSetup
{
    public static string Execute()
    {
        var sb = new StringBuilder();

        sb.AppendLine("TMP imported before: " + TmpEssentials.IsImported());
        TmpEssentials.Ensure();
        sb.AppendLine("TMP imported after:  " + TmpEssentials.IsImported());

        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpEssentials.SettingsPath);
        sb.AppendLine("TMP_Settings load: " + (settings != null ? "ok" : "NULL"));

        if (settings != null)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            sb.AppendLine("default font: " + (font != null ? AssetDatabase.GetAssetPath(font) : "NULL"));
        }

        // Prove a TMP component can be created and gets a font assigned automatically.
        GameObject probe = new GameObject("TmpProbe");
        TextMeshProUGUI text = probe.AddComponent<TextMeshProUGUI>();
        text.text = "probe";
        sb.AppendLine("probe font: " + (text.font != null ? text.font.name : "NULL"));
        sb.AppendLine("probe material: " + (text.fontSharedMaterial != null ? "ok" : "NULL"));
        Object.DestroyImmediate(probe);

        return sb.ToString();
    }
}
