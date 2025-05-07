using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using DungeonGen.Generation;
using DungeonGen;

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

    [Header("プレイヤー設定")]
    [SerializeField] private PlayerSpawner playerSpawner;

    [Tooltip("追加接続の割合 (0-2.0: 1.0=MST本数と同じ数を追加)")]
    [Range(0, 2.0f)] public float extraEdgeRatio = 0.5f;

    [Header("Prefabs (1‑meter cubes)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;

    private void Awake()
    {
        // PlayerSpawnerコンポーネントを取得（なければ検索）
        if (playerSpawner == null)
        {
            playerSpawner = FindObjectOfType<PlayerSpawner>();
            Debug.Log("PlayerSpawner検索結果: " + (playerSpawner != null ? "見つかりました" : "見つかりませんでした"));
        }
    }

    private void Start()
    {
        // ゲーム開始時にダンジョン生成
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

        // プレイヤー生成を試みる
        if (playerSpawner != null)
        {
            Debug.Log("プレイヤー生成開始...");
            playerSpawner.SpawnPlayer(roomGen.Rooms, map);
        }
        else
        {
            // プレイヤースポナーがなければ、再度検索を試みる
            playerSpawner = FindObjectOfType<PlayerSpawner>();
            if (playerSpawner != null)
            {
                Debug.Log("プレイヤー生成開始（再検索後）...");
                playerSpawner.SpawnPlayer(roomGen.Rooms, map);
            }
            else
            {
                Debug.LogWarning("PlayerSpawnerが見つかりません。プレイヤーは生成されません。");
            }
        }
    }

    // インスペクターからも再生成できるように
    [ContextMenu("Regenerate Dungeon")]
    public void InspectorRegenerate()
    {
        RegenerateDungeon();
    }

    // プレイヤースポナーがアタッチされているか確認するためのメソッド（必要に応じて使用）
    [ContextMenu("Verify Player Spawner")]
    public void VerifyPlayerSpawner()
    {
        if (playerSpawner == null)
        {
            playerSpawner = FindObjectOfType<PlayerSpawner>();
            Debug.Log("PlayerSpawner検索結果: " + (playerSpawner != null ? "見つかりました" : "見つかりませんでした"));
        }
        else
        {
            Debug.Log("PlayerSpawnerは既に設定されています");
        }
    }
}