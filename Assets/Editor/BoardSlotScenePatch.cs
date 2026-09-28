using System.Collections.Generic;
using System.IO;
using DungeonCards;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Throwaway. Brings the live combat scene in line with the rebuilt board row without running the full scene
/// builder, which would regenerate all six scenes and overwrite anything authored by hand in the others.
///
/// The squares lose the number they never managed to show, gain the construct tooltip the cards use, and are
/// laid out by the same numbers CombatManager lays them out with at runtime. Deleted once it has run.
/// </summary>
public static class BoardSlotScenePatch
{
    const string CombatScenePath = "Assets/Scenes/Combat.unity";
    const string ReturnScenePath = "Assets/Scenes/Room.unity";

    public static void Execute()
    {
        Scene previous = SceneManager.GetActiveScene();
        if (previous.isDirty) EditorSceneManager.SaveScene(previous);

        Scene scene = EditorSceneManager.OpenScene(CombatScenePath, OpenSceneMode.Single);

        CombatManager manager = FindInScene<CombatManager>(scene);
        Transform slotsRoot = FindTransform(scene, "BoardSlots");
        Transform boardText = FindTransform(scene, "BoardText");

        if (manager == null || slotsRoot == null || boardText == null)
        {
            Debug.LogError("PATCH ABORTED: manager=" + (manager != null) + " slotsRoot=" + (slotsRoot != null)
                + " boardText=" + (boardText != null));
            return;
        }

        // The tooltip the cards show, read exactly where the scene builder reads it from.
        CardTooltip shared = TooltipOf(manager);

        float step = CombatManager.SlotSize + CombatManager.SlotSpacing;
        var views = new List<BoardSlotView>();

        for (int i = 0; i < PlayerStats.MaxBoardSlots; i++)
        {
            Transform slot = slotsRoot.Find("BoardSlot" + (i + 1));
            if (slot == null)
            {
                Debug.LogError("PATCH ABORTED: BoardSlot" + (i + 1) + " is not in the scene.");
                return;
            }

            GameObject slotGo = slot.gameObject;

            // The square prints nothing now: what it does and how long it has left are both in its tooltip.
            Transform turns = slot.Find("Turns");
            if (turns != null) Object.DestroyImmediate(turns.gameObject);

            // The frame is the square's body and the only thing that catches the pointer.
            Image frame = slotGo.GetComponent<Image>();
            if (frame != null) frame.raycastTarget = true;

            Transform iconTransform = slot.Find("Icon");
            Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;

            BoardSlotView view = slotGo.GetComponent<BoardSlotView>();
            if (view == null) view = slotGo.AddComponent<BoardSlotView>();
            SetReference(view, "icon", icon);
            SetReference(view, "tooltipPrefab", shared);

            CardHoverEffect hover = slotGo.GetComponent<CardHoverEffect>();
            if (hover == null) hover = slotGo.AddComponent<CardHoverEffect>();

            // CardHoverEffect wires these itself on Awake if they are missing, and skips a type already there,
            // so a scene that already has them cannot end up firing the hover twice.
            EventTrigger trigger = slotGo.GetComponent<EventTrigger>();
            if (trigger == null) trigger = slotGo.AddComponent<EventTrigger>();
            if (trigger.triggers == null) trigger.triggers = new List<EventTrigger.Entry>();
            AddEntry(trigger, EventTriggerType.PointerEnter, hover.OnPointerEnter);
            AddEntry(trigger, EventTriggerType.PointerExit, hover.OnPointerExit);

            // The same placement the row gets at runtime, from the same numbers.
            var rect = (RectTransform)slot;
            rect.anchoredPosition = new Vector2((i % CombatManager.SlotsPerRow) * step,
                                               -(i / CombatManager.SlotsPerRow) * step);
            rect.sizeDelta = new Vector2(CombatManager.SlotSize, CombatManager.SlotSize);

            views.Add(view);
        }

        // One line is what the four slots in play need; CombatManager grows the box from here.
        var slotsRect = (RectTransform)slotsRoot;
        slotsRect.sizeDelta = new Vector2(slotsRect.sizeDelta.x, CombatManager.SlotSize);

        var serialized = new SerializedObject(manager);

        SerializedProperty slots = serialized.FindProperty("boardSlots");
        slots.arraySize = views.Count;
        for (int i = 0; i < views.Count; i++)
        {
            slots.GetArrayElementAtIndex(i).objectReferenceValue = views[i];
        }

        serialized.FindProperty("boardSlotsRect").objectReferenceValue = slotsRect;
        serialized.FindProperty("boardTextRect").objectReferenceValue = (RectTransform)boardText;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string report = "PATCHED: " + CombatScenePath + " with " + views.Count + " tooltip squares"
            + "; tooltip=" + (shared != null)
            + "; BoardText y=" + boardText.GetComponent<RectTransform>().anchoredPosition.y;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ReturnScenePath) != null)
        {
            EditorSceneManager.OpenScene(ReturnScenePath, OpenSceneMode.Single);
            report += "; returned to " + ReturnScenePath;
        }

        Debug.Log(report);
    }

    /// <summary>The tooltip the combat cards show, taken off the card prefab the manager holds.</summary>
    static CardTooltip TooltipOf(CombatManager manager)
    {
        var serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("cardViewPrefab");
        var cardView = property != null ? property.objectReferenceValue as CardView : null;
        if (cardView == null) return null;

        var viewSerialized = new SerializedObject(cardView);
        SerializedProperty tooltip = viewSerialized.FindProperty("tooltipPrefab");
        return tooltip != null ? tooltip.objectReferenceValue as CardTooltip : null;
    }

    static void AddEntry(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> action)
    {
        foreach (EventTrigger.Entry existing in trigger.triggers)
        {
            if (existing.eventID == type) return;
        }


        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning("[BoardSlotScenePatch] " + target.name + " has no serialized field '" + field + "'.");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    static Transform FindTransform(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name) return candidate;
            }
        }
        return null;
    }
}
