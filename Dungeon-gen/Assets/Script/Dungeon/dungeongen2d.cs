using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum CellType { Empty, Room, Corridor }

public struct Cell
{
    public CellType type;
    public int roomId; // -1 = not a room
}

/// <summary>
/// 2‑D 可変幅ダンジョン（高さ 1 フロア）。
/// 部屋散布 → ドロネー三角分割 → Prim(MST) → A* で通路彫り。
/// Play ごとに Build() を呼びランダム生成します。
/// </summary>
public class dungeongen2d : MonoBehaviour
{
    // ───────────── Inspector ─────────────
    [Header("Grid size")]
    public int w = 64, h = 64;

    [Header("Random & Room")]
    public int seed = 0;              // 0 = 現在時刻をシード
    public int roomAttempts = 200;
    public int minRoomW = 4, maxRoomW = 10;
    public int minRoomH = 4, maxRoomH = 10;

    [Header("Corridor")]
    [Range(1, 4)] public int corridorWidth = 1;
    [Range(0, 1)] public float extraConnectionChance = 0.15f;

    [Header("Prefabs (1‑meter cubes)")]
    public GameObject floor;
    public GameObject wall;

    // ───────────── 内部フィールド ─────────────
    private Cell[,] map;
    private readonly List<RectInt> rooms = new();
    private System.Random rng;

    private static readonly Vector2Int[] dirs =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    void Start() => Build();

    // =============================================================
    // Build pipeline
    // =============================================================
    public void Build()
    {
        rng = seed == 0 ? new System.Random(Environment.TickCount) : new System.Random(seed);
        map = new Cell[w, h];
        rooms.Clear();

        ScatterRooms();
        var allEdges = TriangulateRooms();
        var graph = MinimumSpanningTree(allEdges);
        AddExtraEdges(allEdges, graph);
        CarveCorridors(graph);
        InstantiatePrefabs();
    }

    // -------------------------------------------------------------
    // 1. 部屋散布
    // -------------------------------------------------------------
    void ScatterRooms()
    {
        for (int i = 0; i < roomAttempts; i++)
        {
            int rw = rng.Next(minRoomW, maxRoomW + 1);
            int rh = rng.Next(minRoomH, maxRoomH + 1);
            int rx = rng.Next(1, w - rw - 1);
            int ry = rng.Next(1, h - rh - 1);

            var r = new RectInt(rx, ry, rw, rh);
            if (rooms.Any(o => o.Overlaps(r))) continue; // 重なりは破棄

            int id = rooms.Count;
            rooms.Add(r);
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    map[x, y] = new Cell { type = CellType.Room, roomId = id };
        }
    }

    // -------------------------------------------------------------
    // 2. ドロネー三角分割
    // -------------------------------------------------------------
    struct Edge { public int a, b; public float len; }

    List<Edge> TriangulateRooms()
    {
        var pts = rooms.Select(r => new Vector2(r.center.x, r.center.y)).ToList();
        var tris = BowyerWatson2D.Triangulate(pts);

        var edgeSet = new HashSet<(int, int)>();
        void AddEdge(int i, int j)
        {
            if (i > j) (i, j) = (j, i);
            edgeSet.Add((i, j));
        }
        foreach (var t in tris)
        {
            AddEdge(t.i0, t.i1);
            AddEdge(t.i1, t.i2);
            AddEdge(t.i2, t.i0);
        }

        return edgeSet.Select(e => new Edge
        {
            a = e.Item1,
            b = e.Item2,
            len = Vector2.Distance(pts[e.Item1], pts[e.Item2])
        }).ToList();
    }

