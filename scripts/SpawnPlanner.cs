using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public static class SpawnPlanner
{
    public static Vector2 GetSectionSpawnPosition(
        LevelGenerator.TileType[,] sectionMask,
        System.Collections.Generic.List<Rect2I> rooms,
        int mapWidth,
        int mapHeight,
        System.Random rng)
    {
        if (rooms == null || rooms.Count == 0) return Vector2.Zero;
        int roomIndex = rng.Next(0, rooms.Count);
        Rect2I room = rooms[roomIndex];
        Vector2I center = room.Position + room.Size / 2;

        bool IsWalkableTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight) return false;
            var t = sectionMask[x, y];
            return t == LevelGenerator.TileType.Room || t == LevelGenerator.TileType.Corridor || t == LevelGenerator.TileType.Background;
        }
        bool HasExit(int x, int y)
        {
            var dirs = new[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
            foreach (var d in dirs)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || ny < 0 || nx >= mapWidth || ny >= mapHeight) continue;
                var t = sectionMask[nx, ny];
                if (t == LevelGenerator.TileType.Room || t == LevelGenerator.TileType.Corridor || t == LevelGenerator.TileType.Background) return true;
            }
            return false;
        }

        if (IsWalkableTile(center.X, center.Y) && HasExit(center.X, center.Y)) return new Vector2(center.X, center.Y);

        int maxRadius = System.Math.Max(room.Size.X, room.Size.Y);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (System.Math.Abs(dx) != radius && System.Math.Abs(dy) != radius) continue;
                int tx = center.X + dx, ty = center.Y + dy;
                if (tx < room.Position.X || ty < room.Position.Y || tx >= room.Position.X + room.Size.X || ty >= room.Position.Y + room.Size.Y) continue;
                if (IsWalkableTile(tx, ty) && HasExit(tx, ty)) return new Vector2(tx, ty);
            }
        }

        int bestDist = int.MaxValue; Vector2I bestCorridor = center;
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (sectionMask[x, y] == LevelGenerator.TileType.Corridor && HasExit(x, y))
            {
                int dx = x - center.X, dy = y - center.Y; int d2 = dx*dx + dy*dy;
                if (d2 < bestDist) { bestDist = d2; bestCorridor = new Vector2I(x, y); }
            }
        }
        if (bestDist != int.MaxValue) return new Vector2(bestCorridor.X, bestCorridor.Y);
        return new Vector2(room.Position.X, room.Position.Y);
    }
    public static bool IsPathToTargetExists(
        LevelGenerator.TileType[,] worldMask,
        Vector2I start,
        Vector2I target,
        int worldTilesX,
        int worldTilesY)
    {
        var visited = new bool[worldTilesX, worldTilesY];
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start);
        visited[start.X, start.Y] = true;

        var directions = new Vector2I[]
        {
            new Vector2I(0,1), new Vector2I(0,-1), new Vector2I(1,0), new Vector2I(-1,0)
        };

        int iterations = 0;
        int maxIterations = worldTilesX * worldTilesY;
        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var current = queue.Dequeue();
            if (current.X == target.X && current.Y == target.Y) return true;

            foreach (var d in directions)
            {
                var next = current + d;
                if (next.X < 0 || next.X >= worldTilesX || next.Y < 0 || next.Y >= worldTilesY) continue;
                if (visited[next.X, next.Y]) continue;
                if (worldMask[next.X, next.Y] != LevelGenerator.TileType.Room) continue;
                visited[next.X, next.Y] = true;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    public static Vector2 FindWorldSpawnPosition(
        Func<Vector2I, Vector2> mapTileToIsometricWorld,
        LevelGenerator.TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY)
    {
        int centerX = worldTilesX / 2;
        int centerY = worldTilesY / 2;
        for (int radius = 0; radius < Math.Max(worldTilesX, worldTilesY) / 2; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius && radius > 0) continue;
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) continue;
                    if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                    bool hasSpace = true;
                    for (int sx = -1; sx <= 1 && hasSpace; sx++)
                    {
                        for (int sy = -1; sy <= 1 && hasSpace; sy++)
                        {
                            int checkX = x + sx;
                            int checkY = y + sy;
                            if (checkX >= 0 && checkX < worldTilesX && checkY >= 0 && checkY < worldTilesY)
                            {
                                if (worldMask[checkX, checkY] != LevelGenerator.TileType.Room) hasSpace = false;
                            }
                        }
                    }
                    if (hasSpace)
                    {
                        return mapTileToIsometricWorld(new Vector2I(x, y));
                    }
                }
            }
        }
        return mapTileToIsometricWorld(new Vector2I(centerX, centerY));
    }
    public static void BuildConnectivityComponents(
        LevelGenerator.TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY,
        out int[,] componentId,
        out int[] componentSizes,
        out int centerComponentId)
    {
        componentId = new int[worldTilesX, worldTilesY];
        var sizes = new List<int> { 0 }; // index 0 reserved
        int currentId = 0;

        var directions = new Vector2I[]
        {
            new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)
        };

        var queue = new Queue<Vector2I>();

        for (int y = 0; y < worldTilesY; y++)
        {
            for (int x = 0; x < worldTilesX; x++)
            {
                if (worldMask[x, y] != LevelGenerator.TileType.Room || componentId[x, y] != 0) continue;
                currentId++;
                int count = 0;
                componentId[x, y] = currentId;
                queue.Clear();
                queue.Enqueue(new Vector2I(x, y));

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    count++;
                    foreach (var d in directions)
                    {
                        int nx = p.X + d.X, ny = p.Y + d.Y;
                        if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                        if (worldMask[nx, ny] != LevelGenerator.TileType.Room) continue;
                        if (componentId[nx, ny] != 0) continue;
                        componentId[nx, ny] = currentId;
                        queue.Enqueue(new Vector2I(nx, ny));
                    }
                }

                sizes.Add(count);
            }
        }

        componentSizes = sizes.ToArray();

        // Determine component id of center (nearest walkable to map center)
        Vector2I center = new Vector2I(worldTilesX / 2, worldTilesY / 2);
        centerComponentId = 0;
        if (worldTilesX > 0 && worldTilesY > 0)
        {
            if (center.X >= 0 && center.X < worldTilesX && center.Y >= 0 && center.Y < worldTilesY &&
                componentId[center.X, center.Y] != 0)
            {
                centerComponentId = componentId[center.X, center.Y];
            }
            else
            {
                int maxR = Math.Max(worldTilesX, worldTilesY);
                for (int r = 1; r <= maxR && centerComponentId == 0; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                            int cx = center.X + dx, cy = center.Y + dy;
                            if (cx < 0 || cx >= worldTilesX || cy < 0 || cy >= worldTilesY) continue;
                            if (componentId[cx, cy] != 0) { centerComponentId = componentId[cx, cy]; break; }
                        }
                        if (centerComponentId != 0) break;
                    }
                }
            }
        }
    }

    public static int EvaluateSpawnSafety(
        LevelGenerator.TileType[,] worldMask,
        Vector2I position,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int centerComponentId,
        int[] componentSizes)
    {
        int x = position.X;
        int y = position.Y;
        int safetyScore = 0;

        if (worldMask[x, y] != LevelGenerator.TileType.Room)
        {
            return 0;
        }
        safetyScore += 10;

        int walkableNeighbors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < worldTilesX && ny >= 0 && ny < worldTilesY)
                {
                    if (worldMask[nx, ny] == LevelGenerator.TileType.Room)
                    {
                        walkableNeighbors++;
                    }
                }
            }
        }
        if (walkableNeighbors < 5) return 0;
        safetyScore += walkableNeighbors * 2;

        int wideAreaWalkable = 0;
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < worldTilesX && ny >= 0 && ny < worldTilesY)
                {
                    if (worldMask[nx, ny] == LevelGenerator.TileType.Room)
                    {
                        wideAreaWalkable++;
                    }
                }
            }
        }
        safetyScore += wideAreaWalkable;

        if (componentId[x, y] <= 0 || componentId[x, y] != centerComponentId)
        {
            return 0;
        }
        safetyScore += 50;

        var testPoints = new List<Vector2I>
        {
            new Vector2I(worldTilesX / 4, worldTilesY / 4),
            new Vector2I(3 * worldTilesX / 4, worldTilesY / 4),
            new Vector2I(worldTilesX / 4, 3 * worldTilesY / 4),
            new Vector2I(3 * worldTilesX / 4, 3 * worldTilesY / 4)
        };
        int reachableQuadrants = 0;
        foreach (var tp in testPoints)
        {
            int cx = Math.Clamp(tp.X, 0, worldTilesX - 1);
            int cy = Math.Clamp(tp.Y, 0, worldTilesY - 1);
            if (worldMask[cx, cy] == LevelGenerator.TileType.Room && componentId[cx, cy] == centerComponentId)
            {
                reachableQuadrants++;
            }
        }
        safetyScore += reachableQuadrants * 15;

        return safetyScore;
    }

    public static Vector2I? FindBestSpawnInCorner(
        LevelGenerator.TileType[,] worldMask,
        int startX,
        int startY,
        int endX,
        int endY,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int centerComponentId,
        int[] componentSizes)
    {
        var validPositions = new List<(Vector2I pos, int score)>();
        for (int radius = 0; radius < Math.Max(endX - startX, endY - startY); radius++)
        {
            for (int x = startX; x < endX; x++)
            {
                for (int y = startY; y < endY; y++)
                {
                    int distanceFromEdge = Math.Min(
                        Math.Min(x - startX, endX - 1 - x),
                        Math.Min(y - startY, endY - 1 - y)
                    );
                    if (distanceFromEdge != radius) continue;
                    if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) continue;
                    var candidate = new Vector2I(x, y);
                    int safetyScore = EvaluateSpawnSafety(worldMask, candidate, worldTilesX, worldTilesY, componentId, centerComponentId, componentSizes);
                    if (safetyScore > 0)
                    {
                        validPositions.Add((candidate, safetyScore));
                    }
                }
            }
        }

        if (validPositions.Count > 0)
        {
            return validPositions.OrderByDescending(p => p.score).First().pos;
        }
        return null;
    }
}


