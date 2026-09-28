using System.Collections.Generic;

namespace DungeonCards
{
    /// <summary>Text icon, label and flavour text for each room type, shared by every screen that lists rooms.</summary>
    public static class RoomTypes
    {
        public static string Icon(RoomType type)
        {
            switch (type)
            {
                case RoomType.Combat: return "!";
                case RoomType.Event: return "?";
                case RoomType.Vantage: return "V";
                case RoomType.Shop: return "$";
                case RoomType.Boss: return "!!";
                default: return "X";
            }
        }

        public static string Label(RoomType type)
        {
            switch (type)
            {
                case RoomType.Combat: return "COMBAT";
                case RoomType.Event: return "EVENT";
                case RoomType.Vantage: return "VANTAGE";
                case RoomType.Shop: return "SHOP";
                case RoomType.Boss: return "BOSS";
                default: return "EXIT";
            }
        }

        public static string Description(RoomType type)
        {
            switch (type)
            {
                case RoomType.Combat: return "A monster guards this path.";
                case RoomType.Event: return "Something waits in the dark.";
                case RoomType.Vantage: return "A high point. Reveals the path ahead.";
                case RoomType.Shop: return "A merchant has set up a stall. Gold changes hands.";
                case RoomType.Boss: return "The way out is behind it.";
                default: return "The way out of the dungeon.";
            }
        }
    }

    /// <summary>One room on the dungeon map. Edges are stored as node ids so the graph stays a plain data structure.</summary>
    public class MapNode
    {
        public int id;
        public int layer;
        public int indexInLayer;
        public RoomType roomType;
        public bool visited;

        /// <summary>Rooms that lead into this one. A room can have several ways in.</summary>
        public readonly List<int> incoming = new List<int>();

        /// <summary>Rooms this one leads to. Deliberately sparse: a room is not guaranteed to reach every exit.</summary>
        public readonly List<int> outgoing = new List<int>();
    }
}
