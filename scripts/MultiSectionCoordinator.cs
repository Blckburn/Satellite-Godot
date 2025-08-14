using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Координирует мультисекционную карту: создание секций, соединение, мосты.
/// </summary>
public sealed class MultiSectionCoordinator
{
    private readonly Random _random;

    public MultiSectionCoordinator(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public void CreateMapSections(
        int gridWidth,
        int gridHeight,
        int mapWidth,
        int mapHeight,
        int sectionSpacing,
        int maxBiomeTypes,
        List<LevelGenerator.MapSection> sections,
        Func<int, string> biomeName)
    {
        sections.Clear();
        for (int y = 0; y < gridHeight; y++)
        for (int x = 0; x < gridWidth; x++)
        {
            int biomeType = _random.Next(0, maxBiomeTypes);
            var section = new LevelGenerator.MapSection(biomeType, x, y, mapWidth, mapHeight)
            {
                WorldOffset = new Vector2(
                    x * (mapWidth + sectionSpacing),
                    y * (mapHeight + sectionSpacing)
                )
            };
            sections.Add(section);
            Logger.Debug($"Created section at grid ({x},{y}) with biome {biomeName(biomeType)}", false);
        }
    }

    public void SelectSpawnSection(
        List<LevelGenerator.MapSection> sections,
        out Vector2 spawnWorldPosition)
    {
        spawnWorldPosition = Vector2.Zero;
        if (sections.Count == 0)
        {
            Logger.Error("No sections available for player spawn!");
            return;
        }

        int sectionIndex = _random.Next(0, sections.Count);
        var spawnSection = sections[sectionIndex];
        if (!spawnSection.SpawnPosition.HasValue)
        {
            Logger.Error($"Selected section at ({spawnSection.GridX}, {spawnSection.GridY}) has no spawn position!");
            return;
        }

        // Локальные тайловые координаты спавна + смещение секции в тайлах
        Vector2 localTile = spawnSection.SpawnPosition.Value;
        Vector2 offTiles = spawnSection.WorldOffset;
        Vector2I worldTile = new Vector2I((int)(localTile.X + offTiles.X), (int)(localTile.Y + offTiles.Y));

        // Преобразуем в изометрические мировые пиксели
        spawnWorldPosition = MapTileToIsometricWorld(worldTile);
        Logger.Debug($"Selected spawn position in section ({spawnSection.GridX}, {spawnSection.GridY}) (tile {worldTile}) -> world {spawnWorldPosition}", true);
    }

    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // Согласуем с формулой проекта (тайл 32x16, соотношение 2:1)
        float tileWidth = 32.0f;
        float tileHeight = 16.0f;
        float x = (tilePos.X - tilePos.Y) * tileWidth / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileHeight / 2.0f;
        return new Vector2(x, y);
    }

