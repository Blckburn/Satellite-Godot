using Godot;
using System;
using System.Collections.Generic;

public sealed class WorldBiomesGenerator
{
    private readonly Random _random;
    private readonly BiomePalette _biome;
    private readonly TileMapLayer _floorsTileMap;
    private readonly TileMapLayer _wallsTileMap;
    private readonly int _floorsSourceId;
    private readonly int _wallsSourceId;

    public WorldBiomesGenerator(
        Random random,
        BiomePalette biome,
        TileMapLayer floorsTileMap,
        TileMapLayer wallsTileMap,
        int floorsSourceId,
        int wallsSourceId)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _biome = biome ?? throw new ArgumentNullException(nameof(biome));
        _floorsTileMap = floorsTileMap ?? throw new ArgumentNullException(nameof(floorsTileMap));
        _wallsTileMap = wallsTileMap ?? throw new ArgumentNullException(nameof(wallsTileMap));
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
        int attempts = 0; int maxAttempts = (worldWidth * worldHeight + 1) * 200;
        int spacing = Math.Max(2, 12); // BiomeMinSpacing по умолчанию 12
        while (centers.Count < (worldWidth * worldHeight) && attempts++ < maxAttempts)
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
            if (attempts % ((worldWidth * worldHeight + 1) * 20) == 0 && spacing > 4) spacing -= 2;
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
            new Vector2I(4, 1), new Vector2I(5, 1)
        };

        // Ice (биом 3)
        var iceBaseTiles = new Vector2I[] { new Vector2I(10, 10) };
        var iceRareTiles = new Vector2I[]
        {
            new Vector2I(4, 6), new Vector2I(5, 6)
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
            _floorsTileMap.SetCell(wp, _floorsSourceId, floorTile);

            if (worldMask[x, y] == LevelGenerator.TileType.Room)
            {
                _wallsTileMap.EraseCell(wp);
            }
            else if (worldBlendBorders)
            {
                // Стены временно отключены
                // var wallInfo = _biome.GetWallTileForBiomeEx(biome, wp);
                // _wallsTileMap.SetCell(wp, wallInfo.sourceId, wallInfo.tile);
            }
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rare);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rare);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
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
                            _floorsTileMap.SetCell(new Vector2I(x, y), _floorsSourceId, rareTile);
                            placedD[x, y] = true;
                            windowCountD[wx, wy]++;
                            need--;
                        }
                    }
                }
            }
        }
        // 6) MST между центрами и карвинг глобальных троп
        var edges = new List<(int a, int b, int w)>();
        for (int i = 0; i < centers.Count; i++)
        for (int j = i + 1; j < centers.Count; j++)
        {
            int dx = centers[i].pos.X - centers[j].pos.X;
            int dy = centers[i].pos.Y - centers[j].pos.Y;
            edges.Add((i, j, dx * dx + dy * dy));
        }
        edges.Sort((e1, e2) => e1.w.CompareTo(e2.w));
        var parent = new int[centers.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int FindP(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        bool UnionP(int x, int y) { x = FindP(x); y = FindP(y); if (x == y) return false; parent[y] = x; return true; }
        var chosen = new List<(int a, int b)>();
        foreach (var e in edges) if (UnionP(e.a, e.b)) chosen.Add((e.a, e.b));

        foreach (var c in chosen)
        {
            var path = FindWorldPathOrganic(centers[c.a].pos, centers[c.b].pos);
            if (path == null) continue;
            var tile = _biome.GetBridgeTile(true, locCarveGlobalTrailsWidth);
            foreach (var wp in path)
            {
                for (int w = -(locCarveGlobalTrailsWidth / 2); w <= (locCarveGlobalTrailsWidth / 2); w++)
                {
                    foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(0, 1) })
                    {
                        var p = new Vector2I(wp.X + d.X * w, wp.Y + d.Y * w);
                        _floorsTileMap.SetCell(p, _floorsSourceId, tile);
                        _wallsTileMap.EraseCell(p);
                    }
                }
            }
        }

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

        foreach (var c in centers)
        {
            var hub = c.pos;
            int searchR = Math.Max(8, locBiomeHallRadius + 18);
            for (int x = Math.Max(0, hub.X - searchR); x < Math.Min(worldTilesX, hub.X + searchR); x++)
            {
                for (int y = Math.Max(0, hub.Y - searchR); y < Math.Min(worldTilesY, hub.Y + searchR); y++)
                {
                    if (biomeLocal[x, y] != c.biome) continue;
                    if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;
                    int dx0 = x - hub.X, dy0 = y - hub.Y; if (dx0 * dx0 + dy0 * dy0 <= locBiomeHallRadius * locBiomeHallRadius) continue;
                    if (((x + y) % 11) != 0) continue;
                    var path = FindWorldPathConstrainedLocal(hub, new Vector2I(x, y), c.biome);
                    if (path == null) continue;
                    var tile = _biome.GetFloorTileForBiome(c.biome);
                    foreach (var wp in path)
                    {
                        for (int w = -(locLocalCorridorWidth / 2); w <= (locLocalCorridorWidth / 2); w++)
                        {
                            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(0, 1) })
                            {
                                var p = new Vector2I(wp.X + d.X * w, wp.Y + d.Y * w);
                                _floorsTileMap.SetCell(p, _floorsSourceId, tile);
                                _wallsTileMap.EraseCell(p);
                                if (p.X >= 0 && p.X < worldTilesX && p.Y >= 0 && p.Y < worldTilesY)
                                    worldMask[p.X, p.Y] = LevelGenerator.TileType.Room;
                            }
                        }
                    }
                }
            }
        }

        // 8) Реки
        for (int ri = 0; ri < locRiverCount; ri++)
        {
            bool horizontal = rng.NextDouble() < 0.5;
            if (horizontal)
            {
                int y0 = rng.Next(worldTilesY);
                for (int x = 0; x < worldTilesX; x++)
                {
                    int y = y0 + (int)(Math.Sin(x * locRiverNoiseFreq) * locRiverNoiseAmp);
                    for (int w = -locRiverWidth / 2; w <= locRiverWidth / 2; w++)
                    {
                        int yy = y + w; if (yy < 0 || yy >= worldTilesY) continue;
                        // Реки: принудительно (0,10) из атласа 4
                        var liquidTile = new Vector2I(0, 10);
                        _floorsTileMap.SetCell(new Vector2I(x, yy), _floorsSourceId, liquidTile);
                        _wallsTileMap.EraseCell(new Vector2I(x, yy));
                        worldMask[x, yy] = LevelGenerator.TileType.Background;
                        waterMask[x, yy] = true;
                    }
                }
            }
            else
            {
                int x0 = rng.Next(worldTilesX);
                for (int y = 0; y < worldTilesY; y++)
                {
                    int x = x0 + (int)(Math.Sin(y * locRiverNoiseFreq) * locRiverNoiseAmp);
                    for (int w = -locRiverWidth / 2; w <= locRiverWidth / 2; w++)
                    {
                        int xx = x + w; if (xx < 0 || xx >= worldTilesX) continue;
                        // Реки: принудительно (0,10) из атласа 4
                        var liquidTile = new Vector2I(0, 10);
                        _floorsTileMap.SetCell(new Vector2I(xx, y), _floorsSourceId, liquidTile);
                        _wallsTileMap.EraseCell(new Vector2I(xx, y));
                        worldMask[xx, y] = LevelGenerator.TileType.Background;
                        waterMask[xx, y] = true;
                    }
                }
            }
        }

        // 9) Мосты над реками по траекториям MST
        foreach (var c in chosen)
        {
            var path = FindWorldPathOrganic(centers[c.a].pos, centers[c.b].pos);
            if (path == null) continue;
            var tile = _biome.GetBridgeTile(false, locCarveGlobalTrailsWidth);
            for (int i = 0; i < path.Count; i++)
            {
                var wp = path[i];
                if (wp.X < 1 || wp.X >= worldTilesX - 1 || wp.Y < 1 || wp.Y >= worldTilesY - 1) continue;
                if (!waterMask[wp.X, wp.Y] && !waterMask[wp.X + 1, wp.Y] && !waterMask[wp.X - 1, wp.Y] && !waterMask[wp.X, wp.Y + 1] && !waterMask[wp.X, wp.Y - 1])
                    continue;

                int waterRunX = 0; for (int dx = -6; dx <= 6; dx++) if (wp.X + dx >= 0 && wp.X + dx < worldTilesX && waterMask[wp.X + dx, wp.Y]) waterRunX++;
                int waterRunY = 0; for (int dy = -6; dy <= 6; dy++) if (wp.Y + dy >= 0 && wp.Y + dy < worldTilesY && waterMask[wp.X, wp.Y + dy]) waterRunY++;
                bool riverVertical = waterRunY >= waterRunX;

                int halfBridge = Math.Max((locCarveGlobalTrailsWidth + 2) / 2, 3);
                int halfSpan = Math.Max(locRiverWidth / 2 + 2, 5);

                if (riverVertical)
                {
                    for (int ox = -halfSpan; ox <= halfSpan; ox++)
                    for (int w = -halfBridge; w <= halfBridge; w++)
                    {
                        var p = new Vector2I(wp.X + ox, wp.Y + w);
                        if (p.X < 0 || p.X >= worldTilesX || p.Y < 0 || p.Y >= worldTilesY) continue;
                        _floorsTileMap.SetCell(p, _floorsSourceId, tile);
                        _wallsTileMap.EraseCell(p);
                        worldMask[p.X, p.Y] = LevelGenerator.TileType.Room; waterMask[p.X, p.Y] = false;
                    }
                }
                else
                {
                    for (int oy = -halfSpan; oy <= halfSpan; oy++)
                    for (int w = -halfBridge; w <= halfBridge; w++)
                    {
                        var p = new Vector2I(wp.X + w, wp.Y + oy);
                        if (p.X < 0 || p.X >= worldTilesX || p.Y < 0 || p.Y >= worldTilesY) continue;
                        _floorsTileMap.SetCell(p, _floorsSourceId, tile);
                        _wallsTileMap.EraseCell(p);
                        worldMask[p.X, p.Y] = LevelGenerator.TileType.Room; waterMask[p.X, p.Y] = false;
                    }
                }
            }
        }

        // 10) Внешние стены (через утилиту) + обновление HUD углов
        BorderWallsBuilder.AddBiomeBasedBorderWalls(
            worldMask,
            worldBiome,
            worldTilesX,
            worldTilesY,
            _wallsTileMap,
            _wallsSourceId,
            _biome,
            onCornersComputed
        );
    }

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


