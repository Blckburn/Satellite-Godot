using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class IsometricSorter : Node
{
    // Ссылка на тайловую карту
    [Export] public NodePath TileMapPath { get; set; }

    // Группы для сортировки
    [Export] public string PlayerGroup { get; set; } = "Player";
    [Export] public string DynamicObjectsGroup { get; set; } = "DynamicObjects";

    // Базовое смещение для Z-индексов
    [Export] public int BaseZOffset { get; set; } = 100; // Уменьшено с 1000 до 100

    // Режим отладки
    [Export] public bool DebugMode { get; set; } = false;

    // Интервал обновления отладочной информации (в кадрах)
    [Export] public int DebugUpdateInterval { get; set; } = 60; // Каждую секунду при 60 FPS

    // Ссылка на тайлмап
    private TileMap _tileMap;

    // Словари для хранения дочерних узлов тайлмап (стены, декорации и т.д.)
    private Dictionary<Vector2I, List<Node2D>> _wallNodes = new Dictionary<Vector2I, List<Node2D>>();
    private Dictionary<Vector2I, List<Node2D>> _floorNodes = new Dictionary<Vector2I, List<Node2D>>();

    // Кэширование размера тайла
    private Vector2I _tileSize;

    // Счетчик кадров для отладки
    private int _debugFrameCounter = 0;

    public override void _Ready()
    {
        // Находим TileMap
        _tileMap = FindTileMap();

        if (_tileMap == null)
        {
            Logger.Error("IsometricSorter: TileMap not found! Unable to perform Z-sorting.");
            return;
        }

        // Получаем размер тайла
        _tileSize = _tileMap.TileSet?.TileSize ?? new Vector2I(64, 32);

        if (DebugMode)
        {
            Logger.Debug($"IsometricSorter: TileMap found, tile size is {_tileSize}", true);
        }

        // Добавляем себя в группу для доступа из других скриптов
        AddToGroup("IsometricSorter");

        // Инициализируем сортировку
        InitializeIsometricSorting();
    }

    public override void _Process(double delta)
    {
        // Обновляем сортировку динамических объектов каждый кадр
        UpdateDynamicObjectsSorting();

        // Отладочная информация с определенным интервалом
        if (DebugMode && (_debugFrameCounter % DebugUpdateInterval == 0))
        {
            LogDebugInfo();
        }

        _debugFrameCounter++;
    }

    private void LogDebugInfo()
    {
        var players = GetTree().GetNodesInGroup(PlayerGroup);
        if (players.Count > 0 && players[0] is Node2D player)
        {
            Vector2I playerTilePos = WorldToTilePosition(player.GlobalPosition);
            int playerZIndex = player.ZIndex;

            Logger.Debug($"Player at {playerTilePos} has Z-index: {playerZIndex}", false);
        }
    }

    // Поиск TileMap в сцене
    private TileMap FindTileMap()
    {
        if (!string.IsNullOrEmpty(TileMapPath))
        {
            return GetNode<TileMap>(TileMapPath);
        }

        var tileMaps = GetTree().GetNodesInGroup("TileMap");
        if (tileMaps.Count > 0 && tileMaps[0] is TileMap tileMap)
        {
            return tileMap;
        }

        return null;
    }

    // Инициализация сортировки
    private void InitializeIsometricSorting()
    {
        // Очищаем существующие словари
        _wallNodes.Clear();
        _floorNodes.Clear();

        // Преобразуем тайлы в отдельные узлы для индивидуальной сортировки
        ConvertTilesToNodes();

        // Устанавливаем начальные Z-индексы для всех объектов
        UpdateAllObjectsSorting();

        if (DebugMode)
        {
            Logger.Debug($"IsometricSorter: Initialized Z-sorting with {_wallNodes.Count} wall positions and {_floorNodes.Count} floor positions", true);
        }
    }

    // Преобразование тайлов TileMap в отдельные узлы Node2D для индивидуальной сортировки
    private void ConvertTilesToNodes()
    {
        if (_tileMap == null) return;

        // Создаем контейнеры для разных типов узлов
        Node wallsContainer = new Node();
        wallsContainer.Name = "WallsContainer";
        Node floorsContainer = new Node();
        floorsContainer.Name = "FloorsContainer";

        AddChild(wallsContainer);
        AddChild(floorsContainer);

        // Перебираем все используемые ячейки на всех слоях
        for (int layerIndex = 0; layerIndex < _tileMap.GetLayersCount(); layerIndex++)
        {
            var usedCells = _tileMap.GetUsedCells(layerIndex);

            foreach (var cell in usedCells)
            {
                // Получаем данные о тайле
                int sourceId = _tileMap.GetCellSourceId(layerIndex, cell);
                Vector2I atlasCoords = _tileMap.GetCellAtlasCoords(layerIndex, cell);

                if (sourceId < 0) continue; // Пропускаем пустые ячейки

                // Определяем, на каком слое находится тайл
                bool isWall = (layerIndex == 1); // Level1 = Стены/декорации
                bool isFloor = (layerIndex == 0); // Level0 = Пол

                // Создаем спрайт для представления тайла
                Sprite2D tileSprite = new Sprite2D();
                tileSprite.Name = $"Tile_{layerIndex}_{cell.X}_{cell.Y}";

                // Получаем текстуру тайла
                var tileData = _tileMap.GetCellTileData(layerIndex, cell);

                if (tileData != null)
                {
                    // Получаем мировую позицию для этого тайла
                    // Используем MapToLocal и ToGlobal для Godot 4
                    Vector2 localPos = _tileMap.MapToLocal(cell);
                    Vector2 worldPos = _tileMap.ToGlobal(localPos);

                    tileSprite.Position = worldPos;

                    // Добавляем спрайт в соответствующий контейнер
                    if (isWall)
                    {
                        wallsContainer.AddChild(tileSprite);

                        // Добавляем в словарь для удобного доступа
                        if (!_wallNodes.ContainsKey(cell))
                        {
                            _wallNodes[cell] = new List<Node2D>();
                        }
                        _wallNodes[cell].Add(tileSprite);
                    }
                    else if (isFloor)
                    {
                        floorsContainer.AddChild(tileSprite);

                        // Добавляем в словарь для удобного доступа
                        if (!_floorNodes.ContainsKey(cell))
                        {
                            _floorNodes[cell] = new List<Node2D>();
                        }
                        _floorNodes[cell].Add(tileSprite);
                    }
                }
            }
        }
    }

    // Обновление сортировки всех объектов
    private void UpdateAllObjectsSorting()
    {
        // Обновляем Z-индексы для стен
        foreach (var pair in _wallNodes)
        {
            Vector2I cellPos = pair.Key;
            List<Node2D> nodes = pair.Value;

            // Рассчитываем Z-индекс на основе позиции
            int zIndex = CalculateIsometricZIndex(cellPos);

            // Применяем Z-индекс ко всем узлам в этой позиции
            foreach (var node in nodes)
            {
                node.ZIndex = zIndex;
            }
        }

        // Обновляем Z-индексы для пола (должны быть ниже всех других объектов)
        foreach (var pair in _floorNodes)
        {
            Vector2I cellPos = pair.Key;
            List<Node2D> nodes = pair.Value;

            // Для пола Z-индекс должен быть самым низким, но тоже зависеть от позиции
            int zIndex = CalculateIsometricZIndex(cellPos) - BaseZOffset;

            // Применяем Z-индекс ко всем узлам в этой позиции
            foreach (var node in nodes)
            {
                node.ZIndex = zIndex;
            }
        }

        // Обновляем Z-индексы для динамических объектов
        UpdateDynamicObjectsSorting();
    }

    // Обновление сортировки только динамических объектов
    private void UpdateDynamicObjectsSorting()
    {
        // Обновляем Z-индексы игрока
        var players = GetTree().GetNodesInGroup(PlayerGroup);
        foreach (var playerNode in players)
        {
            if (playerNode is Node2D player)
            {
                // Получаем тайловую позицию игрока
                Vector2I playerTilePos = WorldToTilePosition(player.GlobalPosition);

                // Рассчитываем Z-индекс для этой позиции
                int zIndex = CalculateIsometricZIndex(playerTilePos);

                // Применяем Z-индекс
                player.ZIndex = zIndex;
            }
        }

        // Обновляем Z-индексы других динамических объектов
        var dynamicObjects = GetTree().GetNodesInGroup(DynamicObjectsGroup);
        foreach (var obj in dynamicObjects)
        {
            // Пропускаем игрока, т.к. он уже обработан
            if (obj is Node2D node2D && !players.Contains(obj))
            {
                // Получаем тайловую позицию объекта
                Vector2I tilePos = WorldToTilePosition(node2D.GlobalPosition);

                // Рассчитываем Z-индекс для этой позиции
                int zIndex = CalculateIsometricZIndex(tilePos);

                // Применяем Z-индекс
                node2D.ZIndex = zIndex;
            }
        }
    }

    // Расчет изометрического Z-индекса на основе тайловой позиции
    private int CalculateIsometricZIndex(Vector2I tilePos)
    {
        // В изометрической проекции Z-индекс определяется суммой координат
        // (X + Y = глубина в сцене)
        // Тайлы с большей суммой находятся "глубже" и должны иметь меньший Z-индекс

        // Нормализуем координаты, чтобы избежать переполнения
        // Используем только относительную глубину, а не абсолютные координаты

        // Ограничиваем диапазон для предотвращения переполнения
        int sum = Math.Min(tilePos.X + tilePos.Y, 1000);

        // Обратная зависимость от суммы, чтобы объекты "глубже" рисовались под объектами "ближе"
        return BaseZOffset - sum;
    }

    // Преобразование мировых координат в тайловые
    private Vector2I WorldToTilePosition(Vector2 worldPos)
    {
        if (_tileMap != null)
        {
            // Используем встроенный метод преобразования, если TileMap доступен
            // Преобразуем глобальные координаты в локальные
            Vector2 localPos = _tileMap.ToLocal(worldPos);
            // Затем локальные в тайловые
            return _tileMap.LocalToMap(localPos);
        }

        // Если TileMap недоступен, используем приблизительное преобразование
        // Формула для изометрической проекции 2:1
        float isoX = worldPos.X / (_tileSize.X / 2.0f);
        float isoY = worldPos.Y / (_tileSize.Y / 2.0f);

        int tileX = (int)Math.Round((isoY + isoX) / 2.0f);
        int tileY = (int)Math.Round((isoY - isoX) / 2.0f);

        return new Vector2I(tileX, tileY);
    }

    // Метод для получения Z-индекса в конкретной тайловой позиции
    public int GetZIndexAtPosition(Vector2I tilePos)
    {
        return CalculateIsometricZIndex(tilePos);
    }

    // Новый метод: Получение информации о стенах в радиусе от заданной позиции
    public List<WallInfo> GetWallsInRadius(Vector2 worldPos, float radius)
    {
        List<WallInfo> result = new List<WallInfo>();

        if (_tileMap == null || _wallNodes.Count == 0)
            return result;

        // Получаем тайловую позицию центра
        Vector2I centerTilePos = WorldToTilePosition(worldPos);

        // Примерно переводим радиус из мировых координат в количество тайлов
        // Это приблизительно, так как размер тайла в мировых единицах может варьироваться
        int tileRadius = (int)Math.Ceiling(radius / (_tileSize.X / 2.0f));

        // Находим все стены в указанном радиусе (квадратная область)
        for (int dx = -tileRadius; dx <= tileRadius; dx++)
        {
            for (int dy = -tileRadius; dy <= tileRadius; dy++)
            {
                Vector2I checkPos = new Vector2I(centerTilePos.X + dx, centerTilePos.Y + dy);

                // Проверяем, есть ли стена на этой позиции
                if (_wallNodes.ContainsKey(checkPos))
                {
                    // Получаем первый узел стены (обычно он только один)
                    var wallNode = _wallNodes[checkPos].FirstOrDefault();
                    if (wallNode != null)
                    {
                        // Проверяем, находится ли стена в заданном радиусе (в мировых координатах)
                        float distance = worldPos.DistanceTo(wallNode.GlobalPosition);
                        if (distance <= radius)
                        {
                            // Добавляем информацию о стене
                            result.Add(new WallInfo
                            {
                                Position = checkPos,
                                WorldPosition = wallNode.GlobalPosition,
                                ZIndex = wallNode.ZIndex,
                                Distance = distance
                            });
                        }
                    }
                }
            }
        }

        // Сортируем по расстоянию от ближайшей к самой дальней
        result.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        return result;
    }

    // Структура для хранения информации о стене
    public struct WallInfo
    {
        public Vector2I Position;       // Тайловая позиция
        public Vector2 WorldPosition;   // Мировая позиция
        public int ZIndex;              // Текущий Z-индекс
        public float Distance;          // Расстояние от заданной точки

        public override string ToString()
        {
            return $"Wall at {Position}, Z-index: {ZIndex}, Distance: {Distance:F2}";
        }
    }
}