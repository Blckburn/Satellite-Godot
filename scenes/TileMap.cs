using Godot;
using System;

public partial class TileMap : Godot.TileMap
{
    // Перечисление для слоев плитки
    public enum Layers
    {
        Level0 = 0,
        Level1 = 1,
        Level2 = 2,
    }

    // Константы для позиций атласа плиток
    public static readonly Vector2I Grass = new Vector2I(0, 0);
    public static readonly Vector2I Stone = new Vector2I(1, 0);
    public static readonly Vector2I Ground = new Vector2I(2, 0);
    public static readonly Vector2I Snow = new Vector2I(3, 0);
    public static readonly Vector2I Sand = new Vector2I(4, 0);
    public static readonly Vector2I GrassTopHalf = new Vector2I(5, 0);
    public static readonly Vector2I GrassBotHalf = new Vector2I(6, 0);

    public override void _Ready()
    {
        // Базовая инициализация
        GD.Print("TileMap initialized");
    }
}