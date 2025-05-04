using UnityEngine;

namespace DungeonGen.Core
{
    public enum CellType { Empty, Room, Corridor }

    public struct Cell
    {
        public CellType Type;
        public int RoomId; // -1 = not a room
    }

    public class CellMap
    {
        public Cell[,] Cells { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public CellMap(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new Cell[width, height];
            // デフォルト値はEmpty、RoomIdは-1
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool InBounds(Vector2Int p)
        {
            return InBounds(p.x, p.y);
        }

        public bool IsFloor(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var t = Cells[x, y].Type;
            return t == CellType.Room || t == CellType.Corridor;
        }
    }
}