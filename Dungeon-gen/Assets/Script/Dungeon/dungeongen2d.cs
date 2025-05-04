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
    [Tooltip("部屋生成の最大試行回数")]
    public int attempts = 200;
    [Tooltip("生成する部屋の数 (0=制限なし)")]
    [Range(0, 50)] public int roomCount = 30;
    public int minRoomW = 4, maxRoomW = 10;
    public int minRoomH = 4, maxRoomH = 10;

    [Header("Corridor")]
    [Range(1, 4)] public int corridorWidth = 1;

    [Tooltip("追加接続の割合 (0-2.0: 1.0=MST本数と同じ数を追加)")]
    [Range(0, 2.0f)] public float extraEdgeRatio = 0.5f;

    [Header("Prefabs (1‑meter cubes)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;

    void Start()
    {
        // 起動時にダンジョン生成を行う
        RegenerateDungeon();
    }

    // リロード可能なダンジョン生成メソッド
    public void RegenerateDungeon()
    {
        Debug.Log($"ダンジョン生成開始 - 追加接続割合: {extraEdgeRatio:F2}, 部屋数: {roomCount}");

        // 既存のオブジェクト削除
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        var rng = seed == 0 ? new System.Random() : new System.Random(seed);
        var map = new CellMap(width, height);

        // 1. Rooms
        var roomGen = new RoomGenerator(map, rng,
            minRoomW, maxRoomW, minRoomH, maxRoomH, attempts);
        roomGen.Scatter(roomCount);
        Debug.Log($"部屋生成完了: {roomGen.Rooms.Count}個の部屋" +
                 (roomCount > 0 && roomGen.Rooms.Count < roomCount ? $" (指定数{roomCount}に到達できませんでした)" : ""));

        // 2‑3. Graph
        var edges = GraphGenerator.CreateEdges(roomGen.Rooms);
        var graph = GraphGenerator.PrimMST(edges, roomGen.Rooms.Count);

        // 追加の接続を割合で指定（extraEdgeRatio=1.0でMSTと同じ数を追加）
        var finalGraph = GraphGenerator.AddExtraConnections(edges, graph, extraEdgeRatio);
        Debug.Log($"グラフ生成完了: 基本接続{graph.Count}本 + 追加接続{finalGraph.Count - graph.Count}本");

        // 4. Corridors
        var carver = new CorridorCarver(map);
        carver.Carve(finalGraph, roomGen.Rooms, corridorWidth);

        // 5. Render
        new PrefabPlacer(map, transform, floorPrefab, wallPrefab).Render();
    }

    // インスペクターからも再生成できるように
    [ContextMenu("Regenerate Dungeon")]
    public void InspectorRegenerate()
    {
        RegenerateDungeon();
    }
}