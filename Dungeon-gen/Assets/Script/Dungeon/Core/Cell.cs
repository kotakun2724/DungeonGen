namespace DungeonGen.Core
{
    public enum CellType { Empty, Room, Corridor }

    [System.Serializable]
    public struct Cell
    {
        public CellType Type;
        public int RoomId; // -1 = not a room
    }
}
