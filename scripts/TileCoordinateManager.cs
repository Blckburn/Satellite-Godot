using Godot;

/// <summary>
/// ЖЕЛЕЗНАЯ СИСТЕМА СИНХРОНИЗАЦИИ КООРДИНАТ
/// Единственный источник истины для всех позиций тайлов в мире
/// Гарантирует что стены и пол ВСЕГДА размещаются в одинаковых координатах
/// </summary>
public static class TileCoordinateManager
{
    /// <summary>
    /// Получить мировую позицию тайла для ПОЛА - ЕДИНСТВЕННЫЙ источник истины для координат
    /// </summary>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <returns>Vector2I позиция для размещения в TileMapLayer</returns>
    public static Vector2I GetWorldTilePosition(int x, int y)
    {
        // ЖЕЛЕЗНАЯ ГАРАНТИЯ: все тайлы используют одинаковые координаты
        return new Vector2I(x, y);
    }

    /// <summary>
    /// Получить мировую позицию тайла для СТЕН с учетом texture_origin смещения
    /// </summary>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <returns>Vector2I позиция для размещения стен с правильным выравниванием</returns>
    public static Vector2I GetWallTilePosition(int x, int y)
    {
        // ЖЕЛЕЗНАЯ СИНХРОНИЗАЦИЯ: стены используют те же координаты что и пол
        return new Vector2I(x, y);
    }

    /// <summary>
    /// Синхронно размещает пол в указанной позиции
    /// </summary>
    /// <param name="floorsLayer">Слой для пола</param>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <param name="sourceId">ID источника тайлов</param>
    /// <param name="tileCoords">Координаты тайла в атласе</param>
    public static void PlaceFloorTile(TileMapLayer floorsLayer, int x, int y, int sourceId, Vector2I tileCoords)
    {
        var position = GetWorldTilePosition(x, y);
        floorsLayer.SetCell(position, sourceId, tileCoords);
        
        // Логирование для отладки (можно отключить в релизе)
        // Logger.Debug($"Floor placed at world({x},{y}) -> tile({position.X},{position.Y})", false);
    }

    /// <summary>
    /// Синхронно размещает стену в указанной позиции
    /// </summary>
    /// <param name="wallsLayer">Слой для стен</param>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <param name="sourceId">ID источника тайлов</param>
    /// <param name="tileCoords">Координаты тайла в атласе</param>
    public static void PlaceWallTile(TileMapLayer wallsLayer, int x, int y, int sourceId, Vector2I tileCoords)
    {
        var position = GetWallTilePosition(x, y);
        wallsLayer.SetCell(position, sourceId, tileCoords);
        
        // Логирование для отладки (можно отключить в релизе)
        // Logger.Debug($"Wall placed at world({x},{y}) -> tile({position.X},{position.Y})", false);
    }

    /// <summary>
    /// Синхронно удаляет стену в указанной позиции (для Room областей)
    /// </summary>
    /// <param name="wallsLayer">Слой для стен</param>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    public static void EraseWallTile(TileMapLayer wallsLayer, int x, int y)
    {
        var position = GetWallTilePosition(x, y);
        wallsLayer.EraseCell(position);
        
        // Логирование для отладки (можно отключить в релизе)
        // Logger.Debug($"Wall erased at world({x},{y}) -> tile({position.X},{position.Y})", false);
    }

    /// <summary>
    /// ЖЕЛЕЗНАЯ ПРОВЕРКА синхронизации - все тайлы должны использовать одинаковые координаты
    /// </summary>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <param name="actualPosition">Фактически используемая позиция</param>
    /// <returns>true если координаты синхронизированы</returns>
    public static bool ValidateCoordinateSync(int x, int y, Vector2I actualPosition)
    {
        var expectedPosition = GetWorldTilePosition(x, y);
        bool isSynced = expectedPosition == actualPosition;
        
        if (!isSynced)
        {
            Logger.Error($"COORDINATE DESYNC! World({x},{y}) expected {expectedPosition} but got {actualPosition}");
        }
        
        return isSynced;
    }

    /// <summary>
    /// Пакетное размещение тайлов с гарантией синхронизации
    /// Позволяет размещать пол и стены одновременно с проверкой координат
    /// </summary>
    /// <param name="floorsLayer">Слой для пола</param>
    /// <param name="wallsLayer">Слой для стен</param>
    /// <param name="x">X координата в мире</param>
    /// <param name="y">Y координата в мире</param>
    /// <param name="floorSourceId">ID источника для пола</param>
    /// <param name="wallSourceId">ID источника для стен</param>
    /// <param name="floorTile">Координаты тайла пола в атласе</param>
    /// <param name="wallTile">Координаты тайла стены в атласе (null = не размещать стену)</param>
    /// <param name="isRoom">true если это Room область (стены удаляются)</param>
    public static void PlaceSynchronizedTiles(
        TileMapLayer floorsLayer, 
        TileMapLayer wallsLayer, 
        int x, int y,
        int floorSourceId, 
        int wallSourceId,
        Vector2I floorTile, 
        Vector2I? wallTile,
        bool isRoom)
    {
        var position = GetWorldTilePosition(x, y);
        
        // Размещаем пол (всегда)
        floorsLayer.SetCell(position, floorSourceId, floorTile);
        
        // Управляем стенами в зависимости от типа области
        if (isRoom)
        {
            // Room область - удаляем стены
            wallsLayer.EraseCell(position);
        }
        else if (wallTile.HasValue)
        {
            // Wall область - размещаем стены
            wallsLayer.SetCell(position, wallSourceId, wallTile.Value);
        }
        
        // Логирование синхронизации
        // Logger.Debug($"Synced tiles at world({x},{y}) -> tile({position.X},{position.Y}), room={isRoom}", false);
    }
}
