using Godot;
using System;
using System.Collections.Generic;

public static class SectionConnectorOrchestrator
{
    public sealed class Context
    {
        public int MapWidth { get; init; }
        public int MapHeight { get; init; }
        public int GridWidth { get; init; }
        public int GridHeight { get; init; }
        public int ConnectorWidth { get; init; }
        public int SectionSpacing { get; init; }
        public TileMapLayer FloorsTileMap { get; init; }
        public TileMapLayer WallsTileMap { get; init; }
        public int FloorsSourceID { get; init; }
        public int WallsSourceID { get; init; }
        public int MAP_LAYER { get; init; }
        public Func<int, Vector2I> GetBiomeFloorTile { get; init; }
        public Func<int, Vector2I, (int sourceId, Vector2I tile)> GetBiomeWallTile { get; init; }
        public MultiSectionCoordinator MultiSection { get; init; }
        public CorridorCarver CorridorCarver { get; init; }
        public SectionConnector SectionConnector { get; init; }
    }

    public static void ConnectAdjacentSections(Context ctx, System.Collections.Generic.List<LevelGenerator.MapSection> sections)
    {
        // Горизонтальные связи
        for (int y = 0; y < ctx.GridHeight; y++)
        {
            for (int x = 0; x < ctx.GridWidth - 1; x++)
            {
                var left = sections.Find(s => s.GridX == x && s.GridY == y);
                var right = sections.Find(s => s.GridX == x + 1 && s.GridY == y);
                if (left != null && right != null)
                {
                    ConnectSectionsHorizontally(ctx, left, right);
                }
            }
        }

        // Вертикальные связи
        for (int x = 0; x < ctx.GridWidth; x++)
        {
            for (int y = 0; y < ctx.GridHeight - 1; y++)
            {
                var top = sections.Find(s => s.GridX == x && s.GridY == y);
                var bottom = sections.Find(s => s.GridX == x && s.GridY == y + 1);
                if (top != null && bottom != null)
                {
                    ConnectSectionsVertically(ctx, top, bottom);
                }
            }
        }
    }

