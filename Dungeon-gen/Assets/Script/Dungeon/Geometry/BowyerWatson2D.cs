using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGen.Geometry
{
    public static class BowyerWatson2D
    {
        public struct Tri { public int i0, i1, i2; public Tri(int a, int b, int c) { i0 = a; i1 = b; i2 = c; } }

        public static List<Tri> Triangulate(List<Vector2> pts)
        {
            float minX = pts.Min(p => p.x), maxX = pts.Max(p => p.x);
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y);
            float d = Mathf.Max(maxX - minX, maxY - minY) * 20f;
            Vector2 p1 = new(minX - d, minY - 1), p2 = new(maxX + d, minY - 1),
                    p3 = new((minX + maxX) / 2f, maxY + d);
            pts.AddRange(new[] { p1, p2, p3 });
            int si1 = pts.Count - 3, si2 = pts.Count - 2, si3 = pts.Count - 1;

            var tris = new List<Tri> { new(si1, si2, si3) };
            for (int i = 0; i < pts.Count - 3; i++)
            {
                var bad = new List<Tri>(); var poly = new List<(int, int)>();
                foreach (var t in tris)
                    if (InCirc(pts[i], pts[t.i0], pts[t.i1], pts[t.i2]))
                    {
                        bad.Add(t); poly.Add((t.i0, t.i1)); poly.Add((t.i1, t.i2)); poly.Add((t.i2, t.i0));
                    }
                tris = tris.Except(bad).ToList();
                poly = RemoveDup(poly);
                foreach (var (a, b) in poly) tris.Add(new Tri(a, b, i));
            }
            tris = tris.Where(t => t.i0 < si1 && t.i1 < si1 && t.i2 < si1).ToList();
            pts.RemoveRange(pts.Count - 3, 3);
            return tris;
        }

        static bool InCirc(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ax = a.x - p.x, ay = a.y - p.y, bx = b.x - p.x, by = b.y - p.y, cx = c.x - p.x, cy = c.y - p.y;
            float det = (ax * ax + ay * ay) * (bx * cy - by * cx) - (bx * bx + by * by) * (ax * cy - ay * cx)
                      + (cx * cx + cy * cy) * (ax * by - ay * bx);
            return det > 0f;
        }
        static List<(int, int)> RemoveDup(List<(int, int)> e)
        {
            var u = new List<(int, int)>();
            foreach (var ed in e) { var rev = (ed.Item2, ed.Item1); if (u.Contains(rev)) u.Remove(rev); else u.Add(ed); }
            return u;
        }
    }
}
