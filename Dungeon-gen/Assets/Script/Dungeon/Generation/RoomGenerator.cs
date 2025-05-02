using System;
using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;

namespace DungeonGen.Generation
{
    public class RoomGenerator
    {
        readonly CellMap map;
        readonly System.Random rng;
        readonly int minW, maxW, minH, maxH, attempts;
        readonly List<RectInt> rooms = new();
        public IReadOnlyList<RectInt> Rooms => rooms;

        public RoomGenerator(CellMap m, System.Random r, int minW, int maxW, int minH, int maxH, int attempts)
        {
            map = m; rng = r; this.minW = minW; this.maxW = maxW; this.minH = minH; this.maxH = maxH; this.attempts = attempts;
        }

        public void Scatter()
        {
            rooms.Clear();
            for (int i = 0; i < attempts; i++)
            {
                int rw = rng.Next(minW, maxW + 1), rh = rng.Next(minH, maxH + 1);
                int rx = rng.Next(1, map.Width - rw - 1), ry = rng.Next(1, map.Height - rh - 1);
                var rect = new RectInt(rx, ry, rw, rh);
                if (rooms.Exists(o => o.Overlaps(rect))) continue;
                int id = rooms.Count; rooms.Add(rect);
                for (int x = rect.xMin; x < rect.xMax; x++)
                    for (int y = rect.yMin; y < rect.yMax; y++)
                        map.Cells[x, y] = new Cell { Type = CellType.Room, RoomId = id };
            }
        }
    }
}
