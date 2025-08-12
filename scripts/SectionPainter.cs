using Godot;
using System;

public static class SectionPainter
{
    public static void ResetSectionMask(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight)
    {
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
            sectionMask[x, y] = LevelGenerator.TileType.None;
    }

    public static void FillSectionBaseFloor(TileMapLayer floorsTileMap, int floorsSourceId, LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, Vector2 worldOffset, Vector2I backgroundTile)
    {
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
            floorsTileMap.SetCell(worldPos, floorsSourceId, backgroundTile);
            sectionMask[x, y] = LevelGenerator.TileType.Background;
        }
    }

    public static void CreateSectionRoom(TileMapLayer floorsTileMap, int floorsSourceId, LevelGenerator.TileType[,] sectionMask, Vector2 worldOffset, int mapWidth, int mapHeight, Rect2I room, Vector2I floorTile)
    {
        for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
        for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
        {
            if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight) continue;
            Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
            floorsTileMap.SetCell(worldPos, floorsSourceId, floorTile);
            sectionMask[x, y] = LevelGenerator.TileType.Room;
        }
    }

    public static void FillSectionWithBackgroundTiles(TileMapLayer wallsTileMap, int wallsSourceId, LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, Vector2 worldOffset, Func<Vector2I, Vector2I> selectWallTile)
    {
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (sectionMask[x, y] == LevelGenerator.TileType.Room || sectionMask[x, y] == LevelGenerator.TileType.Corridor) continue;
            Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
            var wallTile = selectWallTile(worldPos);
            wallsTileMap.SetCell(worldPos, wallsSourceId, wallTile);
            if (sectionMask[x, y] == LevelGenerator.TileType.None)
                sectionMask[x, y] = LevelGenerator.TileType.Background;
        }
    }
}


