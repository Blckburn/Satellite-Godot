using Godot;
using System;

/// <summary>
/// Отображает изометрическую сетку для удобства размещения объектов на станции.
/// </summary>
public partial class StationGrid : Node2D
{
    // Размеры тайла
    [Export] public float TileWidth { get; set; } = 64f;
    [Export] public float TileHeight { get; set; } = 32f;

    // Размеры сетки в тайлах
    [Export] public int GridWidth { get; set; } = 20;
    [Export] public int GridHeight { get; set; } = 20;

    // Настройки отображения
    [Export] public Color GridColor { get; set; } = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [Export] public Color OriginColor { get; set; } = new Color(1.0f, 0.2f, 0.2f, 0.8f);
    [Export] public bool ShowGrid { get; set; } = true;
    [Export] public bool HighlightOrigin { get; set; } = true;
    [Export] public float LineWidth { get; set; } = 1.0f;

    // Смещение сетки
    [Export] public Vector2 GridOffset { get; set; } = Vector2.Zero;

    public override void _Ready()
    {
        // Обновляем сетку при изменении видимого размера
        GetViewport().SizeChanged += () => QueueRedraw();
    }

    public override void _Draw()
    {
        if (!ShowGrid)
            return;

        // Рисуем изометрическую сетку
        DrawIsometricGrid();
    }

    private void DrawIsometricGrid()
    {
        // Половина высоты и ширины тайла для расчетов
        float halfWidth = TileWidth / 2;
        float halfHeight = TileHeight / 2;

        // 1. Рисуем горизонтальные линии сетки (с северо-запада на юго-восток)
        for (int y = 0; y <= GridHeight; y++)
        {
            // Начальная и конечная точки линии
            Vector2 startPoint = IsometricToScreen(0, y);
            Vector2 endPoint = IsometricToScreen(GridWidth, y);

            // Рисуем линию
            DrawLine(startPoint + GridOffset, endPoint + GridOffset, GridColor, LineWidth);
        }

        // 2. Рисуем вертикальные линии сетки (с северо-востока на юго-запад)
        for (int x = 0; x <= GridWidth; x++)
        {
            // Начальная и конечная точки линии
            Vector2 startPoint = IsometricToScreen(x, 0);
            Vector2 endPoint = IsometricToScreen(x, GridHeight);

            // Рисуем линию
            DrawLine(startPoint + GridOffset, endPoint + GridOffset, GridColor, LineWidth);
        }

        // 3. Дополнительно выделяем начало координат (центр сетки)
        if (HighlightOrigin)
        {
            int centerX = GridWidth / 2;
            int centerY = GridHeight / 2;

            Vector2 originPos = IsometricToScreen(centerX, centerY);

            // Рисуем маркер начала координат (ромб)
            Vector2[] originPoints = new Vector2[4];
            originPoints[0] = originPos + new Vector2(0, -halfHeight) + GridOffset; // Верх
            originPoints[1] = originPos + new Vector2(halfWidth, 0) + GridOffset;   // Право
            originPoints[2] = originPos + new Vector2(0, halfHeight) + GridOffset;  // Низ
            originPoints[3] = originPos + new Vector2(-halfWidth, 0) + GridOffset;  // Лево

            DrawColoredPolygon(originPoints, OriginColor);
        }
    }

    /// <summary>
    /// Преобразует изометрические координаты в экранные
    /// </summary>
    private Vector2 IsometricToScreen(float isoX, float isoY)
    {
        // Формула преобразования изо координат в экранные для соотношения 2:1
        float screenX = (isoX - isoY) * (TileWidth / 2);
        float screenY = (isoX + isoY) * (TileHeight / 2);

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Преобразует экранные координаты в изометрические
    /// </summary>
    private Vector2 ScreenToIsometric(float screenX, float screenY)
    {
        // Обратная формула преобразования
        float isoX = (screenX / (TileWidth / 2) + screenY / (TileHeight / 2)) / 2;
        float isoY = (screenY / (TileHeight / 2) - screenX / (TileWidth / 2)) / 2;

        return new Vector2(isoX, isoY);
    }

    /// <summary>
    /// Отображает координаты тайла под курсором (полезно для отладки)
    /// </summary>
    public Vector2I GetTileCoordUnderCursor()
    {
        Vector2 mousePos = GetGlobalMousePosition() - GridOffset;
        Vector2 isoCoord = ScreenToIsometric(mousePos.X, mousePos.Y);

        return new Vector2I(Mathf.FloorToInt(isoCoord.X), Mathf.FloorToInt(isoCoord.Y));
    }

    /// <summary>
    /// Получает мировую позицию для указанных координат тайла
    /// </summary>
    public Vector2 GetWorldPositionForTile(int tileX, int tileY)
    {
        return IsometricToScreen(tileX, tileY) + GridOffset;
    }

    /// <summary>
    /// Отлаживает сетку, выводя информацию в консоль
    /// </summary>
    public void DebugGrid()
    {
        Logger.Debug($"--- Grid Debug ---", true);
        Logger.Debug($"Tile Size: {TileWidth}x{TileHeight}", true);
        Logger.Debug($"Grid Dimensions: {GridWidth}x{GridHeight} tiles", true);
        Logger.Debug($"Grid Offset: {GridOffset}", true);
        Logger.Debug($"Center Tile: ({GridWidth / 2}, {GridHeight / 2})", true);
        Logger.Debug($"Center Position: {GetWorldPositionForTile(GridWidth / 2, GridHeight / 2)}", true);
        Logger.Debug($"------------------", true);
    }
}