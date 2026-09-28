using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonCards
{
    /// <summary>
    /// Lifts a card or a relic icon while the pointer is over it: it grows slightly, and it reports the
    /// hover so the hand fan or the shop's relic rack can straighten it and push its neighbours aside.
    ///
    /// The event comes from an EventTrigger on the object, which the scene builder wires to these two
    /// methods. They are also wired here at runtime if they are missing, so a card can never be left
    /// with a hover effect that does nothing.
    ///
    /// What it lifts is an IHoverView rather than a CardView, because the shop's relics wear the same
    /// lift without being cards.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardHoverEffect : MonoBehaviour
    {
        [Tooltip("The view whose tooltip and numbers this lift drives. Found on this object when left empty.")]
        [SerializeField] MonoBehaviour hoverView;

        [Header("Hover")]
        [SerializeField] float hoverScale = 1.16f;
        [Tooltip("How quickly the object grows and shrinks. Higher is snappier.")]
        [SerializeField] float speed = 14f;
        RectTransform rect;
        IHoverView view;
        float current = 1f;
        float target = 1f;

        /// <summary>True while the pointer is on the card, which is what a forced lift has to defer to.</summary>
        bool pointerOver;

        /// <summary>True while the card is lifted without the pointer, as a pending choice does.</summary>
        bool forced;

        /// <summary>
        /// True while the pointer is over this card. The hand fan reads it to bend the row, so the hovered
        /// card straightens and rises while its neighbours part. Nothing here depends on it.
        /// </summary>
        public bool IsHovered { get; private set; }

        void Awake()
        {
            rect = (RectTransform)transform;
            view = ResolveView();

            EnsureTrigger();
        }

        /// <summary>
        /// Finds what to tell about the hover. A card and a relic icon both answer to the same two calls,
        /// so this lift does not care which of them it is standing on.
        /// </summary>
        IHoverView ResolveView()
        {
            if (hoverView != null)
            {
                var assigned = hoverView as IHoverView;
                if (assigned != null) return assigned;

                Debug.LogWarning("[CardHoverEffect] " + name + " has a view assigned that is not an IHoverView, " +
                                 "so the hover has nothing to tell.");
            }

            var found = GetComponent<IHoverView>();
            if (found == null)
            {
                Debug.LogWarning("[CardHoverEffect] " + name + " has no IHoverView beside it, so the hover lift " +
                                 "will move it without ever explaining it.");
            }

            return found;
        }

        void OnDisable()
        {
            current = target = 1f;
            IsHovered = false;
            pointerOver = false;
            forced = false;
            if (rect != null) rect.localScale = Vector3.one;
            if (view != null) view.SetHovered(false);
        }



        void Update()
        {
            if (Mathf.Abs(current - target) < 0.001f)
            {
                if (!Mathf.Approximately(current, target)) Apply(target);
                return;
            }

            // Framerate independent approach, so the pop feels the same at any refresh rate.
            current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            if (Mathf.Abs(current - target) < 0.001f) current = target;
            Apply(current);
        }

        void Apply(float scale)
        {
            if (rect != null) rect.localScale = new Vector3(scale, scale, 1f);
        }

        public void OnPointerEnter(BaseEventData data)
        {
            pointerOver = true;
            target = hoverScale;
            IsHovered = true;
            if (view != null) view.SetHovered(true);
        }

        public void OnPointerExit(BaseEventData data)
        {
            pointerOver = false;
            if (view != null) view.SetHovered(false);

            // A lifted card stays lifted: it is being offered rather than merely pointed at, and several of
            // them are up at once, so the pointer leaving one says nothing about it.
            if (forced) return;

            target = 1f;
            IsHovered = false;
        }

        /// <summary>
        /// Lifts the card as though the pointer were on it. Palm Trick draws the cards it turns up this way,
        /// so the choice reads as picking a card up rather than as reading a list.
        ///
        /// The tooltip is not part of it. There is one tooltip for the whole scene, so three cards asking
        /// for it at once would leave it on whichever of them was built last.
        /// </summary>
        public void SetForcedHover(bool value)
        {
            forced = value;

            if (value)
            {
                target = hoverScale;
                IsHovered = true;
                if (view != null) view.SetLifted(true);
                return;
            }

            if (view != null) view.SetLifted(pointerOver);

            // Handed back to the pointer: whatever it is over now is what the card should be doing.
            target = pointerOver ? hoverScale : 1f;
            IsHovered = pointerOver;
        }



        // Nothing here reorders the card or gives it a canvas of its own.
        //
        // Reordering looks like the obvious way to lift the hovered card, but the hand and the deck grid
        // are both laid out by layout groups: changing a card's sibling index makes the group reflow, so
        // the card slides out from under the pointer, which fires PointerExit, which puts it back, which
        // slides it under the pointer again. That is the hover jitter.
        //
        // A canvas on each card with override sorting fixes the reflow but breaks the other two screens:
        // such a canvas is sorted against every other canvas in the scene, so cards at sorting order 0
        // were drawn behind the deck view and the inventory (orders 200 and 100) and the whole deck grid
        // was invisible. It also escapes the deck grid's scroll view mask.
        //
        // The scale alone is therefore the lift, and the containers leave a gutter wide enough that a
        // grown card never reaches its neighbour.



        void EnsureTrigger()
        {
            EventTrigger trigger = GetComponent<EventTrigger>();
            if (trigger == null) trigger = gameObject.AddComponent<EventTrigger>();
            if (trigger.triggers == null) trigger.triggers = new List<EventTrigger.Entry>();

            AddEntry(trigger, EventTriggerType.PointerEnter, true);
            AddEntry(trigger, EventTriggerType.PointerExit, false);
        }

        /// <summary>
        /// Makes sure the trigger has a live entry for this event.
        ///
        /// An entry that exists but carries no listener is not wired, it is only labelled. The event table can
        /// hold a PointerEnter whose callback is empty, which fires into nothing, and treating that as already
        /// wired is what leaves an object silently dead: the entry's presence says nothing about whether
        /// anything is bound to it. The listener count is therefore what is checked, not the event's name.
        /// </summary>
        void AddEntry(EventTrigger trigger, EventTriggerType type, bool entering)
        {
            foreach (EventTrigger.Entry existing in trigger.triggers)
            {
                if (existing == null || existing.eventID != type) continue;

                // Already carrying a call, whether it was authored in the scene or bound by a previous Awake.
                // Nothing to add, and adding anyway would double every listener on the object.
                if (existing.callback != null && existing.callback.GetPersistentEventCount() > 0) return;

                if (existing.callback == null) existing.callback = new EventTrigger.TriggerEvent();
                if (entering) existing.callback.AddListener(OnPointerEnter);
                else existing.callback.AddListener(OnPointerExit);
                return;
            }

            var entry = new EventTrigger.Entry { eventID = type };
            if (entering) entry.callback.AddListener(OnPointerEnter);
            else entry.callback.AddListener(OnPointerExit);
            trigger.triggers.Add(entry);
        }
    }
}
