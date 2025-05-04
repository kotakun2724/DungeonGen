using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DungeonGen.Core;

namespace DungeonGen.Generation
{
    public class RoomGenerator
    {
        private CellMap map;
        private System.Random rng;
        private int minWidth, maxWidth;
        private int minHeight, maxHeight;
        private int attempts;

        public List<RectInt> Rooms { get; private set; } = new List<RectInt>();

        public RoomGenerator(CellMap map, System.Random rng,
                             int minWidth, int maxWidth,
                             int minHeight, int maxHeight,
                             int attempts)
        {
            this.map = map;
            this.rng = rng;
            this.minWidth = minWidth;
            this.maxWidth = maxWidth;
            this.minHeight = minHeight;
            this.maxHeight = maxHeight;
            this.attempts = attempts;
        }

        public void Scatter()
        {
            Rooms.Clear();
            for (int i = 0; i < attempts; i++)
            {
                int rw = rng.Next(minWidth, maxWidth + 1);
                int rh = rng.Next(minHeight, maxHeight + 1);
                int rx = rng.Next(1, map.Width - rw - 1);
                int ry = rng.Next(1, map.Height - rh - 1);

                var r = new RectInt(rx, ry, rw, rh);
                if (Rooms.Any(o => o.Overlaps(r))) continue; // 重なりは破棄

                int id = Rooms.Count;
                Rooms.Add(r);
                for (int x = r.xMin; x < r.xMax; x++)
                    for (int y = r.yMin; y < r.yMax; y++)
                        map.Cells[x, y] = new Cell { Type = CellType.Room, RoomId = id };
            }
        }
    }
}