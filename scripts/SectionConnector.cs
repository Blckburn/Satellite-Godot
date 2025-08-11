using Godot;
using System;

/// <summary>
/// Соединяет секции между собой проходами и декоративными стенами (межсекционные переходы).
/// </summary>
public sealed class SectionConnector
{
    private readonly Random _random;

    public SectionConnector(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public void AddDecorativeHorizontalWalls(
        LevelGenerator.MapSection leftSection,
        LevelGenerator.MapSection rightSection,
        int passageY,
        int tunnelWidth,
        int mapWidth,
        int mapHeight,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        Vector2I leftWallTile = wallTileSelector(leftSection.BiomeType, new Vector2I(0, 0));
        Vector2I rightWallTile = wallTileSelector(rightSection.BiomeType, new Vector2I(0, 0));

        int topWallY = passageY - tunnelWidth / 2 - 1;
        int bottomWallY = passageY + tunnelWidth / 2 + 1;
        if (topWallY < 0 || bottomWallY >= mapHeight) return;

        // Рисуем только у краёв секций, где коридоры подходят к стыку
        int edgeRange = 12; // ширина декоративной зоны у края секции
        int leftStart = Math.Max(0, mapWidth - edgeRange);
        int leftEnd = mapWidth - 1;
        int rightStart = 0;
        int rightEnd = Math.Min(mapWidth - 1, edgeRange - 1);

        for (int x = leftStart; x <= leftEnd; x++)
        {
            wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + x, (int)leftSection.WorldOffset.Y + topWallY), wallsSourceId, leftWallTile);
            wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + x, (int)leftSection.WorldOffset.Y + bottomWallY), wallsSourceId, leftWallTile);
        }

        for (int x = rightStart; x <= rightEnd; x++)
        {
            wallsTileMap.SetCell(new Vector2I((int)rightSection.WorldOffset.X + x, (int)rightSection.WorldOffset.Y + topWallY), wallsSourceId, rightWallTile);
            wallsTileMap.SetCell(new Vector2I((int)rightSection.WorldOffset.X + x, (int)rightSection.WorldOffset.Y + bottomWallY), wallsSourceId, rightWallTile);
        }
    }

    public void AddDecorativeVerticalWalls(
        LevelGenerator.MapSection topSection,
        LevelGenerator.MapSection bottomSection,
        int passageX,
        int tunnelWidth,
        int mapWidth,
        int mapHeight,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        Vector2I topWallTile = wallTileSelector(topSection.BiomeType, new Vector2I(0, 0));
        Vector2I bottomWallTile = wallTileSelector(bottomSection.BiomeType, new Vector2I(0, 0));

        int leftWallX = passageX - tunnelWidth / 2 - 1;
        int rightWallX = passageX + tunnelWidth / 2 + 1;
        if (leftWallX < 0 || rightWallX >= mapWidth) return;

        // Рисуем только у нижней/верхней кромки секций, где вертикальные коридоры подходят к стыку
        int edgeRange = 12;
        int topStartY = Math.Max(0, mapHeight - edgeRange);
        int topEndY = mapHeight - 1;
        int bottomStartY = 0;
        int bottomEndY = Math.Min(mapHeight - 1, edgeRange - 1);

        for (int y = topStartY; y <= topEndY; y++)
        {
            wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + leftWallX, (int)topSection.WorldOffset.Y + y), wallsSourceId, topWallTile);
            wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + rightWallX, (int)topSection.WorldOffset.Y + y), wallsSourceId, topWallTile);
        }

        for (int y = bottomStartY; y <= bottomEndY; y++)
        {
            wallsTileMap.SetCell(new Vector2I((int)bottomSection.WorldOffset.X + leftWallX, (int)bottomSection.WorldOffset.Y + y), wallsSourceId, bottomWallTile);
            wallsTileMap.SetCell(new Vector2I((int)bottomSection.WorldOffset.X + rightWallX, (int)bottomSection.WorldOffset.Y + y), wallsSourceId, bottomWallTile);
        }
    }

    public void AddWallsAroundHorizontalConnector(
        LevelGenerator.MapSection leftSection,
        LevelGenerator.MapSection rightSection,
        int passageY,
        int tunnelWidth,
        int mapWidth,
        int mapHeight,
        int sectionSpacing,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        try
        {
            Vector2I leftWallTile = wallTileSelector(leftSection.BiomeType, new Vector2I(0, 0));
            Vector2I rightWallTile = wallTileSelector(rightSection.BiomeType, new Vector2I(0, 0));

            int topWallY = passageY - tunnelWidth / 2 - 1;
            int bottomWallY = passageY + tunnelWidth / 2 + 1;
            if (topWallY < 0 || bottomWallY >= mapHeight) return;

            for (int x = mapWidth - sectionSpacing; x < mapWidth; x++)
            {
                wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + x, (int)leftSection.WorldOffset.Y + topWallY), wallsSourceId, leftWallTile);
                wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + x, (int)leftSection.WorldOffset.Y + bottomWallY), wallsSourceId, leftWallTile);
            }

            for (int x = 0; x < sectionSpacing; x++)
            {
                wallsTileMap.SetCell(new Vector2I((int)rightSection.WorldOffset.X + x, (int)rightSection.WorldOffset.Y + topWallY), wallsSourceId, rightWallTile);
                wallsTileMap.SetCell(new Vector2I((int)rightSection.WorldOffset.X + x, (int)rightSection.WorldOffset.Y + bottomWallY), wallsSourceId, rightWallTile);
            }

            for (int bridgeX = 0; bridgeX < sectionSpacing; bridgeX++)
            {
                Vector2I tile = (bridgeX < sectionSpacing / 2) ? leftWallTile : rightWallTile;
                wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + mapWidth + bridgeX, (int)leftSection.WorldOffset.Y + topWallY), wallsSourceId, tile);
                wallsTileMap.SetCell(new Vector2I((int)leftSection.WorldOffset.X + mapWidth + bridgeX, (int)leftSection.WorldOffset.Y + bottomWallY), wallsSourceId, tile);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"SectionConnector: Error adding walls around horizontal connector: {e.Message}");
        }
    }

    public void AddWallsAroundVerticalConnector(
        LevelGenerator.MapSection topSection,
        LevelGenerator.MapSection bottomSection,
        int passageX,
        int tunnelWidth,
        int mapWidth,
        int mapHeight,
        int sectionSpacing,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        try
        {
            Vector2I topWallTile = wallTileSelector(topSection.BiomeType, new Vector2I(0, 0));
            Vector2I bottomWallTile = wallTileSelector(bottomSection.BiomeType, new Vector2I(0, 0));

            int leftWallX = passageX - tunnelWidth / 2 - 1;
            int rightWallX = passageX + tunnelWidth / 2 + 1;
            if (leftWallX < 0 || rightWallX >= mapWidth) return;

            for (int y = mapHeight - sectionSpacing; y < mapHeight; y++)
            {
                wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + leftWallX, (int)topSection.WorldOffset.Y + y), wallsSourceId, topWallTile);
                wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + rightWallX, (int)topSection.WorldOffset.Y + y), wallsSourceId, topWallTile);
            }

            for (int y = 0; y < sectionSpacing; y++)
            {
                wallsTileMap.SetCell(new Vector2I((int)bottomSection.WorldOffset.X + leftWallX, (int)bottomSection.WorldOffset.Y + y), wallsSourceId, bottomWallTile);
                wallsTileMap.SetCell(new Vector2I((int)bottomSection.WorldOffset.X + rightWallX, (int)bottomSection.WorldOffset.Y + y), wallsSourceId, bottomWallTile);
            }

            for (int bridgeY = 0; bridgeY < sectionSpacing; bridgeY++)
            {
                Vector2I tile = (bridgeY < sectionSpacing / 2) ? topWallTile : bottomWallTile;
                wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + leftWallX, (int)topSection.WorldOffset.Y + mapHeight + bridgeY), wallsSourceId, tile);
                wallsTileMap.SetCell(new Vector2I((int)topSection.WorldOffset.X + rightWallX, (int)topSection.WorldOffset.Y + mapHeight + bridgeY), wallsSourceId, tile);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"SectionConnector: Error adding walls around vertical connector: {e.Message}");
        }
    }

    public void AddDecorativeWallsForConnection(
        LevelGenerator.MapSection section,
        int position,
        int width,
        int start,
        int end,
        bool isHorizontal,
        int mapWidth,
        int mapHeight,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int wallsSourceId,
        Func<int, Vector2I, Vector2I> wallTileSelector)
    {
        try
        {
            Vector2 worldOffset = section.WorldOffset;
            Vector2I wallTile = wallTileSelector(section.BiomeType, new Vector2I(0, 0));

            if (isHorizontal)
            {
                int topWallY = position - width / 2 - 1;
                int bottomWallY = position + width / 2 + 1;
                if (topWallY >= 0 && bottomWallY < mapHeight)
                {
                    for (int x = start; x <= end; x++)
                    {
                        if (x < 0 || x >= mapWidth) continue;
                        Vector2I topWallPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + topWallY);
                        wallsTileMap.SetCell(topWallPos, wallsSourceId, wallTile);
                        Vector2I bottomWallPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + bottomWallY);
                        wallsTileMap.SetCell(bottomWallPos, wallsSourceId, wallTile);
                    }
                }
            }
            else
            {
                int leftWallX = position - width / 2 - 1;
                int rightWallX = position + width / 2 + 1;
                if (leftWallX >= 0 && rightWallX < mapWidth)
                {
                    for (int y = start; y <= end; y++)
                    {
                        if (y < 0 || y >= mapHeight) continue;
                        Vector2I leftWallPos = new Vector2I((int)worldOffset.X + leftWallX, (int)worldOffset.Y + y);
                        wallsTileMap.SetCell(leftWallPos, wallsSourceId, wallTile);
                        Vector2I rightWallPos = new Vector2I((int)worldOffset.X + rightWallX, (int)worldOffset.Y + y);
                        wallsTileMap.SetCell(rightWallPos, wallsSourceId, wallTile);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"SectionConnector: Error adding decorative walls for connection: {e.Message}");
        }
    }
}


