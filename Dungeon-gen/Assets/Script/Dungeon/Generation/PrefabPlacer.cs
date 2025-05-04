using UnityEngine;
using DungeonGen.Core;

namespace DungeonGen.Generation
{
    public class PrefabPlacer
    {
        private CellMap map;
        private Transform parent;
        private GameObject floorPrefab;
        private GameObject wallPrefab;

        public PrefabPlacer(CellMap map, Transform parent, GameObject floorPrefab, GameObject wallPrefab)
        {
            this.map = map;
            this.parent = parent;
            this.floorPrefab = floorPrefab;
            this.wallPrefab = wallPrefab;
        }

        public void Render()
        {
            // 既存オブジェクトをクリア
            foreach (Transform c in parent) Object.Destroy(c.gameObject);

            Quaternion rot90 = Quaternion.Euler(0, 90, 0);

            // 1. 床配置
            for (int x = 0; x < map.Width; ++x)
                for (int y = 0; y < map.Height; ++y)
                    if (map.IsFloor(x, y))
                        Object.Instantiate(floorPrefab, new Vector3(x, 0f, y), Quaternion.identity, parent);

            // 2. 壁配置：各床セルの4辺をチェック
            for (int x = 0; x < map.Width; ++x)
                for (int y = 0; y < map.Height; ++y)
                    if (map.IsFloor(x, y))
                    {
                        // 上 (Z+)
                        if (!map.IsFloor(x, y + 1))
                            Object.Instantiate(wallPrefab, new Vector3(x, 0.5f, y + 0.5f), Quaternion.identity, parent);

                        // 下 (Z-)
                        if (!map.IsFloor(x, y - 1))
                            Object.Instantiate(wallPrefab, new Vector3(x, 0.5f, y - 0.5f), Quaternion.identity, parent);

                        // 右 (X+)
                        if (!map.IsFloor(x + 1, y))
                            Object.Instantiate(wallPrefab, new Vector3(x + 0.5f, 0.5f, y), rot90, parent);

                        // 左 (X-)
                        if (!map.IsFloor(x - 1, y))
                            Object.Instantiate(wallPrefab, new Vector3(x - 0.5f, 0.5f, y), rot90, parent);
                    }
        }
    }
}