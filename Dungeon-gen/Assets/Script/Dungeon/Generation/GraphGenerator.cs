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

            public override bool Equals(object obj)
            {
                if (!(obj is Edge other)) return false;
                return (A == other.A && B == other.B) || (A == other.B && B == other.A);
            }

            public override int GetHashCode()
            {
                int min = Mathf.Min(A, B);
                int max = Mathf.Max(A, B);
                return min * 10000 + max;
            }
        }

        public static List<Edge> CreateEdges(IReadOnlyList<RectInt> rooms)
        {
            var pts = rooms.Select(r => new Vector2(r.center.x, r.center.y)).ToList();
            var tris = DelaunayTriangulation.Triangulate(pts);

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
                A = e.Item1,
                B = e.Item2,
                Len = Vector2.Distance(pts[e.Item1], pts[e.Item2])
            }).ToList();
        }

        public static List<Edge> PrimMST(List<Edge> edges, int nRooms)
        {
            // エッジがない場合は早期リターン
            if (edges.Count == 0 || nRooms <= 1)
            {
                Debug.LogWarning("接続するための有効なエッジまたは部屋がありません");
                return new List<Edge>();
            }

            var vis = new HashSet<int> { 0 };  // 最初の部屋から開始
            var mst = new List<Edge>();

            // すべての部屋が訪問されるまで繰り返す
            while (vis.Count < nRooms)
            {
                Edge bestEdge = new Edge { A = -1, B = -1, Len = float.MaxValue };
                bool foundEdge = false;

                // まだ訪問されていない部屋につながるエッジの中で最短のものを探す
                foreach (var e in edges)
                {
                    // XORで「片方だけ訪問済み」のエッジを選ぶ
                    if (vis.Contains(e.A) ^ vis.Contains(e.B))
                    {
                        if (e.Len < bestEdge.Len)
                        {
                            bestEdge = e;
                            foundEdge = true;
                        }
                    }
                }

                // 接続可能なエッジが見つからない場合（グラフが切断されている）
                if (!foundEdge)
                {
                    Debug.LogWarning($"MST生成: {vis.Count}/{nRooms}部屋を接続した後、接続可能なエッジが見つかりません");

                    // 未訪問の部屋を強制的に最も近い訪問済み部屋と接続
                    foreach (int i in Enumerable.Range(0, nRooms).Where(i => !vis.Contains(i)))
                    {
                        Edge closestEdge = edges
                            .Where(e => (e.A == i && vis.Contains(e.B)) || (e.B == i && vis.Contains(e.A)))
                            .OrderBy(e => e.Len)
                            .FirstOrDefault();

                        if (closestEdge.A != -1) // 有効なエッジが見つかった
                        {
                            mst.Add(closestEdge);
                            vis.Add(vis.Contains(closestEdge.A) ? closestEdge.B : closestEdge.A);
                        }
                        else
                        {
                            // 完全に孤立した部屋の場合、最も近い訪問済み部屋と強制的に接続
                            int closest = -1;
                            float minDist = float.MaxValue;

                            foreach (int v in vis)
                            {
                                float dist = Vector2.Distance(
                                    new Vector2(i % nRooms, i / nRooms),
                                    new Vector2(v % nRooms, v / nRooms)
                                );

                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    closest = v;
                                }
                            }

                            if (closest != -1)
                            {
                                var newEdge = new Edge { A = i, B = closest, Len = minDist };
                                mst.Add(newEdge);
                                vis.Add(i);
                            }
                        }
                    }
                    continue;
                }

                mst.Add(bestEdge);
                vis.Add(vis.Contains(bestEdge.A) ? bestEdge.B : bestEdge.A);
            }

            Debug.Log($"MST生成完了: すべての{nRooms}部屋を{mst.Count}本のエッジで接続");
            return mst;
        }

        public static List<Edge> AddExtraConnections(List<Edge> allEdges, List<Edge> mstEdges, float extraConnectionChance)
        {
            var result = new List<Edge>(mstEdges);
            int initialCount = result.Count;

            // MST以外の余分なエッジを取得し、長さでソート
            var remainingEdges = allEdges
                .Where(e => !mstEdges.Any(m => (m.A == e.A && m.B == e.B) || (m.A == e.B && m.B == e.A)))
                .OrderBy(e => e.Len)  // 長さの短いエッジを優先
                .ToList();

            Debug.Log($"MST接続数: {initialCount}, 追加候補接続数: {remainingEdges.Count}, 確率: {extraConnectionChance:F2}");

            // 余分な接続を制限
            int maxExtraEdges = Mathf.Min(remainingEdges.Count, mstEdges.Count / 3); // 基本接続の1/3まで
            int addedCount = 0;
            HashSet<int> connectedRooms = new HashSet<int>();

            // すでに接続されている部屋のペアを記録
            foreach (var edge in mstEdges)
            {
                connectedRooms.Add(edge.A * 10000 + edge.B);
                connectedRooms.Add(edge.B * 10000 + edge.A);
            }

            foreach (var edge in remainingEdges)
            {
                // すでに追加済みのエッジ数が上限に達した場合は終了
                if (addedCount >= maxExtraEdges) break;

                // 過密接続を避ける - 同じ部屋ペアへの複数接続を制限
                int key1 = edge.A * 10000 + edge.B;
                int key2 = edge.B * 10000 + edge.A;

                // すでに接続されているペアは追加確率を下げる
                float actualChance = extraConnectionChance;
                if (connectedRooms.Contains(key1) || connectedRooms.Contains(key2))
                {
                    actualChance *= 0.3f; // 70%減の確率
                }

                if (Random.value < actualChance)
                {
                    result.Add(edge);
                    connectedRooms.Add(key1);
                    connectedRooms.Add(key2);
                    addedCount++;
                }
            }

            Debug.Log($"追加接続数: {addedCount}, 最終接続数: {result.Count}");
            return result;
        }
    }
}