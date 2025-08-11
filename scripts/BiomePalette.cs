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
            // ВОЗВРАТ К ИСХОДНЫМ КООРДИНАТАМ floors.png
            // 0: Grass, 1: Stone, 2: Ground, 3: Snow, 4: Sand, 5: Water
            // 0-й ряд; 1-й ряд: 0: Ice, 1: Lava, 2: ForestFloor, 3: Techno, 4: Anomal, 5: Empty
            case 1: return new Vector2I(2, 1); // Forest → ForestFloor
            case 2: return new Vector2I(4, 0); // Desert → Sand
            case 3: return new Vector2I(3, 0); // Ice → Snow (пол)
            case 4: return new Vector2I(3, 1); // Techno
            case 5: return new Vector2I(4, 1); // Anomal
            case 6: return new Vector2I(1, 1); // Lava Springs
            default: return new Vector2I(0, 0); // Grassland → Grass
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
        // Без специальных мостов: твёрдый каменный пол
        return new Vector2I(1, 0); // Stone
    }
}


