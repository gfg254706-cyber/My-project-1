using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// The whole dungeon, generated once when a run starts. Layers are depths; a room only ever
    /// connects to rooms in the next layer, so the player always moves forward.
    /// </summary>
    public class DungeonMap
    {
        public readonly List<MapNode> nodes = new List<MapNode>();

        /// <summary>Node ids grouped by layer, index 0 being the entrance layer.</summary>
        public readonly List<List<int>> layers = new List<List<int>>();

        public int CurrentNodeId = -1;

        public int LayerCount { get { return layers.Count; } }

        public int EntranceId { get { return layers.Count > 0 && layers[0].Count > 0 ? layers[0][0] : -1; } }

        public MapNode Get(int id)
        {
            if (id < 0 || id >= nodes.Count) return null;
            return nodes[id];
        }

        public MapNode Current { get { return Get(CurrentNodeId); } }

        public void Connect(int fromId, int toId)
        {
            MapNode from = Get(fromId);
            MapNode to = Get(toId);
            if (from == null || to == null || from == to) return;

            if (!from.outgoing.Contains(toId)) from.outgoing.Add(toId);
            if (!to.incoming.Contains(fromId)) to.incoming.Add(fromId);
        }

        /// <summary>The rooms reachable from the room the player is standing in.</summary>
        public List<MapNode> ExitsFrom(int nodeId)
        {
            var result = new List<MapNode>();
            MapNode node = Get(nodeId);
            if (node == null) return result;

            foreach (int id in node.outgoing)
            {
                MapNode target = Get(id);
                if (target != null) result.Add(target);
            }
            return result;
        }

        public List<MapNode> NodesInLayer(int layer)
        {
            var result = new List<MapNode>();
            if (layer < 0 || layer >= layers.Count) return result;

            foreach (int id in layers[layer])
            {
                MapNode node = Get(id);
                if (node != null) result.Add(node);
            }
            return result;
        }
    }

    /// <summary>
    /// The vantage room's reward: a map item that reads the rooms immediately ahead of the player,
    /// wherever they stand. It is spent after a few encounters and then fades away.
    /// </summary>
    public class MapReveal
    {
        public bool IsActive { get; private set; }
        public int Remaining { get; private set; }

        /// <summary>Hands over a map item that survives the given number of encounters.</summary>
        public void Grant(int encounters)
        {
            IsActive = true;
            Remaining = Mathf.Max(1, encounters);
        }

        /// <summary>Consumes one encounter of map life. Returns true when the map just expired.</summary>
        public bool Tick()
        {
            if (!IsActive) return false;

            Remaining--;
            if (Remaining > 0) return false;

            Expire();
            return true;
        }

        public void Expire()
        {
            IsActive = false;
            Remaining = 0;
        }

        public void Clear()
        {
            Expire();
        }
    }
}
