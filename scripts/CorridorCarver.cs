using Godot;
using System;

/// <summary>
/// Отвечает за соединение комнат коридорами внутри секции.
/// </summary>
public sealed class CorridorCarver
{
    private readonly Random _random;

    public CorridorCarver(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public void ConnectSectionRooms(
        LevelGenerator.MapSection section,
        int mapWidth,
        int mapHeight,
        int corridorWidth,
        Func<int, Vector2I> floorTileSelector,
        Godot.TileMapLayer floorsTileMap,
        int mapLayer,
        int floorsSourceId)
    {
        if (section.Rooms.Count < 2)
        {
            Logger.Debug($"Not enough rooms to connect in section ({section.GridX},{section.GridY})", false);
            return;
        }

        section.Rooms.Sort((a, b) => (a.Position.X + a.Position.Y).CompareTo(b.Position.X + b.Position.Y));

        for (int i = 0; i < section.Rooms.Count; i++)
        {
            int nextIndex = (i + 1) % section.Rooms.Count;
            ConnectTwoRooms(section, section.Rooms[i], section.Rooms[nextIndex], mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);

            if (_random.Next(0, 100) < 30)
            {
                int randomIndex = _random.Next(0, section.Rooms.Count);
                if (randomIndex != i && randomIndex != nextIndex)
                {
                    ConnectTwoRooms(section, section.Rooms[i], section.Rooms[randomIndex], mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);
                }
            }
        }
    }

    private void ConnectTwoRooms(
        LevelGenerator.MapSection section,
        Rect2I roomA,
        Rect2I roomB,
        int mapWidth,
        int mapHeight,
        int corridorWidth,
        Func<int, Vector2I> floorTileSelector,
        Godot.TileMapLayer floorsTileMap,
        int mapLayer,
        int floorsSourceId)
    {
        try
        {
            Vector2I centerA = roomA.Position + roomA.Size / 2;
            Vector2I centerB = roomB.Position + roomB.Size / 2;

            if (_random.Next(0, 2) == 0)
            {
                CreateHorizontalTunnel(section, centerA.X, centerB.X, centerA.Y, mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);
                CreateVerticalTunnel(section, centerA.Y, centerB.Y, centerB.X, mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);
            }
            else
            {
                CreateVerticalTunnel(section, centerA.Y, centerB.Y, centerA.X, mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);
                CreateHorizontalTunnel(section, centerA.X, centerB.X, centerB.Y, mapWidth, mapHeight, corridorWidth, floorTileSelector, floorsTileMap, mapLayer, floorsSourceId);
            }

            Logger.Debug($"Connected rooms in section ({section.GridX},{section.GridY}) at ({roomA.Position.X},{roomA.Position.Y}) and ({roomB.Position.X},{roomB.Position.Y})", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting rooms in section ({section.GridX},{section.GridY}): {e.Message}");
        }
    }

    private void CreateHorizontalTunnel(
        LevelGenerator.MapSection section,
        int x1,
        int x2,
        int y,
        int mapWidth,
        int mapHeight,
        int corridorWidth,
        Func<int, Vector2I> floorTileSelector,
        Godot.TileMapLayer floorsTileMap,
        int mapLayer,
        int floorsSourceId)
    {
        Vector2I floorTile = floorTileSelector(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        int start = Math.Min(x1, x2);
        int end = Math.Max(x1, x2);

        for (int x = start; x <= end; x++)
        {
            for (int offset = 0; offset < corridorWidth; offset++)
            {
                int yPos = y - (corridorWidth / 2) + offset;
                if (yPos >= 0 && yPos < mapHeight && x >= 0 && x < mapWidth)
                {
                    try
                    {
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + yPos);
                        floorsTileMap.SetCell(worldPos, floorsSourceId, floorTile);
                        if (section.SectionMask[x, yPos] != LevelGenerator.TileType.Room)
                        {
                            section.SectionMask[x, yPos] = LevelGenerator.TileType.Corridor;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private void CreateVerticalTunnel(
        LevelGenerator.MapSection section,
        int y1,
        int y2,
        int x,
        int mapWidth,
        int mapHeight,
        int corridorWidth,
        Func<int, Vector2I> floorTileSelector,
        Godot.TileMapLayer floorsTileMap,
        int mapLayer,
        int floorsSourceId)
    {
        Vector2I floorTile = floorTileSelector(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        int start = Math.Min(y1, y2);
        int end = Math.Max(y1, y2);

        for (int y = start; y <= end; y++)
        {
            for (int offset = 0; offset < corridorWidth; offset++)
            {
                int xPos = x - (corridorWidth / 2) + offset;
                if (xPos >= 0 && xPos < mapWidth && y >= 0 && y < mapHeight)
                {
                    try
                    {
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + xPos, (int)worldOffset.Y + y);
                        floorsTileMap.SetCell(worldPos, floorsSourceId, floorTile);
                        if (section.SectionMask[xPos, y] != LevelGenerator.TileType.Room)
                        {
                            section.SectionMask[xPos, y] = LevelGenerator.TileType.Corridor;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    // Находит и соединяет коридор с ближайшими комнатами
    public void FindAndConnectToNearbyRooms(
        LevelGenerator.MapSection section,
        int x1,
        int x2,
        int y,
        int width,
        Vector2I floorTile,
        bool isHorizontal,
        int mapWidth,
        int mapHeight,
        Action<LevelGenerator.MapSection, int, int, int, Vector2I> createHorizontalConnection,
        Action<LevelGenerator.MapSection, int, int, int, Vector2I> createVerticalConnection)
    {
        try
        {
            if (section.Rooms.Count == 0) return;

            if (isHorizontal)
            {
                foreach (Rect2I room in section.Rooms)
                {
                    if (room.Position.X > x2 || (room.Position.X + room.Size.X) < x1) continue;
                    int roomTopY = room.Position.Y;
                    int roomBottomY = room.Position.Y + room.Size.Y;

                    if (roomBottomY < y - width / 2 && y - width / 2 - roomBottomY < 10)
                    {
                        int passageX = Math.Max(room.Position.X + room.Size.X / 2, x1);
                        passageX = Math.Min(passageX, Math.Min(x2, room.Position.X + room.Size.X));
                        createVerticalConnection(section, passageX, roomBottomY, y - width / 2, floorTile);
                    }
                    else if (roomTopY > y + width / 2 && roomTopY - (y + width / 2) < 10)
                    {
                        int passageX = Math.Max(room.Position.X + room.Size.X / 2, x1);
                        passageX = Math.Min(passageX, Math.Min(x2, room.Position.X + room.Size.X));
                        createVerticalConnection(section, passageX, y + width / 2, roomTopY, floorTile);
                    }
                }
            }
            else
            {
                int passageX = x1;
                int tunnelWidth = x2;
                int startY = y;
                int endY = width;

                foreach (Rect2I room in section.Rooms)
                {
                    if (room.Position.Y > endY || (room.Position.Y + room.Size.Y) < startY) continue;
                    int roomLeftX = room.Position.X;
                    int roomRightX = room.Position.X + room.Size.X;

                    if (roomRightX < passageX - tunnelWidth / 2 && passageX - tunnelWidth / 2 - roomRightX < 10)
                    {
                        int passageY = Math.Max(room.Position.Y + room.Size.Y / 2, startY);
                        passageY = Math.Min(passageY, Math.Min(endY, room.Position.Y + room.Size.Y));
                        createHorizontalConnection(section, roomRightX, passageX - tunnelWidth / 2, passageY, floorTile);
                    }
                    else if (roomLeftX > passageX + tunnelWidth / 2 && roomLeftX - (passageX + tunnelWidth / 2) < 10)
                    {
                        int passageY = Math.Max(room.Position.Y + room.Size.Y / 2, startY);
                        passageY = Math.Min(passageY, Math.Min(endY, room.Position.Y + room.Size.Y));
                        createHorizontalConnection(section, passageX + tunnelWidth / 2, roomLeftX, passageY, floorTile);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"CorridorCarver: error connecting corridor to nearby rooms: {e.Message}");
        }
    }
}


