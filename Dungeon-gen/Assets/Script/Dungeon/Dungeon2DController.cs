using UnityEngine;
using DungeonGen.Core;
using DungeonGen.Generation;
using DungeonGen.Presentation;

namespace DungeonGen
{
    public class Dungeon2DController : MonoBehaviour
    {
        [Header("Grid")]
        public int width = 64, height = 64;

        [Header("Room scatter")]
        public int seed = 0, attempts = 200;
        public int minRoomW = 4, maxRoomW = 10, minRoomH = 4, maxRoomH = 10;

        [Header("Corridor")]
        [Range(1, 4)] public int corridorWidth = 1;
        [Range(0, 1)] public float extraEdgeChance = 0.15f;

        [Header("Prefabs")]
        public GameObject floorPrefab, wallPrefab;

        void Start()
        {
            var rng = seed == 0 ? new System.Random() : new System.Random(seed);
            var map = new CellMap(width, height);

            // 1. Rooms
            var roomGen = new RoomGenerator(map, rng,
                minRoomW, maxRoomW, minRoomH, maxRoomH, attempts);
            roomGen.Scatter();

            // 2‑3. Graph
            var edges = GraphGenerator.CreateEdges(roomGen.Rooms);
            var graph = GraphGenerator.PrimMST(edges, roomGen.Rooms.Count);
            var finalGraph = GraphGenerator.AddExtraConnections(edges, graph, extraEdgeChance);

            // 4. Corridors
            var carver = new CorridorCarver(map);
            carver.Carve(finalGraph, roomGen.Rooms, corridorWidth);

            // 5. Render
            new PrefabPlacer(map, transform, floorPrefab, wallPrefab).Render();
        }
    }
}
