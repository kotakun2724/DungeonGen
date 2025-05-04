using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using DungeonGen.Generation;

/// <summary>
/// 2‑D 可変幅ダンジョン（高さ 1 フロア）。
/// 部屋散布 → ドロネー三角分割 → Prim(MST) → A* で通路彫り。
/// </summary>
public class Dungeon2DController : MonoBehaviour
{
    [Header("Grid size")]
    public int width = 64, height = 64;

    [Header("Random & Room")]
    public int seed = 0;
    public int attempts = 200;
    public int minRoomW = 4, maxRoomW = 10;
    public int minRoomH = 4, maxRoomH = 10;

    [Header("Corridor")]
    [Range(1, 4)] public int corridorWidth = 1;
    [Range(0, 1)] public float extraEdgeChance = 0.15f;

    [Header("Prefabs (1‑meter cubes)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;

    void Start()
    {
        var rng = seed == 0 ? new System.Random() : new System.Random(seed);
        var map = new CellMap(width, height);

        // 1. Rooms
        var roomGen = new RoomGenerator(map, rng,
            minRoomW, maxRoomW, minRoomH, maxRoomH, attempts);
        roomGen.Scatter();

        // 2‑3. Graph
        var edges = GraphGenerator.CreateEdges(roomGen.Rooms);
        var graph = GraphGenerator.PrimMST(edges, roomGen.Rooms.Count);
        var finalGraph = GraphGenerator.AddExtraConnections(edges, graph, extraEdgeChance);

        // 4. Corridors
        var carver = new CorridorCarver(map);
        carver.Carve(finalGraph, roomGen.Rooms, corridorWidth);

        // 5. Render
        new PrefabPlacer(map, transform, floorPrefab, wallPrefab).Render();
    }
}