    // -------------------------------------------------------------
    // 3. 最小全域木 (Prim)
    // -------------------------------------------------------------
    List<Edge> MinimumSpanningTree(List<Edge> edges)
    {
        var visited = new HashSet<int> { 0 };
        var mst = new List<Edge>();
        while (visited.Count < rooms.Count)
        {
            var best = edges
                .Where(e => visited.Contains(e.a) ^ visited.Contains(e.b))
                .OrderBy(e => e.len)
                .First();
            mst.Add(best);
            visited.Add(visited.Contains(best.a) ? best.b : best.a);
        }
        return mst;
    }

    void AddExtraEdges(List<Edge> all, List<Edge> graph)
    {
        foreach (var e in all)
            if (!graph.Contains(e) && rng.NextDouble() < extraConnectionChance)
                graph.Add(e);
    }

    // -------------------------------------------------------------
    // 4. A* で通路生成
    // -------------------------------------------------------------
    void CarveCorridors(List<Edge> graph)
    {
        foreach (var e in graph)
        {
            Vector2Int start = Vector2Int.RoundToInt(rooms[e.a].center);
            Vector2Int goal = Vector2Int.RoundToInt(rooms[e.b].center);

            foreach (var p in AStar(start, goal))
                PaintCorridor(p);
        }
    }

    IEnumerable<Vector2Int> AStar(Vector2Int s, Vector2Int g)
    {
        var open = new PriorityQueue<Vector2Int>();
        var came = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, int> { [s] = 0 };

        open.Enqueue(s, 0);
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == g) return Reconstruct(cur);

