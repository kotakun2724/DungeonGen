using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using System.Linq;
using DungeonGen.Generation;

namespace DungeonGen.Generation
{
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

        // 斜め方向も含む全8方向
        readonly Vector2Int[] allDirs =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left,
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1)
        };

        // ノイズマップとRNG
        private int[,] noiseMap;
        private System.Random rng;

        public CorridorCarver(CellMap map)
        {
            this.map = map;
            this.rng = new System.Random();
            GenerateNoiseMap();
        }

        private void GenerateNoiseMap()
        {
            noiseMap = new int[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    noiseMap[x, y] = rng.Next(1, 5);
        }

        public void Carve(IEnumerable<GraphGenerator.Edge> graph,
                  IReadOnlyList<RectInt> rooms,
                  int corridorWidth)
        {
            Debug.Log($"通路の掘削を開始 - 部屋数: {rooms.Count}");
            List<(Vector2Int start, Vector2Int end, bool success)> connectionResults = new List<(Vector2Int, Vector2Int, bool)>();

            // グラフ接続を処理
            foreach (var e in graph)
            {
                try
                {
                    // 部屋の中心点
                    Vector2Int start = Vector2Int.RoundToInt(rooms[e.A].center);
                    Vector2Int goal = Vector2Int.RoundToInt(rooms[e.B].center);

                    // 既に同じ部屋の場合はスキップ
                    if (map.InBounds(start.x, start.y) && map.InBounds(goal.x, goal.y) &&
                        map.Cells[start.x, start.y].RoomId == map.Cells[goal.x, goal.y].RoomId)
                    {
                        Debug.Log($"部屋{e.A}と部屋{e.B}は同じRoomIdなのでスキップします");
                        connectionResults.Add((start, goal, true)); // 同じ部屋なので接続済みとみなす
                        continue;
                    }

                    // 部屋の入口/出口ポイントを取得（中心ではなく境界上の点）
                    Vector2Int entryStart = GetRoomEntryPoint(rooms[e.A], goal);
                    Vector2Int entryGoal = GetRoomEntryPoint(rooms[e.B], start);

                    // まずA*で経路探索
                    var path = AStarNatural(entryStart, entryGoal);
                    bool success = path.Count > 0;

                    if (!success)
                    {
                        // 最大3回まで異なるエントリポイントでリトライ
                        for (int attempt = 0; attempt < 3 && !success; attempt++)
                        {
                            // 異なる入口/出口点を試す
                            entryStart = GetRoomEntryPoint(rooms[e.A], goal, attempt + 1);
                            entryGoal = GetRoomEntryPoint(rooms[e.B], start, attempt + 1);
                            path = AStarNatural(entryStart, entryGoal);
                            success = path.Count > 0;
                        }

                        // それでも失敗した場合はフォールバック
                        if (!success)
                        {
                            Debug.LogWarning($"部屋{e.A}から部屋{e.B}への経路が見つかりませんでした。強制接続を使用します。");
                            path = CreateRobustFallbackPath(entryStart, entryGoal);
                            success = true; // 強制的な接続は常に成功とみなす
                        }
                    }

                    // 通路を掘り、結果を記録
                    foreach (var p in path)
                        Paint(p, corridorWidth);

                    connectionResults.Add((start, goal, success));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"通路生成中にエラーが発生しました: {ex.Message}");
                }
            }

            // 接続結果のログ
            int successCount = connectionResults.Count(r => r.success);
            Debug.Log($"通路生成完了: {successCount}/{connectionResults.Count}の接続に成功");

            // すべての部屋の接続性を確認 - 最も重要な部分
            EnhancedConnectivityCheck(rooms);

            // 不要な通路を削除
            // RemoveDeadEndCorridors();
        }

        // 部屋の入り口ポイントを取得
        private Vector2Int GetRoomEntryPoint(RectInt room, Vector2Int target, int alternativeIndex = 0)
        {
            Vector2Int roomCenter = Vector2Int.RoundToInt(room.center);
            Vector2Int direction = target - roomCenter;

            // 方向の主要成分を決定（X軸かY軸のどちらが大きいか）
            bool useXAxis = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);

            int x, y;

            if (alternativeIndex == 0) // デフォルトエントリーポイント
            {
                if (useXAxis)
                {
                    // X軸方向に出る
                    x = direction.x > 0 ? room.xMax - 1 : room.xMin;
                    // Y軸はランダムではなく中央に近い位置
                    y = roomCenter.y;
                }
                else
                {
                    // Y軸方向に出る
                    y = direction.y > 0 ? room.yMax - 1 : room.yMin;
                    // X軸は中央に近い位置
                    x = roomCenter.x;
                }
            }
            else
            {
                // 代替エントリーポイント - 異なる場所を試す
                int offset = alternativeIndex;

                if (useXAxis)
                {
                    x = direction.x > 0 ? room.xMax - 1 : room.xMin;
                    y = Mathf.Clamp(roomCenter.y + (offset % 2 == 0 ? offset : -offset), room.yMin, room.yMax - 1);
                }
                else
                {
                    y = direction.y > 0 ? room.yMax - 1 : room.yMin;
                    x = Mathf.Clamp(roomCenter.x + (offset % 2 == 0 ? offset : -offset), room.xMin, room.xMax - 1);
                }
            }

            return new Vector2Int(x, y);
        }

        // A*アルゴリズムによる経路探索（自然な通路生成のためノイズマップも考慮）
        private List<Vector2Int> AStarNatural(Vector2Int start, Vector2Int goal)
        {
            var result = new List<Vector2Int>();
            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
                return result;

            // 既に同じ部屋にいる場合は経路不要
            if (map.Cells[start.x, start.y].RoomId >= 0 &&
                map.Cells[start.x, start.y].RoomId == map.Cells[goal.x, goal.y].RoomId)
                return result;

            // A*データ構造
            var openSet = new List<Vector2Int>();
            var closedSet = new HashSet<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float>();
            var fScore = new Dictionary<Vector2Int, float>();

            // 初期化
            openSet.Add(start);
            gScore[start] = 0;
            fScore[start] = HeuristicCost(start, goal);

            while (openSet.Count > 0)
            {
                // fスコアが最小のノードを取得
                Vector2Int current = openSet.OrderBy(p => fScore.GetValueOrDefault(p, float.MaxValue)).First();

                // 目標に到達
                if (current == goal)
                {
                    return ReconstructPath(cameFrom, current);
                }

                openSet.Remove(current);
                closedSet.Add(current);

                // 隣接するノードをチェック
                foreach (var dir in dirs)
                {
                    Vector2Int neighbor = current + dir;

                    //範囲外か訪問済みならスキップ
                    if (!map.InBounds(neighbor.x, neighbor.y) || closedSet.Contains(neighbor))
                        continue;

                    // 通路探索では空きスペース(Empty)も通過可能にする（サイクル形成のため）
                    // ただし、目標点が部屋内なら、部屋にも入れる
                    if (neighbor != goal && map.Cells[neighbor.x, neighbor.y].Type == CellType.Empty)
                    {
                        // 現在地が既に通路または部屋なら、確率的にEmptyセルへ進むことを許可
                        // これにより新しい通路を掘る可能性が高まる
                        bool canMoveToEmpty = map.Cells[current.x, current.y].Type != CellType.Empty &&
                                             Random.Range(0f, 1f) < 0.7f; // 70%の確率で許可

                        if (!canMoveToEmpty)
                        {
                            closedSet.Add(neighbor);
                            continue;
                        }
                    }

                    // 現在位置からのコスト（ノイズマップを考慮して自然な曲がりを出す）
                    float moveCost = 1.0f;
                    if (map.InBounds(neighbor.x, neighbor.y))
                    {
                        // 通路を掘るコストを地形に応じて調整
                        if (map.Cells[neighbor.x, neighbor.y].Type == CellType.Empty)
                        {
                            // ノイズマップを使って自然な曲がりを生成
                            moveCost += noiseMap[neighbor.x, neighbor.y] * 0.5f;
                        }
                        else if (map.Cells[neighbor.x, neighbor.y].Type == CellType.Corridor)
                        {
                            moveCost += 8.0f; // 通路は少しコストが高い
                        }
                    }

                    float tentativeGScore = gScore[current] + moveCost;

                    // 新しい経路が見つかった場合、または更新が必要な場合
                    if (!openSet.Contains(neighbor) || tentativeGScore < gScore.GetValueOrDefault(neighbor, float.MaxValue))
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = tentativeGScore + HeuristicCost(neighbor, goal);

                        if (!openSet.Contains(neighbor))
                            openSet.Add(neighbor);
                    }
                }
            }

            // 経路が見つからなかった場合は空リストを返す
            return result;
        }

        // ヒューリスティックコスト（マンハッタン距離）
        private float HeuristicCost(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        // 経路再構築
        private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
        {
            var path = new List<Vector2Int>();

            // 逆順で経路を構築
            while (cameFrom.ContainsKey(current))
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Add(current); // スタート地点
            path.Reverse(); // 正しい順序に戻す

            return path;
        }

        // マップにセルを描画（通路を掘る）
        private void Paint(Vector2Int pos, int width)
        {
            if (!map.InBounds(pos.x, pos.y))
                return;

            // 既に部屋ならそのまま（部屋は保持）
            if (map.Cells[pos.x, pos.y].Type == CellType.Room)
                return;

            // 中心点を通路にする
            map.Cells[pos.x, pos.y].Type = CellType.Corridor;

            // 指定された幅で通路を拡張
            if (width > 1)
            {
                int radius = width / 2;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (dx == 0 && dy == 0) continue; // 中心点は既に設定済み

                        int nx = pos.x + dx;
                        int ny = pos.y + dy;

                        if (map.InBounds(nx, ny) && map.Cells[nx, ny].Type != CellType.Room)
                        {
                            map.Cells[nx, ny].Type = CellType.Corridor;
                        }
                    }
                }
            }
        }

        // 強化版接続性チェックと修正
        private void EnhancedConnectivityCheck(IReadOnlyList<RectInt> rooms)
        {
            Debug.Log("【重要】強化版接続性チェック開始...");

            // 部屋IDのマッピングを作成（部屋インデックス → RoomId）
            var roomIdMapping = new Dictionary<int, int>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Vector2Int center = Vector2Int.RoundToInt(rooms[i].center);
                if (map.InBounds(center.x, center.y))
                {
                    int id = map.Cells[center.x, center.y].RoomId;
                    roomIdMapping[i] = id;
                }
            }

            // 連結性を解析（Union-Find方式）
            var forest = new DisjointSets(rooms.Count);

            // 各部屋ペアの接続を確認
            for (int i = 0; i < rooms.Count; i++)
            {
                Vector2Int startCenter = Vector2Int.RoundToInt(rooms[i].center);

                for (int j = i + 1; j < rooms.Count; j++)
                {
                    Vector2Int goalCenter = Vector2Int.RoundToInt(rooms[j].center);

                    // 実際に歩けるかどうかチェック（BFS）
                    if (HasAccessiblePath(startCenter, goalCenter))
                    {
                        forest.Union(i, j);
                    }
                }
            }

            // 連結成分の数を取得
            var componentIds = new HashSet<int>();
            for (int i = 0; i < rooms.Count; i++)
            {
                componentIds.Add(forest.Find(i));
            }

            Debug.Log($"連結成分数: {componentIds.Count} / 部屋数: {rooms.Count}");

            // 複数の連結成分がある場合は強制的に接続
            if (componentIds.Count > 1)
            {
                Debug.LogWarning($"{componentIds.Count}個の孤立グループを検出 - 強制接続を実行します");
                var components = new Dictionary<int, List<int>>();

                // 部屋を連結成分ごとにグループ化
                for (int i = 0; i < rooms.Count; i++)
                {
                    int compId = forest.Find(i);
                    if (!components.ContainsKey(compId))
                    {
                        components[compId] = new List<int>();
                    }
                    components[compId].Add(i);
                }

                // 最大の連結成分を特定
                var mainComp = components
                    .OrderByDescending(c => c.Value.Count)
                    .First().Key;

                // 各孤立グループをメイングループに接続
                foreach (var component in components)
                {
                    if (component.Key != mainComp)
                    {
                        ConnectIsolatedComponent(component.Value, components[mainComp], rooms);
                    }
                }

                // 接続後の連結性を再確認
                Debug.Log("孤立グループ接続後の連結性を再確認しています...");
                bool fullyConnected = VerifyFullConnectivity(rooms);

                if (!fullyConnected)
                {
                    Debug.LogError("最終的な連結性確認に失敗 - 緊急対応を実行");
                    EmergencyConnector(rooms);
                }
            }
            else
            {
                Debug.Log("全ての部屋が適切に接続されています");
            }
        }

        // 孤立コンポーネントをメインコンポーネントに接続
        private void ConnectIsolatedComponent(List<int> isolatedRooms, List<int> mainRooms, IReadOnlyList<RectInt> rooms)
        {
            Debug.Log($"孤立グループ（{isolatedRooms.Count}部屋）をメイングループ（{mainRooms.Count}部屋）に接続します");

            // 全ての可能な部屋ペアの距離を計算して最短のものを選ぶ
            float bestDist = float.MaxValue;
            int bestIsolated = -1;
            int bestMain = -1;

            foreach (int isolated in isolatedRooms)
            {
                foreach (int main in mainRooms)
                {
                    float dist = Vector2.Distance(rooms[isolated].center, rooms[main].center);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIsolated = isolated;
                        bestMain = main;
                    }
                }
            }

            // 最も近い部屋ペアが見つかった場合、接続通路を作成
            if (bestIsolated != -1 && bestMain != -1)
            {
                Debug.Log($"最短距離ペア: 部屋{bestIsolated} ↔ 部屋{bestMain}, 距離: {bestDist:F1}");

                // 部屋境界の入口点を取得
                Vector2Int startRoom = Vector2Int.RoundToInt(rooms[bestIsolated].center);
                Vector2Int endRoom = Vector2Int.RoundToInt(rooms[bestMain].center);

                Vector2Int entryStart = GetRoomEntryPoint(rooms[bestIsolated], endRoom);
                Vector2Int entryEnd = GetRoomEntryPoint(rooms[bestMain], startRoom);

                // 太い幅2の通路を掘る（確実に接続するため）
                var path = CreateDirectFallbackPath(entryStart, entryEnd);
                foreach (var p in path)
                {
                    if (map.InBounds(p.x, p.y))
                    {
                        // 既存の部屋は保持
                        if (map.Cells[p.x, p.y].Type != CellType.Room)
                        {
                            map.Cells[p.x, p.y].Type = CellType.Corridor;
                        }
                    }
                }

                // 複数箇所で接続（冗長性確保）
                if (isolatedRooms.Count > 1 && mainRooms.Count > 1)
                {
                    // 異なる第2の接続点を見つける
                    float secondBestDist = float.MaxValue;
                    int secondIsolated = -1;
                    int secondMain = -1;

                    foreach (int isolated in isolatedRooms)
                    {
                        if (isolated == bestIsolated) continue; // 既に接続した部屋をスキップ

                        foreach (int main in mainRooms)
                        {
                            if (main == bestMain) continue; // 既に接続した部屋をスキップ

                            float dist = Vector2.Distance(rooms[isolated].center, rooms[main].center);
                            if (dist < secondBestDist)
                            {
                                secondBestDist = dist;
                                secondIsolated = isolated;
                                secondMain = main;
                            }
                        }
                    }

                    // 第2の接続点が見つかった場合
                    if (secondIsolated != -1 && secondMain != -1)
                    {
                        Debug.Log($"冗長接続: 部屋{secondIsolated} ↔ 部屋{secondMain}, 距離: {secondBestDist:F1}");

                        startRoom = Vector2Int.RoundToInt(rooms[secondIsolated].center);
                        endRoom = Vector2Int.RoundToInt(rooms[secondMain].center);

                        entryStart = GetRoomEntryPoint(rooms[secondIsolated], endRoom);
                        entryEnd = GetRoomEntryPoint(rooms[secondMain], startRoom);

                        // 確実に接続するために直線接続
                        path = CreateDirectFallbackPath(entryStart, entryEnd);
                        foreach (var p in path)
                        {
                            if (map.InBounds(p.x, p.y))
                            {
                                if (map.Cells[p.x, p.y].Type != CellType.Room)
                                {
                                    map.Cells[p.x, p.y].Type = CellType.Corridor;
                                }
                            }
                        }
                    }
                }
            }
        }

        // 緊急接続処理 - 最後の手段として全ての部屋を強制的に接続
        private void EmergencyConnector(IReadOnlyList<RectInt> rooms)
        {
            Debug.LogWarning("緊急接続処理を実行中...");

            // 第1の部屋を基点として各部屋に直接接続
            if (rooms.Count < 2) return;

            int baseRoom = 0;
            Vector2Int baseCenter = Vector2Int.RoundToInt(rooms[baseRoom].center);

            for (int i = 1; i < rooms.Count; i++)
            {
                Vector2Int targetCenter = Vector2Int.RoundToInt(rooms[i].center);

                // 既に接続されている場合はスキップ
                if (HasAccessiblePath(baseCenter, targetCenter))
                {
                    continue;
                }

                Debug.Log($"緊急接続: 部屋0 → 部屋{i}");

                Vector2Int entryBase = GetRoomEntryPoint(rooms[baseRoom], targetCenter);
                Vector2Int entryTarget = GetRoomEntryPoint(rooms[i], baseCenter);

                // 単純な直線で接続（最も信頼性の高い方法）
                var path = CreateStraightLineConnection(entryBase, entryTarget);
                foreach (var p in path)
                {
                    if (map.InBounds(p.x, p.y) && map.Cells[p.x, p.y].Type != CellType.Room)
                    {
                        map.Cells[p.x, p.y].Type = CellType.Corridor;
                    }
                }
            }
        }

        // 頑健なフォールバック経路を作成
        private List<Vector2Int> CreateRobustFallbackPath(Vector2Int start, Vector2Int end)
        {
            // ジグザグに進む経路を作成（単純な直線よりも見栄えが良い）
            var path = new List<Vector2Int>();
            Vector2Int current = start;
            path.Add(current);

            // X方向とY方向に交互に進む
            bool moveX = true;
            while (current != end)
            {
                if (moveX && current.x != end.x)
                {
                    current.x += (current.x < end.x) ? 1 : -1;
                }
                else if (!moveX && current.y != end.y)
                {
                    current.y += (current.y < end.y) ? 1 : -1;
                }
                else
                {
                    // 一方向の移動が終わったら切り替え
                    moveX = !moveX;
                    continue;
                }

                path.Add(current);

                // 確率的に方向転換
                if (Random.Range(0, 4) == 0)
                {
                    moveX = !moveX;
                }
            }

            return path;
        }

        // 非常に単純な直線接続（完全な信頼性が必要な場合用）
        private List<Vector2Int> CreateStraightLineConnection(Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            path.Add(start);

            // X軸に沿って移動
            Vector2Int current = start;
            while (current.x != end.x)
            {
                current.x += (current.x < end.x) ? 1 : -1;
                path.Add(current);
            }

            // Y軸に沿って移動
            while (current.y != end.y)
            {
                current.y += (current.y < end.y) ? 1 : -1;
                path.Add(current);
            }

            return path;
        }

        // 直接的でシンプルなフォールバックパス（接続の信頼性確保用）
        private List<Vector2Int> CreateDirectFallbackPath(Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            Vector2Int current = start;
            path.Add(current);

            // X方向に移動してからY方向に移動（単純なL字型経路）
            while (current.x != end.x)
            {
                current.x += (current.x < end.x) ? 1 : -1;
                path.Add(current);
            }

            while (current.y != end.y)
            {
                current.y += (current.y < end.y) ? 1 : -1;
                path.Add(current);
            }

            return path;
        }

        // 実際に歩けるパスがあるかどうかをBFSで確認
        private bool HasAccessiblePath(Vector2Int start, Vector2Int goal)
        {
            // マップ範囲外チェック
            if (!map.InBounds(start.x, start.y) || !map.InBounds(goal.x, goal.y))
                return false;

            // 同じ部屋IDなら既に接続されている
            if (map.Cells[start.x, start.y].RoomId == map.Cells[goal.x, goal.y].RoomId)
                return true;

            // BFSで検索
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // 目標に到達
                if (current == goal)
                    return true;

                // 4方向を探索（通路探索なので斜めは無視）
                foreach (var dir in dirs)
                {
                    Vector2Int next = current + dir;

                    // 範囲外または訪問済みはスキップ
                    if (!map.InBounds(next.x, next.y) || visited.Contains(next))
                        continue;

                    // 通過可能なマスのみ（通路または部屋）
                    var cell = map.Cells[next.x, next.y];
                    if (cell.Type == CellType.Empty)
                        continue;

                    queue.Enqueue(next);
                    visited.Add(next);
                }
            }

            return false;
        }

        // 全ての部屋が接続されているか再確認
        private bool VerifyFullConnectivity(IReadOnlyList<RectInt> rooms)
        {
            if (rooms.Count <= 1) return true;

            // 最初の部屋から他のすべての部屋に到達できるかチェック
            Vector2Int firstRoom = Vector2Int.RoundToInt(rooms[0].center);

            for (int i = 1; i < rooms.Count; i++)
            {
                Vector2Int otherRoom = Vector2Int.RoundToInt(rooms[i].center);
                if (!HasAccessiblePath(firstRoom, otherRoom))
                {
                    Debug.LogError($"部屋0から部屋{i}への接続がありません");
                    return false;
                }
            }

            return true;
        }

        // 行き止まりの通路を削除する
        private void RemoveDeadEndCorridors()
        {
            // サイクルを形成している通路を保護するために接続情報を記録
            var corridorConnections = new Dictionary<Vector2Int, int>();

            // まず全ての通路の接続数を計算
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (map.Cells[x, y].Type != CellType.Corridor) continue;

                    Vector2Int pos = new Vector2Int(x, y);
                    int connections = CountConnections(pos);
                    corridorConnections[pos] = connections;
                }
            }

            // サイクル検出（接続が2以上の通路はサイクルの一部である可能性が高い）
            var potentialCycles = corridorConnections.Where(kv => kv.Value >= 2).Select(kv => kv.Key).ToList();
            Debug.Log($"サイクル保護対象セル数: {potentialCycles.Count}");

            // サイクル内の通路セルを記録
            var protectedCells = new HashSet<Vector2Int>(potentialCycles);

            // 部屋に隣接しているセルを保護（これらも残す必要がある）
            var cellsAdjacentToRooms = new HashSet<Vector2Int>();
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (map.Cells[x, y].Type != CellType.Corridor) continue;

                    Vector2Int pos = new Vector2Int(x, y);
                    if (IsAdjacentToRoom(pos))
                    {
                        cellsAdjacentToRooms.Add(pos);
                    }
                }
            }

            // 行き止まり除去処理（サイクルと部屋隣接セルは保護）
            bool removed;
            int iteration = 0;
            int totalRemoved = 0;

            do
            {
                removed = false;
                iteration++;

                for (int x = 1; x < map.Width - 1; x++)
                {
                    for (int y = 1; y < map.Height - 1; y++)
                    {
                        Vector2Int pos = new Vector2Int(x, y);

                        // 通路でない、またはサイクルの一部、または部屋に隣接している場合はスキップ
                        if (map.Cells[x, y].Type != CellType.Corridor ||
                            protectedCells.Contains(pos) ||
                            cellsAdjacentToRooms.Contains(pos)) continue;

                        // 接続方向をカウント
                        int connections = CountConnections(pos);

                        // 接続が1つだけなら行き止まり
                        if (connections <= 1)
                        {
                            map.Cells[x, y].Type = CellType.Empty;
                            removed = true;
                            totalRemoved++;
                        }
                    }
                }

            } while (removed && iteration < 20); // 反復回数を減らしてサイクル保護

            Debug.Log($"行き止まり通路除去: {totalRemoved}個のセルを削除 ({iteration}回反復)");
        }
        // 接続数をカウント
        private int CountConnections(Vector2Int pos)
        {
            int connections = 0;
            foreach (var dir in dirs)
            {
                int nx = pos.x + dir.x;
                int ny = pos.y + dir.y;

                if (map.InBounds(nx, ny) && map.Cells[nx, ny].Type != CellType.Empty)
                {
                    connections++;
                }
            }
            return connections;
        }

        // 部屋隣接チェックメソッド
        private bool IsAdjacentToRoom(Vector2Int pos)
        {
            foreach (var dir in dirs)
            {
                int nx = pos.x + dir.x;
                int ny = pos.y + dir.y;

                if (map.InBounds(nx, ny) && map.Cells[nx, ny].Type == CellType.Room)
                {
                    return true;
                }
            }
            return false;
        }


    }

    // Union-Find構造（連結成分の効率的管理用）
    public class DisjointSets
    {
        private int[] parent;
        private int[] rank;

        public DisjointSets(int size)
        {
            parent = new int[size];
            rank = new int[size];

            for (int i = 0; i < size; i++)
            {
                parent[i] = i;
                rank[i] = 0;
            }
        }

        public int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        public void Union(int x, int y)
        {
            int xRoot = Find(x);
            int yRoot = Find(y);

            if (xRoot == yRoot)
                return;

            if (rank[xRoot] < rank[yRoot])
                parent[xRoot] = yRoot;
            else if (rank[xRoot] > rank[yRoot])
                parent[yRoot] = xRoot;
            else
            {
                parent[yRoot] = xRoot;
                rank[xRoot]++;
            }
        }
    }
}