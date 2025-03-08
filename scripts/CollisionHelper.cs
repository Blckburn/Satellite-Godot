using Godot;
using System;

public static class CollisionHelper
{
    // Настраивает коллизии для тайла на основе его проходимости
    public static void SetupTileCollision(TileMap tileMap, int x, int y, bool isWalkable)
    {
        // Получаем позицию тайла
        Vector2I tilePos = new Vector2I(x, y);

        // Получаем данные тайла
        TileData tileData = tileMap.GetCellTileData(0, tilePos);

        if (tileData == null)
            return;

        // Устанавливаем пользовательские данные
        tileData.SetCustomData("is_walkable", isWalkable);

        // Метод для обновления физических свойств тайла
        // Примечание: Этот метод может потребовать изменений в зависимости 
        // от конкретной структуры вашего проекта
        UpdateTilePhysics(tileMap, x, y, isWalkable);
    }

    // Обновляет физические свойства тайла
    private static void UpdateTilePhysics(TileMap tileMap, int x, int y, bool isWalkable)
    {
        // Здесь может быть дополнительная логика для настройки коллизий
        // в зависимости от типа тайла, биома и т.д.

        // Пример: Создание или удаление статического тела для непроходимых тайлов
        // Это можно реализовать, если тайлы не имеют встроенной коллизии

        // GD.Print($"Updating tile physics at ({x}, {y}) - Walkable: {isWalkable}");
    }

    // Проверяет, можно ли пройти через точку в мировых координатах
    public static bool IsPointWalkable(IsometricMap map, Vector2 worldPosition)
    {
        // Преобразуем мировые координаты в координаты тайла
        Vector2I tilePos = map.WorldToMap(worldPosition);

        // Проверяем проходимость тайла
        return map.IsTileWalkable(tilePos.X, tilePos.Y);
    }

    // Получает вектор скольжения для движения вдоль стен
    public static Vector2 GetSlideVector(IsometricMap map, Vector2 startPos, Vector2 direction, float distance)
    {
        Vector2 targetPos = startPos + direction * distance;

        // Проверяем, доступна ли целевая позиция
        if (IsPointWalkable(map, targetPos))
            return direction;

        // Пробуем двигаться только по оси X
        Vector2 horizontalDir = new Vector2(direction.X, 0).Normalized();
        Vector2 horizontalTarget = startPos + horizontalDir * distance;

        // Пробуем двигаться только по оси Y
        Vector2 verticalDir = new Vector2(0, direction.Y).Normalized();
        Vector2 verticalTarget = startPos + verticalDir * distance;

        // Выбираем направление, которое позволяет двигаться
        if (IsPointWalkable(map, horizontalTarget))
            return horizontalDir;

        if (IsPointWalkable(map, verticalTarget))
            return verticalDir;

        // Если оба направления блокированы, возвращаем нулевой вектор
        return Vector2.Zero;
    }
}