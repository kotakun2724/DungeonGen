using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace DungeonGen.Generation
{
    public class DelaunayTriangulation
    {
        public struct Triangle
        {
            public int i0, i1, i2;  // 頂点インデックス
        }

        // ドロネー三角分割を実行
        public static List<Triangle> Triangulate(List<Vector2> points)
        {
            // 結果を格納するリスト
            var triangles = new List<Triangle>();

            // 点が3つ未満なら三角形は存在しない
            if (points.Count < 3)
                return triangles;

            // 最初に超三角形（すべての点を含む大きな三角形）を作成
            var superTriangle = CreateSuperTriangle(points);
            points.Add(superTriangle.Item1);
            points.Add(superTriangle.Item2);
            points.Add(superTriangle.Item3);

            // 最初は超三角形のみ
            triangles.Add(new Triangle { i0 = points.Count - 3, i1 = points.Count - 2, i2 = points.Count - 1 });

            // 各点を追加して三角形分割を更新
            for (int i = 0; i < points.Count - 3; i++)
            {
                var badTriangles = new List<Triangle>();

                // 外接円内に点があるような「不良な三角形」を探す
                foreach (var t in triangles)
                {
                    if (IsPointInCircumcircle(points[i], points[t.i0], points[t.i1], points[t.i2]))
                    {
                        badTriangles.Add(t);
                    }
                }

                // 不良な三角形の辺を集め、境界を形成
                var polygon = new List<(int, int)>();

                foreach (var t in badTriangles)
                {
                    if (IsUniqueEdge(polygon, t.i0, t.i1))
                        polygon.Add((t.i0, t.i1));

                    if (IsUniqueEdge(polygon, t.i1, t.i2))
                        polygon.Add((t.i1, t.i2));

                    if (IsUniqueEdge(polygon, t.i2, t.i0))
                        polygon.Add((t.i2, t.i0));
                }

                // 不良な三角形を削除
                triangles = triangles.Except(badTriangles).ToList();

                // 新しい点と境界の各辺で新しい三角形を作成
                foreach (var edge in polygon)
                {
                    triangles.Add(new Triangle { i0 = edge.Item1, i1 = edge.Item2, i2 = i });
                }
            }

            // 超三角形の頂点を含む三角形を削除
            triangles = triangles.Where(t =>
                t.i0 < points.Count - 3 &&
                t.i1 < points.Count - 3 &&
                t.i2 < points.Count - 3).ToList();

            return triangles;
        }

        // 辺が既存の多角形の辺と一致するかどうかをチェック
        private static bool IsUniqueEdge(List<(int, int)> edges, int a, int b)
        {
            foreach (var edge in edges)
            {
                if ((edge.Item1 == a && edge.Item2 == b) || (edge.Item1 == b && edge.Item2 == a))
                    return false;
            }
            return true;
        }

        // 点が三角形の外接円内にあるかどうかをチェック
        private static bool IsPointInCircumcircle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = a.sqrMagnitude;
            float cd = c.sqrMagnitude;
            float ef = b.sqrMagnitude;

            float ax = a.x;
            float ay = a.y;
            float bx = b.x;
            float by = b.y;
            float cx = c.x;
            float cy = c.y;

            float circum_x = ((ay - by) * cd + (by - cy) * ab + (cy - ay) * ef) /
                            (2 * ((ax - bx) * (by - cy) - (bx - cx) * (ay - by)));

            float circum_y = ((ax - bx) * cd + (bx - cx) * ab + (cx - ax) * ef) /
                            (2 * ((ay - by) * (bx - cx) - (by - cy) * (ax - bx)));

            Vector2 circum = new Vector2(circum_x, circum_y);
            float circum_radius = (a - circum).sqrMagnitude;

            float dist = (p - circum).sqrMagnitude;
            return dist <= circum_radius;
        }

        // すべてのポイントを含む超三角形を作成
        private static (Vector2, Vector2, Vector2) CreateSuperTriangle(List<Vector2> points)
        {
            // バウンディングボックスを見つける
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var p in points)
            {
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }

            float dx = (maxX - minX) * 10;
            float dy = (maxY - minY) * 10;

            Vector2 v1 = new Vector2(minX - dx, minY - dy);
            Vector2 v2 = new Vector2(maxX + dx, minY - dy);
            Vector2 v3 = new Vector2((minX + maxX) / 2, maxY + dy);

            return (v1, v2, v3);
        }
    }
}