            foreach (var dir in dirs)
            {
                var nxt = cur + dir;
                if (!InBounds(nxt)) continue;

                int tentative = gScore[cur] + Cost(nxt);
                if (!gScore.TryGetValue(nxt, out int prev) || tentative < prev)
                {
                    came[nxt] = cur;
                    gScore[nxt] = tentative;
                    int f = tentative + Heuristic(nxt, g);
                    open.Enqueue(nxt, f);
                }
            }
        }
        return Array.Empty<Vector2Int>();

        IEnumerable<Vector2Int> Reconstruct(Vector2Int c)
        {
            var path = new List<Vector2Int> { c };
            while (came.TryGetValue(c, out var p)) { c = p; path.Add(c); }
            path.Reverse();
            return path;
        }

        int Cost(Vector2Int p) => map[p.x, p.y].type == CellType.Room ? 2 : 1;
    }

    int Heuristic(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    bool InBounds(Vector2Int p) => p.x > 0 && p.x < w - 1 && p.y > 0 && p.y < h - 1;

    void PaintCorridor(Vector2Int p)
    {
        int r = corridorWidth / 2;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                int x = p.x + dx, y = p.y + dy;
                if (!InBounds(new Vector2Int(x, y))) continue;
                if (map[x, y].type == CellType.Empty)
                    map[x, y] = new Cell { type = CellType.Corridor, roomId = -1 };
            }
    }

    // -------------------------------------------------------------
    // 5. プレハブ配置
    // -------------------------------------------------------------
    void InstantiatePrefabs()
    {
        // 既存の子オブジェクトを削除
        foreach (Transform c in transform) Destroy(c.gameObject);

        // 壁は "Room/Corridor" と接している Empty セルだけに置く
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                CellType t = map[x, y].type;
                if (t == CellType.Room || t == CellType.Corridor)
                {
                    Instantiate(floor, new Vector3(x, 0f, y), Quaternion.identity, transform);
                }
                else // Empty
                {
                    if (IsBorder(x, y))
                    {
                        // 周囲に床がある境界セルだけ壁を設置
                        Instantiate(wall, new Vector3(x, 0.5f, y), Quaternion.identity, transform);
                    }
                }
            }
    }

    bool IsBorder(int x, int y)
    {
        // orthogonal floor detection for cleaner straight walls
        bool left = IsFloor(x - 1, y);
        bool right = IsFloor(x + 1, y);
        bool up = IsFloor(x, y + 1);
        bool down = IsFloor(x, y - 1);

        bool orthAdjacent = left || right || up || down;
        bool corner = (left || right) && (up || down); // diagonally touching both axes

        return orthAdjacent && !corner; // place wall only if straight border, skip corners
    }

    bool IsFloor(int x, int y)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return false;
        var t = map[x, y].type;
        return t == CellType.Room || t == CellType.Corridor;
    }

    // =============================================================
    // ヘルパークラス
    // =============================================================

    // ---------- PriorityQueue (min‑heap) ----------
    class PriorityQueue<T>
    {
        readonly List<(T item, int prio)> heap = new();
        public int Count => heap.Count;
        public void Enqueue(T item, int prio)
        {
            heap.Add((item, prio));
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].prio <= heap[i].prio) break;
                (heap[parent], heap[i]) = (heap[i], heap[parent]);
                i = parent;
            }
        }
        public T Dequeue()
        {
            var root = heap[0].item;
            heap[0] = heap[^1];
            heap.RemoveAt(heap.Count - 1);
            Heapify(0);
            return root;
        }
        void Heapify(int i)
        {
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < heap.Count && heap[l].prio < heap[smallest].prio) smallest = l;
                if (r < heap.Count && heap[r].prio < heap[smallest].prio) smallest = r;
                if (smallest == i) break;
                (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
                i = smallest;
            }
        }
    }

    // ---------- Bowyer–Watson 2D (簡易) ----------
    public static class BowyerWatson2D
    {
        public struct Tri { public int i0, i1, i2; public Tri(int a, int b, int c) { i0 = a; i1 = b; i2 = c; } }

        public static List<Tri> Triangulate(List<Vector2> pts)
        {
            // スーパー三角形を追加
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x);
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            float d = Mathf.Max(maxX - minX, maxY - minY) * 20f;
            Vector2 p1 = new(minX - d, minY - 1);
            Vector2 p2 = new(maxX + d, minY - 1);
            Vector2 p3 = new((minX + maxX) / 2f, maxY + d);
            pts.AddRange(new[] { p1, p2, p3 });
            int si1 = pts.Count - 3, si2 = pts.Count - 2, si3 = pts.Count - 1;

            var tris = new List<Tri> { new(si1, si2, si3) };

            for (int i = 0; i < pts.Count - 3; i++)
            {
                var bad = new List<Tri>();
                var poly = new List<(int, int)>();
                foreach (var t in tris)
                {
                    if (InCircumcircle(pts[i], pts[t.i0], pts[t.i1], pts[t.i2]))
                    {
                        bad.Add(t);
                        poly.Add((t.i0, t.i1));
                        poly.Add((t.i1, t.i2));
                        poly.Add((t.i2, t.i0));
                    }
                }
                tris = tris.Except(bad).ToList();

                poly = RemoveDuplicateEdges(poly);
                foreach (var (a, b) in poly)
                    tris.Add(new Tri(a, b, i));
            }

            // スーパー三角形関連三角形を除外
            tris = tris.Where(t => t.i0 < si1 && t.i1 < si1 && t.i2 < si1).ToList();
            pts.RemoveRange(pts.Count - 3, 3);
            return tris;
        }

        static bool InCircumcircle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ax = a.x - p.x, ay = a.y - p.y;
            float bx = b.x - p.x, by = b.y - p.y;
            float cx = c.x - p.x, cy = c.y - p.y;
            float det = (ax * ax + ay * ay) * (bx * cy - by * cx) -
                        (bx * bx + by * by) * (ax * cy - ay * cx) +
                        (cx * cx + cy * cy) * (ax * by - ay * bx);
            return det > 0f; // 正のとき点 p は外接円内
        }

        static List<(int, int)> RemoveDuplicateEdges(List<(int, int)> edges)
        {
            var uniq = new List<(int, int)>();
            foreach (var e in edges)
            {
                var rev = (e.Item2, e.Item1);
                if (uniq.Contains(rev)) uniq.Remove(rev);
                else uniq.Add(e);
            }
            return uniq;
        }
    }
}
