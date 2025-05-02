using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using System.Linq;

namespace DungeonGen.Generation
{
    /// <summary>
    /// グラフを A* で結び、通路セル（Empty → Corridor）を掘り起こす。
    /// Room セルを極力通らず外壁を残すことで、部屋の結合を防ぐ。
    /// </summary>
    public class CorridorCarver
    {
        readonly CellMap map;
        readonly Vector2Int[] dirs =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        public CorridorCarver(CellMap m) => map = m;

        // ───────────────────────────────────
        // 外部 API
        // ───────────────────────────────────
        public void Carve(IEnumerable<GraphGenerator.Edge> graph,
                  IReadOnlyList<RectInt> rooms,
                  int corridorWidth)
        {
            foreach (var e in graph)
            {
                Vector2Int start = Vector2Int.RoundToInt(rooms[e.A].center);
                Vector2Int goal = Vector2Int.RoundToInt(rooms[e.B].center);

                // A*で経路を探索
                var path = AStar(start, goal).ToList();

                // パスが空の場合（経路が見つからない場合）、直線パスを試みる
                if (path.Count == 0)
                {
                    Debug.Log($"部屋{e.A}から部屋{e.B}への経路が見つかりませんでした。直線経路を使用します。");
                    // 簡易的な直線経路を生成
                    path = CreateDirectPath(start, goal);
                }

                // 経路に沿って通路を掘る
                foreach (var p in path)
                    Paint(p, corridorWidth);
            }
        }

        // 直線経路を生成するヘルパーメソッド
        private List<Vector2Int> CreateDirectPath(Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            Vector2Int current = start;

            // X方向に移動
            while (current.x != end.x)
            {
                current.x += (current.x < end.x) ? 1 : -1;
                path.Add(current);
            }

            // Y方向に移動
            while (current.y != end.y)
            {
                current.y += (current.y < end.y) ? 1 : -1;
                path.Add(current);
            }

            return path;
        }

        // ───────────────────────────────────
        // A* 最短経路
        // ───────────────────────────────────
        IEnumerable<Vector2Int> AStar(Vector2Int s, Vector2Int t)
        {
            var open = new PriorityQueue<Vector2Int>();     // 毎回新品
            open.Enqueue(s, 0);

            var came = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [s] = 0 };

            while (open.Count > 0)
            {
                var cur = open.Dequeue();
                if (!gScore.TryGetValue(cur, out int gCur))
                    continue;                                // 旧エントリ

                if (cur == t)
                    return Reconstruct(cur, came);

                foreach (var d in dirs)
                {
                    var nxt = cur + d;
                    if (!map.InBounds(nxt)) continue;

                    var curCell = map.Cells[cur.x, cur.y];
                    var nxtCell = map.Cells[nxt.x, nxt.y];

                    bool curIsRoom = curCell.Type == CellType.Room;
                    bool nxtIsRoom = nxtCell.Type == CellType.Room;
                    bool sameRoom = curIsRoom && nxtIsRoom &&
                                     curCell.RoomId == nxtCell.RoomId;

                    /* ───── ここがポイント ─────
                       ・現在と同じ部屋なら OK (部屋中心→外壁へ移動可)
                       ・ゴール部屋も OK (最後に入る)
                       ・それ以外の部屋には入らない
                    */
                    if (nxtIsRoom && !sameRoom && nxt != t)
                        continue;

                    int stepCost = nxtIsRoom ? 5 : 1;      // 室内は高コスト
                    int tentative = gCur + stepCost;

                    if (!gScore.TryGetValue(nxt, out int prev) || tentative < prev)
                    {
                        came[nxt] = cur;
                        gScore[nxt] = tentative;
                        open.Enqueue(nxt, tentative + Heu(nxt, t));
                    }
                }

            }
            return new List<Vector2Int>();                   // ゴール不可（稀）
        }

        static int Heu(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        static IEnumerable<Vector2Int> Reconstruct(
            Vector2Int node,
            Dictionary<Vector2Int, Vector2Int> came)
        {
            var path = new List<Vector2Int> { node };
            while (came.TryGetValue(node, out var prev))
            {
                node = prev;
                path.Add(node);
            }
            path.Reverse();
            return path;
        }

        // corridorWidth に応じて床を掘る
        void Paint(Vector2Int p, int width)
        {
            int r = width / 2;
            for (int dx = -r; dx <= r; ++dx)
                for (int dy = -r; dy <= r; ++dy)
                {
                    int x = p.x + dx, y = p.y + dy;
                    if (!map.InBounds(x, y)) continue;

                    if (map.Cells[x, y].Type == CellType.Empty || HasRoomNeighbor(x, y))
                        map.Cells[x, y] = new Cell
                        { Type = CellType.Corridor, RoomId = -1 };

                }
        }

        private bool HasRoomNeighbor(int x, int y)
        {
            if (map.Cells[x, y].Type != CellType.Empty) return false;

            foreach (var d in dirs)
            {
                int nx = x + d.x, ny = y + d.y;
                if (map.InBounds(nx, ny) && map.Cells[nx, ny].Type == CellType.Room)
                    return true;
            }
            return false;
        }
    }
}
