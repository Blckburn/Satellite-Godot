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
            case 1:
                // Grass: use high-quality supersampled Wang variants packed at indices 12..23 in atlas row 0
                return new Vector2I(12 + _random.Next(0, 12), 0);
            case 2: return new Vector2I(4, 0); // Sand
            case 3: return new Vector2I(3, 0); // Snow
            case 4: return new Vector2I(1, 0); // Stone
            case 5: return new Vector2I(2, 0); // Ground
            case 6: return new Vector2I(2, 0); // Ground
            default: return new Vector2I(2, 1); // ForestFloor (по умолчанию)
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
        bool useVariation = _useVariedWalls() && _random.Next(0, 100) < 30;

        switch (biomeType)
        {
            case 1: return useVariation && _random.Next(0, 100) < 50 ? new Vector2I(3, 0) : new Vector2I(2, 1); // Snow / ForestFloor
            case 2: return useVariation && _random.Next(0, 100) < 40 ? new Vector2I(2, 0) : new Vector2I(1, 0); // Ground / Stone
            case 3: return useVariation && _random.Next(0, 100) < 40 ? new Vector2I(1, 0) : new Vector2I(0, 1); // Stone / Ice
            case 4: return useVariation && _random.Next(0, 100) < 40 ? new Vector2I(4, 1) : new Vector2I(3, 1); // Anomal / Techno
            case 5:
                if (useVariation)
                {
                    int v = _random.Next(0, 100);
                    if (v < 40) return new Vector2I(1, 1); // Lava
                    return new Vector2I(4, 1); // Anomal
                }
                return new Vector2I(4, 1);
            case 6: return useVariation && _random.Next(0, 100) < 40 ? new Vector2I(1, 0) : new Vector2I(1, 1); // Stone / Lava
            default: return useVariation && _random.Next(0, 100) < 40 ? new Vector2I(2, 0) : new Vector2I(0, 0); // Ground / Grass
        }
    }

    public Vector2I GetBridgeTile(bool horizontal, int width)
    {
        // Используем сгенерированные в процедурном атласе индексы:
        // 6 — горизонтальный мост, 7 — вертикальный мост
        return new Vector2I(horizontal ? 6 : 7, 0);
    }
}


