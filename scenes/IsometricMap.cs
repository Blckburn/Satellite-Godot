using Godot;
using System;

public partial class IsometricMap : TileMap
{
    [Export] public int BiomeType { get; set; } = 0;

    public override void _Ready()
    {
        base._Ready();

        // Инициализация карты
        GD.Print("IsometricMap initialized with biome type: " + BiomeType);
    }

    // Метод для установки тайла на карте
    public void SetIsometricTile(int x, int y, Vector2I tilePos, int layer = 0)
    {
        SetCell((int)Layers.Level0, new Vector2I(x, y), 0, tilePos);
    }

    // Метод для проверки, можно ли пройти через тайл
    public bool IsTileWalkable(int x, int y)
    {
        // Получаем данные тайла
        TileData tileData = GetCellTileData((int)Layers.Level0, new Vector2I(x, y));

        if (tileData == null)
            return false;

        // Проверка пользовательских данных тайла для определения проходимости
        var customData = tileData.GetCustomData("is_walkable");

        // Правильная проверка для типа Variant
        if (customData.VariantType != Variant.Type.Nil)
        {
            // Теперь мы можем проверить тип и получить значение
            if (customData.VariantType == Variant.Type.Bool)
            {
                return (bool)customData;
            }
        }

        // По умолчанию тайлы проходимы
        return true;
    }

    // Метод для получения координат тайла из мировых координат
    public Vector2I WorldToMap(Vector2 worldPos)
    {
        return LocalToMap(ToLocal(worldPos));
    }

    // Метод для получения мировых координат из координат тайла
    public Vector2 MapToWorld(Vector2I mapPos)
    {
        return ToGlobal(MapToLocal(mapPos));
    }
}