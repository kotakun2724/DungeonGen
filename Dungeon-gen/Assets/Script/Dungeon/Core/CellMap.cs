using UnityEngine;

namespace DungeonGen.Core
{
    public class CellMap
    {
        public readonly int Width, Height;
        public readonly Cell[,] Cells;

        public CellMap(int w, int h)
        {
            Width = w; Height = h;
            Cells = new Cell[w, h];
        }

        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public bool InBounds(Vector2Int p) => InBounds(p.x, p.y);
    }
}
