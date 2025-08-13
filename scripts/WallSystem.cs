using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// MVP системы стен для WorldBiomes: строит магистральный коридор, холлы в биомах,
/// маленькие комнаты и затем заполняет оставшееся пространство стенами.
/// Без автотайлинга, без масок соседей; акцент на топологии и проходимости.
/// </summary>
public static class WallSystem
{
    // Базовый сид для детерминированности шумов внутри этого модуля
    private static int _seedBase = 1337;
    public sealed class Params
    {
        public int MainCorridorWidth = 5;
        public int LocalCorridorWidth = 4;
        public float HallRadiusScale = 0.06f; // устарело: оставлено для совместимости
        public int MinRoomsPerBiome = 3;
        public int MaxRoomsPerBiome = 12;
        public Vector2I MinRoomSize = new Vector2I(12, 10);
        public Vector2I MaxRoomSize = new Vector2I(22, 16);
        public int DoorwayWidth = 3;
        public Vector2I WallsTileOffset = new Vector2I(0, 0);
        public float TargetWallsFraction = 0.25f; // 20-30% стен, берём середину
        public float MinHallFraction = 0.18f;     // минимум площади под холл
        public float MaxHallFraction = 0.35f;     // максимум площади под холл
        public int MinHallRadius = 10;
        public int MinHallSpacingTiles = 24;      // минимальная дистанция между центрами холлов
        public float EdgeBiasWeight = 1.0f;       // тяга к краям
        public float SpacingWeight = 1.6f;        // тяга к взаимному удалению
        public int HallCenterSampleStep = 2;      // шаг дискретизации поиска центра
    }

