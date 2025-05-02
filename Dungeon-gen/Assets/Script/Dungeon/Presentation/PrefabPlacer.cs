using UnityEngine;
using DungeonGen.Core;
using DungeonGen.Generation;
namespace DungeonGen.Presentation
{
    public class PrefabPlacer
    {
        readonly CellMap map;
        readonly Transform parent;
        readonly GameObject floor, wall;
        readonly Quaternion rot90 = Quaternion.Euler(0, 90, 0);

        public PrefabPlacer(CellMap m, Transform parent, GameObject floor, GameObject wall)
        {
            map = m; this.parent = parent; this.floor = floor; this.wall = wall;
        }

        public void Render()
        {
            foreach (Transform c in parent) Object.Destroy(c.gameObject);

            // 床
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (IsFloor(x, y))
                        Object.Instantiate(floor, new Vector3(x, 0, y), Quaternion.identity, parent);

            // 壁
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (IsFloor(x, y))
                    {
                        if (!IsFloor(x, y + 1))
                            Object.Instantiate(wall, new Vector3(x, 0.5f, y + 0.5f), Quaternion.identity, parent);
                        if (!IsFloor(x, y - 1))
                            Object.Instantiate(wall, new Vector3(x, 0.5f, y - 0.5f), Quaternion.identity, parent);
                        if (!IsFloor(x + 1, y))
                            Object.Instantiate(wall, new Vector3(x + 0.5f, 0.5f, y), rot90, parent);
                        if (!IsFloor(x - 1, y))
                            Object.Instantiate(wall, new Vector3(x - 0.5f, 0.5f, y), rot90, parent);
                    }
        }

        bool IsFloor(int x, int y) =>
            map.InBounds(x, y) && map.Cells[x, y].Type != CellType.Empty;
    }
}
