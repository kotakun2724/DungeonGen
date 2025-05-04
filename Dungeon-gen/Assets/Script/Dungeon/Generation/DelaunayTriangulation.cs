using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGen.Generation
{
    public static class DelaunayTriangulation
    {
        public struct Triangle { public int i0, i1, i2; public Triangle(int a, int b, int c) { i0 = a; i1 = b; i2 = c; } }

        public static List<Triangle> Triangulate(List<Vector2> pts)
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

            var tris = new List<Triangle> { new(si1, si2, si3) };

            for (int i = 0; i < pts.Count - 3; i++)
            {
                var bad = new List<Triangle>();
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
                    tris.Add(new Triangle(a, b, i));
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