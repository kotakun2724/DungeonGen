using System.Collections.Generic;
using UnityEngine;
using DungeonGen.Core;
using System.Linq;


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

    // 部屋数を指定できるように修正（デフォルト値0は無制限）
    public void Scatter(int targetRoomCount = 0)
    {
        Rooms.Clear();
        int totalAttempts = 0;  // 実際の試行回数をトラック
        int consecutiveFailures = 0;  // 連続失敗回数
        const int MAX_CONSECUTIVE_FAILURES = 50;  // 連続失敗の許容上限

        // 部屋数指定がある場合は達成するまで、なければ指定試行回数だけ試す
        while ((targetRoomCount <= 0 && totalAttempts < attempts) ||
               (targetRoomCount > 0 && Rooms.Count < targetRoomCount && totalAttempts < attempts * 2 && consecutiveFailures < MAX_CONSECUTIVE_FAILURES))
        {
            totalAttempts++;

            int rw = rng.Next(minWidth, maxWidth + 1);
            int rh = rng.Next(minHeight, maxHeight + 1);

            // マップ全体に対してより広い範囲で部屋の配置を試みる
            int rx = rng.Next(1, map.Width - rw - 1);
            int ry = rng.Next(1, map.Height - rh - 1);

            var r = new RectInt(rx, ry, rw, rh);

            // 重なりチェック - 少し余裕を持たせる
            bool overlaps = Rooms.Any(existingRoom =>
            {
                // 部屋同士の間に最低1マスの余裕を持たせる
                RectInt expandedExisting = new RectInt(
                    existingRoom.x - 1,
                    existingRoom.y - 1,
                    existingRoom.width + 2,
                    existingRoom.height + 2
                );
                return expandedExisting.Overlaps(r);
            });

            if (overlaps)
            {
                consecutiveFailures++;
                continue; // 重なりがある場合はスキップ
            }

            // 部屋を追加
            int id = Rooms.Count;
            Rooms.Add(r);

            // セルマップに部屋を記録
            for (int x = r.xMin; x < r.xMax; x++)
                for (int y = r.yMin; y < r.yMax; y++)
                    map.Cells[x, y] = new Cell { Type = CellType.Room, RoomId = id };

            consecutiveFailures = 0; // 成功したらリセット

            // 目標部屋数に達したら終了
            if (targetRoomCount > 0 && Rooms.Count >= targetRoomCount)
                break;
        }

        Debug.Log($"部屋生成: {totalAttempts}回試行, {Rooms.Count}個の部屋を配置");
    }
}