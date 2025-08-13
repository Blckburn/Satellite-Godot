using Godot;
using System;

/// <summary>
/// Централизует выбор тайлов в зависимости от биома: пол, стена, декорации.
/// </summary>
public sealed class BiomePalette
{
    private readonly Random _random;
    private readonly Func<bool> _useVariedWalls;

    public BiomePalette(Random random, Func<bool> useVariedWalls)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _useVariedWalls = useVariedWalls ?? (() => false);
    }

    public Vector2I GetFloorTileForBiome(int biomeType)
    {
        switch (biomeType)
        {
            case 0: // Grassland — только базовые тайлы для всех вызовов вне основного прохода
                {
                    var grassBaseTiles = new Vector2I[] { new Vector2I(5, 2), new Vector2I(6, 2) };
                    return grassBaseTiles[_random.Next(grassBaseTiles.Length)];
                }
            // Forest: базовые тайлы
            case 1:
                {
                    var forestBaseTiles = new Vector2I[] { new Vector2I(0, 2), new Vector2I(1, 2), new Vector2I(2, 2) };
                    return forestBaseTiles[_random.Next(forestBaseTiles.Length)];
                }
            case 2: return new Vector2I(9, 0); // Desert базовый
            case 3: return new Vector2I(10, 10); // Ice базовый под стенами и в коридорах
            case 4: return new Vector2I(8, 5); // Techno базовый
            case 5: return new Vector2I(6, 8); // Anomal базовый
            case 6: return new Vector2I(8, 5); // Lava Springs базовый
            default: return new Vector2I(2, 9); // Заглушка по умолчанию
        }
    }

    public Vector2I GetDecorationTileForBiome(int biomeType)
    {
        switch (biomeType)
        {
            case 1: return _random.Next(0, 100) < 50 ? new Vector2I(3, 0) : new Vector2I(2, 1); // Snow / ForestFloor
            case 2: return _random.Next(0, 100) < 60 ? new Vector2I(4, 0) : new Vector2I(1, 0); // Sand / Stone
            case 3: return _random.Next(0, 100) < 70 ? new Vector2I(3, 0) : new Vector2I(0, 1); // Snow / Ice
            case 4: return _random.Next(0, 100) < 50 ? new Vector2I(1, 0) : new Vector2I(3, 1); // Stone / Techno
            case 5: return _random.Next(0, 100) < 50 ? new Vector2I(4, 1) : new Vector2I(1, 1); // Anomal / Lava
            case 6: return _random.Next(0, 100) < 60 ? new Vector2I(1, 1) : new Vector2I(1, 0); // Lava / Stone
            default: return _random.Next(0, 100) < 70 ? new Vector2I(0, 0) : new Vector2I(2, 0); // Grass / Ground
        }
    }

    public Vector2I GetWallTileForBiome(int biomeType, Vector2I _)
    {
        switch (biomeType)
        {
            case 0: // Grassland - случайный выбор из вариантов стен
                {
                var grassWallTiles = new Vector2I[]
                {
                    new Vector2I(7, 2), new Vector2I(8, 2), new Vector2I(9, 2),
                    new Vector2I(10, 2), new Vector2I(0, 3), new Vector2I(1, 3),
                    new Vector2I(2, 3), new Vector2I(3, 3)
                };
                    return grassWallTiles[_random.Next(grassWallTiles.Length)];
                }
            // ОБНОВЛЕННЫЕ КООРДИНАТЫ для стен walls.png
            // Подбираем стабильные, нерандомные значения, чтобы стены точно отличались от пола
            case 1: return new Vector2I(0, 0); // Forest wall
            case 2: return new Vector2I(1, 0); // Desert wall
            case 3: return new Vector2I(0, 1); // Ice wall
            case 4: return new Vector2I(3, 1); // Techno wall
            case 5: return new Vector2I(4, 1); // Anomal wall
            case 6: return new Vector2I(1, 1); // Lava wall
            default: return new Vector2I(2, 0); // Fallback
        }
    }

    // Возвращает пару: (sourceId, atlasCoords) для стен — нужно, если один биом использует несколько TileSets
    public (int sourceId, Vector2I tile) GetWallTileForBiomeEx(int biomeType, Vector2I pos)
    {
        switch (biomeType)
        {
            case 0: // Grassland — матрица со смешанными Atlas ID
                {
                    // БАЗОВЫЕ СТЕНЫ (как у пола: 2 варианта, детерминированный шум и лёгкий джиттер)
                    var baseTiles = new (int src, Vector2I tile)[]
                    {
                        (0, new Vector2I(90, 10)),
                        (0, new Vector2I(58, 18))
                    };
                    var rareTiles = new (int src, Vector2I tile)[]
                    {
                        (0, new Vector2I(0, 0)),
                        (0, new Vector2I(46, 10)),
                        (0, new Vector2I(93, 1))
                    };

                    // Когерентный шум по крупной сетке, как для пола
                    int rx = pos.X / 4; int ry = pos.Y / 4;
                    int h = Hash2D(rx, ry, 733); // устойчивый хеш
                    int idx = Math.Abs(h) % baseTiles.Length;
                    // Небольшой джиттер для разрушения прямых границ
                    int j = Hash2D(pos.X, pos.Y, 2115);
                    if ((j % 100) < 15) idx = (idx + 1) % baseTiles.Length; // ~15%

                    // РЕДКИЕ ВАРИАЦИИ (как у пола — ~20%)
                    int r = Hash2D(pos.X, pos.Y, 2026) % 1000;
                    if (r < 200)
                    {
                        var rare = rareTiles[_random.Next(rareTiles.Length)];
                        return (rare.src, rare.tile);
                    }
                    var pick = baseTiles[idx];
                    return (pick.src, pick.tile);
                }
            case 1: // Forest — матрица, как у пола Forest (база + редкие с низкой плотностью)
                {
                    // Новая матрица для Forest
                    var baseTiles = new (int src, Vector2I tile)[]
                    {
                        (0, new Vector2I(0, 0)),
                        (0, new Vector2I(8, 0)),
                        (0, new Vector2I(52, 0))
                    };
                    var rareTiles = new (int src, Vector2I tile)[]
                    {
                        (0, new Vector2I(44, 1)),
                        (0, new Vector2I(43, 0)),
                        (0, new Vector2I(60, 0)),
                        (0, new Vector2I(68, 0))
                    };

                    int rx = pos.X / 4; int ry = pos.Y / 4;
                    int h = Hash2D(rx, ry, 1723);
                    int idx = Math.Abs(h) % baseTiles.Length;
                    // Forest: более мягкий джиттер 12%
                    int j = Hash2D(pos.X, pos.Y, 1912);
                    if ((j % 100) < 12) idx = (idx + 1) % baseTiles.Length;

                    // Редкие Forest — как у пола ~7%
                    int r = Hash2D(pos.X, pos.Y, 3026) % 1000;
                    if (r < 70)
                    {
                        var rare = rareTiles[_random.Next(rareTiles.Length)];
                        return (rare.src, rare.tile);
                    }
                    var pick = baseTiles[idx];
                    return (pick.src, pick.tile);
                }
            case 2: // Desert — базовый один тайл, остальные как редкие ~7%
                {
                    var baseTiles = new (int src, Vector2I tile)[]
                    {
                        (4, new Vector2I(3, 1))
                    };
                    var rareTiles = new (int src, Vector2I tile)[]
                    {
                        (4, new Vector2I(4, 1)),
                        (4, new Vector2I(5, 1)),
                        (4, new Vector2I(2, 5)),
                        (4, new Vector2I(4, 5)),
                        (4, new Vector2I(5, 5))
                    };
                    int rx = pos.X / 4; int ry = pos.Y / 4;
                    int h = Hash2D(rx, ry, 2711);
                    int idx = Math.Abs(h) % baseTiles.Length;
                    int r = Hash2D(pos.X, pos.Y, 4026) % 1000;
                    if (r < 70)
                    {
                        var rare = rareTiles[_random.Next(rareTiles.Length)];
                        return (rare.src, rare.tile);
                    }
                    var pick = baseTiles[idx];
                    return (pick.src, pick.tile);
                }
            case 3: return (4, new Vector2I(0, 1));
            case 4: return (4, new Vector2I(3, 1));
            case 5: return (4, new Vector2I(4, 1));
            case 6: return (4, new Vector2I(1, 1));
            default: return (4, new Vector2I(2, 0));
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

    public Vector2I GetBridgeTile(bool horizontal, int width)
    {
        // Для единообразия: общий базовый тайл пола
        return new Vector2I(3, 0);
    }
}