    public static void ConnectSectionsHorizontally(Context ctx, LevelGenerator.MapSection left, LevelGenerator.MapSection right)
    {
        int passageY = ctx.MapHeight / 2;
        Vector2I leftFloorTile = ctx.GetBiomeFloorTile(left.BiomeType);
        Vector2I rightFloorTile = ctx.GetBiomeFloorTile(right.BiomeType);
        int tunnelWidth = Math.Max(3, ctx.ConnectorWidth);

        ctx.MultiSection.CreateHorizontalCorridorPart(
            left,
            ctx.MapWidth - 10,
            ctx.MapWidth,
            passageY,
            tunnelWidth,
            leftFloorTile,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.FloorsTileMap,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.FloorsSourceID,
            (section, x1, x2, y, width, floor) => ctx.CorridorCarver.FindAndConnectToNearbyRooms(
                section, x1, x2, y, width, floor, true,
                ctx.MapWidth, ctx.MapHeight,
                (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(ctx, s, sx, ex, py, ft),
                (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(ctx, s, px, sy, ey, ft)
            )
        );

        ctx.MultiSection.CreateHorizontalCorridorPart(
            right,
            0,
            10,
            passageY,
            tunnelWidth,
            rightFloorTile,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.FloorsTileMap,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.FloorsSourceID,
            (section, x1, x2, y, width, floor) => ctx.CorridorCarver.FindAndConnectToNearbyRooms(
                section, x1, x2, y, width, floor, true,
                ctx.MapWidth, ctx.MapHeight,
                (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(ctx, s, sx, ex, py, ft),
                (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(ctx, s, px, sy, ey, ft)
            )
        );

        if (ctx.SectionSpacing > 0)
        {
            ctx.MultiSection.FillHorizontalGap(
                left,
                right,
                passageY,
                tunnelWidth,
                ctx.SectionSpacing,
                ctx.MapWidth,
                ctx.FloorsTileMap,
                ctx.WallsTileMap,
                ctx.MAP_LAYER,
                ctx.FloorsSourceID,
                biome => ctx.GetBiomeFloorTile(biome)
            );
        }

        ctx.SectionConnector.AddDecorativeHorizontalWalls(
            left,
            right,
            passageY,
            tunnelWidth,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(biome, pos)
        );

        ctx.SectionConnector.AddWallsAroundHorizontalConnector(
            left,
            right,
            passageY,
            tunnelWidth,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.SectionSpacing,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(biome, pos)
        );
    }

    public static void ConnectSectionsVertically(Context ctx, LevelGenerator.MapSection top, LevelGenerator.MapSection bottom)
    {
        int passageX = ctx.MapWidth / 2;
        Vector2I topFloorTile = ctx.GetBiomeFloorTile(top.BiomeType);
        Vector2I bottomFloorTile = ctx.GetBiomeFloorTile(bottom.BiomeType);
        int tunnelWidth = Math.Max(3, ctx.ConnectorWidth);

        ctx.MultiSection.CreateVerticalCorridorPart(
            top,
            ctx.MapHeight - 10,
            ctx.MapHeight,
            passageX,
            tunnelWidth,
            topFloorTile,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.FloorsTileMap,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.FloorsSourceID,
            (section, x, width, y1, y2, floor, isHorizontal) => ctx.CorridorCarver.FindAndConnectToNearbyRooms(
                section, x, width, y1, y2, floor, false,
                ctx.MapWidth, ctx.MapHeight,
                (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(ctx, s, sx, ex, py, ft),
                (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(ctx, s, px, sy, ey, ft)
            )
        );

        ctx.MultiSection.CreateVerticalCorridorPart(
            bottom,
            0,
            10,
            passageX,
            tunnelWidth,
            bottomFloorTile,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.FloorsTileMap,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.FloorsSourceID,
            (section, x, width, y1, y2, floor, isHorizontal) => ctx.CorridorCarver.FindAndConnectToNearbyRooms(
                section, x, width, y1, y2, floor, false,
                ctx.MapWidth, ctx.MapHeight,
                (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(ctx, s, sx, ex, py, ft),
                (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(ctx, s, px, sy, ey, ft)
            )
        );

        if (ctx.SectionSpacing > 0)
        {
            ctx.MultiSection.FillVerticalGap(
                top,
                bottom,
                passageX,
                tunnelWidth,
                ctx.SectionSpacing,
                ctx.MapHeight,
                ctx.FloorsTileMap,
                ctx.WallsTileMap,
                ctx.MAP_LAYER,
                ctx.FloorsSourceID,
                biome => ctx.GetBiomeFloorTile(biome)
            );
        }

        ctx.SectionConnector.AddDecorativeVerticalWalls(
            top,
            bottom,
            passageX,
            tunnelWidth,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(biome, pos)
        );

        ctx.SectionConnector.AddWallsAroundVerticalConnector(
            top,
            bottom,
            passageX,
            tunnelWidth,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.SectionSpacing,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(biome, pos)
        );
    }

    // Эти делегаты передаются в CorridorCarver
    private static void CreateHorizontalConnectionToRoom(Context ctx, LevelGenerator.MapSection section, int startX, int endX, int y, Vector2I floorTile)
    {
        int width = Math.Max(3, ctx.ConnectorWidth);
        Vector2 worldOffset = section.WorldOffset;
        int xStart = Math.Min(startX, endX);
        int xEnd = Math.Max(startX, endX);
        for (int offsetY = -width / 2; offsetY <= width / 2; offsetY++)
        {
            int posY = y + offsetY;
            if (posY < 0 || posY >= ctx.MapHeight) continue;
            for (int posX = xStart; posX <= xEnd; posX++)
            {
                if (posX < 0 || posX >= ctx.MapWidth) continue;
                Vector2I worldPos = new Vector2I((int)worldOffset.X + posX, (int)worldOffset.Y + posY);
                ctx.FloorsTileMap.SetCell(worldPos, ctx.FloorsSourceID, floorTile);
                ctx.WallsTileMap.EraseCell(worldPos);
                section.SectionMask[posX, posY] = LevelGenerator.TileType.Corridor;
            }
        }
        ctx.SectionConnector.AddDecorativeWallsForConnection(
            section,
            y,
            width,
            xStart,
            xEnd,
            true,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(section.BiomeType, pos)
        );
    }

    private static void CreateVerticalConnectionToRoom(Context ctx, LevelGenerator.MapSection section, int x, int startY, int endY, Vector2I floorTile)
    {
        int width = Math.Max(3, ctx.ConnectorWidth);
        Vector2 worldOffset = section.WorldOffset;
        int yStart = Math.Min(startY, endY);
        int yEnd = Math.Max(startY, endY);
        for (int offsetX = -width / 2; offsetX <= width / 2; offsetX++)
        {
            int posX = x + offsetX;
            if (posX < 0 || posX >= ctx.MapWidth) continue;
            for (int posY = yStart; posY <= yEnd; posY++)
            {
                if (posY < 0 || posY >= ctx.MapHeight) continue;
                Vector2I worldPos = new Vector2I((int)worldOffset.X + posX, (int)worldOffset.Y + posY);
                ctx.FloorsTileMap.SetCell(worldPos, ctx.FloorsSourceID, floorTile);
                ctx.WallsTileMap.EraseCell(worldPos);
                section.SectionMask[posX, posY] = LevelGenerator.TileType.Corridor;
            }
        }
        ctx.SectionConnector.AddDecorativeWallsForConnection(
            section,
            x,
            width,
            yStart,
            yEnd,
            false,
            ctx.MapWidth,
            ctx.MapHeight,
            ctx.WallsTileMap,
            ctx.MAP_LAYER,
            ctx.WallsSourceID,
            (biome, pos) => ctx.GetBiomeWallTile(section.BiomeType, pos)
        );
    }
}


