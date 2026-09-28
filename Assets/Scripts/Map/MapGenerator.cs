using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Builds the dungeon once per run, from a seed, so the layout is fixed in advance rather than
    /// rolled room by room.
    ///
    /// Shape: an entrance room, a run of intermediate layers whose width opens and closes as the run
    /// goes on, and a single boss room at the end. Layer widths are decided up front as a random walk
    /// rather than being derived from the layer before, which is what lets a junction offer two, three
    /// or four ways on instead of collapsing into a chain.
    ///
    /// Every room reaches at least one room in the next layer and every room is reachable from the
    /// entrance. A room reaches a contiguous span of the next layer, so routes spread and merge as the
    /// player descends.
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>Rooms in a single layer never exceed this.</summary>
        public const int MaxRoomsPerLayer = 4;

        /// <summary>A layer never drops below this many rooms.</summary>
        public const int MinRoomsPerLayer = 1;

        /// <summary>
        /// Edges leaving a single room, which is also the most routes a junction can offer at once.
        /// </summary>
        public const int MaxExitsPerRoom = 4;

        /// <summary>Shortest run: the entrance, three rooms, and the boss. Four rooms before the boss.</summary>
        public const int MinLayerCount = 5;

        /// <summary>Longest run: the entrance, seven rooms, and the boss. Eight rooms before the boss.</summary>
        public const int MaxLayerCount = 9;

        /// <summary>Builds a dungeon of randomly chosen length.</summary>
        public static DungeonMap Generate(int seed)
        {
            return Generate(seed, 0);
        }

        /// <summary>
        /// Builds a dungeon with a fixed number of layers. A layerCount below MinLayerCount rolls a
        /// length between MinLayerCount and MaxLayerCount instead.
        /// </summary>
        public static DungeonMap Generate(int seed, int layerCount)
        {
            var rng = new System.Random(seed);
            var map = new DungeonMap();

            if (layerCount < MinLayerCount) layerCount = rng.Next(MinLayerCount, MaxLayerCount + 1);

            List<int> widths = BuildWidthProfile(rng, layerCount);

            BuildLayers(map, rng, widths);
            ConnectLayers(map, rng);
            GuaranteeVantageRoom(map, rng);
            GuaranteeShopRoom(map, rng);

            return map;
        }


        // ------------------------------------------------------------------ layers

        /// <summary>
        /// Decides how many rooms each layer holds before any room exists. The walk opens the map up
        /// early, wanders, and closes it back down to the single boss room, so some runs stay wide and
        /// some pinch to a single corridor.
        /// </summary>
        static List<int> BuildWidthProfile(System.Random rng, int layerCount)
        {
            var widths = new List<int> { 1 };                                  // the entrance stands alone

            widths.Add(rng.Next(2, MaxRoomsPerLayer + 1));                     // the first step opens the map up

            for (int layer = 2; layer < layerCount - 1; layer++)
            {
                int previous = widths[layer - 1];

                // Reachability: the next layer can never hold more rooms than the one before it has
                // exits to reach them with, and never fewer than one room per source can fill.
                int widest = Mathf.Min(MaxRoomsPerLayer, previous * MaxExitsPerRoom);
                int narrowest = Mathf.Max(MinRoomsPerLayer, Mathf.CeilToInt(previous / (float)MaxExitsPerRoom));

                widths.Add(Mathf.Clamp(previous + rng.Next(-1, 2), narrowest, widest));
            }

            widths.Add(1);                                                     // the boss stands alone

            return widths;
        }

        static void BuildLayers(DungeonMap map, System.Random rng, List<int> widths)
        {
            int layerCount = widths.Count;

            for (int layer = 0; layer < layerCount; layer++)
            {
                var ids = new List<int>();
                for (int i = 0; i < widths[layer]; i++)
                {
                    var node = new MapNode
                    {
                        id = map.nodes.Count,
                        layer = layer,
                        indexInLayer = i,
                        roomType = PickRoomType(rng, layer, layerCount)
                    };
                    map.nodes.Add(node);
                    ids.Add(node.id);
                }
                map.layers.Add(ids);
            }
        }

        static RoomType PickRoomType(System.Random rng, int layer, int layerCount)
        {
            if (layer == 0) return RoomType.Combat;
            if (layer == layerCount - 1) return RoomType.Boss;

            int roll = rng.Next(0, 100);
            if (roll < 42) return RoomType.Combat;
            if (roll < 70) return RoomType.Event;
            if (roll < 84) return RoomType.Vantage;
            return RoomType.Shop;
        }

        // ------------------------------------------------------------------ edges

        static void ConnectLayers(DungeonMap map, System.Random rng)
        {
            for (int layer = 0; layer < map.LayerCount - 1; layer++)
            {
                List<int> current = map.layers[layer];
                List<int> next = map.layers[layer + 1];

                // Each room reaches a run of neighbouring rooms in the next layer, so edges stay local
                // instead of tangling across the whole map.
                for (int ai = 0; ai < current.Count; ai++)
                {
                    float position = current.Count == 1 ? 0.5f : ai / (float)(current.Count - 1);
                    int centre = Mathf.RoundToInt(position * (next.Count - 1));

                    int span = PickSpan(rng, next.Count);
                    int start = Mathf.Clamp(centre - (span - 1) / 2, 0, next.Count - span);

                    for (int k = 0; k < span; k++) map.Connect(current[ai], next[start + k]);
                }

                // Anything left without a way in gets one, so no room is unreachable. The layer widths
                // guarantee a source with spare capacity exists, so this never piles exits onto one room.
                for (int bi = 0; bi < next.Count; bi++)
                {
                    MapNode node = map.Get(next[bi]);
                    if (node == null || node.incoming.Count > 0) continue;

                    int source = FindSourceWithCapacity(map, current, bi, next.Count);
                    map.Connect(current[source], next[bi]);
                }
            }
        }

        /// <summary>
        /// How much of the next layer a room reaches. Often a single route, sometimes two, and
        /// occasionally the whole junction, which is what makes some rooms dead ends and others hubs.
        /// </summary>
        static int PickSpan(System.Random rng, int nextCount)
        {
            int widest = Mathf.Max(1, Mathf.Min(MaxExitsPerRoom, nextCount));
            if (widest == 1) return 1;

            int roll = rng.Next(0, 100);
            if (roll < 30) return 1;
            if (roll < 65) return Mathf.Min(2, widest);
            return widest;
        }

        /// <summary>
        /// Picks the room nearest the orphan's position that still has room for another exit, falling
        /// back to the nearest room if every source is already full.
        /// </summary>
        static int FindSourceWithCapacity(DungeonMap map, List<int> current, int orphanIndex, int nextCount)
        {
            float position = nextCount == 1 ? 0.5f : orphanIndex / (float)(nextCount - 1);
            int nearest = Mathf.Clamp(Mathf.RoundToInt(position * (current.Count - 1)), 0, current.Count - 1);

            for (int offset = 0; offset < current.Count; offset++)
            {
                int forward = nearest + offset;
                if (forward < current.Count)
                {
                    MapNode candidate = map.Get(current[forward]);
                    if (candidate != null && candidate.outgoing.Count < MaxExitsPerRoom) return forward;
                }

                int backward = nearest - offset;
                if (backward >= 0)
                {
                    MapNode candidate = map.Get(current[backward]);
                    if (candidate != null && candidate.outgoing.Count < MaxExitsPerRoom) return backward;
                }
            }

            return nearest;
        }

        /// <summary>Forces one vantage room on runs where the type roll never produced one.</summary>
        static void GuaranteeVantageRoom(DungeonMap map, System.Random rng)
        {
            if (map.LayerCount <= 3) return;

            foreach (MapNode node in map.nodes)
            {
                if (node.roomType == RoomType.Vantage) return;
            }

            var candidates = new List<MapNode>();
            foreach (MapNode node in map.nodes)
            {
                if (node.layer > 0 && node.layer < map.LayerCount - 1) candidates.Add(node);
            }

            if (candidates.Count > 0) candidates[rng.Next(0, candidates.Count)].roomType = RoomType.Vantage;
        }

        /// <summary>
        /// Forces one shop on runs where the type roll never produced one.
        ///
        /// The roll leaves a shop out of roughly one run in seven, and gold has exactly one use, so a run
        /// with no shop in it is a run with a whole system missing. The vantage room has had the same
        /// guarantee since it was written, and for the same reason.
        ///
        /// This runs after the vantage guarantee, so a map that needs both gets both.
        /// </summary>
        static void GuaranteeShopRoom(DungeonMap map, System.Random rng)
        {
            if (map.LayerCount <= 3) return;

            foreach (MapNode node in map.nodes)
            {
                if (node.roomType == RoomType.Shop) return;
            }

            var candidates = new List<MapNode>();
            foreach (MapNode node in map.nodes)
            {
                if (node.layer > 0 && node.layer < map.LayerCount - 1) candidates.Add(node);
            }

            if (candidates.Count > 0) candidates[rng.Next(0, candidates.Count)].roomType = RoomType.Shop;
        }
    }
}

