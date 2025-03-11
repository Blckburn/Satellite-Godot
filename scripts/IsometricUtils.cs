using Godot;
using System;

/// <summary>
/// Вспомогательный класс для работы с изометрической проекцией
/// </summary>
public static class IsometricUtils
{
    /// <summary>
    /// Преобразует мировые координаты в тайловые координаты для изометрической проекции
    /// </summary>
    /// <param name="worldPos">Позиция в мировых координатах</param>
    /// <param name="tileSize">Размер тайла (обычно 64x32 для изометрии)</param>
    /// <returns>Координаты тайла</returns>
    public static Vector2I WorldToTile(Vector2 worldPos, Vector2I tileSize)
    {
        // Преобразование из мировых координат в тайловые для изометрии 2:1
        float isoX = worldPos.X / (tileSize.X / 2.0f);
        float isoY = worldPos.Y / (tileSize.Y / 2.0f);

        // Обратное преобразование из изометрических координат в тайловые
        int tileX = (int)Math.Round((isoY + isoX) / 2.0f);
        int tileY = (int)Math.Round((isoY - isoX) / 2.0f);

        return new Vector2I(tileX, tileY);
    }

    /// <summary>
    /// Преобразует тайловые координаты в мировые координаты для изометрической проекции
    /// </summary>
    /// <param name="tilePos">Позиция в тайловых координатах</param>
    /// <param name="tileSize">Размер тайла (обычно 64x32 для изометрии)</param>
    /// <returns>Мировые координаты</returns>
    public static Vector2 TileToWorld(Vector2I tilePos, Vector2I tileSize)
    {
        // Преобразование из тайловых координат в мировые для изометрии 2:1
        float x = (tilePos.X - tilePos.Y) * (tileSize.X / 2.0f);
        float y = (tilePos.X + tilePos.Y) * (tileSize.Y / 2.0f);

        return new Vector2(x, y);
    }

    /// <summary>
    /// Определяет Z-индекс для объекта в изометрической проекции
    /// </summary>
    /// <param name="tilePos">Позиция объекта в тайловых координатах</param>
    /// <param name="baseZ">Базовый Z-индекс объекта</param>
    /// <returns>Рассчитанный Z-индекс</returns>
    public static int CalculateZIndex(Vector2I tilePos, int baseZ)
    {
        // В изометрии глубина определяется суммой X и Y координат
        // Чем больше сумма, тем "глубже" объект в сцене
        int depth = tilePos.X + tilePos.Y;

        // Возвращаем Z-индекс, уменьшенный на глубину
        // Это обеспечивает, что объекты "глубже" (больше X+Y) будут отображаться ниже
        return baseZ - depth;
    }

    /// <summary>
    /// Проверяет, находится ли один тайл "перед" другим в изометрической проекции
    /// </summary>
    /// <param name="tileA">Первый тайл</param>
    /// <param name="tileB">Второй тайл</param>
    /// <returns>true, если tileA находится перед tileB (ниже на экране)</returns>
    public static bool IsTileInFront(Vector2I tileA, Vector2I tileB)
    {
        // В изометрии тайл находится "перед" другим, если сумма его координат больше
        return (tileA.X + tileA.Y) > (tileB.X + tileB.Y);
    }

    /// <summary>
    /// Получает список соседних тайлов в изометрической сетке
    /// </summary>
    /// <param name="centerTile">Центральный тайл</param>
    /// <param name="radius">Радиус проверки (1 = только соседние, 2 = соседние соседних и т.д.)</param>
    /// <returns>Массив координат соседних тайлов</returns>
    public static Vector2I[] GetNeighborTiles(Vector2I centerTile, int radius = 1)
    {
        // Для изометрии 4 базовых направления движения
        Vector2I[] directions = new Vector2I[]
        {
            new Vector2I(1, 0),  // Вправо
            new Vector2I(0, 1),  // Вниз
            new Vector2I(-1, 0), // Влево
            new Vector2I(0, -1)  // Вверх
        };

        // Для радиуса 1 возвращаем только 4 соседних тайла
        if (radius == 1)
        {
            Vector2I[] neighbors = new Vector2I[4];
            for (int i = 0; i < 4; i++)
            {
                neighbors[i] = centerTile + directions[i];
            }
            return neighbors;
        }

        // Для радиуса > 1 создаем больший массив соседей
        int tileCount = (2 * radius + 1) * (2 * radius + 1) - 1; // Все тайлы в квадрате минус центральный
        Vector2I[] result = new Vector2I[tileCount];

        int index = 0;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx == 0 && dy == 0) continue; // Пропускаем центральный тайл

                result[index] = centerTile + new Vector2I(dx, dy);
                index++;
            }
        }

        return result;
    }
}