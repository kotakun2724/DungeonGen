using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DungeonGen.Core;

namespace DungeonGen.Generation
{
    public static class GraphGenerator
    {
        public struct Edge
        {
            public int A, B;
            public float Len;

            // 等値比較のためのメソッドを追加
            public override bool Equals(object obj)
            {
                if (!(obj is Edge other)) return false;
                // エッジは無向なので (A,B) と (B,A) は同じとみなす
                return (A == other.A && B == other.B) || (A == other.B && B == other.A);
            }

            public override int GetHashCode()
            {
                // 小さい値・大きい値の順で一意のハッシュを生成
                int min = Mathf.Min(A, B);
                int max = Mathf.Max(A, B);
                return min * 10000 + max;
            }
        }

        public static List<Edge> CreateEdges(IReadOnlyList<RectInt> rooms)
        {
            var pts = rooms.Select(r => new Vector2(r.center.x, r.center.y)).ToList();
            var edges = new List<Edge>();

            // 完全グラフを作成（全部屋間の接続を考慮）
            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = i + 1; j < pts.Count; j++)
                {
                    edges.Add(new Edge
                    {
                        A = i,
                        B = j,
                        Len = Vector2.Distance(pts[i], pts[j])
                    });
                }
            }

            return edges;
        }

        public static List<Edge> PrimMST(List<Edge> edges, int nRooms)
        {
            var vis = new HashSet<int> { 0 }; var mst = new List<Edge>();
            while (vis.Count < nRooms)
            {
                var e = edges.Where(ed => vis.Contains(ed.A) ^ vis.Contains(ed.B))
                           .OrderBy(ed => ed.Len).First();
                mst.Add(e); vis.Add(vis.Contains(e.A) ? e.B : e.A);
            }
            return mst;
        }

        // 余分サイクルを追加するメソッドを追加
        public static List<Edge> AddExtraConnections(List<Edge> allEdges, List<Edge> mstEdges, float extraConnectionChance)
        {
            // デバッグのため、結果を新しいリストとして作成
            var result = new List<Edge>(mstEdges);
            int originalCount = result.Count;
            int addedCount = 0;

            // デバッグ出力を追加
            Debug.Log($"MST接続数: {mstEdges.Count}, 全接続候補: {allEdges.Count}, 確率: {extraConnectionChance:F2}");

            // 重要：MSTに含まれていないエッジだけを処理する
            foreach (var edge in allEdges)
            {
                bool isDuplicate = false;

                // 手動で重複チェック（Equalsが正しく機能しない場合に備えて）
                foreach (var existingEdge in mstEdges)
                {
                    if ((edge.A == existingEdge.A && edge.B == existingEdge.B) ||
                        (edge.A == existingEdge.B && edge.B == existingEdge.A))
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                // 重複がなく、確率条件を満たす場合のみ追加
                if (!isDuplicate && Random.value < extraConnectionChance)
                {
                    result.Add(edge);
                    addedCount++;
                }
            }

            // デバッグ出力
            Debug.Log($"追加接続数: {addedCount}, 合計: {result.Count}");

            return result;
        }
    }
}