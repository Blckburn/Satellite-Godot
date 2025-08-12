using Godot;
using System;

public static class BorderWallsBuilder
{
    public static void AddBiomeBasedBorderWalls(
        LevelGenerator.TileType[,] worldMask,
        int[,] worldBiome,
        int worldTilesX,
        int worldTilesY,
        Godot.TileMapLayer wallsTileMap,
        int wallsSourceId,
        BiomePalette biome,
        Action<Godot.Vector2I, Godot.Vector2, Godot.Vector2, Godot.Vector2, Godot.Vector2> onCornersComputed)
    {
        const int WALL_THICKNESS = 1;

        Vector2I topLeft = new Vector2I(0, 0);
        Vector2I topRight = new Vector2I(worldTilesX - 1, 0);
        Vector2I bottomLeft = new Vector2I(0, worldTilesY - 1);
        Vector2I bottomRight = new Vector2I(worldTilesX - 1, worldTilesY - 1);

        Vector2 topLeftWorld = MapTileToIsometricWorld(topLeft);
        Vector2 topRightWorld = MapTileToIsometricWorld(topRight);
        Vector2 bottomLeftWorld = MapTileToIsometricWorld(bottomLeft);
        Vector2 bottomRightWorld = MapTileToIsometricWorld(bottomRight);

        onCornersComputed?.Invoke(topLeft, topLeftWorld, topRightWorld, bottomLeftWorld, bottomRightWorld);

        for (int x = -WALL_THICKNESS; x < worldTilesX + WALL_THICKNESS; x++)
        {
            for (int y = -WALL_THICKNESS; y < worldTilesY + WALL_THICKNESS; y++)
            {
                bool isOutsideMap = (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY);
                if (!isOutsideMap) continue;
                int biomeForWall = GetNearestBiomeForOuterWall(worldBiome, x, y, worldTilesX, worldTilesY);
                if (wallsTileMap != null)
                {
                    Vector2I tilePos = new Vector2I(x, y);
                    // Внешние стены временно отключены
                    // var wallInfo = biome.GetWallTileForBiomeEx(biomeForWall, tilePos);
                    // wallsTileMap.SetCell(tilePos, wallInfo.sourceId, wallInfo.tile);
                }
            }
        }
    }

    private static int GetNearestBiomeForOuterWall(int[,] worldBiome, int wallX, int wallY, int worldTilesX, int worldTilesY)
    {
        int nearestX = Math.Max(0, Math.Min(worldTilesX - 1, wallX));
        int nearestY = Math.Max(0, Math.Min(worldTilesY - 1, wallY));
        return worldBiome[nearestX, nearestY];
    }

    private static Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        Vector2I tileSize = new Vector2I(32, 16);
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;
        return new Vector2(x, y);
    }
}


