using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using DungeonGen.Utils;
using System.Linq;

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
            List<(Vector2Int start, Vector2Int end, bool success)> connectionResults = new List<(Vector2Int, Vector2Int, bool)>();

            foreach (var e in graph)
            {
                // 部屋の中心点
                Vector2Int start = Vector2Int.RoundToInt(rooms[e.A].center);
                Vector2Int goal = Vector2Int.RoundToInt(rooms[e.B].center);

                // 既に同じ部屋の場合はスキップ
                if (map.Cells[start.x, start.y].RoomId == map.Cells[goal.x, goal.y].RoomId)
                {
                    Debug.Log($"部屋{e.A}と部屋{e.B}は同じRoomIdなのでスキップします");
                    connectionResults.Add((start, goal, true)); // 同じ部屋なので接続済みとみなす
                    continue;
                }

                // 部屋の入口/出口ポイントを取得（中心ではなく境界上の点）
                Vector2Int entryStart = GetRoomEntryPoint(rooms[e.A], goal);
                Vector2Int entryGoal = GetRoomEntryPoint(rooms[e.B], start);

                // まずA*で経路探索
                var path = AStarNatural(entryStart, entryGoal).ToList();
                bool success = path.Count > 0;

                if (!success)
                {
                    // 最大3回まで異なるエントリポイントでリトライ
                    for (int attempt = 0; attempt < 3 && !success; attempt++)
                    {
                        // 異なる入口/出口点を試す
                        entryStart = GetRoomEntryPoint(rooms[e.A], goal, attempt);
                        entryGoal = GetRoomEntryPoint(rooms[e.B], start, attempt);
                        path = AStarNatural(entryStart, entryGoal).ToList();
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

            // 接続結果のログ
            int successCount = connectionResults.Count(r => r.success);
            Debug.Log($"通路生成完了: {successCount}/{connectionResults.Count}の接続に成功");

            // すべての部屋の接続性を確認
            VerifyAndFixConnectivity(rooms);

            RemoveDeadEndCorridors();
        }

        private void RemoveDeadEndCorridors()
        {
            bool removedAny;
            int iterationCount = 0;
            int totalRemoved = 0;

            do
            {
                removedAny = false;
                iterationCount++;
                int removedInThisIteration = 0;

                for (int x = 0; x < map.Width; x++)
                {
                    for (int y = 0; y < map.Height; y++)
                    {
                        if (map.Cells[x, y].Type == CellType.Corridor)
                        {
                            int corridorCount = 0;
                            int roomCount = 0;
                            foreach (var d in dirs)
                            {
                                int nx = x + d.x;
                                int ny = y + d.y;
                                if (!map.InBounds(nx, ny)) continue;

                                var cell = map.Cells[nx, ny];
                                if (cell.Type == CellType.Corridor)
                                {
                                    corridorCount++;
                                }
                                else if (cell.Type == CellType.Room)
                                {
                                    roomCount++;
                                }
                            }

                            bool shouldRemove = false;
                            // ケース1: 完全な行き止まり（接続1つ以下）で部屋に隣接していない
                            if (corridorCount <= 1 && roomCount == 0)
                            {
                                shouldRemove = true;
                            }
                            // ケース2: 部屋に隣接していて、通路接続が0 （完全孤立した1マス通路）
                            else if (roomCount > 0 && corridorCount == 0)
                            {
                                shouldRemove = true;
                            }
                            // ケース3: 複数の部屋に囲まれた短い通路は不要
                            else if (roomCount >= 2 && corridorCount <= 1)
                            {
                                shouldRemove = true;
                            }

                            if (shouldRemove)
                            {
                                map.Cells[x, y].Type = CellType.Empty;
                                removedAny = true;
                                removedInThisIteration++;
                            }

                        }
                    }
                }

                totalRemoved += removedInThisIteration;
            } while (removedAny && iterationCount < 100);
        }

        /// 部屋の適切な入口/出口点を取得するメソッド - 変換エラーを修正
        private Vector2Int GetRoomEntryPoint(RectInt room, Vector2Int target, int variant = 0)
        {
            // 部屋の中心点と境界上の点を結ぶ方向を計算
            Vector2 center = room.center;
            // ここでエラーが発生 - Vector2をVector2Intに直接キャストできない
            // Vector2 dir = (target - (Vector2Int)center).normalized; // ← エラー行

            // 修正: targetをVector2に変換して計算
            Vector2 targetVec2 = new Vector2(target.x, target.y);
            Vector2 dir = (targetVec2 - center).normalized;

            // 以降のコードはそのまま
            float halfWidth = room.width / 2f;
            float halfHeight = room.height / 2f;

            // バリアントに応じて位置をずらす（複数回試行用）
            float offsetX = 0, offsetY = 0;
            if (variant > 0)
            {
                // 部屋のサイズに応じてオフセットを調整
                offsetX = (variant % 2 == 1) ? halfWidth * 0.5f : -halfWidth * 0.5f;
                offsetY = (variant > 1) ? halfHeight * 0.5f : -halfHeight * 0.5f;
            }
            // 部屋の境界との交点を計算
            float t;
            Vector2Int entry;

            if (Mathf.Abs(dir.x * halfWidth) > Mathf.Abs(dir.y * halfHeight))
            {
                // X方向の壁との交点
                t = halfWidth / Mathf.Abs(dir.x);
                int x = (dir.x > 0) ? room.xMax - 1 : room.xMin;
                int y = Mathf.RoundToInt(center.y + dir.y * t + offsetY);
                y = Mathf.Clamp(y, room.yMin, room.yMax - 1);
                entry = new Vector2Int(x, y);
            }
            else
            {
                // Y方向の壁との交点
                t = halfHeight / Mathf.Abs(dir.y);
                int y = (dir.y > 0) ? room.yMax - 1 : room.yMin;
                int x = Mathf.RoundToInt(center.x + dir.x * t + offsetX);
                x = Mathf.Clamp(x, room.xMin, room.xMax - 1);
                entry = new Vector2Int(x, y);
            }

            return entry;
        }

        // 自然な曲がりを持つフォールバックパス
        private List<Vector2Int> CreateNaturalFallbackPath(Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int> { start };
            Vector2Int current = start;

            // 目標地点への方向ベクトル
            Vector2Int targetDir = new Vector2Int(
                goal.x > current.x ? 1 : (goal.x < current.x ? -1 : 0),
                goal.y > current.y ? 1 : (goal.y < current.y ? -1 : 0)
            );

            // 目標までの距離
            int remainingX = Mathf.Abs(goal.x - start.x);
            int remainingY = Mathf.Abs(goal.y - start.y);

            // 今向いている方向
            Vector2Int currentDir = targetDir;

            while (current != goal)
            {
                // 高確率で曲がるように設定
                bool shouldTurn = rng.Next(100) < 60; // 60%の確率で曲がり

                if (shouldTurn && (remainingX > 1 && remainingY > 1))
                {
                    // 曲がる場合、可能な方向を検討
                    var possibleDirs = new List<Vector2Int>();

                    // X方向の移動が残っている場合
                    if (current.x != goal.x)
                    {
                        possibleDirs.Add(new Vector2Int(targetDir.x, 0));
                    }

                    // Y方向の移動が残っている場合
                    if (current.y != goal.y)
                    {
                        possibleDirs.Add(new Vector2Int(0, targetDir.y));
                    }

                    // ランダムに方向転換を加える（迂回行動）- 確率増加
                    if (rng.Next(100) < 30 && possibleDirs.Count > 0) // 30%に増加
                    {
                        // 一時的に目標と反対方向に進む
                        possibleDirs.Add(new Vector2Int(-targetDir.x, 0));
                        possibleDirs.Add(new Vector2Int(0, -targetDir.y));
                    }

                    // 可能な方向が一つ以上ある場合、ランダムに選択
                    if (possibleDirs.Count > 0)
                    {
                        currentDir = possibleDirs[rng.Next(possibleDirs.Count)];
                    }
                }
                else
                {
                    // できるだけ目標に近づく方向を選ぶ
                    if (current.x != goal.x && (current.y == goal.y || rng.Next(2) == 0))
                    {
                        currentDir = new Vector2Int(targetDir.x, 0);
                    }
                    else if (current.y != goal.y)
                    {
                        currentDir = new Vector2Int(0, targetDir.y);
                    }
                }

                // 短い区間ずつ進むように修正（1-2マスに制限）
                int steps = rng.Next(1, Mathf.Min(3, Mathf.Max(remainingX, remainingY) + 1));
                for (int i = 0; i < steps; i++)
                {
                    current += currentDir;

                    // 目標を超えないよう調整
                    if ((currentDir.x > 0 && current.x > goal.x) || (currentDir.x < 0 && current.x < goal.x))
                    {
                        current.x = goal.x;
                    }
                    if ((currentDir.y > 0 && current.y > goal.y) || (currentDir.y < 0 && current.y < goal.y))
                    {
                        current.y = goal.y;
                    }

                    path.Add(current);

                    // 残りの距離を更新
                    remainingX = Mathf.Abs(goal.x - current.x);
                    remainingY = Mathf.Abs(goal.y - current.y);

                    // 目標に到達したら終了
                    if (current == goal) break;
                }
            }

            return path;
        }

        // より強固なフォールバック経路生成（常に成功するように設計）
        private List<Vector2Int> CreateRobustFallbackPath(Vector2Int start, Vector2Int goal)
        {
            var path = new List<Vector2Int> { start };
            Vector2Int current = start;

            // 進行方向をランダムに決める（X優先かY優先か）
            bool xFirst = rng.Next(2) == 0;

            if (xFirst)
            {
                // まずX方向に移動（曲がりを入れながら）
                while (current.x != goal.x)
                {
                    int dx = (current.x < goal.x) ? 1 : -1;

                    // 少しずつ進む（1-3マス）
                    int steps = rng.Next(1, Mathf.Min(4, Mathf.Abs(current.x - goal.x) + 1));
                    for (int i = 0; i < steps; i++)
                    {
                        current.x += dx;
                        path.Add(current);

                        if (current.x == goal.x) break;
                    }

                    // 時々Y方向に少し曲がる（ジグザグにする）
                    if (current.x != goal.x && current.y != goal.y && rng.Next(100) < 70)
                    {
                        int dy = (current.y < goal.y) ? 1 : -1;
                        int ySteps = rng.Next(1, Mathf.Min(3, Mathf.Abs(current.y - goal.y) + 1));
                        for (int i = 0; i < ySteps; i++)
                        {
                            current.y += dy;
                            path.Add(current);

                            if (current.y == goal.y) break;
                        }
                    }
                }

                // 次にY方向に移動
                while (current.y != goal.y)
                {
                    current.y += (current.y < goal.y) ? 1 : -1;
                    path.Add(current);
                }
            }
            else
            {
                // まずY方向に移動（曲がりを入れながら）
                while (current.y != goal.y)
                {
                    int dy = (current.y < goal.y) ? 1 : -1;

                    // 少しずつ進む（1-3マス）
                    int steps = rng.Next(1, Mathf.Min(4, Mathf.Abs(current.y - goal.y) + 1));
                    for (int i = 0; i < steps; i++)
                    {
                        current.y += dy;
                        path.Add(current);

                        if (current.y == goal.y) break;
                    }

                    // 時々X方向に少し曲がる（ジグザグにする）
                    if (current.y != goal.y && current.x != goal.x && rng.Next(100) < 70)
                    {
                        int dx = (current.x < goal.x) ? 1 : -1;
                        int xSteps = rng.Next(1, Mathf.Min(3, Mathf.Abs(current.x - goal.x) + 1));
                        for (int i = 0; i < xSteps; i++)
                        {
                            current.x += dx;
                            path.Add(current);

                            if (current.x == goal.x) break;
                        }
                    }
                }

                // 次にX方向に移動
                while (current.x != goal.x)
                {
                    current.x += (current.x < goal.x) ? 1 : -1;
                    path.Add(current);
                }
            }

            return path;
        }

        // A*アルゴリズムの最適化版 - 直線通路の発生を防止
        IEnumerable<Vector2Int> AStarNatural(Vector2Int s, Vector2Int t)
        {
            // 無限ループ防止のための反復回数上限
            const int MAX_ITERATIONS = 5000;
            int iterations = 0;

            int estimatedPathLength = Mathf.Abs(s.x - t.x) + Mathf.Abs(s.y - t.y);
            int maxPathCost = estimatedPathLength * 4;

            var open = new PriorityQueue<Vector2Int>();
            open.Enqueue(s, 0);

            var came = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [s] = 0 };
            var dirChanges = new Dictionary<Vector2Int, Vector2Int>();
            dirChanges[s] = Vector2Int.zero;

            // 探索を効率化するために範囲を制限
            var searchBounds = new RectInt(
                Mathf.Min(s.x, t.x) - 20,
                Mathf.Min(s.y, t.y) - 20,
                Mathf.Abs(s.x - t.x) + 40,
                Mathf.Abs(s.y - t.y) + 40
            );

            // 直線通路を防止するためのパラメータ強化
            const int DIRECTION_CHANGE_PENALTY = -1;        // 方向変更にボーナス
            const int CORRIDOR_BONUS = -4;                  // 既存通路への接続ボーナスを強化
            const int MAX_STRAIGHT_LENGTH = 1;              // 最大直線長を極端に短く
            const int STRAIGHT_LENGTH_PENALTY_FACTOR = 8;   // 直線ペナルティ係数を大幅強化
            const int ROOM_COST = 10;                       // 部屋通過コスト
            const int NOISE_FACTOR = 5;                     // ノイズ影響度を強化
            const int RANDOM_TURN_CHANCE = 50;              // ランダム方向転換確率を大幅に上げる
            const int RANDOM_TURN_BONUS = -10;              // 方向転換ボーナスを強化

            // 直線の長さを追跡
            var straightLength = new Dictionary<Vector2Int, int> { [s] = 0 };

            while (open.Count > 0 && iterations < MAX_ITERATIONS)
            {
                iterations++;
                var cur = open.Dequeue();
                if (!gScore.TryGetValue(cur, out int gCur))
                    continue;

                if (cur == t)
                {
                    Debug.Log($"経路探索成功: {iterations}回の反復");
                    return Reconstruct(cur, came);
                }

                // 8方向をシャッフル（斜め含む）
                var directions = ShuffleAllDirs();

                foreach (var d in directions)
                {
                    var nxt = cur + d;
                    if (!map.InBounds(nxt)) continue;

                    var curCell = map.Cells[cur.x, cur.y];
                    var nxtCell = map.Cells[nxt.x, nxt.y];

                    bool curIsRoom = curCell.Type == CellType.Room;
                    bool nxtIsRoom = nxtCell.Type == CellType.Room;
                    bool sameRoom = curIsRoom && nxtIsRoom &&
                                     curCell.RoomId == nxtCell.RoomId;
                    bool isCorridorCell = nxtCell.Type == CellType.Corridor;

                    // 別の部屋（目的地以外）には入らない
                    if (nxtIsRoom && !sameRoom && nxt != t)
                        continue;

                    // 斜め移動の場合、通過するセルもチェック
                    if (Mathf.Abs(d.x) == 1 && Mathf.Abs(d.y) == 1)
                    {
                        if (!map.InBounds(cur.x + d.x, cur.y) ||
                            !map.InBounds(cur.x, cur.y + d.y) ||
                            map.Cells[cur.x + d.x, cur.y].Type == CellType.Room ||
                            map.Cells[cur.x, cur.y + d.y].Type == CellType.Room)
                            continue;
                    }

                    // コスト計算 - より自然な通路のため複雑なコスト計算
                    // 基本コスト
                    int baseCost = nxtIsRoom ? ROOM_COST : (Mathf.Abs(d.x) + Mathf.Abs(d.y) == 2 ? 2 : 1);

                    // ノイズコスト（強化：通路の揺らぎを増加）
                    int noiseCost = noiseMap[nxt.x, nxt.y] * NOISE_FACTOR;

                    // 方向変更コスト
                    var prevDir = dirChanges.ContainsKey(cur) ? dirChanges[cur] : Vector2Int.zero;
                    bool isDirChange = prevDir != Vector2Int.zero && prevDir != d;

                    // 直線長の追跡（強化：長すぎる直線にペナルティ）
                    int curStraightLen = straightLength.ContainsKey(cur) ? straightLength[cur] : 0;
                    int nextStraightLen = isDirChange ? 1 : curStraightLen + 1;

                    // 方向転換と直線ペナルティの計算
                    int dirChangeCost = 0;
                    if (isDirChange)
                    {
                        // 方向変更にはボーナス
                        dirChangeCost = DIRECTION_CHANGE_PENALTY;
                    }
                    else if (nextStraightLen > MAX_STRAIGHT_LENGTH)
                    {
                        // 直線が長すぎる場合、急激に増加するペナルティ
                        dirChangeCost = (nextStraightLen - MAX_STRAIGHT_LENGTH) * STRAIGHT_LENGTH_PENALTY_FACTOR;
                    }

                    // 既存通路への接続ボーナス
                    int corridorBonus = isCorridorCell ? CORRIDOR_BONUS : 0;

                    // ランダムに曲がりを促進（確率を高く）
                    int randomTurnBonus = 0;
                    if (rng.Next(100) < RANDOM_TURN_CHANCE)
                    {
                        randomTurnBonus = isDirChange ? RANDOM_TURN_BONUS : 0;
                    }

                    // 総コスト計算
                    int stepCost = baseCost + noiseCost + dirChangeCost + corridorBonus + randomTurnBonus;
                    int tentative = gCur + stepCost;

                    if (!gScore.TryGetValue(nxt, out int prev) || tentative < prev)
                    {
                        came[nxt] = cur;
                        gScore[nxt] = tentative;
                        dirChanges[nxt] = d;
                        straightLength[nxt] = nextStraightLen;

                        // ヒューリスティック関数にさらなるランダム性
                        int heuristic = Heuristic(nxt, t);
                        int randomFactor = rng.Next(0, 20); // 0-19の大きなランダム性
                        open.Enqueue(nxt, tentative + heuristic + randomFactor);
                    }
                }
            }

            // 反復回数上限に達した場合のログ
            if (iterations >= MAX_ITERATIONS)
            {
                Debug.Log($"経路探索が制限に達しました: {s}から{t}への経路を{MAX_ITERATIONS}回の反復で見つけられませんでした");
            }

            // 経路が見つからない場合
            return new List<Vector2Int>();
        }

        // 部屋の接続性を検証し、必要に応じて追加の接続を生成
        private void VerifyAndFixConnectivity(IReadOnlyList<RectInt> rooms)
        {
            // BFSで接続コンポーネントを特定
            var visited = new bool[rooms.Count];
            var components = new List<List<int>>();

            for (int i = 0; i < rooms.Count; i++)
            {
                if (visited[i]) continue;

                // 新しい接続コンポーネントを開始
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);

                    // 現在の部屋から到達可能なすべての部屋を探索
                    for (int j = 0; j < rooms.Count; j++)
                    {
                        if (visited[j] || !AreRoomsConnected(rooms[current], rooms[j])) continue;

                        queue.Enqueue(j);
                        visited[j] = true;
                    }
                }

                components.Add(component);
            }

            // 複数のコンポーネントがある場合は追加接続が必要
            if (components.Count > 1)
            {
                Debug.LogWarning($"断絶したダンジョン領域を検出: {components.Count}個の切断された領域があります");

                // 各コンポーネント間に接続を追加
                for (int i = 0; i < components.Count - 1; i++)
                {
                    int roomA = components[i][0]; // 最初のコンポーネントから代表部屋を選択
                    int roomB = components[i + 1][0]; // 次のコンポーネントから代表部屋を選択

                    Debug.Log($"孤立した部屋群を接続: 部屋{roomA}と部屋{roomB}間に強制接続を作成");

                    // 2つの部屋間に強制的に接続を作成
                    Vector2Int start = Vector2Int.RoundToInt(rooms[roomA].center);
                    Vector2Int goal = Vector2Int.RoundToInt(rooms[roomB].center);

                    var path = CreateRobustFallbackPath(start, goal);
                    foreach (var p in path)
                        Paint(p, 1); // 接続通路は幅1で十分
                }
            }
            else
            {
                Debug.Log("接続性検証: すべての部屋が正しく接続されています");
            }
        }

        // 2つの部屋が通路で接続されているかチェック
        private bool AreRoomsConnected(RectInt roomA, RectInt roomB)
        {
            // 同じ部屋は当然接続されている
            if (roomA == roomB) return true;

            Vector2Int startPos = Vector2Int.RoundToInt(roomA.center);
            Vector2Int goalPos = Vector2Int.RoundToInt(roomB.center);

            // RoomIDが同じかチェック
            if (map.Cells[startPos.x, startPos.y].RoomId == map.Cells[goalPos.x, goalPos.y].RoomId)
                return true;

            // BFSで接続チェック
            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            queue.Enqueue(startPos);
            visited.Add(startPos);

            while (queue.Count > 0)
            {
                var pos = queue.Dequeue();

                if (map.Cells[pos.x, pos.y].Type == CellType.Room &&
                    map.Cells[pos.x, pos.y].RoomId == map.Cells[goalPos.x, goalPos.y].RoomId)
                    return true;

                // 4方向の隣接セルをチェック
                foreach (var d in dirs)
                {
                    Vector2Int next = pos + d;

                    // 範囲外チェック
                    if (!map.InBounds(next)) continue;

                    // 訪問済みチェック
                    if (visited.Contains(next)) continue;

                    // 通路または部屋のみ通過可能
                    var cell = map.Cells[next.x, next.y];
                    if (cell.Type == CellType.Empty) continue;

                    queue.Enqueue(next);
                    visited.Add(next);
                }
            }

            return false; // 接続が見つからなかった
        }

        // 方向配列をシャッフル
        private Vector2Int[] ShuffleAllDirs()
        {
            // 8方向すべてを使用
            var shuffled = allDirs.ToArray();
            int n = shuffled.Length;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                var temp = shuffled[k];
                shuffled[k] = shuffled[n];
                shuffled[n] = temp;
            }
            return shuffled;
        }

        // マンハッタン距離ヒューリスティック
        int Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        // Reconstruct メソッドを修正し、最大経路長に制限を追加
        IEnumerable<Vector2Int> Reconstruct(Vector2Int node, Dictionary<Vector2Int, Vector2Int> came)
        {
            // 経路の最大長を制限
            const int MAX_PATH_LENGTH = 1000;

            var path = new List<Vector2Int>(MAX_PATH_LENGTH) { node };
            int pathLength = 1;

            // 循環参照と経路長制限をチェック
            var visited = new HashSet<Vector2Int> { node };

            while (came.TryGetValue(node, out var prev) && pathLength < MAX_PATH_LENGTH)
            {
                // 循環参照のチェック
                if (visited.Contains(prev))
                {
                    Debug.LogWarning($"経路再構築で循環参照を検出! 経路長: {pathLength}");
                    break;
                }

                visited.Add(prev);
                node = prev;
                path.Add(node);
                pathLength++;
            }

            // 経路長が制限に達した場合は警告
            if (pathLength >= MAX_PATH_LENGTH)
            {
                Debug.LogWarning($"経路が最大長({MAX_PATH_LENGTH})に達しました。経路を切り詰めます。");
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

                    // Empty セルまたは部屋に隣接する場所を掘る
                    if (map.Cells[x, y].Type == CellType.Empty || HasRoomNeighbor(x, y))
                        map.Cells[x, y] = new Cell
                        { Type = CellType.Corridor, RoomId = -1 };
                }
        }

        // 部屋に隣接しているかチェック
        bool HasRoomNeighbor(int x, int y)
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