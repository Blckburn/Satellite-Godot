using Godot;
using System;

public sealed class Decorator
{
    private readonly Random _random;

    public Decorator(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public void AddSectionWalls(
        LevelGenerator.MapSection section,
        int mapWidth,
        int mapHeight,
        Godot.TileMap wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        var wallPositions = new System.Collections.Generic.List<Vector2I>();
        Vector2 worldOffset = section.WorldOffset;

        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (section.SectionMask[x, y] != LevelGenerator.TileType.Background)
                continue;

            bool shouldBeWall = false;
            for (int dx = -1; dx <= 1 && !shouldBeWall; dx++)
            for (int dy = -1; dy <= 1 && !shouldBeWall; dy++)
            {
                if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0)) continue;
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < mapWidth && ny >= 0 && ny < mapHeight)
                {
                    var t = section.SectionMask[nx, ny];
                    if (t == LevelGenerator.TileType.Room || t == LevelGenerator.TileType.Corridor)
                        shouldBeWall = true;
                }
            }

            if (shouldBeWall)
            {
                wallPositions.Add(new Vector2I(x, y));
                section.SectionMask[x, y] = LevelGenerator.TileType.Wall;
            }
        }

        foreach (var localPos in wallPositions)
        {
            try
            {
                Vector2I worldPos = new Vector2I((int)worldOffset.X + localPos.X, (int)worldOffset.Y + localPos.Y);
                Vector2I tile = wallTileSelector(section.BiomeType, localPos);
                wallsTileMap.SetCell(mapLayer, worldPos, wallsSourceId, tile);
            }
            catch (Exception e)
            {
                Logger.Debug($"Decorator: error placing wall at {localPos}: {e.Message}", false);
            }
        }
    }

    public void AddSectionDecorationsAndObstacles(
        LevelGenerator.MapSection section,
        int mapWidth,
        Godot.TileMap wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I> decorationTileSelector)
    {
        Vector2 worldOffset = section.WorldOffset;
        Vector2I decorationTile = decorationTileSelector(section.BiomeType);

        foreach (var room in section.Rooms)
        {
            if (room.Size.X <= 5 || room.Size.Y <= 5) continue;

            int roomArea = room.Size.X * room.Size.Y;
            int maxDecorations = roomArea / 16;
            int numDecorations = _random.Next(1, maxDecorations + 1);

            for (int i = 0; i < numDecorations; i++)
            {
                try
                {
                    int x = _random.Next(room.Position.X + 1, room.Position.X + room.Size.X - 1);
                    int y = _random.Next(room.Position.Y + 1, room.Position.Y + room.Size.Y - 1);

                    bool canPlace = true;
                    for (int dx = -1; dx <= 1 && canPlace; dx++)
                    for (int dy = -1; dy <= 1 && canPlace; dy++)
                    {
                        int cx = x + dx, cy = y + dy;
                        if (cx >= 0 && cx < mapWidth && cy >= 0 && cy < mapWidth)
                        {
                            if (section.SectionMask[cx, cy] == LevelGenerator.TileType.Decoration)
                                canPlace = false;
                        }
                    }

                    if (!canPlace) continue;

                    Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
                    wallsTileMap.SetCell(mapLayer, worldPos, wallsSourceId, decorationTile);
                    section.SectionMask[x, y] = LevelGenerator.TileType.Decoration;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Decorator: error placing decoration: {e.Message}", false);
                }
            }
        }
    }

    public void AddSectionHazards(
        LevelGenerator.MapSection section,
        Godot.TileMap floorsTileMap,
        Godot.TileMap wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        int wallsSourceId,
        Func<int, Vector2I> hazardTileSelector)
    {
        Vector2 worldOffset = section.WorldOffset;
        Vector2I hazardTile = hazardTileSelector(section.BiomeType);

        int hazardChance = 20 + (section.BiomeType * 10);
        if (_random.Next(0, 100) >= hazardChance || section.Rooms.Count == 0)
            return;

        try
        {
            int roomIndex = _random.Next(0, section.Rooms.Count);
            Rect2I room = section.Rooms[roomIndex];
            if (room.Size.X <= 5 || room.Size.Y <= 5) return;

            int hazardWidth = _random.Next(2, Math.Min(room.Size.X - 3, 4));
            int hazardHeight = _random.Next(2, Math.Min(room.Size.Y - 3, 4));
            int maxStartX = room.Position.X + room.Size.X - hazardWidth - 2;
            int maxStartY = room.Position.Y + room.Size.Y - hazardHeight - 2;
            int startX = _random.Next(room.Position.X + 2, maxStartX);
            int startY = _random.Next(room.Position.Y + 2, maxStartY);

            for (int x = startX; x < startX + hazardWidth; x++)
            for (int y = startY; y < startY + hazardHeight; y++)
            {
                Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
                floorsTileMap.SetCell(mapLayer, worldPos, floorsSourceId, hazardTile);
                if (hazardTile == new Vector2I(1,1))
                {
                    wallsTileMap.SetCell(mapLayer, worldPos, wallsSourceId, hazardTile);
                }
            }

            Logger.Debug($"Decorator: added {hazardWidth}x{hazardHeight} hazard area", false);
        }
        catch (Exception e)
        {
            Logger.Debug($"Decorator: error adding hazards: {e.Message}", false);
        }
    }
}


