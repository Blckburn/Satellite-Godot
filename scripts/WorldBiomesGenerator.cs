using Godot;
using System;
using System.Collections.Generic;

public sealed class WorldBiomesGenerator
{
    private readonly Random _random;
    private readonly BiomePalette _biome;
    private readonly TileMapLayer _floorsTileMap;
    private readonly TileMapLayer _wallsTileMap;
    // Новый слой для верхних стен
    private readonly TileMapLayer _wallsOverlayTileMap;
    private readonly int _floorsSourceId;
    private readonly int _wallsSourceId;

    public WorldBiomesGenerator(
        Random random,
        BiomePalette biome,
        TileMapLayer floorsTileMap,
        TileMapLayer wallsTileMap,
        TileMapLayer wallsOverlayTileMap,
        int floorsSourceId,
        int wallsSourceId)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _biome = biome ?? throw new ArgumentNullException(nameof(biome));
        _floorsTileMap = floorsTileMap ?? throw new ArgumentNullException(nameof(floorsTileMap));
        _wallsTileMap = wallsTileMap ?? throw new ArgumentNullException(nameof(wallsTileMap));
        _wallsOverlayTileMap = wallsOverlayTileMap; // может быть null, тогда оверлей не рисуем
        _floorsSourceId = floorsSourceId;
        _wallsSourceId = wallsSourceId;
    }

    public void GenerateWorld(
        int mapWidth,
        int mapHeight,
        int worldWidth,
        int worldHeight,
        int maxBiomeTypes,
        // CA / Density / Trails params
        float caveInitialFill,
        int caveSmoothSteps,
        int caveBirthLimit,
        int caveDeathLimit,
        float worldOpenTarget,
        int carveGlobalTrailsWidth,
        int biomeHallRadius,
        int riverCount,
        int riverWidth,
        float riverNoiseFreq,
        float riverNoiseAmp,
        int localCorridorWidth,
        bool randomizeWorldParams,
        bool worldBlendBorders,
        out LevelGenerator.TileType[,] worldMask,
        out int[,] worldBiome,
        Action<Vector2I, Vector2, Vector2, Vector2, Vector2> onCornersComputed)
    {
        // Размеры мира в тайлах
        int worldTilesX = Math.Max(1, worldWidth) * mapWidth;
        int worldTilesY = Math.Max(1, worldHeight) * mapHeight;

        // Лёгкая рандомизация параметров
        var rng = _random;
        int seedBase = rng.Next(int.MaxValue);
        int locRiverCount = riverCount;
        int locRiverWidth = riverWidth;
        int locCarveGlobalTrailsWidth = carveGlobalTrailsWidth;
        int locBiomeHallRadius = biomeHallRadius;
        int locLocalCorridorWidth = localCorridorWidth;
        float locRiverNoiseFreq = riverNoiseFreq;
        float locRiverNoiseAmp = riverNoiseAmp;
        float locWorldOpenTarget = worldOpenTarget;

        if (randomizeWorldParams)
        {
            int rivers = rng.Next(Math.Max(1, locRiverCount - 1), locRiverCount + 2);
            locRiverCount = Math.Max(1, rivers);
            locRiverWidth = Math.Clamp(locRiverWidth + rng.Next(-1, 2), 4, 10);
            locCarveGlobalTrailsWidth = Math.Clamp(locCarveGlobalTrailsWidth + rng.Next(-1, 2), 3, 8);
            locBiomeHallRadius = Math.Clamp(locBiomeHallRadius + rng.Next(-2, 3), 8, 14);
            locLocalCorridorWidth = Math.Clamp(locLocalCorridorWidth + rng.Next(-1, 2), 2, 5);
            locRiverNoiseFreq = Math.Clamp(locRiverNoiseFreq + (float)((rng.NextDouble() - 0.5) * 0.01), 0.02f, 0.08f);
            locRiverNoiseAmp = Math.Clamp(locRiverNoiseAmp + (float)((rng.NextDouble() - 0.5) * 2.0), 6f, 12f);
            locWorldOpenTarget = Math.Clamp(locWorldOpenTarget + (float)((rng.NextDouble() - 0.5) * 0.06), 0.30f, 0.50f);
        }

        // 1) Poisson-lite центры биомов
        var centers = new List<(Vector2I pos, int biome)>();
        // Динамическое количество биомов от площади карты
        int worldArea = worldTilesX * worldTilesY;
        int desiredBiomeArea = Math.Max(mapWidth * mapHeight * 4, 8000); // больше целевая площадь биома → гарантированный холл
        int targetBiomeCount = Math.Max(4, Math.Min(16, Math.Max((int)MathF.Round(worldArea / (float)desiredBiomeArea), 4)));
        int attempts = 0; int maxAttempts = (targetBiomeCount + 1) * 500;
        int spacing = Math.Max(18, (int)MathF.Round(MathF.Sqrt(desiredBiomeArea) * 0.40f)); // шире разводим биомы
        while (centers.Count < targetBiomeCount && attempts++ < maxAttempts)
        {
            int x = rng.Next(4, worldTilesX - 4);
            int y = rng.Next(4, worldTilesY - 4);
            bool ok = true;
            foreach (var c in centers)
            {
                int dx = c.pos.X - x, dy = c.pos.Y - y;
                if (dx * dx + dy * dy < spacing * spacing) { ok = false; break; }
            }
            if (!ok) continue;
            int biome = rng.Next(0, maxBiomeTypes);
            centers.Add((new Vector2I(x, y), biome));
            if (attempts % ((targetBiomeCount + 1) * 20) == 0 && spacing > 12) spacing -= 2;
        }
        if (centers.Count == 0)
            centers.Add((new Vector2I(worldTilesX / 2, worldTilesY / 2), 0));

        // 2) Voronoi L1
        worldBiome = new int[worldTilesX, worldTilesY];
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            int best = int.MaxValue; int b = 0;
            foreach (var c in centers)
            {
                int d = Math.Abs(c.pos.X - x) + Math.Abs(c.pos.Y - y);
                if (d < best) { best = d; b = c.biome; }
            }
            worldBiome[x, y] = b;
        }

        // 3) Шум + залы
        worldMask = new LevelGenerator.TileType[worldTilesX, worldTilesY];
        var waterMask = new bool[worldTilesX, worldTilesY];
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
            worldMask[x, y] = (rng.NextDouble() < caveInitialFill) ? LevelGenerator.TileType.Room : LevelGenerator.TileType.Background;

        foreach (var c in centers)
        {
            int r = Math.Max(2, locBiomeHallRadius);
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                int x = c.pos.X + dx, y = c.pos.Y + dy;
                if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) continue;
                if (dx * dx + dy * dy <= r * r && worldBiome[x, y] == c.biome)
                    worldMask[x, y] = LevelGenerator.TileType.Room;
            }
        }

        // 4) Сглаживание CA с учётом границ биомов
        for (int step = 0; step < caveSmoothSteps; step++)
        {
            var next = new LevelGenerator.TileType[worldTilesX, worldTilesY];
            for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
            {
                int walls = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) { walls++; continue; }
                    if (worldBiome[nx, ny] != worldBiome[x, y] || worldMask[nx, ny] != LevelGenerator.TileType.Room) walls++;
                }
                if (worldMask[x, y] != LevelGenerator.TileType.Room)
                    next[x, y] = (walls >= caveDeathLimit + 1) ? LevelGenerator.TileType.Background : LevelGenerator.TileType.Room;
                else
                    next[x, y] = (walls > caveBirthLimit + 1) ? LevelGenerator.TileType.Background : LevelGenerator.TileType.Room;
            }
            worldMask = next;
        }

        // 4b) Подстройка под целевую открытость
        int openCount = 0;
        for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
                if (worldMask[x, y] == LevelGenerator.TileType.Room) openCount++;
        float openRatio = (float)openCount / (worldTilesX * worldTilesY);
        if (openRatio < locWorldOpenTarget)
        {
            var next = new LevelGenerator.TileType[worldTilesX, worldTilesY];
            for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
            {
                int walls = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) { walls++; continue; }
                    if (worldBiome[nx, ny] != worldBiome[x, y] || worldMask[nx, ny] != LevelGenerator.TileType.Room) walls++;
                }
                if (worldMask[x, y] != LevelGenerator.TileType.Room)
                    next[x, y] = (walls >= caveDeathLimit - 1) ? LevelGenerator.TileType.Background : LevelGenerator.TileType.Room;
                else
                    next[x, y] = (walls > caveBirthLimit + 2) ? LevelGenerator.TileType.Background : LevelGenerator.TileType.Room;
            }
            worldMask = next;
        }

        // 5) БАЗОВАЯ ЗАЛИВКА ПОЛА ПО БИОМУ ДЛЯ ВСЕЙ КАРТЫ + РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ GRASSLAND
        var grassBaseTiles = new Vector2I[] { new Vector2I(5, 2), new Vector2I(6, 2) }; // основа Grassland
        var grassRareTiles = new Vector2I[]
        {
            new Vector2I(7, 2), new Vector2I(8, 2), new Vector2I(9, 2), new Vector2I(10, 2),
            new Vector2I(0, 3), new Vector2I(1, 3), new Vector2I(2, 3), new Vector2I(3, 3)
        };

        // Forest (биом 1)
        var forestBaseTiles = new Vector2I[] { new Vector2I(0, 2), new Vector2I(1, 2), new Vector2I(2, 2) };
        var forestRareTiles = new Vector2I[]
        {
            new Vector2I(7, 2), new Vector2I(8, 2), new Vector2I(9, 2), new Vector2I(10, 2),
            new Vector2I(0, 3), new Vector2I(1, 3), new Vector2I(2, 3), new Vector2I(3, 3)
        };

        // Desert (биом 2)
        var desertBaseTiles = new Vector2I[] { new Vector2I(9, 0) };
        var desertRareTiles = new Vector2I[]
        {
            new Vector2I(0, 1), new Vector2I(1, 1), new Vector2I(2, 1),
            new Vector2I(5, 1)
        };

        // Ice (биом 3)
        var iceBaseTiles = new Vector2I[] { new Vector2I(10, 8) };
        var iceRareTiles = new Vector2I[]
        {
            new Vector2I(4, 6), new Vector2I(5, 6), new Vector2I(9, 9)
        };

        // Techno (биом 4)
        var technoBaseTiles = new Vector2I[] { new Vector2I(8, 5) };
        var technoRareTiles = new Vector2I[] { new Vector2I(6, 5), new Vector2I(7, 5) };

        // Anomal (биом 5)
        var anomalBaseTiles = new Vector2I[] { new Vector2I(6, 8) };
        var anomalRareTiles = new Vector2I[] { new Vector2I(7, 8), new Vector2I(8, 8) };

        // Lava Springs (биом 6)
        var lavaBaseTiles = new Vector2I[] { new Vector2I(8, 5) };
        var lavaRareTiles = new Vector2I[] { new Vector2I(6, 5), new Vector2I(7, 5), new Vector2I(9, 8) };

        // 5a) Базовая заливка пола (включая фон/под будущими стенами)
        // Локальная детерминированная функция шума (хеш)
        static int Hash2D(int x, int y, int seed)
        {
            unchecked
            {
                int h = seed;
                h ^= x * 0x45d9f3b;
                h = (h ^ (h >> 16)) * 0x45d9f3b;
                h ^= y * 0x27d4eb2d;
                h ^= (h >> 15);
                return h;
            }
        }
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            int biome = worldBiome[x, y];
            var wp = new Vector2I(x, y);
            Vector2I floorTile;
            if (biome == 0)
            {
                // Когерентный шум: крупные пятна с небольшим джиттером
                int rx = x / 4; // масштаб пятен
                int ry = y / 4;
                int h = Hash2D(rx, ry, 1337);
                bool variantA = (h & 1) == 0;
                // Небольшой джиттер для разрушения прямых границ
                int j = Hash2D(x, y, 911);
                if ((j % 100) < 15) variantA = !variantA; // ~15%
                floorTile = variantA ? grassBaseTiles[0] : grassBaseTiles[1];
            }
            else if (biome == 1)
            {
                // Forest: 3 базовые вариации с когерентным шумом (заливаем везде, включая фон)
                int rx = x / 4; int ry = y / 4;
                int h = Hash2D(rx, ry, 2237);
                int idx = Math.Abs(h) % forestBaseTiles.Length;
                // Джиттер 12%
                int j = Hash2D(x, y, 912);
                if ((j % 100) < 12) idx = (idx + 1) % forestBaseTiles.Length;
                floorTile = forestBaseTiles[idx];
            }
            else if (biome == 2)
            {
                // Desert: единый базовый тайл (3,0)
                floorTile = desertBaseTiles[0];
            }
            else if (biome == 3)
            {
                // Ice: единый базовый тайл (10,10) — везде, включая под стенами
                floorTile = iceBaseTiles[0];
            }
            else if (biome == 4)
            {
                // Techno: базовый тайл (8,5) — везде, включая под стенами
                floorTile = technoBaseTiles[0];
            }
            else if (biome == 5)
            {
                // Anomal: базовый тайл (6,8) — везде, включая под стенами
                floorTile = anomalBaseTiles[0];
            }
            else if (biome == 6)
            {
                // Lava Springs: базовый (8,5)
                floorTile = lavaBaseTiles[0];
            }
            else
            {
                // Прочие биомы (пока без явной базы): заглушка (2,9)
                floorTile = new Vector2I(2, 9);
            }
            // ЖЕЛЕЗНАЯ СИНХРОНИЗАЦИЯ: используем TileCoordinateManager для гарантированной синхронизации
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, floorTile);

            // ИСПРАВЛЕНИЕ: Убираем старую логику стен - теперь стены управляются только через WallSystem
            // if (worldMask[x, y] == LevelGenerator.TileType.Room)
            // {
            //     _wallsTileMap.EraseCell(wp);
            // }
            // else if (worldBlendBorders)
            // {
            //     // Стены временно отключены
            //     // var wallInfo = _biome.GetWallTileForBiomeEx(biome, wp);
            //     // _wallsTileMap.SetCell(wp, wallInfo.sourceId, wallInfo.tile);
            // }
        }

        // 5b) Редкие «вкрапления» для Grassland (без соседних таких же тайлов)
        // Плотность вкраплений: можно подстроить при необходимости
        const float grassRareDensity = 0.20f; // 20% кандидатов, с ограничением неприлипания
        // Жесткая декорация без соседей (Grassland): сначала кандидаты, затем размещение с учётом окна/соседей
        var want = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 0) continue; // только Grassland, без учета Room/Background
            // Детерминированное «случайное» поле для равномерности
            int r = Hash2D(x, y, 2025) % 1000;
            if (r < (int)(grassRareDensity * 1000)) want.Add(new Vector2I(x, y));
        }
        // Перемешаем кандидатов, чтобы равномернее расходиться
        for (int i = want.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (want[i], want[j]) = (want[j], want[i]);
        }
        var placed = new bool[worldTilesX, worldTilesY];
        // Балансировка по окнам 4x4: поддерживаем коридор значений [min..max]
        int window = 4;
        float expectedPerWindowF = window * window * grassRareDensity;
        int minPerWindow = Math.Max(2, (int)(expectedPerWindowF * 0.60f));
        int maxPerWindow = Math.Max(minPerWindow + 1, (int)(expectedPerWindowF * 1.40f));
        int[,] windowCount = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in want)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCount[wx, wy] >= maxPerWindow) continue; // не переливаем окно
            bool blocked = false;
            // Проверяем только 4-соседей (Манхэттен)
            var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
            foreach (var d in dirs)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placed[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = grassRareTiles[_random.Next(grassRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            placed[x, y] = true;
            windowCount[wx, wy]++;
        }

        // Заполняем окна, где редких тайлов недостаточно (минимум) — Grassland
        int wxCount = windowCount.GetLength(0);
        int wyCount = windowCount.GetLength(1);
        for (int wx = 0; wx < wxCount; wx++)
        {
            for (int wy = 0; wy < wyCount; wy++)
            {
                int need = minPerWindow - windowCount[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);

                // Проход по клеткам окна в шахматном порядке, чтобы соблюдать 4‑соседей
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            // Разрешаем вкрапления и под будущими стенами: проверяем только биом
                            if (worldBiome[x, y] != 0) continue;
                            // 4‑соседа нет в placed
                            bool blocked = false;
                            var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
                            foreach (var d in dirs)
                            {
                                int nx = x + d.X, ny = y + d.Y;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placed[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;

                            var rareTile = grassRareTiles[_random.Next(grassRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            placed[x, y] = true;
                            windowCount[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ FOREST (биом 1) — та же логика
        // Параметры FOREST редких вкраплений
        const float forestRareDensity = 0.07f; // 7%
        var wantF = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 1) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue; // редкие только на проходимых
            int r = Hash2D(x, y, 3025) % 1000;
            if (r < (int)(forestRareDensity * 1000)) wantF.Add(new Vector2I(x, y));
        }
        for (int i = wantF.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantF[i], wantF[j]) = (wantF[j], wantF[i]);
        }
        var placedF = new bool[worldTilesX, worldTilesY];
        int[,] windowCountF = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        // Собственные пороги для Forest, чтобы не завышать минимумы
        float expectedPerWindowFForest = window * window * forestRareDensity;
        int minPerWindowF = Math.Max(1, (int)(expectedPerWindowFForest * 0.60f));
        int maxPerWindowF = Math.Max(minPerWindowF + 1, (int)(expectedPerWindowFForest * 1.40f));
        foreach (var p in wantF)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountF[wx, wy] >= maxPerWindowF) continue;
            bool blocked = false;
            // 8-соседей (включая диагонали)
            for (int dx = -1; dx <= 1 && !blocked; dx++)
            for (int dy = -1; dy <= 1 && !blocked; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placedF[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = forestRareTiles[_random.Next(forestRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            placedF[x, y] = true;
            windowCountF[wx, wy]++;
        }
        int wxCountF = windowCountF.GetLength(0);
        int wyCountF = windowCountF.GetLength(1);
        for (int wx = 0; wx < wxCountF; wx++)
        {
            for (int wy = 0; wy < wyCountF; wy++)
            {
                int need = minPerWindowF - windowCountF[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 1 || worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                            bool blocked = false;
                            for (int dx = -1; dx <= 1 && !blocked; dx++)
                            for (int dy = -1; dy <= 1 && !blocked; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placedF[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;
                            var rareTile = forestRareTiles[_random.Next(forestRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            placedF[x, y] = true;
                            windowCountF[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ DESERT (биом 2) — те же правила
        const float desertRareDensity = 0.07f; // 7%
        var desertRarePositions = new System.Collections.Generic.HashSet<Vector2I>();
        var wantD = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 2) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue; // редкие только на проходимых
            int r = Hash2D(x, y, 4025) % 1000;
            if (r < (int)(desertRareDensity * 1000)) wantD.Add(new Vector2I(x, y));
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ ICE (биом 3)
        const float iceRareDensity = 0.005f; // 0.5%
        var wantI = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 3) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue; // редкие только на проходимых
            int r = Hash2D(x, y, 5025) % 1000;
            if (r < (int)(iceRareDensity * 1000)) wantI.Add(new Vector2I(x, y));
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ TECHNO (биом 4)
        const float technoRareDensity = 0.005f; // 0.5%
        var wantT = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 4) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;
            int r = Hash2D(x, y, 6025) % 1000;
            if (r < (int)(technoRareDensity * 1000)) wantT.Add(new Vector2I(x, y));
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ ANOMAL (биом 5) — как у Desert
        const float anomalRareDensity = 0.07f; // 7%
        var wantA = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 5) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue; // редкие только на проходимых
            int r = Hash2D(x, y, 7025) % 1000;
            if (r < (int)(anomalRareDensity * 1000)) wantA.Add(new Vector2I(x, y));
        }

        // РЕДКИЕ ВКРАПЛЕНИЯ ДЛЯ LAVA SPRINGS (биом 6) — как у Grassland, но tiny density
        const float lavaRareDensity = 0.20f; // 20% для базовых редких (6,5),(7,5),(0,10) с исключением соседства (4‑соседей)
        var wantL = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldBiome[x, y] != 6) continue;
            int r = Hash2D(x, y, 8025) % 1000;
            if (r < (int)(lavaRareDensity * 1000)) wantL.Add(new Vector2I(x, y));
        }
        for (int i = wantL.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantL[i], wantL[j]) = (wantL[j], wantL[i]);
        }
        // Отдельные карты размещения по типам, чтобы управлять правилом «не рядом с такими же»
        var placedL65 = new bool[worldTilesX, worldTilesY];
        var placedL75 = new bool[worldTilesX, worldTilesY];
        var placedL98 = new bool[worldTilesX, worldTilesY];
        int[,] windowCountL = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in wantL)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountL[wx, wy] >= maxPerWindow) continue;
            var rare = lavaRareTiles[_random.Next(lavaRareTiles.Length)];
            bool blocked = false;
            if (!(rare.X == 9 && rare.Y == 8))
            {
                // Проверяем только на совпадающий тип (4‑соседей)
                var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
                foreach (var d in dirs)
                {
                    int nx = x + d.X, ny = y + d.Y;
                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                    if ((rare.X == 6 && rare.Y == 5 && placedL65[nx, ny]) ||
                        (rare.X == 7 && rare.Y == 5 && placedL75[nx, ny]))
                    { blocked = true; break; }
                }
                if (blocked) continue;
            }
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rare);
            if (rare.X == 6 && rare.Y == 5) placedL65[x, y] = true;
            else if (rare.X == 7 && rare.Y == 5) placedL75[x, y] = true;
            else if (rare.X == 9 && rare.Y == 8) placedL98[x, y] = true;
            windowCountL[wx, wy]++;
        }
        // Дорасстановка минимального количества, как для Grassland
        for (int wx = 0; wx < windowCountL.GetLength(0); wx++)
        {
            for (int wy = 0; wy < windowCountL.GetLength(1); wy++)
            {
                int need = minPerWindow - windowCountL[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 6) continue;
                            var rare = lavaRareTiles[_random.Next(lavaRareTiles.Length)];
                            bool blocked = false;
                            if (!(rare.X == 9 && rare.Y == 8))
                            {
                                var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
                                foreach (var d in dirs)
                                {
                                    int nx = x + d.X, ny = y + d.Y;
                                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                    if ((rare.X == 6 && rare.Y == 5 && placedL65[nx, ny]) ||
                                        (rare.X == 7 && rare.Y == 5 && placedL75[nx, ny]))
                                    { blocked = true; break; }
                                }
                                if (blocked) continue;
                            }
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rare);
                            if (rare.X == 6 && rare.Y == 5) placedL65[x, y] = true;
                            else if (rare.X == 7 && rare.Y == 5) placedL75[x, y] = true;
                            else if (rare.X == 9 && rare.Y == 8) placedL98[x, y] = true;
                            windowCountL[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        for (int i = wantA.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantA[i], wantA[j]) = (wantA[j], wantA[i]);
        }
        var placedA = new bool[worldTilesX, worldTilesY];
        float expectedPerWindowA = window * window * anomalRareDensity;
        int minPerWindowA = Math.Max(1, (int)(expectedPerWindowA * 0.60f));
        int maxPerWindowA = Math.Max(minPerWindowA + 1, (int)(expectedPerWindowA * 1.40f));
        int[,] windowCountA = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in wantA)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountA[wx, wy] >= maxPerWindowA) continue;
            bool blocked = false;
            // 8-соседей (как в Desert/Techno)
            for (int dx = -1; dx <= 1 && !blocked; dx++)
            for (int dy = -1; dy <= 1 && !blocked; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placedA[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = anomalRareTiles[_random.Next(anomalRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            placedA[x, y] = true;
            windowCountA[wx, wy]++;
        }
        int wxCountA = windowCountA.GetLength(0);
        int wyCountA = windowCountA.GetLength(1);
        for (int wx = 0; wx < wxCountA; wx++)
        {
            for (int wy = 0; wy < wyCountA; wy++)
            {
                int need = minPerWindowA - windowCountA[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 5 || worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                            bool blocked = false;
                            for (int dx = -1; dx <= 1 && !blocked; dx++)
                            for (int dy = -1; dy <= 1 && !blocked; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placedA[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;
                            var rareTile = anomalRareTiles[_random.Next(anomalRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            placedA[x, y] = true;
                            windowCountA[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        for (int i = wantT.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantT[i], wantT[j]) = (wantT[j], wantT[i]);
        }
        var placedT = new bool[worldTilesX, worldTilesY];
        float expectedPerWindowT = window * window * technoRareDensity;
        int minPerWindowT = Math.Max(0, (int)(expectedPerWindowT * 0.60f));
        int maxPerWindowT = Math.Max(minPerWindowT + 1, (int)(expectedPerWindowT * 1.40f));
        int[,] windowCountT = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in wantT)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountT[wx, wy] >= maxPerWindowT) continue;
            bool blocked = false;
            for (int dx = -1; dx <= 1 && !blocked; dx++)
            for (int dy = -1; dy <= 1 && !blocked; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placedT[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = technoRareTiles[_random.Next(technoRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            placedT[x, y] = true;
            windowCountT[wx, wy]++;
        }
        int wxCountT = windowCountT.GetLength(0);
        int wyCountT = windowCountT.GetLength(1);
        for (int wx = 0; wx < wxCountT; wx++)
        {
            for (int wy = 0; wy < wyCountT; wy++)
            {
                int need = minPerWindowT - windowCountT[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 4 || worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                            bool blocked = false;
                            for (int dx = -1; dx <= 1 && !blocked; dx++)
                            for (int dy = -1; dy <= 1 && !blocked; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placedT[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;
                            var rareTile = technoRareTiles[_random.Next(technoRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            placedT[x, y] = true;
                            windowCountT[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        for (int i = wantI.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantI[i], wantI[j]) = (wantI[j], wantI[i]);
        }
        var placedI = new bool[worldTilesX, worldTilesY];
        float expectedPerWindowI = window * window * iceRareDensity;
        int minPerWindowI = Math.Max(0, (int)(expectedPerWindowI * 0.60f));
        int maxPerWindowI = Math.Max(minPerWindowI + 1, (int)(expectedPerWindowI * 1.40f));
        int[,] windowCountI = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in wantI)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountI[wx, wy] >= maxPerWindowI) continue;
            bool blocked = false;
            // 8-соседей для неприлипания
            for (int dx = -1; dx <= 1 && !blocked; dx++)
            for (int dy = -1; dy <= 1 && !blocked; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placedI[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = iceRareTiles[_random.Next(iceRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            placedI[x, y] = true;
            windowCountI[wx, wy]++;
        }
        int wxCountI = windowCountI.GetLength(0);
        int wyCountI = windowCountI.GetLength(1);
        for (int wx = 0; wx < wxCountI; wx++)
        {
            for (int wy = 0; wy < wyCountI; wy++)
            {
                int need = minPerWindowI - windowCountI[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 3 || worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                            bool blocked = false;
                            for (int dx = -1; dx <= 1 && !blocked; dx++)
                            for (int dy = -1; dy <= 1 && !blocked; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placedI[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;
                            var rareTile = iceRareTiles[_random.Next(iceRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            placedI[x, y] = true;
                            windowCountI[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        for (int i = wantD.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (wantD[i], wantD[j]) = (wantD[j], wantD[i]);
        }
        var placedD = new bool[worldTilesX, worldTilesY];
        float expectedPerWindowD = window * window * desertRareDensity;
        int minPerWindowD = Math.Max(1, (int)(expectedPerWindowD * 0.60f));
        int maxPerWindowD = Math.Max(minPerWindowD + 1, (int)(expectedPerWindowD * 1.40f));
        int[,] windowCountD = new int[(worldTilesX + window - 1) / window, (worldTilesY + window - 1) / window];
        foreach (var p in wantD)
        {
            int x = p.X, y = p.Y;
            int wx = x / window, wy = y / window;
            if (windowCountD[wx, wy] >= maxPerWindowD) continue;
            bool blocked = false;
            for (int dx = -1; dx <= 1 && !blocked; dx++)
            for (int dy = -1; dy <= 1 && !blocked; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                if (placedD[nx, ny]) { blocked = true; break; }
            }
            if (blocked) continue;
            var rareTile = desertRareTiles[_random.Next(desertRareTiles.Length)];
            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
            desertRarePositions.Add(new Vector2I(x, y));
            placedD[x, y] = true;
            windowCountD[wx, wy]++;
        }
        int wxCountD = windowCountD.GetLength(0);
        int wyCountD = windowCountD.GetLength(1);
        for (int wx = 0; wx < wxCountD; wx++)
        {
            for (int wy = 0; wy < wyCountD; wy++)
            {
                int need = minPerWindowD - windowCountD[wx, wy];
                if (need <= 0) continue;
                int startX = wx * window;
                int startY = wy * window;
                int endX = Math.Min(worldTilesX, startX + window);
                int endY = Math.Min(worldTilesY, startY + window);
                for (int parity = 0; parity < 2 && need > 0; parity++)
                {
                    for (int y = startY; y < endY && need > 0; y++)
                    {
                        for (int x = startX; x < endX && need > 0; x++)
                        {
                            if (((x + y) & 1) != parity) continue;
                            if (worldBiome[x, y] != 2 || worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                            bool blocked = false;
                            for (int dx = -1; dx <= 1 && !blocked; dx++)
                            for (int dy = -1; dy <= 1 && !blocked; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                                if (placedD[nx, ny]) { blocked = true; break; }
                            }
                            if (blocked) continue;
                            var rareTile = desertRareTiles[_random.Next(desertRareTiles.Length)];
                            TileCoordinateManager.PlaceFloorTile(_floorsTileMap, x, y, _floorsSourceId, rareTile);
                            desertRarePositions.Add(new Vector2I(x, y));
                            placedD[x, y] = true;
                            windowCountD[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        // 6) Магистраль/глобальные тропы — отключено: магистраль используется только в логике, без отрисовки
        // [disabled]

        // 7) Локальные связки от «зал» к ближайшим комнатам внутри биома
        var biomeLocal = worldBiome; // избегаем использования out-параметра внутри локальной функции
        List<Vector2I> FindWorldPathConstrainedLocal(Vector2I start, Vector2I goal, int allowedBiome)
        {
            var open = new SortedSet<(int, int, Vector2I)>(Comparer<(int, int, Vector2I)>.Create((a, b) =>
                a.Item1 != b.Item1 ? a.Item1 - b.Item1 : a.Item2 != b.Item2 ? a.Item2 - b.Item2 : a.Item3.X != b.Item3.X ? a.Item3.X - b.Item3.X : a.Item3.Y - b.Item3.Y));
            var came = new Dictionary<Vector2I, Vector2I>();
            var gScore = new Dictionary<Vector2I, int>();
            int H(Vector2I p) => Math.Abs(p.X - goal.X) + Math.Abs(p.Y - goal.Y);
            open.Add((H(start), 0, start)); gScore[start] = 0;
            var dirs = new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) };
            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                var p = cur.Item3;
                if (p == goal)
                {
                    var path = new List<Vector2I>();
                    while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                    path.Reverse(); return path;
                }
                foreach (var d in dirs)
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= worldTilesX || n.Y < 0 || n.Y >= worldTilesY) continue;
                    if (biomeLocal[n.X, n.Y] != allowedBiome) continue;
                    int ng = cur.Item2 + 1;
                    if (!gScore.TryGetValue(n, out var old) || ng < old)
                    { gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n)); }
                }
            }
            return null;
        }

        // 7) Локальные связки от «зал» к ближайшим комнатам — отключено: не влияет на отрисовку пола
        // [disabled]

        // 8) Реки — временно отключены для MVP стен
        // [disabled]

        // 9) Мосты — временно отключены
        // [disabled]

        // 9a) КОНФИГУРАЦИЯ СТЕН ОТКЛЮЧЕНА: TileData нельзя менять во время выполнения в Godot 4
        // WallTileConfigurator.ConfigureWallTiles(_wallsTileMap);
        // Вместо этого используем смещение координат в TileCoordinateManager

        // 9b) Стены/коридоры/холлы/комнаты — включаем (логика без перекраски пола; стены рисуются)
        WallSystem.BuildTopology(
            rng,
            _floorsTileMap,
            _wallsTileMap,
            _floorsSourceId,
            _wallsSourceId,
            _biome,
            worldBiome,
            worldMask,
            worldTilesX,
            worldTilesY,
            centers,
            new WallSystem.Params { MainCorridorWidth = locCarveGlobalTrailsWidth, LocalCorridorWidth = locLocalCorridorWidth, HallRadiusScale = 0.06f }
        );

        // 9b-ext) Прореживание внутренних стен (включая лавовый биом 6 как у аномального)
        ThinInteriorWallsForBiomes(rng, worldMask, worldBiome, worldTilesX, worldTilesY, new int[] { 0, 1, 3, 4, 5, 6 });

        // 9c) ДОБАВЛЯЕМ ВЕРХНИЙ СЛОЙ СТЕН: ПУСТЫНЯ (биом 2) и спец-акценты для ТЕХНО (биом 4)
        if (_wallsOverlayTileMap != null)
        {
            bool hasDesert = false, hasTechno = false, hasAnomal = false, hasLava = false;
            for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
            {
                if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
                if (worldBiome[x, y] == 2) hasDesert = true;
                if (worldBiome[x, y] == 4) hasTechno = true;
                if (worldBiome[x, y] == 5) hasAnomal = true;
                if (worldBiome[x, y] == 6) hasLava = true;
            }
            if (hasDesert)
                AddDesertWallOverlays(rng, _wallsOverlayTileMap, _wallsSourceId, worldMask, worldBiome, worldTilesX, worldTilesY, desertRarePositions);
            if (hasTechno)
                AddTechnoPulsingOverlays(_wallsOverlayTileMap, _wallsSourceId, worldMask, worldBiome, worldTilesX, worldTilesY);
            if (hasAnomal)
                AddAnomalPulsingOverlays(_wallsOverlayTileMap, _wallsSourceId, worldMask, worldBiome, worldTilesX, worldTilesY);
            if (hasLava)
            {
                AddLavaPulsingOverlays(_wallsOverlayTileMap, _wallsSourceId, worldMask, worldBiome, worldTilesX, worldTilesY);
                // Дополнительно: пульсация пола лавы для редкого тайла Atlas 4: (9,8)
                AddLavaFloorPulsingOverlays(_wallsOverlayTileMap, _floorsSourceId, worldMask, worldBiome, worldTilesX, worldTilesY);
            }
        }

        // Логика «шапок» удалена

        // 10) Обновление HUD углов (вместо старого BorderWallsBuilder)
        onCornersComputed?.Invoke(
            new Vector2I(0, 0),
            LocalMapTileToIsometricWorld(new Vector2I(0, 0)),
            LocalMapTileToIsometricWorld(new Vector2I(worldTilesX - 1, 0)),
            LocalMapTileToIsometricWorld(new Vector2I(0, worldTilesY - 1)),
            LocalMapTileToIsometricWorld(new Vector2I(worldTilesX - 1, worldTilesY - 1))
        );

        // Показ сида в HUD
        UIManager.ShowSeed(seedBase);
    }

    // Прореживание внутренних стен (глубже первого ряда) для выбранных биомов.
    // Сохраняем крайний «первый ряд» стен, а глубже — снимаем часть тайлов по шуму,
    // переводя их в фон (под ними уже нарисован пол). Движение по-прежнему ограничено внешним рядом.
    private void ThinInteriorWallsForBiomes(Random rng, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h, int[] targetBiomes)
    {
        if (_wallsTileMap == null || worldMask == null || worldBiome == null) return;

        var target = new System.Collections.Generic.HashSet<int>(targetBiomes);

        // 1) Определяем пограничные клетки стен (первый ряд)
        var isBoundary = new bool[w, h];
        var dirs4 = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        for (int x = 1; x < w - 1; x++)
        for (int y = 1; y < h - 1; y++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            foreach (var d in dirs4)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (worldMask[nx, ny] != LevelGenerator.TileType.Wall) { isBoundary[x, y] = true; break; }
            }
        }

        // 2.0) В первом ряду (граница с воздухом) скрываем каждый второй тайл (только визуально),
        //      чтобы край не выглядел слишком плотным. Коллизии на этих клетках пропадут, если они
        //      задаются тайлом. Если нужна сохранённая коллизия — стоит завести прозрачный тайл с коллизией.
        for (int x = 2; x < w - 2; x++)
        for (int y = 2; y < h - 2; y++)
        {
            if (!isBoundary[x, y]) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            int biome = worldBiome[x, y];
            if (!target.Contains(biome)) continue;
            if (((x + y + biome) & 1) == 0)
            {
                _wallsTileMap.EraseCell(new Vector2I(x, y));
                // worldMask[x, y] остаётся Wall — логика карт остаётся прежней
            }
        }

        // 2) Определяем клетки, прилегающие ко «второму ряду», чтобы не разрушать толщину 2, если нужна
        var isNearBoundary = new bool[w, h];
        for (int x = 1; x < w - 1; x++)
        for (int y = 1; y < h - 1; y++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Wall || isBoundary[x, y]) continue;
            foreach (var d in dirs4)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (isBoundary[nx, ny]) { isNearBoundary[x, y] = true; break; }
            }
        }

        // 2.5) «Через один» для слоя, прилегающего к воздуху (внутренний ряд рядом с первым).
        //      Сохраняем первый ряд полностью; второй ряд делаем шахматкой для визуального разрежения.
        for (int x = 2; x < w - 2; x++)
        for (int y = 2; y < h - 2; y++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            if (!isNearBoundary[x, y] || isBoundary[x, y]) continue;
            int biome = worldBiome[x, y];
            if (!target.Contains(biome)) continue;

            // Шахматный узор «через 1»; можно смещать сидом биома, чтобы не совпадало глобально
            int parity = (x + y + (biome * 3)) & 1;
            if (parity == 0)
            {
                _wallsTileMap.EraseCell(new Vector2I(x, y));
                worldMask[x, y] = LevelGenerator.TileType.Background;
            }
        }

        // 3) Точечное прореживание: редкие одиночные точки и короткие микрокластеры (1–3 клетки)
        //    Разрежаем глубоко внутри массива стен. Сохраняем первый ряд и ближайший к нему слой.

        // Биом-специфичная «плотность» стартов кластеров (в % на клетку внутри массива)
        System.Func<int, int> densityForBiome = (biome) =>
        {
            switch (biome)
            {
                case 0: return 68; // grassland
                case 1: return 60; // forest
                case 3: return 53; // ice
                case 4: return 34; // techno — на 40% меньше от базового 57 → 34
                case 5: return 34; // anomal — как у techno
                default: return 57;
            }
        };

        var removed = new bool[w, h];
        int win = 16; int maxPerWindow = 192; // увеличиваем плотность стартов (~×1.5)
        int wx = (w + win - 1) / win, wy = (h + win - 1) / win;
        var winCount = new int[wx, wy];

        for (int y = 2; y < h - 2; y++)
        for (int x = 2; x < w - 2; x++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            bool nearEdge = isNearBoundary[x, y];
            if (isBoundary[x, y]) continue;

            int biome = worldBiome[x, y];
            if (!target.Contains(biome)) continue;

            // (расслаблено) почти без ограничения на близость — допускаем высокую плотность

            int wxi = x / win, wyi = y / win;
            if (winCount[wxi, wyi] >= maxPerWindow) continue;

            // вероятность старта кластера
            if (rng.Next(100) >= densityForBiome(biome)) continue;

            // размер микрокластера
            int length;
            if (nearEdge)
            {
                // возле второго ряда — чаще длиной 2, но одиночки тоже встречаются
                length = (rng.Next(100) < 40) ? 1 : 2;
            }
            else
            {
                // глубже — чуть длиннее кластеры
                int r = rng.Next(100);
                length = (r < 10) ? 1 : (r < 50) ? 2 : (r < 85) ? 3 : (r < 97) ? 4 : 5;
            }
            var dir = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) }[rng.Next(4)];

            int removedHere = 0;
            for (int i = 0; i < length; i++)
            {
                int tx = x + dir.X * i, ty = y + dir.Y * i;
                if (tx < 2 || tx >= w - 2 || ty < 2 || ty >= h - 2) break;
                if (worldMask[tx, ty] != LevelGenerator.TileType.Wall) break;
                if (isBoundary[tx, ty]) break;
                if (nearEdge && isNearBoundary[tx, ty] && i > 0) break; // возле края не растягиваемся

                // Не даём кластеру «подлезть» близко к уже удалённым точкам вне своего кластера
                bool conflict = false;
                for (int dx = -1; dx <= 1 && !conflict; dx++)
                for (int dy = -1; dy <= 1 && !conflict; dy++)
                {
                    int nx = tx + dx, ny = ty + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (removed[nx, ny] && (nx != x || ny != y)) conflict = true;
                }
                if (conflict) break;

                _wallsTileMap.EraseCell(new Vector2I(tx, ty));
                worldMask[tx, ty] = LevelGenerator.TileType.Background;
                removed[tx, ty] = true;
                removedHere++;
            }

            if (removedHere > 0)
            {
                winCount[wxi, wyi]++;
            }
        }

        // 4) Дополнительное точечное прореживание одиночными тайлами по шахматному шаблону,
        //    чтобы значительно «разрыхлить» массив без образования больших пятен.
        System.Func<int, int> singleProb = (biome) =>
        {
            switch (biome)
            {
                case 0: return 72; // grassland
                case 1: return 68; // forest
                case 3: return 63; // ice
                case 4: return 41; // techno — ~40% меньше от 68 → 41
                case 5: return 41; // anomal — как у techno
                default: return 68;
            }
        };

        for (int y = 2; y < h - 2; y++)
        for (int x = 2; x < w - 2; x++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            if (isBoundary[x, y] || isNearBoundary[x, y]) continue;
            int biome = worldBiome[x, y];
            if (!target.Contains(biome)) continue;

            if (rng.Next(100) < singleProb(biome))
            {
                _wallsTileMap.EraseCell(new Vector2I(x, y));
                worldMask[x, y] = LevelGenerator.TileType.Background;
            }
        }
    }

    private static int Hash2D(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed;
            h = (h ^ (x * 374761393)) * 668265263;
            h = (h ^ (y * 1274126177)) * 461845907;
            h ^= h >> 13; h *= 1274126177; h ^= h >> 16;
            return h & int.MaxValue;
        }
    }

    // Оверлей для пустынных стен: короткие полосы 1–3 клетки строго базовым тайлом пустынной стены.
    // Избегаем клеток, у которых ниже по Y находится комната/коридор (чтобы не «торчали»),
    // и не ставим рядом с редкими пустынными тайлами пола.
    private void AddDesertWallOverlays(Random rng, TileMapLayer overlay, int wallsSourceId, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h, System.Collections.Generic.HashSet<Vector2I> desertRarePositions)
    {
        Vector2I overlayTile = new Vector2I(3, 1); // тот же, что базовая стена пустыни

        var edges = new System.Collections.Generic.List<Vector2I>();
        var dirs = new[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        for (int x = 1; x < w - 1; x++)
        for (int y = 1; y < h - 1; y++)
        {
            if (worldBiome[x, y] != 2) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            bool boundary = false;
            foreach (var d in dirs)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) { boundary = true; break; }
                if (worldMask[nx, ny] != LevelGenerator.TileType.Wall) { boundary = true; break; }
            }
            if (boundary) edges.Add(new Vector2I(x, y));
        }

        foreach (var start in edges)
        {
            int hsh = Hash2D(start.X, start.Y, 8713);
            if ((hsh & 255) >= 128) continue; // ~50%

            Vector2I[] normals = new[] { new Vector2I(0, 1), new Vector2I(0, -1), new Vector2I(1, 0), new Vector2I(-1, 0) };
            Vector2I normal = new Vector2I(0, 0);
            foreach (var n in normals)
            {
                int nx = start.X + n.X, ny = start.Y + n.Y;
                if (nx <= 0 || nx >= w - 1 || ny <= 0 || ny >= h - 1) continue;
                if (worldMask[nx, ny] == LevelGenerator.TileType.Wall) { normal = n; break; }
            }
            if (normal == new Vector2I(0, 0)) continue;

            int openX = 0, openY = 0;
            if (worldMask[start.X + 1, start.Y] != LevelGenerator.TileType.Wall) openX++;
            if (worldMask[start.X - 1, start.Y] != LevelGenerator.TileType.Wall) openX++;
            if (worldMask[start.X, start.Y + 1] != LevelGenerator.TileType.Wall) openY++;
            if (worldMask[start.X, start.Y - 1] != LevelGenerator.TileType.Wall) openY++;
            Vector2I tangent = openX > openY ? new Vector2I(1, 0) : new Vector2I(0, 1);
            if ((hsh & 1) == 1) tangent *= -1;

            int length = 1 + (hsh % 3); // 1..3
            int inwardOffset = Hash2D(start.X, start.Y, 19009) % 4; // 0..3
            Vector2I p = new Vector2I(start.X + normal.X * inwardOffset, start.Y + normal.Y * inwardOffset);
            for (int i = 0; i < length; i++)
            {
                int x = p.X, y = p.Y;
                if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1) break;
                if (worldBiome[x, y] != 2 || worldMask[x, y] != LevelGenerator.TileType.Wall) break;

                if (worldMask[x, y + 1] == LevelGenerator.TileType.Room || worldMask[x, y + 1] == LevelGenerator.TileType.Corridor) break;

                bool nearRare = false;
                for (int dx = -1; dx <= 1 && !nearRare; dx++)
                for (int dy = -1; dy <= 1 && !nearRare; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var rp = new Vector2I(x + dx, y + dy);
                    if (rp.X < 0 || rp.X >= w || rp.Y < 0 || rp.Y >= h) continue;
                    if (desertRarePositions.Contains(rp)) nearRare = true;
                }
                if (nearRare) { p += tangent; continue; }

                int jitter = Hash2D(x, y, 2221) & 15;
                if (jitter < 3) { p += tangent; continue; }

                overlay.SetCell(new Vector2I(x, y), wallsSourceId, overlayTile);

                p += tangent;
            }
        }

        // Глубокие участки внутри стены: далеко от любого пола/фона
        int deepRadius = 3;
        var deepCells = new System.Collections.Generic.List<Vector2I>();
        for (int x = deepRadius; x < w - deepRadius; x++)
        for (int y = deepRadius; y < h - deepRadius; y++)
        {
            if (worldBiome[x, y] != 2) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;
            bool deep = true;
            for (int dx = -deepRadius; dx <= deepRadius && deep; dx++)
            for (int dy = -deepRadius; dy <= deepRadius && deep; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (worldMask[nx, ny] != LevelGenerator.TileType.Wall) deep = false;
            }
            if (deep) deepCells.Add(new Vector2I(x, y));
        }

        // Увеличиваем насыпанность оверлея глубоко внутри: длиннее и чаще, но с ограничением по окнам
        var overlayPlaced = new bool[w, h];
        int deepWindow = 8;
        int[,] deepWindowCount = new int[(w + deepWindow - 1) / deepWindow, (h + deepWindow - 1) / deepWindow];
        int deepMaxPerWindow = 6;

        foreach (var start in deepCells)
        {
            int hsh = Hash2D(start.X, start.Y, 11939);
            if ((hsh & 255) >= 160) continue; // ~62%

            // Случайное направление (горизонталь/вертикаль)
            Vector2I dir = ((hsh >> 1) & 1) == 0 ? new Vector2I(1, 0) : new Vector2I(0, 1);
            if ((hsh & 1) == 1) dir *= -1;

            int length = 3 + (Hash2D(start.X, start.Y, 19073) % 4); // 3..6
            Vector2I p = start;
            for (int i = 0; i < length; i++)
            {
                int x = p.X, y = p.Y;
                if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1) break;
                if (worldBiome[x, y] != 2 || worldMask[x, y] != LevelGenerator.TileType.Wall) break;

                // Разрывы для натуральности
                int jitter = Hash2D(x, y, 3221) & 15;
                if (jitter < 3) { p += dir; continue; }

                // Не рядом с редкими пустынными тайлами и открытым пространством (радиус 2)
                bool nearRare = false;
                for (int dx = -2; dx <= 2 && !nearRare; dx++)
                for (int dy = -2; dy <= 2 && !nearRare; dy++)
                {
                    var rp = new Vector2I(x + dx, y + dy);
                    if (rp.X < 0 || rp.X >= w || rp.Y < 0 || rp.Y >= h) continue;
                    if (desertRarePositions.Contains(rp)) nearRare = true;
                    if (worldMask[rp.X, rp.Y] == LevelGenerator.TileType.Room || worldMask[rp.X, rp.Y] == LevelGenerator.TileType.Corridor)
                        nearRare = true;
                }
                if (nearRare) { p += dir; continue; }

                // Минимальное расстояние между оверлей-тайлами (1 клетка)
                bool tooClose = false;
                for (int dx = -1; dx <= 1 && !tooClose; dx++)
                for (int dy = -1; dy <= 1 && !tooClose; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (overlayPlaced[nx, ny]) tooClose = true;
                }
                if (tooClose) { p += dir; continue; }

                // Ограничение по окнам
                int wxi = x / deepWindow, wyi = y / deepWindow;
                if (deepWindowCount[wxi, wyi] >= deepMaxPerWindow) { p += dir; continue; }

                overlay.SetCell(new Vector2I(x, y), wallsSourceId, overlayTile);
                overlayPlaced[x, y] = true;
                deepWindowCount[wxi, wyi]++;

                p += dir;
            }
        }
    }

    // Техно-акценты: расставляем редкие «пульсирующие» панели (6,7) Atlas ID 4 на верхнем слое стен.
    // Мы только ставим клетки; анимацию пульса добавит Tween в LevelGenerator при старте сцены.
    private void AddTechnoPulsingOverlays(TileMapLayer overlay, int wallsSourceId, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h)
    {
        if (overlay == null) return;
        Vector2I pulseTile = new Vector2I(6, 7);
        int seed = 24681;
        for (int x = 2; x < w - 2; x++)
        for (int y = 2; y < h - 2; y++)
        {
            if (worldBiome[x, y] != 4) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;

            // Ставим очень редко, преимущественно на границах стен, чтобы было видно
            bool boundary = false;
            foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) { boundary = true; break; }
                if (worldMask[nx, ny] != LevelGenerator.TileType.Wall) { boundary = true; break; }
            }
            if (!boundary) continue;

            int hsh = Hash2D(x, y, seed);
            if ((hsh % 1000) < 35) // ~3.5%
            {
                overlay.SetCell(new Vector2I(x, y), wallsSourceId, pulseTile);
            }
        }
    }

    // Аномальные акценты: пульсируем ИМЕННО редкие тайлы аномальных стен
    // Если под клеткой редкая аномальная стена (Atlas 5: (4,38) или Atlas 0: (68,18)),
    // ставим на верхний слой ТОТ ЖЕ тайл (тот же sourceId и coords), чтобы его «подсветить»
    private void AddAnomalPulsingOverlays(TileMapLayer overlay, int wallsSourceId, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h)
    {
        if (overlay == null) return;
        for (int x = 2; x < w - 2; x++)
        for (int y = 2; y < h - 2; y++)
        {
            if (worldBiome[x, y] != 5) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;

            // Пульсируем именно редкий тайл аномальных стен
            var cellPos = new Vector2I(x, y);
            int src = _wallsTileMap.GetCellSourceId(cellPos);
            Vector2I coords = _wallsTileMap.GetCellAtlasCoords(cellPos);
            if ((src == 5 && coords == new Vector2I(4, 38)) || (src == 0 && coords == new Vector2I(68, 18)))
            {
                // Ставим тот же тайл на верхнем слое, чтобы анимировать модулейтом
                overlay.SetCell(cellPos, src, coords);
            }
        }
    }

    // Лавовые акценты: пульсируем ИМЕННО редкие тайлы лавовых стен (биом 6):
    // Atlas 5: (19,39) и (21,38). Ставим на верхний слой тот же тайл для пульсации.
    private void AddLavaPulsingOverlays(TileMapLayer overlay, int wallsSourceId, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h)
    {
        if (overlay == null) return;
        for (int x = 2; x < w - 2; x++)
        for (int y = 2; y < h - 2; y++)
        {
            if (worldBiome[x, y] != 6) continue;
            if (worldMask[x, y] != LevelGenerator.TileType.Wall) continue;

            var cellPos = new Vector2I(x, y);
            int src = _wallsTileMap.GetCellSourceId(cellPos);
            Vector2I coords = _wallsTileMap.GetCellAtlasCoords(cellPos);
            if (src == 5 && (coords == new Vector2I(19, 39) || coords == new Vector2I(21, 38)))
            {
                overlay.SetCell(cellPos, src, coords);
            }
        }
    }

    // Лава: пульс для пола — только для конкретного тайла Atlas 4: (9,8) и только в биоме 6.
    // Ставим тот же тайл на overlay-слой, чтобы global tween анимировал модулейт (осветление/затухание).
    private void AddLavaFloorPulsingOverlays(TileMapLayer overlay, int floorsSourceId, LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int w, int h)
    {
        if (overlay == null || _floorsTileMap == null) return;
        Vector2I targetFloor = new Vector2I(9, 8); // Atlas 4
        for (int x = 1; x < w - 1; x++)
        for (int y = 1; y < h - 1; y++)
        {
            if (worldBiome[x, y] != 6) continue;
            // Пульсация делается только на проходимых клетках, чтобы было видно игроку
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;

            var cellPos = new Vector2I(x, y);
            int src = _floorsTileMap.GetCellSourceId(cellPos);
            if (src != floorsSourceId) continue;
            Vector2I coords = _floorsTileMap.GetCellAtlasCoords(cellPos);
            if (coords == targetFloor)
            {
                overlay.SetCell(cellPos, floorsSourceId, targetFloor);
            }
        }
    }

    // Удалено: пульсация пола биома 6

    private static Vector2 LocalMapTileToIsometricWorld(Vector2I tilePos)
    {
        Vector2I tileSize = new Vector2I(32, 16);
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;
        return new Vector2(x, y);
    }

    // Удалён помощник «шапок»

    // Поиск пути по мировым тайлам для MST/мостов
    private List<Vector2I> FindWorldPathOrganic(Vector2I startWp, Vector2I goalWp)
    {
        var open = new SortedSet<(int, int, Vector2I)>(Comparer<(int, int, Vector2I)>.Create((a, b) =>
            a.Item1 != b.Item1 ? a.Item1 - b.Item1 : a.Item2 != b.Item2 ? a.Item2 - b.Item2 : a.Item3.X != b.Item3.X ? a.Item3.X - b.Item3.X : a.Item3.Y - b.Item3.Y));
        var came = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, int>();
        int H(Vector2I p) => Math.Abs(p.X - goalWp.X) + Math.Abs(p.Y - goalWp.Y);
        open.Add((H(startWp), 0, startWp)); gScore[startWp] = 0;
        var dirs = new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            var p = cur.Item3;
            if (p == goalWp)
            {
                var path = new List<Vector2I>();
                while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                path.Reverse(); return path;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.Y < 0) continue;
                int ng = cur.Item2 + 1;
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                { gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n)); }
            }
        }
        return null;
    }
}