    public static void BuildTopology(
        Random rng,
        TileMapLayer floors,
        TileMapLayer walls,
        int floorsSourceId,
        int wallsSourceId,
        BiomePalette palette,
        int[,] worldBiome,
        LevelGenerator.TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY,
        IReadOnlyList<(Vector2I pos, int biome)> biomeCenters,
        Params p
    )
    {
        if (rng == null) throw new ArgumentNullException(nameof(rng));
        if (floors == null) throw new ArgumentNullException(nameof(floors));
        if (walls == null) throw new ArgumentNullException(nameof(walls));
        if (palette == null) throw new ArgumentNullException(nameof(palette));
        if (worldBiome == null || worldMask == null) throw new ArgumentNullException("world data");
        if (biomeCenters == null || biomeCenters.Count == 0) return;

        // Обновляем базовый сид (зависит от глобального RNG)
        _seedBase = rng.Next(int.MaxValue);

        // 0) Сбрасываем предыдущие «комнатные» маски (от CA) — иначе появятся «дыры» без проходов
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
            worldMask[x, y] = LevelGenerator.TileType.Background;

        // 1) Магистраль: вместо змейки строим MST по центрам биомов
        var mainCorridor = new HashSet<Vector2I>();
        var mstEdges = BuildMstEdges(biomeCenters);

        // Для каждого ребра MST создаём сглаженную полилинию и вырезаем «органичный» коридор
        foreach (var e in mstEdges)
        {
            var poly = BuildSmoothedPolyline(e.a, e.b, rng);
            // Магистраль: без «карманов», только округлая кисть и вариативная ширина
            CarveOrganicPolyline(worldBiome, -1, worldMask, worldTilesX, worldTilesY, poly, p.MainCorridorWidth, rng, mainCorridor, 1.2f, 0.32f, scatter:false, edgeScatter:true);
        }
        // Постобработка по всем записанным клеткам магистрали
        FuzzCorridorEdges(worldBiome, -1, worldMask, worldTilesX, worldTilesY, mainCorridor, 9337);

        // 2) Холлы: у каждого биома — круглый зал. Размер от площади биома и целевой доли стен
        // Порядок: сначала большие биомы, чтобы крупные холлы заняли лучшие позиции
        var biomeOrder = new List<(Vector2I pos, int biome)>(biomeCenters);
        // Разносим по четырём углам приоритетно: вычисляем квадранты
        biomeOrder.Sort((a, b) =>
        {
            int areaB = EstimateBiomeArea(worldBiome, b.biome, worldTilesX, worldTilesY);
            int areaA = EstimateBiomeArea(worldBiome, a.biome, worldTilesX, worldTilesY);
            return areaB.CompareTo(areaA);
        });
        var chosenHallCenters = new List<(Vector2I Center, int Radius)>();
        var biomeToHall = new Dictionary<int, (Vector2I Center, int Radius)>();

        foreach (var c in biomeOrder)
        {
            int area = EstimateBiomeArea(worldBiome, c.biome, worldTilesX, worldTilesY);
            float targetRoomsFraction = 1.0f - Math.Clamp(p.TargetWallsFraction, 0.2f, 0.3f); // 0.7..0.8
            int targetRoomsArea = (int)(targetRoomsFraction * area);

            // Холл берём как ~половину прежней зависимости от площади биома
            float hallFrac = Math.Clamp(0.11f, p.MinHallFraction * 0.5f, p.MaxHallFraction * 0.5f);
            int hallArea = Math.Clamp((int)(hallFrac * area), (int)(p.MinHallFraction * 0.5f * area), (int)(p.MaxHallFraction * 0.5f * area));
            hallArea = Math.Min(hallArea, Math.Max(targetRoomsArea - (int)(0.35f * area), (int)(0.15f * area))); // не съедать весь бюджет

            int r = Math.Max(p.MinHallRadius, (int)MathF.Sqrt(hallArea / MathF.PI));
            // Находим оптимальный центр холла через дистанц-поле до границы биома
            var boundaryDist = ComputeBoundaryDistance(worldBiome, c.biome, worldTilesX, worldTilesY);
            Vector2I hallCenter = FindBestHallCenter(
                worldBiome, c.biome, worldTilesX, worldTilesY, r,
                chosenHallCenters, p.MinHallSpacingTiles, p.EdgeBiasWeight, p.SpacingWeight, p.HallCenterSampleStep,
                c.pos, biomeCenters);

            // Гарантия: радиус не превышает clearance от границы
            int clearance = boundaryDist[hallCenter.X, hallCenter.Y];
            int safeR = Math.Min(r, Math.Max(0, clearance - 2));
            if (safeR < p.MinHallRadius)
            {
                // Попробуем найти точку с clearance >= MinHallRadius
                var better = FindCenterWithClearance(boundaryDist, worldBiome, c.biome, worldTilesX, worldTilesY, p.MinHallRadius, chosenHallCenters, p.MinHallSpacingTiles);
                if (better != null)
                {
                    hallCenter = better.Value;
                    clearance = boundaryDist[hallCenter.X, hallCenter.Y];
                    safeR = Math.Min(r, Math.Max(0, clearance - 2));
                }
                else
                {
                    // Возьмем точку с максимальным clearance и построим максимально возможный холл (>0)
                    var bestAny = FindCenterWithClearance(boundaryDist, worldBiome, c.biome, worldTilesX, worldTilesY, 1, chosenHallCenters, p.MinHallSpacingTiles);
                    if (bestAny != null)
                    {
                        hallCenter = bestAny.Value;
                        clearance = boundaryDist[hallCenter.X, hallCenter.Y];
                        safeR = Math.Max(2, Math.Min(r, clearance - 1));
                    }
                }
            }

            // Страховка: если даже после всех попыток safeR < 2, поднимаем до минимума 2
            if (safeR < 2) safeR = 2;
            CarveHall(floors, walls, floorsSourceId, worldMask, worldTilesX, worldTilesY, hallCenter, safeR, palette.GetFloorTileForBiome(c.biome), c.biome, worldBiome, rng);
            // Соединяем холл с магистралью органичным коридором по своему биому
            var mainTarget = FindNearestMainPoint(worldBiome, c.biome, worldMask, worldTilesX, worldTilesY, hallCenter, mainCorridor);
            if (mainTarget != null)
            {
                // Локальные связки: собираем клетки, чтобы после пройти пост-рассеиванием
                var localRecord = new System.Collections.Generic.HashSet<Vector2I>();
                CarveOrganicCorridor(worldBiome, c.biome, worldMask, worldTilesX, worldTilesY, hallCenter, mainTarget.Value, p.LocalCorridorWidth, rng, localRecord, 0.9f, 0.30f, scatter:false, edgeScatter:true);
                FuzzCorridorEdges(worldBiome, c.biome, worldMask, worldTilesX, worldTilesY, localRecord, 1451 + c.biome * 17);
            }
            chosenHallCenters.Add((Center: hallCenter, Radius: safeR));
            biomeToHall[c.biome] = (Center: hallCenter, Radius: safeR);
        }

        // 3) Малые комнаты: от площади; добираем покрытие до целевой доли 70-80%
        foreach (var c in biomeCenters)
        {
            int area = EstimateBiomeArea(worldBiome, c.biome, worldTilesX, worldTilesY);
            float targetRoomsFraction = 1.0f - Math.Clamp(p.TargetWallsFraction, 0.2f, 0.3f);
            int desiredRoomsAreaTotal = (int)(targetRoomsFraction * area);
            var hallData = biomeToHall.ContainsKey(c.biome) ? biomeToHall[c.biome] : (Center: c.pos, Radius: Math.Max(p.MinHallRadius, (int)MathF.Sqrt(Math.Clamp((int)(0.11f * area), (int)(p.MinHallFraction*0.5f*area), (int)(p.MaxHallFraction*0.5f*area)) / MathF.PI)));
            int hallAreaActual = (int)(MathF.PI * hallData.Radius * hallData.Radius);
            int desiredRoomsArea = Math.Max(0, desiredRoomsAreaTotal - hallAreaActual);

            // Базовое количество комнат как ступенчатая прогрессия от площади
            int minRooms = p.MinRoomsPerBiome;
            int maxRooms = p.MaxRoomsPerBiome;
            float ratio = area / (float)Math.Max(1, hallData.Radius * hallData.Radius * 8); // отношение к «минимальному» биому
            if (ratio >= 1.5f) { minRooms += 1; maxRooms += 2; }
            if (ratio >= 2.5f) { minRooms += 1; maxRooms += 2; }
            if (ratio >= 4.0f) { minRooms += 1; maxRooms += 2; }

            var rooms = PlaceRoomsInBiomeFillingBudget(
                rng, worldBiome, c.biome, worldTilesX, worldTilesY,
                desiredRoomsArea, hallData.Center, hallData.Radius, p.MinRoomSize, p.MaxRoomSize,
                minRooms, maxRooms
            );
            foreach (var room in rooms)
            {
                // Соединяем комнату с холлом органичным коридором
                CarveOrganicCorridor(worldBiome, c.biome, worldMask, worldTilesX, worldTilesY, RectCenter(room), hallData.Center, p.LocalCorridorWidth, rng);
                // Прорезаем саму комнату как органическую область
                CarveOrganicRoom(worldMask, worldBiome, c.biome, worldTilesX, worldTilesY, room, rng);
            }
        }

        // 4) Стены: всё, что не Room — помечаем как Wall и ставим простой биомный тайл стены
        // Ставим стены (теперь — только стены; пол не красим)
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            if (worldMask[x, y] == LevelGenerator.TileType.Room) continue;
            worldMask[x, y] = LevelGenerator.TileType.Wall;
            int biomeAt = worldBiome[x, y];
            var info = palette.GetWallTileForBiomeEx(biomeAt, new Vector2I(x, y));
            walls.SetCell(new Vector2I(x, y), info.sourceId, info.tile);
        }
    }

    private static int EstimateBiomeArea(int[,] worldBiome, int biome, int w, int h)
    {
        int s = 0; for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (worldBiome[x, y] == biome) s++; return s;
    }

    private static void CarveHall(TileMapLayer floors, TileMapLayer walls, int floorsSourceId, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I center, int radius, Vector2I floorTile, int biome, int[,] worldBiome, Random rng)
    {
        float jitterFrac = 0.5f; // сильнее волнистость
        float sx = 1f + ((float)rng.NextDouble() - 0.5f) * 0.5f;
        float sy = 1f + ((float)rng.NextDouble() - 0.5f) * 0.5f;
        for (int dx = -radius - 2; dx <= radius + 2; dx++)
        for (int dy = -radius - 2; dy <= radius + 2; dy++)
        {
            int x = center.X + dx, y = center.Y + dy;
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            if (worldBiome[x, y] != biome) continue;
            float ndx = dx / sx, ndy = dy / sy;
            float d = ndx * ndx + ndy * ndy;
            float n1 = (((Hash2D(x, y, biome * 7919 + radius * 101) & 1023) / 1023f) - 0.5f) * 2f;
            float n2 = (((Hash2D(x+17, y-13, biome * 9173 + radius * 211) & 1023) / 1023f) - 0.5f) * 2f;
            float noise = n1 * 0.6f + n2 * 0.4f; // [-1..1]
            float rj = radius * (1f + jitterFrac * noise);
            if (d <= rj * rj)
                worldMask[x, y] = LevelGenerator.TileType.Room;
        }
    }

    private static void EnsureConnectionToMain(TileMapLayer floors, TileMapLayer walls, int floorsSourceId, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I center, int radius, int doorwayWidth, Vector2I floorTile)
    {
        // Простой дверной проём к ближайшему участку уже вырезанной магистрали (по 4-соседям)
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        Vector2I? best = null; int bestD = int.MaxValue;
        for (int x = Math.Max(0, center.X - radius - 8); x < Math.Min(w, center.X + radius + 8); x++)
        for (int y = Math.Max(0, center.Y - radius - 8); y < Math.Min(h, center.Y + radius + 8); y++)
        {
            if (worldMask[x, y] != LevelGenerator.TileType.Room) continue;
            int d2 = (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y);
            if (d2 < bestD && d2 > radius * radius) { bestD = d2; best = new Vector2I(x, y); }
        }
        if (!best.HasValue) return;

        // Прорисовываем логическую связь (без перекраски пола)
        var p0 = center; var p1 = best.Value;
        Vector2I step = new Vector2I(Math.Sign(p1.X - p0.X), Math.Sign(p1.Y - p0.Y));
        Vector2I p = p0;
        for (int i = 0; i < 2048; i++)
        {
            if (p == p1) break;
            for (int woff = -(doorwayWidth/2); woff <= (doorwayWidth/2); woff++)
            {
                int cx = p.X + (step.Y != 0 ? woff : 0);
                int cy = p.Y + (step.X != 0 ? woff : 0);
                if (cx < 0 || cx >= w || cy < 0 || cy >= h) continue;
                worldMask[cx, cy] = LevelGenerator.TileType.Room;
            }
            p = new Vector2I(p.X + step.X, p.Y + step.Y);
        }
    }

    private static void CarvePathRect(TileMapLayer floors, TileMapLayer walls, int floorsSourceId, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I a, Vector2I b, int width, Func<int, Vector2I> floorForBiome, HashSet<Vector2I> record = null)
    {
        int half = Math.Max(1, width / 2);
        Vector2I dirX = new Vector2I(Math.Sign(b.X - a.X), 0);
        Vector2I dirY = new Vector2I(0, Math.Sign(b.Y - a.Y));

        // L-образный путь: сначала X, потом Y (простая связка без A*) — только логика, без перекраски пола
        Vector2I p = a;
        while (p.X != b.X)
        {
            for (int woff = -half; woff <= half; woff++)
            {
                int cx = p.X;
                int cy = p.Y + woff;
                if (cx < 0 || cx >= w || cy < 0 || cy >= h) continue;
                worldMask[cx, cy] = LevelGenerator.TileType.Room;
                record?.Add(new Vector2I(cx, cy));
            }
            p = new Vector2I(p.X + dirX.X, p.Y);
        }
        while (p.Y != b.Y)
        {
            for (int woff = -half; woff <= half; woff++)
            {
                int cx = p.X + woff;
                int cy = p.Y;
                if (cx < 0 || cx >= w || cy < 0 || cy >= h) continue;
                worldMask[cx, cy] = LevelGenerator.TileType.Room;
                record?.Add(new Vector2I(cx, cy));
            }
            p = new Vector2I(p.X, p.Y + dirY.Y);
        }

        // Слегка расширяем угловую точку, чтобы не оставалось «щелей» в повороте
        for (int woff = -half; woff <= half; woff++)
        {
            int cx = b.X + woff;
            int cy = b.Y + woff;
            if (cx >= 0 && cx < w && cy >= 0 && cy < h)
            {
                worldMask[cx, cy] = LevelGenerator.TileType.Room;
                record?.Add(new Vector2I(cx, cy));
            }
        }
    }

    private static void CarveRect(TileMapLayer floors, int floorsSourceId, LevelGenerator.TileType[,] worldMask, Rect2I rect, Vector2I floorTile)
    {
        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            worldMask[x, y] = LevelGenerator.TileType.Room;
        }
    }

    private static List<Rect2I> PlaceRoomsInBiomeFillingBudget(
        Random rng, int[,] worldBiome, int biome, int w, int h,
        int desiredRoomsArea, Vector2I hallCenter, int hallRadius,
        Vector2I minSize, Vector2I maxSize, int minRooms, int maxRooms)
    {
        var rooms = new List<Rect2I>();
        int areaAccum = 0;
        int attempts = 2000;
        while (attempts-- > 0 && areaAccum < desiredRoomsArea && rooms.Count < maxRooms)
        {
            int rw = rng.Next(minSize.X, maxSize.X + 1);
            int rh = rng.Next(minSize.Y, maxSize.Y + 1);
            int x = rng.Next(Math.Max(0, hallCenter.X - 4 * hallRadius), Math.Min(w - rw, hallCenter.X + 4 * hallRadius));
            int y = rng.Next(Math.Max(0, hallCenter.Y - 4 * hallRadius), Math.Min(h - rh, hallCenter.Y + 4 * hallRadius));
            var r = new Rect2I(new Vector2I(x, y), new Vector2I(rw, rh));
            if (!IsInsideBiome(worldBiome, biome, r)) continue;
            bool intersects = false;
            foreach (var ex in rooms) { if (ex.Grow(2).Intersects(r)) { intersects = true; break; } }
            if (intersects) continue;
            rooms.Add(r);
            areaAccum += rw * rh;
            if (rooms.Count >= minRooms && areaAccum >= desiredRoomsArea) break;
        }
        return rooms;
    }

    private static bool IsInsideBiome(int[,] worldBiome, int biome, Rect2I r)
    {
        for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
        for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
        {
            if (x < 0 || y < 0 || x >= worldBiome.GetLength(0) || y >= worldBiome.GetLength(1)) return false;
            if (worldBiome[x, y] != biome) return false;
        }
        return true;
    }

    private static Vector2I RectCenter(Rect2I r)
    {
        return new Vector2I(r.Position.X + r.Size.X / 2, r.Position.Y + r.Size.Y / 2);
    }

    private static void CarveSolidRoom(LevelGenerator.TileType[,] worldMask, int w, int h, Rect2I room)
    {
        for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
        for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            worldMask[x, y] = LevelGenerator.TileType.Room;
        }
    }

    private static void CarveOrganicRoom(LevelGenerator.TileType[,] worldMask, int[,] worldBiome, int biome, int w, int h, Rect2I room, Random rng)
    {
        // Эллипсоидная «пузырчатая» комната с шумом как у холла
        Vector2I center = new Vector2I(room.Position.X + room.Size.X / 2, room.Position.Y + room.Size.Y / 2);
        int rx = Math.Max(3, room.Size.X / 2);
        int ry = Math.Max(3, room.Size.Y / 2);
        float jitterFrac = 0.5f; // хаос как у холла
        float sx = 1f + ((float)rng.NextDouble() - 0.5f) * 0.5f;
        float sy = 1f + ((float)rng.NextDouble() - 0.5f) * 0.5f;
        for (int dx = -rx - 2; dx <= rx + 2; dx++)
        for (int dy = -ry - 2; dy <= ry + 2; dy++)
        {
            int x = center.X + dx, y = center.Y + dy;
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            if (worldBiome[x, y] != biome) continue;
            float ndx = (dx / (float)rx) / sx;
            float ndy = (dy / (float)ry) / sy;
            float d = ndx * ndx + ndy * ndy;
            float n1 = (((Hash2D(x, y, biome * 3217 + rx * 97) & 1023) / 1023f) - 0.5f) * 2f;
            float n2 = (((Hash2D(x + 23, y - 17, biome * 7907 + ry * 131) & 1023) / 1023f) - 0.5f) * 2f;
            float noise = n1 * 0.6f + n2 * 0.4f; // [-1..1]
            float rj = 1f + jitterFrac * noise;
            if (d <= rj * rj)
                worldMask[x, y] = LevelGenerator.TileType.Room;
        }
    }

    private static int[] GenerateOffsetProfile(int len, int amp, int step, Random rng)
    {
        int points = Math.Max(2, (len + step - 1) / step + 1);
        float[] anchors = new float[points];
        for (int i = 0; i < points; i++)
        {
            // три гармоники для «зубчиков»
            float a = (float)(rng.NextDouble() * 2 - 1);
            float b = (float)(rng.NextDouble() * 2 - 1);
            float c = (float)(rng.NextDouble() * 2 - 1);
            anchors[i] = (a * 0.5f + b * 0.35f + c * 0.15f) * amp;
        }
        // сглаживание отключено — оставляем «жёсткую» гофру на краях
        int[] result = new int[len];
        for (int x = 0; x < len; x++)
        {
            float t = x / (float)step;
            int i0 = Math.Clamp((int)MathF.Floor(t), 0, points - 2);
            float frac = t - i0;
            float v = anchors[i0] * (1 - frac) + anchors[i0 + 1] * frac;
            result[x] = (int)MathF.Round(v);
        }
        return result;
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

    private static void ConnectHallToMainBfs(
        int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask,
        int w, int h, Vector2I hallCenter, int hallRadius,
        HashSet<Vector2I> mainCorridor, int corridorWidth)
    {
        // Находим ближайшую точку магистрали в пределах своего биома и режем BFS/A* по Background ячейкам
        var targets = new List<Vector2I>(mainCorridor);
        Vector2I start = hallCenter;
        var dirs = new[]{new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)};
        var q = new Queue<Vector2I>();
        var came = new Dictionary<Vector2I, Vector2I>();
        var seen = new HashSet<Vector2I>();
        q.Enqueue(start); seen.Add(start);
        Vector2I? goal = null;
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            if (mainCorridor.Contains(p)) { goal = p; break; }
            foreach (var d in dirs)
            {
                int nx = p.X + d.X, ny = p.Y + d.Y;
                var n = new Vector2I(nx, ny);
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (seen.Contains(n)) continue;
                if (worldBiome[nx, ny] != biome) continue; // только свой биом
                if (worldMask[nx, ny] == LevelGenerator.TileType.Wall) continue; // по background/room
                seen.Add(n); came[n] = p; q.Enqueue(n);
            }
        }
        if (goal == null) return;
        var path = new List<Vector2I>();
        var cur = goal.Value;
        while (cur != start && came.ContainsKey(cur)) { path.Add(cur); cur = came[cur]; }
        int half = Math.Max(1, corridorWidth/2);
        foreach (var p in path)
        {
            foreach (var d in dirs)
            {
                for (int woff = -half; woff <= half; woff++)
                {
                    int cx = p.X + (d.Y != 0 ? woff : 0);
                    int cy = p.Y + (d.X != 0 ? woff : 0);
                    if (cx < 0 || cx >= w || cy < 0 || cy >= h) continue;
                    worldMask[cx, cy] = LevelGenerator.TileType.Room;
                }
                break; // одно направление
            }
        }
    }

    private static Vector2I? FindNearestMainPoint(int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I start, HashSet<Vector2I> main)
    {
        var q = new Queue<Vector2I>();
        var seen = new HashSet<Vector2I>();
        q.Enqueue(start); seen.Add(start);
        var dirs = new[]{new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)};
        int visits = 0;
        int maxVisits = Math.Max(10_000, (w * h) / 3); // страховка от «зависаний»
        int maxRadius = Math.Max(w, h) / 2;
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            if (main.Contains(p)) return p;
            foreach (var d in dirs)
            {
                int nx = p.X + d.X, ny = p.Y + d.Y; var n = new Vector2I(nx, ny);
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (seen.Contains(n)) continue; seen.Add(n);
                if (worldBiome[nx, ny] != biome) continue;
                // ограничиваем радиус поиска, чтобы не обходить весь мир
                if (Math.Abs(n.X - start.X) + Math.Abs(n.Y - start.Y) > maxRadius) continue;
                q.Enqueue(n);
                if (++visits > maxVisits) return null;
            }
        }
        return null;
    }

    private static void CarveOrganicCorridor(int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I a, Vector2I b, int width, Random rng, HashSet<Vector2I> record = null, float wobbleAmp = 1.6f, float wobbleFreq = 0.45f, bool scatter = false, bool edgeScatter = false)
    {
        int half = Math.Max(1, width / 2);
        // fBm-профиль поперечного смещения вдоль параметра t
        float length = new Vector2(b.X - a.X, b.Y - a.Y).Length();
        int steps = Math.Max(2, (int)MathF.Ceiling(length));
        int seed = (a.X * 73856093) ^ (a.Y * 19349663);
        Vector2I last = a;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float nx = Lerp(a.X, b.X, t);
            float ny = Lerp(a.Y, b.Y, t);
            Vector2 dir = new Vector2(b.X - a.X, b.Y - a.Y).Normalized();
            Vector2 n = new Vector2(-dir.Y, dir.X);
            // fBm шум: три октавы с разной частотой
            float f1 = 0.04f, f2 = 0.09f, f3 = 0.21f;
            float a1 = 1.0f, a2 = 0.6f, a3 = 0.35f;
            float fbm = a1 * NoiseSmooth(nx * f1, ny * f1, seed) + a2 * NoiseSmooth(nx * f2, ny * f2, seed + 1337) + a3 * NoiseSmooth(nx * f3, ny * f3, seed + 7331);
            fbm = (fbm * 2f - 1f) * wobbleAmp * 2.6f; // [-amp..amp]
            Vector2 center = new Vector2(nx, ny) + n * fbm;
            int cx = (int)MathF.Round(center.X);
            int cy = (int)MathF.Round(center.Y);
            // Дополнительно модулируем ширину коридора вдоль пути (как у холлов)
            float rNoise = (NoiseSmooth(nx * 0.07f, ny * 0.07f, seed + 9999) * 2f - 1f);
            int localHalf = Math.Max(1, half + (int)MathF.Round(rNoise * MathF.Max(1f, half * 0.6f)));
            // Растеризация между last и текущей точкой, чтобы не было дыр
            if (edgeScatter)
                RasterizeRoundBrushSegment(worldBiome, biome, worldMask, w, h, last, new Vector2I(cx, cy), localHalf, seed, record);
            else
                RasterizeThickSegment(worldBiome, biome, worldMask, w, h, last, new Vector2I(cx, cy), localHalf, record);
            if (scatter)
            {
                // «Рассеянные» края: случайные карманы/выступы вдоль коридора
                if ((Hash2D(cx, cy, seed) & 15) == 0) // чаще (~1/16)
                {
                    int rad = rng.Next(Math.Max(1, half/2), Math.Max(2, half + 2));
                    for (int dx = -rad; dx <= rad; dx++)
                    for (int dy = -rad; dy <= rad; dy++)
                    {
                        if (dx*dx + dy*dy > rad*rad) continue;
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || px >= w || py < 0 || py >= h) continue;
                        if (biome >= 0 && worldBiome[px, py] != biome) continue;
                        worldMask[px, py] = LevelGenerator.TileType.Room;
                        record?.Add(new Vector2I(px, py));
                    }
                }
            }
            last = new Vector2I(cx, cy);
        }
    }

    private static void CarveOrganicPolyline(
        int[,] worldBiome,
        int biome,
        LevelGenerator.TileType[,] worldMask,
        int w,
        int h,
        System.Collections.Generic.List<Vector2I> poly,
        int width,
        Random rng,
        HashSet<Vector2I> record,
        float wobbleAmp,
        float wobbleFreq,
        bool scatter,
        bool edgeScatter)
    {
        if (poly == null || poly.Count < 2) return;
        for (int i = 0; i + 1 < poly.Count; i++)
        {
            CarveOrganicCorridor(worldBiome, biome, worldMask, w, h, poly[i], poly[i + 1], width, rng, record, wobbleAmp, wobbleFreq, scatter, edgeScatter);
        }
    }

    private static void RasterizeThickSegment(int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I a, Vector2I b, int half, HashSet<Vector2I> record)
    {
        int dx = Math.Abs(b.X - a.X), sx = a.X < b.X ? 1 : -1;
        int dy = -Math.Abs(b.Y - a.Y), sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy, x = a.X, y = a.Y;
        while (true)
        {
            // толстый поперечник
            foreach (var d in new[]{new Vector2I(1,0), new Vector2I(0,1)})
            {
                for (int woff = -half; woff <= half; woff++)
                {
                    int px = x + (d.Y != 0 ? woff : 0);
                    int py = y + (d.X != 0 ? woff : 0);
                    if (px < 0 || px >= w || py < 0 || py >= h) continue;
                    if (biome >= 0 && worldBiome[px, py] != biome) continue;
                    worldMask[px, py] = LevelGenerator.TileType.Room;
                    record?.Add(new Vector2I(px, py));
                }
            }
            if (x == b.X && y == b.Y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    // «Круглая кисть» с лёгким джиттером радиуса для «лохматых» краёв
    private static void RasterizeRoundBrushSegment(int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask, int w, int h, Vector2I a, Vector2I b, int baseHalf, int seed, HashSet<Vector2I> record)
    {
        int dx = Math.Abs(b.X - a.X), sx = a.X < b.X ? 1 : -1;
        int dy = -Math.Abs(b.Y - a.Y), sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy, x = a.X, y = a.Y;
        while (true)
        {
            // Радиус с джиттером (сильнее, чтобы получить «лохматый» край)
            float j = (Hash2D(x, y, seed) & 1023) / 1023f; // [0..1]
            int r = Math.Max(1, baseHalf + (int)MathF.Round((j - 0.5f) * 5f));
            for (int ox = -r - 1; ox <= r + 1; ox++)
            for (int oy = -r - 1; oy <= r + 1; oy++)
            {
                int px = x + ox, py = y + oy;
                if (px < 0 || px >= w || py < 0 || py >= h) continue;
                if (ox*ox + oy*oy > r*r) continue;
                if (biome >= 0 && worldBiome[px, py] != biome) continue;
                worldMask[px, py] = LevelGenerator.TileType.Room;
                record?.Add(new Vector2I(px, py));
            }
            // Редкая «бахрома» вдоль окружности r+1
            int rr = r + 1;
            for (int ox = -rr - 1; ox <= rr + 1; ox++)
            for (int oy = -rr - 1; oy <= rr + 1; oy++)
            {
                int px = x + ox, py = y + oy; if (px < 0 || px >= w || py < 0 || py >= h) continue;
                int d2 = ox*ox + oy*oy; if (d2 <= rr*rr && d2 > r*r)
                {
                    // Вероятность появления бахромы — ниже вдоль окружности
                    int hsh = Hash2D(px, py, seed + 777);
                    if ((hsh & 255) < 64) // ~25%
                    {
                        if (biome >= 0 && worldBiome[px, py] != biome) continue;
                        worldMask[px, py] = LevelGenerator.TileType.Room;
                        record?.Add(new Vector2I(px, py));
                    }
                }
            }
            if (x == b.X && y == b.Y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float NoiseSmooth(float x, float y, int seed)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = x0 + 1, y1 = y0 + 1;
        float tx = x - x0, ty = y - y0;
        float u = tx * tx * (3 - 2 * tx); // smoothstep
        float v = ty * ty * (3 - 2 * ty);
        float n00 = (Hash2D(x0, y0, seed) & 1023) / 1023f;
        float n10 = (Hash2D(x1, y0, seed) & 1023) / 1023f;
        float n01 = (Hash2D(x0, y1, seed) & 1023) / 1023f;
        float n11 = (Hash2D(x1, y1, seed) & 1023) / 1023f;
        float nx0 = n00 * (1 - u) + n10 * u;
        float nx1 = n01 * (1 - u) + n11 * u;
        return nx0 * (1 - v) + nx1 * v;
    }

    private static float ClampF(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    // Выбор центра холла: тянем к краям и максимально разводим между собой
    private static Vector2I FindBestHallCenter(
        int[,] worldBiome, int biome, int w, int h, int radius,
        List<(Vector2I Center, int Radius)> chosen,
        int minSpacing,
        float edgeBiasWeight,
        float spacingWeight,
        int step,
        Vector2I biomeSeed,
        IReadOnlyList<(Vector2I pos, int biome)> allCenters)
    {
        Vector2I best = new Vector2I(w/2, h/2);
        float bestScore = float.NegativeInfinity;
        bool anyCandidate = false;

        for (int x = 0; x < w; x += Math.Max(1, step))
        for (int y = 0; y < h; y += Math.Max(1, step))
        {
            // Кандидат должен уместить круг радиуса radius целиком в пределах этого биома
            if (!IsDiskInsideBiome(worldBiome, biome, w, h, new Vector2I(x, y), radius)) continue;
            anyCandidate = true;

            // Проверка на минимальное расстояние до уже выбранных холлов
            bool ok = true;
            float spaceScore = 0f;
            foreach (var other in chosen)
            {
                int dx = x - other.Center.X; int dy = y - other.Center.Y;
                float dist = MathF.Sqrt(dx*dx + dy*dy);
                if (dist < (minSpacing + other.Radius + radius)) { ok = false; break; }
                spaceScore += dist;
            }
            if (!ok) continue;

            // Эдж-байас: предпочитаем точки ближе к краям карты (большой min из расстояний до краёв)
            float edgeDist = MathF.Min(MathF.Min(x, w-1-x), MathF.Min(y, h-1-y));
            float edgeScore = (w + h) * 0.5f - edgeDist; // меньше расстояние — выше score

            // Байас к «своему» Poisson-сид центру биома: хотим ближе к нему
            float seedDx = x - biomeSeed.X; float seedDy = y - biomeSeed.Y;
            float seedScore = -MathF.Sqrt(seedDx*seedDx + seedDy*seedDy) * 0.5f; // мягкий штраф за удаление

            float score = spacingWeight * spaceScore + edgeBiasWeight * edgeScore;
            score += seedScore;
            if (score > bestScore)
            {
                bestScore = score; best = new Vector2I(x, y);
            }
        }
        if (anyCandidate) return best;

        // Резерв: если круг радиуса r не влазит в этот биом (узкий биом), уменьшаем радиус до минимума и ищем снова
        for (int rr = radius - 1; rr >= Math.Max(4, radius/2); rr--)
        {
            for (int x = 0; x < w; x += Math.Max(1, step))
            for (int y = 0; y < h; y += Math.Max(1, step))
            {
                if (!IsDiskInsideBiome(worldBiome, biome, w, h, new Vector2I(x, y), rr)) continue;
                bool ok = true;
                foreach (var other in chosen)
                {
                    int dx = x - other.Center.X; int dy = y - other.Center.Y;
                    float dist = MathF.Sqrt(dx*dx + dy*dy);
                    if (dist < (minSpacing + other.Radius + rr)) { ok = false; break; }
                }
                if (!ok) continue;
                return new Vector2I(x, y);
            }
        }

        // Последний резерв: ставим минимум в ближайшей допустимой точке к seed
        for (int rr = Math.Max(4, radius/2); rr >= 3; rr--)
        {
            for (int rad = 0; rad < 360; rad += 5)
            {
                int x = biomeSeed.X + (int)(rr * MathF.Cos(rad * MathF.PI/180f));
                int y = biomeSeed.Y + (int)(rr * MathF.Sin(rad * MathF.PI/180f));
                if (!IsDiskInsideBiome(worldBiome, biome, w, h, new Vector2I(x, y), rr)) continue;
                return new Vector2I(x, y);
            }
        }

        return biomeSeed; // крайний фолбэк
    }

    private static bool IsDiskInsideBiome(int[,] worldBiome, int biome, int w, int h, Vector2I c, int r)
    {
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            int x = c.X + dx, y = c.Y + dy;
            if (dx*dx + dy*dy > r*r) continue;
            if (x < 0 || x >= w || y < 0 || y >= h) return false;
            if (worldBiome[x, y] != biome) return false;
        }
        return true;
    }

    private static Vector2I? FindCenterWithClearance(int[,] boundaryDist, int[,] worldBiome, int biome, int w, int h, int minClearance, List<(Vector2I Center, int Radius)> chosen, int minSpacing)
    {
        Vector2I? best = null;
        int bestC = -1;
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            if (worldBiome[x, y] != biome) continue;
            int c = boundaryDist[x, y];
            if (c < minClearance) continue;
            bool ok = true;
            foreach (var other in chosen)
            {
                int dx = x - other.Center.X; int dy = y - other.Center.Y;
                float dist = MathF.Sqrt(dx*dx + dy*dy);
                if (dist < (minSpacing + other.Radius + minClearance)) { ok = false; break; }
            }
            if (!ok) continue;
            if (c > bestC) { bestC = c; best = new Vector2I(x, y); }
        }
        return best;
    }
    // Целочисленное расстояние до границы биома (манхэттен), 0 на границе и вне
    private static int[,] ComputeBoundaryDistance(int[,] worldBiome, int biome, int w, int h)
    {
        var dist = new int[w, h];
        var queue = new Queue<Vector2I>();
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            if (worldBiome[x, y] != biome) { dist[x, y] = 0; continue; }
            bool isBoundary = false;
            foreach (var d in new[]{new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)})
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || worldBiome[nx, ny] != biome) { isBoundary = true; break; }
            }
            if (isBoundary) { dist[x, y] = 1; queue.Enqueue(new Vector2I(x, y)); } else { dist[x, y] = int.MaxValue/4; }
        }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            int v = dist[p.X, p.Y];
            foreach (var d in new[]{new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)})
            {
                int nx = p.X + d.X, ny = p.Y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (worldBiome[nx, ny] != biome) continue;
                if (dist[nx, ny] > v + 1) { dist[nx, ny] = v + 1; queue.Enqueue(new Vector2I(nx, ny)); }
            }
        }
        return dist;
    }

    // ---------- MST + сглаживание для магистрали ----------

    // Добавляет «бахрому» вдоль края ячеек, вырезанных как Room (магистраль), чтобы визуально совпадать с холлами
    private static void FuzzCorridorEdges(int[,] worldBiome, int biome, LevelGenerator.TileType[,] worldMask, int w, int h, System.Collections.Generic.HashSet<Vector2I> recorded, int seed)
    {
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        // Соберём крайовые клетки коридора
        var edge = new System.Collections.Generic.List<Vector2I>();
        foreach (var p in recorded)
        {
            bool boundary = false;
            foreach (var d in dirs)
            {
                int nx = p.X + d.X, ny = p.Y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) { boundary = true; break; }
                if (worldMask[nx, ny] != LevelGenerator.TileType.Room) { boundary = true; break; }
            }
            if (boundary) edge.Add(p);
        }
        // Для каждой крайовой клетки расширяем наружу на случайные 1..3 клетки с высокой вероятностью
        foreach (var p in edge)
        {
            foreach (var d in dirs)
            {
                int nx = p.X + d.X, ny = p.Y + d.Y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (worldMask[nx, ny] == LevelGenerator.TileType.Room) continue;
                int hsh = Hash2D(nx, ny, seed);
                if ((hsh & 255) < 179) // ~70%
                {
                    if (biome >= 0 && worldBiome[nx, ny] != biome) continue;
                    worldMask[nx, ny] = LevelGenerator.TileType.Room;
                    // Случайная длина продолжения 0..2 (итого 1..3 клетки)
                    int extra = (Hash2D(nx, ny, seed + 7) % 3); // 0..2
                    int cx = nx, cy = ny;
                    for (int t = 0; t < extra; t++)
                    {
                        cx += d.X; cy += d.Y;
                        if (cx < 0 || cx >= w || cy < 0 || cy >= h) break;
                        if (worldMask[cx, cy] == LevelGenerator.TileType.Room) break;
                        if (biome >= 0 && worldBiome[cx, cy] != biome) break;
                        worldMask[cx, cy] = LevelGenerator.TileType.Room;
                    }
                }
            }
        }
        // Редкие «пики» до 5 клеток
        foreach (var p in edge)
        {
            foreach (var d in dirs)
            {
                int nx = p.X + d.X*4, ny = p.Y + d.Y*4; // четвёртая клетка от края (итог 5 с базовой)
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                if (worldMask[nx, ny] == LevelGenerator.TileType.Room) continue;
                int hsh = Hash2D(nx, ny, seed + 31337);
                if ((hsh & 255) < 26) // ~10%
                {
                    // Заполняем «мостик» до неё
                    for (int t = 1; t <= 4; t++)
                    {
                        int fx = p.X + d.X * t, fy = p.Y + d.Y * t;
                        if (fx < 0 || fx >= w || fy < 0 || fy >= h) break;
                        if (biome >= 0 && worldBiome[fx, fy] != biome) break;
                        worldMask[fx, fy] = LevelGenerator.TileType.Room;
                    }
                }
            }
        }
    }

    private static List<(Vector2I a, Vector2I b)> BuildMstEdges(IReadOnlyList<(Vector2I pos, int biome)> centers)
    {
        var edges = new List<(int u, int v, int w)>();
        int n = centers.Count;
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            var p = centers[i].pos; var q = centers[j].pos;
            int w = Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y); // L1: устойчивее на решётке
            edges.Add((i, j, w));
        }
        edges.Sort((a, b) => a.w.CompareTo(b.w));
        var dsu = new DisjointSet(n);
        var result = new List<(Vector2I a, Vector2I b)>();
        foreach (var e in edges)
        {
            if (dsu.Union(e.u, e.v))
                result.Add((centers[e.u].pos, centers[e.v].pos));
            if (result.Count == n - 1) break;
        }
        return result;
    }

    private sealed class DisjointSet
    {
        private readonly int[] _p; private readonly int[] _r;
        public DisjointSet(int n) { _p = new int[n]; _r = new int[n]; for (int i = 0; i < n; i++) _p[i] = i; }
        private int Find(int x) { return _p[x] == x ? x : _p[x] = Find(_p[x]); }
        public bool Union(int a, int b)
        {
            a = Find(a); b = Find(b); if (a == b) return false;
            if (_r[a] < _r[b]) { int t = a; a = b; b = t; }
            _p[b] = a;
            if (_r[a] == _r[b]) _r[a]++;
            return true;
        }
    }

    private static System.Collections.Generic.List<Vector2I> BuildSmoothedPolyline(Vector2I a, Vector2I b, Random rng)
    {
        var pts = new System.Collections.Generic.List<Vector2> { new Vector2(a.X, a.Y) };
        Vector2 A = new Vector2(a.X, a.Y), B = new Vector2(b.X, b.Y);
        Vector2 dir = (B - A);
        float len = dir.Length(); if (len < 1f) return new System.Collections.Generic.List<Vector2I> { a, b };
        dir /= len; Vector2 perp = new Vector2(-dir.Y, dir.X);
        float off = ClampF(len * 0.12f, 3f, 18f);
        float o1 = (float)(rng.NextDouble() * 2 - 1) * off;
        float o2 = (float)(rng.NextDouble() * 2 - 1) * off;
        Vector2 p1 = A + dir * (len * 0.33f) + perp * o1;
        Vector2 p2 = A + dir * (len * 0.66f) + perp * o2;
        pts.Add(p1); pts.Add(p2); pts.Add(B);

        // Chaikin 2–3 итерации
        for (int it = 0; it < 2; it++) pts = ChaikinOnce(pts);

        var res = new System.Collections.Generic.List<Vector2I>(pts.Count);
        foreach (var p in pts) res.Add(new Vector2I((int)MathF.Round(p.X), (int)MathF.Round(p.Y)));
        return res;
    }

    private static System.Collections.Generic.List<Vector2> ChaikinOnce(System.Collections.Generic.List<Vector2> src)
    {
        if (src.Count < 3) return new System.Collections.Generic.List<Vector2>(src);
        var dst = new System.Collections.Generic.List<Vector2>(src.Count * 2);
        dst.Add(src[0]);
        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector2 p = src[i]; Vector2 q = src[i + 1];
            Vector2 Q = p * 0.75f + q * 0.25f;
            Vector2 R = p * 0.25f + q * 0.75f;
            dst.Add(Q); dst.Add(R);
        }
        dst.Add(src[src.Count - 1]);
        return dst;
    }
}