    public void ConnectAdjacentSections(
        int gridWidth,
        int gridHeight,
        List<LevelGenerator.MapSection> sections,
        Action<LevelGenerator.MapSection, LevelGenerator.MapSection> connectHorizontally,
        Action<LevelGenerator.MapSection, LevelGenerator.MapSection> connectVertically)
    {
        Logger.Debug("Connecting adjacent sections", true);

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth - 1; x++)
            {
                var left = sections.Find(s => s.GridX == x && s.GridY == y);
                var right = sections.Find(s => s.GridX == x + 1 && s.GridY == y);
                if (left != null && right != null)
                {
                    Logger.Debug($"Connecting sections horizontally: ({left.GridX},{left.GridY}) to ({right.GridX},{right.GridY})", false);
                    connectHorizontally(left, right);
                }
            }
        }

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight - 1; y++)
            {
                var top = sections.Find(s => s.GridX == x && s.GridY == y);
                var bottom = sections.Find(s => s.GridX == x && s.GridY == y + 1);
                if (top != null && bottom != null)
                {
                    Logger.Debug($"Connecting sections vertically: ({top.GridX},{top.GridY}) to ({bottom.GridX},{bottom.GridY})", false);
                    connectVertically(top, bottom);
                }
            }
        }

        Logger.Debug("All adjacent sections connected successfully", true);
    }

    public void CreateHorizontalCorridorPart(
        LevelGenerator.MapSection section,
        int startX,
        int endX,
        int passageY,
        int tunnelWidth,
        Vector2I floorTile,
        int mapWidth,
        int mapHeight,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        Action<LevelGenerator.MapSection, int, int, int, int, Vector2I> connectNearbyRooms)
    {
        try
        {
            Vector2 worldOffset = section.WorldOffset;
            for (int y = passageY - tunnelWidth / 2; y <= passageY + tunnelWidth / 2; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (y >= 0 && y < mapHeight && x >= 0 && x < mapWidth)
                    {
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
                        floorsTileMap.SetCell(worldPos, floorsSourceId, floorTile);
                        if (wallsTileMap != null) wallsTileMap.EraseCell(worldPos);
                        if (section.SectionMask[x, y] != LevelGenerator.TileType.Room)
                        {
                            section.SectionMask[x, y] = LevelGenerator.TileType.Corridor;
                        }
                    }
                }
            }

            connectNearbyRooms?.Invoke(section, startX, endX, passageY, tunnelWidth, floorTile);
        }
        catch (Exception e)
        {
            Logger.Error($"Error creating horizontal corridor part: {e.Message}");
        }
    }

    public void CreateVerticalCorridorPart(
        LevelGenerator.MapSection section,
        int startY,
        int endY,
        int passageX,
        int tunnelWidth,
        Vector2I floorTile,
        int mapWidth,
        int mapHeight,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        Action<LevelGenerator.MapSection, int, int, int, int, Vector2I, bool> connectNearbyRooms)
    {
        try
        {
            Vector2 worldOffset = section.WorldOffset;
            for (int x = passageX - tunnelWidth / 2; x <= passageX + tunnelWidth / 2; x++)
            {
                for (int y = startY; y < endY; y++)
                {
                    if (y >= 0 && y < mapHeight && x >= 0 && x < mapWidth)
                    {
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);
                        floorsTileMap.SetCell(worldPos, floorsSourceId, floorTile);
                        if (wallsTileMap != null) wallsTileMap.EraseCell(worldPos);
                        if (section.SectionMask[x, y] != LevelGenerator.TileType.Room)
                        {
                            section.SectionMask[x, y] = LevelGenerator.TileType.Corridor;
                        }
                    }
                }
            }

            connectNearbyRooms?.Invoke(section, passageX, tunnelWidth, startY, endY, floorTile, false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error creating vertical corridor part: {e.Message}");
        }
    }

    public void FillHorizontalGap(
        LevelGenerator.MapSection leftSection,
        LevelGenerator.MapSection rightSection,
        int passageY,
        int tunnelWidth,
        int sectionSpacing,
        int mapWidth,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        Func<int, Vector2I> getFloorTile)
    {
        try
        {
            Vector2I leftFloorTile = getFloorTile(leftSection.BiomeType);
            Vector2I rightFloorTile = getFloorTile(rightSection.BiomeType);
            for (int y = passageY - tunnelWidth / 2; y <= passageY + tunnelWidth / 2; y++)
            {
                for (int bridgeX = 0; bridgeX < sectionSpacing; bridgeX++)
                {
                    Vector2I bridgeWorldPos = new Vector2I(
                        (int)leftSection.WorldOffset.X + mapWidth + bridgeX,
                        (int)leftSection.WorldOffset.Y + y
                    );
                    Vector2I floorTile = (bridgeX < sectionSpacing / 2) ? leftFloorTile : rightFloorTile;
                    floorsTileMap.SetCell(bridgeWorldPos, floorsSourceId, floorTile);
                    wallsTileMap.EraseCell(bridgeWorldPos);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error filling horizontal gap: {e.Message}");
        }
    }

    public void FillVerticalGap(
        LevelGenerator.MapSection topSection,
        LevelGenerator.MapSection bottomSection,
        int passageX,
        int tunnelWidth,
        int sectionSpacing,
        int mapHeight,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        Func<int, Vector2I> getFloorTile)
    {
        try
        {
            Vector2I topFloorTile = getFloorTile(topSection.BiomeType);
            Vector2I bottomFloorTile = getFloorTile(bottomSection.BiomeType);
            for (int x = passageX - tunnelWidth / 2; x <= passageX + tunnelWidth / 2; x++)
            {
                for (int bridgeY = 0; bridgeY < sectionSpacing; bridgeY++)
                {
                    Vector2I bridgeWorldPos = new Vector2I(
                        (int)topSection.WorldOffset.X + x,
                        (int)topSection.WorldOffset.Y + mapHeight + bridgeY
                    );
                    Vector2I floorTile = (bridgeY < sectionSpacing / 2) ? topFloorTile : bottomFloorTile;
                    floorsTileMap.SetCell(bridgeWorldPos, floorsSourceId, floorTile);
                    wallsTileMap.EraseCell(bridgeWorldPos);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error filling vertical gap: {e.Message}");
        }
    }

    public void ConnectSectionsHorizontally(
        LevelGenerator.MapSection leftSection,
        LevelGenerator.MapSection rightSection,
        int mapWidth,
        int mapHeight,
        int connectorWidth,
        int sectionSpacing,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        int wallsSourceId,
        BiomePalette biome,
        SectionConnector sectionConnector,
        CorridorCarver corridorCarver)
    {
        int passageY = mapHeight / 2;
        Vector2I leftFloorTile = biome.GetFloorTileForBiome(leftSection.BiomeType);
        Vector2I rightFloorTile = biome.GetFloorTileForBiome(rightSection.BiomeType);
        int tunnelWidth = Math.Max(3, connectorWidth);

        CreateHorizontalCorridorPart(
            leftSection,
            mapWidth - 10,
            mapWidth,
            passageY,
            tunnelWidth,
            leftFloorTile,
            mapWidth,
            mapHeight,
            floorsTileMap,
            wallsTileMap,
            mapLayer,
            floorsSourceId,
            (section, x1, x2, y, width, floor) => corridorCarver.FindAndConnectToNearbyRooms(
                section, x1, x2, y, width, floor, true,
                mapWidth, mapHeight,
                (s, sx, ex, py, ft) => { /* handled in LevelGenerator via explicit method */ },
                (s, px, sy, ey, ft) => { /* handled in LevelGenerator via explicit method */ }
            )
        );

        CreateHorizontalCorridorPart(
            rightSection,
            0,
            10,
            passageY,
            tunnelWidth,
            rightFloorTile,
            mapWidth,
            mapHeight,
            floorsTileMap,
            wallsTileMap,
            mapLayer,
            floorsSourceId,
            (section, x1, x2, y, width, floor) => corridorCarver.FindAndConnectToNearbyRooms(
                section, x1, x2, y, width, floor, true,
                mapWidth, mapHeight,
                (s, sx, ex, py, ft) => { },
                (s, px, sy, ey, ft) => { }
            )
        );

        if (sectionSpacing > 0)
        {
            FillHorizontalGap(
                leftSection,
                rightSection,
                passageY,
                tunnelWidth,
                sectionSpacing,
                mapWidth,
                floorsTileMap,
                wallsTileMap,
                mapLayer,
                floorsSourceId,
                biomeType => biome.GetFloorTileForBiome(biomeType)
            );
        }

        sectionConnector.AddDecorativeHorizontalWalls(
            leftSection,
            rightSection,
            passageY,
            tunnelWidth,
            mapWidth,
            mapHeight,
            wallsTileMap,
            mapLayer,
            wallsSourceId,
            (biomeType, pos) => biome.GetWallTileForBiomeEx(biomeType, pos)
        );

        sectionConnector.AddWallsAroundHorizontalConnector(
            leftSection,
            rightSection,
            passageY,
            tunnelWidth,
            mapWidth,
            mapHeight,
            sectionSpacing,
            wallsTileMap,
            mapLayer,
            wallsSourceId,
            (biomeType, pos) => biome.GetWallTileForBiomeEx(biomeType, pos)
        );
    }

    public void ConnectSectionsVertically(
        LevelGenerator.MapSection topSection,
        LevelGenerator.MapSection bottomSection,
        int mapWidth,
        int mapHeight,
        int connectorWidth,
        int sectionSpacing,
        Godot.TileMapLayer floorsTileMap,
        Godot.TileMapLayer wallsTileMap,
        int mapLayer,
        int floorsSourceId,
        int wallsSourceId,
        BiomePalette biome,
        SectionConnector sectionConnector,
        CorridorCarver corridorCarver)
    {
        int passageX = mapWidth / 2;
        Vector2I topFloorTile = biome.GetFloorTileForBiome(topSection.BiomeType);
        Vector2I bottomFloorTile = biome.GetFloorTileForBiome(bottomSection.BiomeType);
        int tunnelWidth = Math.Max(3, connectorWidth);

        CreateVerticalCorridorPart(
            topSection,
            mapHeight - 10,
            mapHeight,
            passageX,
            tunnelWidth,
            topFloorTile,
            mapWidth,
            mapHeight,
            floorsTileMap,
            wallsTileMap,
            mapLayer,
            floorsSourceId,
            (section, x, width, y1, y2, floor, isHorizontal) => corridorCarver.FindAndConnectToNearbyRooms(
                section, x, width, y1, y2, floor, false,
                mapWidth, mapHeight,
                (s, sx, ex, py, ft) => { },
                (s, px, sy, ey, ft) => { }
            )
        );

        CreateVerticalCorridorPart(
            bottomSection,
            0,
            10,
            passageX,
            tunnelWidth,
            bottomFloorTile,
            mapWidth,
            mapHeight,
            floorsTileMap,
            wallsTileMap,
            mapLayer,
            floorsSourceId,
            (section, x, width, y1, y2, floor, isHorizontal) => corridorCarver.FindAndConnectToNearbyRooms(
                section, x, width, y1, y2, floor, false,
                mapWidth, mapHeight,
                (s, sx, ex, py, ft) => { },
                (s, px, sy, ey, ft) => { }
            )
        );

        if (sectionSpacing > 0)
        {
            FillVerticalGap(
                topSection,
                bottomSection,
                passageX,
                tunnelWidth,
                sectionSpacing,
                mapHeight,
                floorsTileMap,
                wallsTileMap,
                mapLayer,
                floorsSourceId,
                biomeType => biome.GetFloorTileForBiome(biomeType)
            );
        }

        sectionConnector.AddDecorativeVerticalWalls(
            topSection,
            bottomSection,
            passageX,
            tunnelWidth,
            mapWidth,
            mapHeight,
            wallsTileMap,
            mapLayer,
            wallsSourceId,
            (biomeType, pos) => biome.GetWallTileForBiomeEx(biomeType, pos)
        );

        sectionConnector.AddWallsAroundVerticalConnector(
            topSection,
            bottomSection,
            passageX,
            tunnelWidth,
            mapWidth,
            mapHeight,
            sectionSpacing,
            wallsTileMap,
            mapLayer,
            wallsSourceId,
            (biomeType, pos) => biome.GetWallTileForBiomeEx(biomeType, pos)
        );
    }
}


