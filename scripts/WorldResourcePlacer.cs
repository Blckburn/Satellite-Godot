using Godot;
using System;
using System.Collections.Generic;

public static class WorldResourcePlacer
{
    public sealed class Context
    {
        public PackedScene ResourceNodeScene { get; init; }
        public Node YSortContainer { get; init; }
        public Random Random { get; init; }
        public Func<Vector2I, Vector2> MapTileToIsometricWorld { get; init; }
    }

    public static int GenerateResources(
        Context ctx,
        LevelGenerator.TileType[,] worldMask,
        int[,] worldBiome,
        int worldTilesX,
        int worldTilesY)
    {
        if (ctx == null || ctx.ResourceNodeScene == null || ctx.YSortContainer == null || ctx.Random == null || ctx.MapTileToIsometricWorld == null)
        {
            Logger.Error("WorldResourcePlacer: invalid context");
            return 0;
        }

        int resourcesPlaced = 0;
        int maxResources = (worldTilesX * worldTilesY) / 50; // ~2%

        const int BORDER_MARGIN = 2;
        int startX = BORDER_MARGIN;
        int endX = worldTilesX - BORDER_MARGIN;
        int startY = BORDER_MARGIN;
        int endY = worldTilesY - BORDER_MARGIN;

        for (int x = startX; x < endX && resourcesPlaced < maxResources; x += 2)
        {
            for (int y = startY; y < endY && resourcesPlaced < maxResources; y += 2)
            {
                if (worldMask[x, y] != LevelGenerator.TileType.Room)
                    continue;

                if (!IsAreaWalkable(worldMask, x, y, worldTilesX, worldTilesY, 1))
                    continue;

                int biome = worldBiome[x, y];
                float spawnChance = GetResourceSpawnChance(biome);
                if (ctx.Random.NextDouble() > spawnChance)
                    continue;

                if (PlaceWorldResource(ctx, x, y, biome))
                {
                    resourcesPlaced++;
                }
            }
        }

        Logger.Info($"🎯 РЕСУРСЫ: {resourcesPlaced} размещено");
        return resourcesPlaced;
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

    private static float GetResourceSpawnChance(int biome)
    {
        switch (biome)
        {
            case 0: return 0.15f; // Grassland
            case 1: return 0.20f; // Forest
            case 2: return 0.18f; // Desert
            case 3: return 0.12f; // Ice
            case 4: return 0.25f; // Techno
            case 5: return 0.22f; // Anomal
            case 6: return 0.16f; // Lava Springs
            default: return 0.10f;
        }
    }

    private static bool PlaceWorldResource(Context ctx, int worldX, int worldY, int biome)
    {
        try
        {
            var resourceNode = ctx.ResourceNodeScene.Instantiate<ResourceNode>();
            if (resourceNode == null) return false;

            ResourceType resourceType = SelectResourceTypeForBiome(ctx.Random, biome);
            Item resourceItem = LoadResourceItemForType(resourceType);
            if (resourceItem == null)
            {
                Logger.Error($"WorldResourcePlacer: failed to load ResourceItem for {resourceType}");
                resourceNode.QueueFree();
                return false;
            }

            resourceNode.Type = resourceType;
            resourceNode.ResourceItem = resourceItem;
            resourceNode.ResourceAmount = ctx.Random.Next(1, 4);

            Vector2 worldPosition = ctx.MapTileToIsometricWorld(new Vector2I(worldX, worldY));
            worldPosition.Y += 16;
            resourceNode.Position = worldPosition;

            ctx.YSortContainer.AddChild(resourceNode);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"WorldResourcePlacer: error placing resource at ({worldX},{worldY}): {e.Message}");
            return false;
        }
    }

    private static Item LoadResourceItemForType(ResourceType resourceType)
    {
        string resourcePath = resourceType switch
        {
            ResourceType.Metal => "res://scenes/resources/items/metal_ore.tres",
            ResourceType.Crystal => "res://scenes/resources/items/resource_crystal.tres",
            ResourceType.Organic => "res://scenes/resources/items/organic_matter.tres",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(resourcePath))
        {
            Logger.Error($"WorldResourcePlacer: no ResourceItem path for {resourceType}");
            return null;
        }

        try
        {
            var item = ResourceLoader.Load<Item>(resourcePath);
            if (item == null) Logger.Error($"WorldResourcePlacer: failed to load {resourcePath}");
            return item;
        }
        catch (Exception e)
        {
            Logger.Error($"WorldResourcePlacer: exception loading {resourcePath}: {e.Message}");
            return null;
        }
    }

    private static ResourceType SelectResourceTypeForBiome(Random rng, int biome)
    {
        float r() => (float)rng.NextDouble();
        switch (biome)
        {
            case 0: // Grassland
                { float v = r(); if (v < 0.4f) return ResourceType.Metal; if (v < 0.7f) return ResourceType.Organic; return ResourceType.Crystal; }
            case 1: // Forest
                { float v = r(); if (v < 0.6f) return ResourceType.Organic; if (v < 0.8f) return ResourceType.Metal; return ResourceType.Crystal; }
            case 2: // Desert
                { float v = r(); if (v < 0.5f) return ResourceType.Metal; if (v < 0.8f) return ResourceType.Crystal; return ResourceType.Organic; }
            case 3: // Ice
                { float v = r(); if (v < 0.5f) return ResourceType.Crystal; if (v < 0.8f) return ResourceType.Metal; return ResourceType.Organic; }
            case 4: // Techno
                { float v = r(); if (v < 0.4f) return ResourceType.Metal; if (v < 0.7f) return ResourceType.Crystal; return ResourceType.Organic; }
            case 5: // Anomal
                { float v = r(); if (v < 0.4f) return ResourceType.Crystal; if (v < 0.7f) return ResourceType.Metal; return ResourceType.Organic; }
            case 6: // Lava Springs
                { float v = r(); if (v < 0.5f) return ResourceType.Metal; if (v < 0.8f) return ResourceType.Crystal; return ResourceType.Organic; }
            default:
                return ResourceType.Metal;
        }
    }
}


