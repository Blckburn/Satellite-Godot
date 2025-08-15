using Godot;
using System;
using System.Collections.Generic;

public static class WorldContainerPlacer
{
    public sealed class Context
    {
        public PackedScene ContainerScene { get; init; }
        public Node YSortContainer { get; init; }
        public Random Random { get; init; }
        public Func<Vector2I, Vector2> MapTileToIsometricWorld { get; init; }
    }

    public static int GenerateContainers(
        Context ctx,
        LevelGenerator.TileType[,] worldMask,
        int[,] worldBiome,
        int worldTilesX,
        int worldTilesY)
    {
        if (ctx == null || ctx.ContainerScene == null || ctx.YSortContainer == null || ctx.Random == null || ctx.MapTileToIsometricWorld == null)
        {
            Logger.Error("WorldContainerPlacer: invalid context");
            return 0;
        }

        int containersPlaced = 0;
        int maxContainers = (worldTilesX * worldTilesY) / 200; // ~0.5%

        const int BORDER_MARGIN = 3;
        int startX = BORDER_MARGIN;
        int endX = worldTilesX - BORDER_MARGIN;
        int startY = BORDER_MARGIN;
        int endY = worldTilesY - BORDER_MARGIN;

        for (int x = startX; x < endX && containersPlaced < maxContainers; x += 6)
        {
            for (int y = startY; y < endY && containersPlaced < maxContainers; y += 6)
            {
                if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                if (!IsAreaWalkable(worldMask, x, y, worldTilesX, worldTilesY, 2)) continue;

                int biome = worldBiome[x, y];
                if (ctx.Random.NextDouble() > 0.3) continue;

                if (PlaceWorldContainer(ctx, x, y, biome))
                {
                    containersPlaced++;
                }
            }
        }

        Logger.Info($"📦 КОНТЕЙНЕРЫ: {containersPlaced} размещено");
        return containersPlaced;
    }

    private static bool IsAreaWalkable(LevelGenerator.TileType[,] worldMask, int centerX, int centerY, int worldTilesX, int worldTilesY, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) return false;
                if (worldMask[x, y] != LevelGenerator.TileType.Room) return false;
            }
        }
        return true;
    }

    private static bool PlaceWorldContainer(Context ctx, int worldX, int worldY, int biome)
    {
        try
        {
            var containerNode = ctx.ContainerScene.Instantiate<Container>();
            if (containerNode == null) return false;

            Vector2 worldPosition = ctx.MapTileToIsometricWorld(new Vector2I(worldX, worldY));
            worldPosition.Y += 16;
            containerNode.Position = worldPosition;

            // Базовая инициализация контейнера
            containerNode.ContainerName = PickBiomeContainerName(ctx.Random, biome);
            containerNode.InventorySize = ctx.Random.Next(5, 16);

            ctx.YSortContainer.AddChild(containerNode);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"WorldContainerPlacer: error placing container at ({worldX},{worldY}): {e.Message}");
            return false;
        }
    }

    private static string PickBiomeContainerName(Random rng, int biome)
    {
        // Лёгкий набор имён по биомам без внешних зависимостей
        string[][] names = new[]
        {
            new [] { "Storage Box", "Abandoned Crate" }, // 0 Grassland
            new [] { "Wooden Chest", "Organic Container" }, // 1 Forest
            new [] { "Ancient Vessel", "Sand-Covered Chest" }, // 2 Desert
            new [] { "Frozen Container", "Cryo Storage" }, // 3 Ice
            new [] { "Tech Storage Unit", "Data Repository" }, // 4 Techno
            new [] { "Anomalous Container", "Mysterious Box" }, // 5 Anomal
            new [] { "Heat-Resistant Locker", "Volcanic Storage" }, // 6 Lava
        };
        var list = names[Math.Clamp(biome, 0, names.Length - 1)];
        return list[rng.Next(list.Length)];
    }
}


