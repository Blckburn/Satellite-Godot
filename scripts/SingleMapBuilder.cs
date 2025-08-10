using Godot;
using System;

/// <summary>
/// Базовые операции для однослойной карты. Оставлено для совместимости.
/// </summary>
public sealed class SingleMapBuilder
{
    private readonly Random _random;

    public SingleMapBuilder(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public void FillBaseFloor(int mapWidth, int mapHeight, Vector2I backgroundTile, Godot.TileMapLayer floors, int mapLayer, int floorsSourceId, LevelGenerator.TileType[,] mapMask)
    {
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            floors.SetCell(mapLayer, new Vector2I(x, y), floorsSourceId, backgroundTile);
            mapMask[x, y] = LevelGenerator.TileType.Background;
        }
    }

    public void FillDecorBackground(int mapWidth, int mapHeight, Vector2I backgroundTile, Godot.TileMapLayer walls, int mapLayer, int wallsSourceId, LevelGenerator.TileType[,] mapMask)
    {
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (mapMask[x, y] == LevelGenerator.TileType.None)
            {
                walls.SetCell(mapLayer, new Vector2I(x, y), wallsSourceId, backgroundTile);
                mapMask[x, y] = LevelGenerator.TileType.Background;
            }
        }
    }
}


