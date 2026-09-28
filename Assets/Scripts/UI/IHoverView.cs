namespace DungeonCards
{
    /// <summary>
    /// Something the pointer can land on that reacts by explaining itself.
    ///
    /// The hover lift, the hand fan and the shop's relic rack only ever need these two calls, and they are
    /// deliberately separate. A card stands the same way whether the pointer found it or a Palm Trick laid
    /// it out, but only the pointer opens its tooltip: there is one tooltip for the whole scene, so three
    /// offered cards asking for it at once would leave it on whichever was built last.
    /// </summary>
    public interface IHoverView
    {
        /// <summary>The pointer arrived on this view, so it shows everything it has, tooltip included.</summary>
        void SetHovered(bool hovered);

        /// <summary>Lifted without the pointer, as an offered card is. Same numbers, no tooltip.</summary>
        void SetLifted(bool lifted);
    }
}
