using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGen.Generation
{
    /// <summary>
    /// ルーム中心を結ぶ全エッジ → MST → 余分エッジを追加
    /// </summary>
    public class GraphGenerator
    {
        public struct Edge
        {
            public int A;     // 部屋インデックス
            public int B;     // 部屋インデックス
            public float Len; // 距離

            public Edge(int a, int b, float len)
            {
                A = a;
                B = b;
                Len = len;
            }
        }

        // 部屋間の全エッジを作成
        public static List<Edge> CreateEdges(IReadOnlyList<RectInt> rooms)
        {
            if (rooms == null || rooms.Count <= 1)
            {
                Debug.LogWarning("部屋が不足しているためエッジは生成されません");
                return new List<Edge>();
            }

            var result = new List<Edge>();

            // 全部屋の中心点を取得
            var centers = rooms.Select(r => r.center).ToList();

            // ドロネー三角分割とフォールバックメカニズム
            try
            {
                // 完全グラフ（全部屋間の接続）を生成
                Debug.Log("全部屋間の接続を生成します（完全グラフ）");
                for (int i = 0; i < centers.Count; i++)
                {
                    for (int j = i + 1; j < centers.Count; j++)
                    {
                        float dist = Vector2.Distance(centers[i], centers[j]);
                        result.Add(new Edge(i, j, dist));
                    }
                }

                Debug.Log($"完全グラフ: {result.Count}本のエッジを生成");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"エッジ生成中にエラー: {e.Message}");
                result.Clear();

                // エラー時の最低限の接続
                for (int i = 0; i < centers.Count - 1; i++)
                {
                    float dist = Vector2.Distance(centers[i], centers[i + 1]);
                    result.Add(new Edge(i, i + 1, dist));
                }
            }

            Debug.Log($"最終的なエッジ数: {result.Count}");
            return result;
        }

        // Primのアルゴリズムで最小全域木を計算
        public static List<Edge> PrimMST(List<Edge> edges, int roomCount)
        {
            if (roomCount <= 1)
                return new List<Edge>();

            var result = new List<Edge>();
            var visited = new HashSet<int>();

            // 最初の部屋を訪問済みに
            visited.Add(0);

            // N-1個のエッジを追加（N = 部屋数）
            while (result.Count < roomCount - 1 && visited.Count < roomCount)
            {
                Edge? bestEdge = null;
                float minLength = float.MaxValue;

                // 訪問済み→未訪問の最短エッジを探す
                foreach (var edge in edges)
                {
                    bool aVisited = visited.Contains(edge.A);
                    bool bVisited = visited.Contains(edge.B);

                    if ((aVisited && !bVisited) || (!aVisited && bVisited))
                    {
                        if (edge.Len < minLength)
                        {
                            bestEdge = edge;
                            minLength = edge.Len;
                        }
                    }
                }

                if (bestEdge.HasValue)
                {
                    result.Add(bestEdge.Value);
                    visited.Add(bestEdge.Value.A);
                    visited.Add(bestEdge.Value.B);
                }
                else
                {
                    break; // これ以上接続できるエッジがない
                }
            }

            return result;
        }

        // 追加エッジ選択（サイクル生成）
        public static List<Edge> AddExtraConnections(List<Edge> allEdges, List<Edge> mstEdges, float extraEdgeRatio)
        {
            var result = new List<Edge>(mstEdges);
            int initialCount = result.Count;

            // 部屋数の推定（最大の部屋インデックス+1で概算）
            int maxRoomId = 0;
            foreach (var edge in mstEdges)
            {
                maxRoomId = Mathf.Max(maxRoomId, edge.A, edge.B);
            }
            int roomCount = maxRoomId + 1;

            // 追加するエッジ数（絶対数値に変換）
            int extraEdgesToAdd = Mathf.Max(5, Mathf.FloorToInt(mstEdges.Count * extraEdgeRatio));
            Debug.Log($"追加接続数: {extraEdgesToAdd}本 (基本MST: {mstEdges.Count}本, 割合: {extraEdgeRatio:F2})");

            // 既存エッジを記録
            var existingPairs = new HashSet<string>();
            foreach (var edge in result)
            {
                int a = Mathf.Min(edge.A, edge.B);
                int b = Mathf.Max(edge.A, edge.B);
                existingPairs.Add($"{a}-{b}");
            }

            // 候補エッジをフィルタリング（既存エッジを除外）
            var validCandidates = new List<Edge>();
            foreach (var edge in allEdges)
            {
                if (edge.A >= roomCount || edge.B >= roomCount)
                {
                    // インデックスエラー防止のため無効なエッジをスキップ
                    continue;
                }

                int a = Mathf.Min(edge.A, edge.B);
                int b = Mathf.Max(edge.A, edge.B);

                if (!existingPairs.Contains($"{a}-{b}"))
                {
                    validCandidates.Add(edge);
                }
            }

            Debug.Log($"候補エッジ数: {validCandidates.Count}本");

            if (validCandidates.Count == 0)
            {
                Debug.LogWarning("追加候補エッジがありません - サイクルは生成されません");
                return result;
            }

            // 全ての候補を距離の長い順にソート
            validCandidates.Sort((a, b) => b.Len.CompareTo(a.Len));

            // 長距離エッジを確実に含める（上位20%）
            int longEdgeCount = Mathf.Max(2, Mathf.FloorToInt(validCandidates.Count * 0.2f));
            longEdgeCount = Mathf.Min(longEdgeCount, extraEdgesToAdd / 2); // 最大でも半分まで

            // 長距離エッジの追加
            for (int i = 0; i < longEdgeCount && i < validCandidates.Count; i++)
            {
                var edge = validCandidates[i];
                result.Add(edge);
                Debug.Log($"長距離サイクル: 部屋{edge.A}-{edge.B}, 距離={edge.Len:F1}");

                // 追加したエッジを記録
                int a = Mathf.Min(edge.A, edge.B);
                int b = Mathf.Max(edge.A, edge.B);
                existingPairs.Add($"{a}-{b}");
            }

            // 残りをランダム選択
            var remainingCandidates = validCandidates
                .Where(e =>
                {
                    int a = Mathf.Min(e.A, e.B);
                    int b = Mathf.Max(e.A, e.B);
                    return !existingPairs.Contains($"{a}-{b}");
                })
                .ToList();

            // ランダム化
            System.Random rng = new System.Random(System.DateTime.Now.Ticks.GetHashCode());
            remainingCandidates = remainingCandidates
                .OrderBy(e => rng.Next())
                .ToList();

            // 残りのエッジを追加
            int remaining = extraEdgesToAdd - longEdgeCount;
            for (int i = 0; i < remaining && i < remainingCandidates.Count; i++)
            {
                result.Add(remainingCandidates[i]);

                int a = Mathf.Min(remainingCandidates[i].A, remainingCandidates[i].B);
                int b = Mathf.Max(remainingCandidates[i].A, remainingCandidates[i].B);
                Debug.Log($"追加サイクル: 部屋{a}-{b}, 距離={remainingCandidates[i].Len:F1}");
            }

            Debug.Log($"サイクル追加完了: {result.Count - initialCount}個 (目標: {extraEdgesToAdd}個)");
            return result;
        }

        // 補助メソッド - エッジの重複チェック
        private static void AddUniqueEdge(List<Edge> edges, int a, int b, List<Vector2> centers)
        {
            // 同じ部屋へのエッジはスキップ
            if (a == b) return;

            // 既に同じ部屋間のエッジがあるかチェック
            foreach (var edge in edges)
            {
                if ((edge.A == a && edge.B == b) || (edge.A == b && edge.B == a))
                    return;
            }

            float dist = Vector2.Distance(centers[a], centers[b]);
            edges.Add(new Edge(a, b, dist));
        }
    }
}