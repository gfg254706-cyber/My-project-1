using System.Collections.Generic;
using System.Text;
using DungeonCards;
using UnityEngine;

/// <summary>Exercises the map generator over many seeds and returns a report on the graph invariants.</summary>
public static class MapGenValidator
{
    public static string Execute()
    {
        const int runs = 400;
        int failures = 0;
        int totalNodes = 0;
        int totalEdges = 0;
        int narrowNodes = 0;
        int branchNodes = 0;
        int maxIncoming = 0;
        int vantageTotal = 0;
        int eventTotal = 0;
        int combatTotal = 0;
        int singleRoomLayers = 0;

        var errors = new List<string>();

        for (int run = 0; run < runs; run++)
        {
            int seed = run * 7919 + 13;
            DungeonMap map = MapGenerator.Generate(seed);
            string error = Validate(map);

            if (error != null)
            {
                failures++;
                if (errors.Count < 5) errors.Add("seed " + seed + ": " + error);
                continue;
            }

            foreach (MapNode node in map.nodes)
            {
                totalNodes++;
                totalEdges += node.outgoing.Count;

                if (node.outgoing.Count == 1) narrowNodes++;
                else if (node.outgoing.Count >= 2) branchNodes++;

                if (node.incoming.Count > maxIncoming) maxIncoming = node.incoming.Count;

                if (node.roomType == RoomType.Vantage) vantageTotal++;
                else if (node.roomType == RoomType.Event) eventTotal++;
                else if (node.roomType == RoomType.Combat) combatTotal++;
            }

            for (int layer = 1; layer < map.LayerCount - 1; layer++)
            {
                if (map.layers[layer].Count == 1) singleRoomLayers++;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("MAP GENERATION over " + runs + " maps (8 layers each)");
        sb.AppendLine("failures: " + failures);
        foreach (string error in errors) sb.AppendLine("   " + error);
        sb.AppendLine("avg rooms/map: " + (totalNodes / (float)runs).ToString("0.0"));
        sb.AppendLine("avg edges/map: " + (totalEdges / (float)runs).ToString("0.0"));
        sb.AppendLine("rooms with exactly 1 exit: " + narrowNodes +
                      "   with 2+ exits: " + branchNodes +
                      "   (" + (100f * narrowNodes / Mathf.Max(1, narrowNodes + branchNodes)).ToString("0") + "% are narrow)");
        sb.AppendLine("max ways into a single room: " + maxIncoming);
        sb.AppendLine("locked-in single-room layers per map: " + (singleRoomLayers / (float)runs).ToString("0.00"));
        sb.AppendLine("room mix over all maps: combat=" + combatTotal + " event=" + eventTotal + " vantage=" + vantageTotal);
        sb.AppendLine("vantage rooms per map: " + (vantageTotal / (float)runs).ToString("0.00"));

        return sb.ToString();
    }

    /// <summary>Returns null when the map is well formed, or a description of the first broken invariant.</summary>
    static string Validate(DungeonMap map)
    {
        if (map.LayerCount != MapGenerator.DefaultLayerCount)
            return "layer count " + map.LayerCount;

        if (map.layers[0].Count != 1) return "entrance layer has " + map.layers[0].Count + " rooms";
        if (map.layers[map.LayerCount - 1].Count != 1) return "exit layer has " + map.layers[map.LayerCount - 1].Count + " rooms";

        int last = map.LayerCount - 1;

        foreach (MapNode node in map.nodes)
        {
            foreach (int targetId in node.outgoing)
            {
                MapNode target = map.Get(targetId);
                if (target == null) return "room " + node.id + " points at a missing room";
                if (target.layer != node.layer + 1) return "room " + node.id + " skips a layer";
            }

            if (node.outgoing.Count == 0 && node.layer < last) return "room " + node.id + " has no way out";
            if (node.outgoing.Count > 3) return "room " + node.id + " has " + node.outgoing.Count + " exits";
            if (node.layer > 0 && node.incoming.Count == 0) return "room " + node.id + " is unreachable";
        }

        if (map.Get(map.EntranceId).roomType != RoomType.Combat) return "the entrance is not a combat room";
        if (map.Get(map.layers[last][0]).roomType != RoomType.Exit) return "the final room is not the exit";

        var seen = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(map.EntranceId);
        seen.Add(map.EntranceId);
        while (queue.Count > 0)
        {
            MapNode node = map.Get(queue.Dequeue());
            foreach (int targetId in node.outgoing)
            {
                if (seen.Contains(targetId)) continue;
                seen.Add(targetId);
                queue.Enqueue(targetId);
            }
        }
        if (seen.Count != map.nodes.Count) return (map.nodes.Count - seen.Count) + " rooms unreachable from the entrance";

        bool hasVantage = false;
        foreach (MapNode node in map.nodes)
        {
            if (node.roomType == RoomType.Vantage) hasVantage = true;
        }
        if (!hasVantage) return "no vantage room";

        return null;
    }
}
