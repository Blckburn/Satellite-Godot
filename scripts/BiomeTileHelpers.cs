using Godot;

public static class BiomeTileHelpers
{
    public static Vector2I GetBackgroundTileForBiome(BiomePalette palette, int biomeType, Vector2I grass, Vector2I forestFloor, Vector2I sand, Vector2I snow, Vector2I stone, Vector2I techno, Vector2I anomal, Vector2I lava, Vector2I ground)
    {
        switch (biomeType)
        {
            case 1: return forestFloor;
            case 2: return stone;
            case 3: return snow;
            case 4: return techno;
            case 5: return anomal;
            case 6: return lava;
            default: return grass;
        }
    }

    public static Vector2I GetFloorTileForBiome(BiomePalette palette, int biomeType, Vector2I grass, Vector2I forestFloor, Vector2I sand, Vector2I snow, Vector2I stone, Vector2I ground)
    {
        switch (biomeType)
        {
            case 1: return grass;
            case 2: return sand;
            case 3: return snow;
            case 4: return stone;
            case 5: return ground;
            case 6: return ground;
            default: return forestFloor;
        }
    }
}


