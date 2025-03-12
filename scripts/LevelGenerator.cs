using Godot;
using System;
using System.Collections.Generic;

public partial class LevelGenerator : Node
{
    // Сигнал о завершении генерации уровня с передачей точки спавна
    [Signal] public delegate void LevelGeneratedEventHandler(Vector2 spawnPosition);

    // Сигнал о завершении генерации всей мульти-секционной карты
    [Signal] public delegate void MultiSectionMapGeneratedEventHandler();

    // Ссылки на раздельные TileMap и контейнеры
    [Export] public Godot.TileMap FloorsTileMap { get; set; } // Для пола
    [Export] public Godot.TileMap WallsTileMap { get; set; }  // Для стен и декораций
    [Export] public Node2D YSortContainer { get; set; }       // Контейнер для игрока и сортировки

    // Ссылка на родительский узел, содержащий все тайлмапы
    [Export] public Node2D IsometricTileset { get; set; }

    // Сцена игрока для спавна
    [Export] public PackedScene PlayerScene { get; set; }

    // Индексы слоев - используем только слой 0 для всех TileMap
    // В Godot индексация слоев начинается с 0
    private const int MAP_LAYER = 0;  // Константа для всех операций с TileMap

    // Настройки размера карты
    [Export] public int MapWidth { get; set; } = 50;
    [Export] public int MapHeight { get; set; } = 50;

    // Настройки комнат
    [Export] public int MinRoomSize { get; set; } = 5;
    [Export] public int MaxRoomSize { get; set; } = 12;
    [Export] public int MaxRooms { get; set; } = 12;
    [Export] public int MinRoomDistance { get; set; } = 2;

    // Настройки коридоров
    [Export] public int CorridorWidth { get; set; } = 2;

    // Настройки биомов
    [Export] public int BiomeType { get; set; } = 0;
    [Export] public int MaxBiomeTypes { get; set; } = 7; // Увеличено до 7 для Lava Springs

    // ID источников тайлов в тайлсете
    [Export] public int WallsSourceID { get; set; } = 2;  // Source ID для тайлсета стен (walls.png)
    [Export] public int FloorsSourceID { get; set; } = 3;  // Source ID для тайлсета пола (floors.png)

    // Клавиша для генерации нового уровня
    [Export] public Key GenerationKey { get; set; } = Key.G;

    // Настройки декорирования
    [Export] public int DecorationDensity { get; set; } = 25;

    // Настройки спавна игрока
    [Export] public bool CreatePlayerOnGeneration { get; set; } = true;
    [Export] public string PlayerGroup { get; set; } = "Player";
    [Export] public bool TeleportExistingPlayer { get; set; } = true;

    // Настройки стен
    [Export] public bool UseVariedWalls { get; set; } = true;  // Включить вариативность стен

    // Настройки мульти-секционной карты
    [Export] public bool UseMultiSectionMap { get; set; } = false;  // Включить/выключить мульти-секционную карту
    [Export] public int GridWidth { get; set; } = 2;  // Количество секций по горизонтали
    [Export] public int GridHeight { get; set; } = 2;  // Количество секций по вертикали
    [Export] public int SectionSpacing { get; set; } = 10;  // Расстояние между секциями в тайлах

    // НОВОЕ: Настройка для соединения секций проходами
    [Export] public bool ConnectSections { get; set; } = true;  // Соединять ли секции проходами
    [Export] public int ConnectorWidth { get; set; } = 3;  // Ширина проходов между секциями

    // Клавиша для генерации мульти-секционной карты
    [Export] public Key MultiSectionGenerationKey { get; set; } = Key.M;

    // Псевдослучайный генератор
    private Random _random;

    // Список сгенерированных комнат
    private List<Rect2I> _rooms = new List<Rect2I>();

    // Тайлы для фонового заполнения
    private Vector2I _backgroundTile;

    // Координаты тайлов
    private static readonly Vector2I Grass = new Vector2I(0, 0);
    private static readonly Vector2I Stone = new Vector2I(1, 0);
    private static readonly Vector2I Ground = new Vector2I(2, 0);
    private static readonly Vector2I Snow = new Vector2I(3, 0);
    private static readonly Vector2I Sand = new Vector2I(4, 0);
    private static readonly Vector2I Water = new Vector2I(5, 0);
    private static readonly Vector2I Ice = new Vector2I(0, 1);
    private static readonly Vector2I Lava = new Vector2I(1, 1);
    private static readonly Vector2I ForestFloor = new Vector2I(2, 1);
    private static readonly Vector2I Techno = new Vector2I(3, 1);
    private static readonly Vector2I Anomal = new Vector2I(4, 1);
    private static readonly Vector2I Empty = new Vector2I(5, 1);

    // Типы тайлов для маски карты (публичное для доступа из MapSection)
    public enum TileType
    {
        None,
        Background,
        Room,
        Corridor,
        Wall,
        Decoration
    }

    // Класс для представления секции карты
    public class MapSection
    {
        public int BiomeType { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public Vector2 WorldOffset { get; set; }
        public List<Rect2I> Rooms { get; set; } = new List<Rect2I>();
        public TileType[,] SectionMask { get; set; }
        public Vector2? SpawnPosition { get; set; } = null;

        public MapSection(int biomeType, int gridX, int gridY, int mapWidth, int mapHeight)
        {
            BiomeType = biomeType;
            GridX = gridX;
            GridY = gridY;
            SectionMask = new TileType[mapWidth, mapHeight];
        }
    }

    // Список секций карты
    private List<MapSection> _mapSections = new List<MapSection>();

    // Текущая секция, с которой мы работаем
    private MapSection _currentSection;

    // Маска карты
    private TileType[,] _mapMask;

    // Текущая позиция спавна игрока
    private Vector2 _currentSpawnPosition = Vector2.Zero;

    // Ссылка на текущего игрока
    private Node2D _currentPlayer;

    // Флаг, указывающий, что уровень был сгенерирован
    private bool _levelGenerated = false;

    public override void _Ready()
    {
        // Инициализируем генератор случайных чисел
        _random = new Random();

        // Инициализируем маску карты
        _mapMask = new TileType[MapWidth, MapHeight];

        // Поиск необходимых сцен компонентов, если они не указаны
        FindRequiredNodes();

        Logger.Debug($"TileMap найдены: Floors: {FloorsTileMap.Name}, Walls: {WallsTileMap.Name}, YSort: {YSortContainer.Name}", true);

        // Генерируем начальный уровень с задержкой
        GetTree().CreateTimer(0.5).Timeout += () => GenerateRandomLevel();
    }

    // Обработка ввода для генерации нового уровня
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == GenerationKey)
            {
                GenerateRandomLevel();
            }
            else if (keyEvent.Keycode == MultiSectionGenerationKey)
            {
                GenerateMultiSectionMap();
            }
        }
    }

    // Метод для генерации мульти-секционной карты
    public void GenerateMultiSectionMap()
    {
        try
        {
            Logger.Debug("Starting generation of multi-section map", true);

            // Включаем мульти-секционный режим
            UseMultiSectionMap = true;

            // Очищаем предыдущие секции
            _mapSections.Clear();

            // Очищаем карту
            ClearAllLayers();

            // Создаем секции в сетке
            CreateMapSections();

            // НОВОЕ: Генерируем все секции карты
            GenerateAllSections();

            // НОВОЕ: Соединяем соседние секции проходами
            if (ConnectSections)
            {
                ConnectAdjacentSections();
            }

            // НОВОЕ: Выбираем секцию для спавна игрока
            SelectSpawnSection();

            Logger.Debug($"Multi-section map generated with {_mapSections.Count} sections", true);

            // Эмитим сигнал о завершении генерации мульти-секции
            EmitSignal("MultiSectionMapGenerated");

            // Спавним или перемещаем игрока
            if (CreatePlayerOnGeneration)
            {
                HandlePlayerSpawn();
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error generating multi-section map: {e.Message}\n{e.StackTrace}");
        }
    }

    // НОВОЕ: Метод для генерации всех секций
    private void GenerateAllSections()
    {
        Logger.Debug("Generating all map sections", true);

        // Проходим по всем секциям и генерируем для каждой уровень
        foreach (var section in _mapSections)
        {
            // Устанавливаем текущую секцию
            _currentSection = section;

            // Устанавливаем тип биома для генерации
            BiomeType = section.BiomeType;

            // Генерируем уровень для этой секции
            GenerateSectionLevel(section);

            Logger.Debug($"Generated section at ({section.GridX},{section.GridY}) with biome {GetBiomeName(section.BiomeType)}", false);
        }
    }

    // НОВОЕ: Метод для генерации уровня для конкретной секции
    private void GenerateSectionLevel(MapSection section)
    {
        try
        {
            // Сохраняем ссылку на текущую секцию
            _currentSection = section;

            // Сбрасываем список комнат секции
            section.Rooms.Clear();

            // Сбрасываем маску секции
            ResetSectionMask(section);

            // Устанавливаем фоновый тайл в зависимости от биома секции
            _backgroundTile = GetBackgroundTileForBiome(section.BiomeType);

            // Заполняем базовый пол секции
            FillSectionBaseFloor(section);

            // Генерируем комнаты в секции
            GenerateSectionRooms(section);

            // Соединяем комнаты в секции коридорами
            ConnectSectionRooms(section);

            // Заполняем фоновыми тайлами пустые области
            FillSectionWithBackgroundTiles(section);

            // Добавляем стены вокруг комнат и коридоров
            AddSectionWalls(section);

            // Добавляем декорации и препятствия
            AddSectionDecorationsAndObstacles(section);

            // Добавляем опасные зоны (вода/лава)
            AddSectionHazards(section);

            // Выбираем точку спавна для секции
            section.SpawnPosition = GetSectionSpawnPosition(section);

            Logger.Debug($"Section level generated at ({section.GridX}, {section.GridY}) with {section.Rooms.Count} rooms", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error generating section level: {e.Message}\n{e.StackTrace}");
        }
    }

    // НОВОЕ: Метод для соединения соседних секций проходами
    private void ConnectAdjacentSections()
    {
        Logger.Debug("Connecting adjacent sections", true);

        // Соединяем секции по горизонтали (слева направо)
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth - 1; x++)
            {
                // Находим секции для соединения
                MapSection leftSection = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                MapSection rightSection = _mapSections.Find(s => s.GridX == x + 1 && s.GridY == y);

                if (leftSection != null && rightSection != null)
                {
                    // Соединяем секции по оси X
                    ConnectSectionsHorizontally(leftSection, rightSection);
                }
            }
        }

        // Соединяем секции по вертикали (сверху вниз)
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight - 1; y++)
            {
                // Находим секции для соединения
                MapSection topSection = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                MapSection bottomSection = _mapSections.Find(s => s.GridX == x && s.GridY == y + 1);

                if (topSection != null && bottomSection != null)
                {
                    // Соединяем секции по оси Y
                    ConnectSectionsVertically(topSection, bottomSection);
                }
            }
        }
    }

    // НОВОЕ: Метод для соединения двух секций по горизонтали
    private void ConnectSectionsHorizontally(MapSection leftSection, MapSection rightSection)
    {
        try
        {
            // Определяем позицию прохода по вертикали (Y) - примерно посередине секции
            int passageY = MapHeight / 2 + _random.Next(-MapHeight / 4, MapHeight / 4);

            // Определяем тайлы пола для обеих секций
            Vector2I leftFloorTile = GetFloorTileForBiome(leftSection.BiomeType);
            Vector2I rightFloorTile = GetFloorTileForBiome(rightSection.BiomeType);

            // Создаем проход из левой секции в правую
            for (int offsetY = -ConnectorWidth / 2; offsetY <= ConnectorWidth / 2; offsetY++)
            {
                int y = passageY + offsetY;

                if (y < 0 || y >= MapHeight)
                    continue;

                // Устанавливаем проход из левой секции (правый край)
                for (int x = MapWidth - 5; x < MapWidth; x++)
                {
                    // Рассчитываем смещенные координаты
                    Vector2I leftPos = new Vector2I(x, y);
                    Vector2 leftWorldOffset = leftSection.WorldOffset;
                    Vector2I leftWorldPos = new Vector2I((int)leftWorldOffset.X + leftPos.X, (int)leftWorldOffset.Y + leftPos.Y);

                    // Размещаем тайл пола
                    FloorsTileMap.SetCell(MAP_LAYER, leftWorldPos, FloorsSourceID, leftFloorTile);

                    // Обновляем маску секции
                    if (x < MapWidth && y < MapHeight)
                    {
                        leftSection.SectionMask[x, y] = TileType.Corridor;
                    }
                }

                // Устанавливаем проход из правой секции (левый край)
                for (int x = 0; x < 5; x++)
                {
                    // Рассчитываем смещенные координаты
                    Vector2I rightPos = new Vector2I(x, y);
                    Vector2 rightWorldOffset = rightSection.WorldOffset;
                    Vector2I rightWorldPos = new Vector2I((int)rightWorldOffset.X + rightPos.X, (int)rightWorldOffset.Y + rightPos.Y);

                    // Размещаем тайл пола
                    FloorsTileMap.SetCell(MAP_LAYER, rightWorldPos, FloorsSourceID, rightFloorTile);

                    // Обновляем маску секции
                    if (x < MapWidth && y < MapHeight)
                    {
                        rightSection.SectionMask[x, y] = TileType.Corridor;
                    }
                }
            }

            Logger.Debug($"Connected sections horizontally at Y={passageY}", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting sections horizontally: {e.Message}");
        }
    }

    // НОВОЕ: Метод для соединения двух секций по вертикали
    private void ConnectSectionsVertically(MapSection topSection, MapSection bottomSection)
    {
        try
        {
            // Определяем позицию прохода по горизонтали (X) - примерно посередине секции
            int passageX = MapWidth / 2 + _random.Next(-MapWidth / 4, MapWidth / 4);

            // Определяем тайлы пола для обеих секций
            Vector2I topFloorTile = GetFloorTileForBiome(topSection.BiomeType);
            Vector2I bottomFloorTile = GetFloorTileForBiome(bottomSection.BiomeType);

            // Создаем проход из верхней секции в нижнюю
            for (int offsetX = -ConnectorWidth / 2; offsetX <= ConnectorWidth / 2; offsetX++)
            {
                int x = passageX + offsetX;

                if (x < 0 || x >= MapWidth)
                    continue;

                // Устанавливаем проход из верхней секции (нижний край)
                for (int y = MapHeight - 5; y < MapHeight; y++)
                {
                    // Рассчитываем смещенные координаты
                    Vector2I topPos = new Vector2I(x, y);
                    Vector2 topWorldOffset = topSection.WorldOffset;
                    Vector2I topWorldPos = new Vector2I((int)topWorldOffset.X + topPos.X, (int)topWorldOffset.Y + topPos.Y);

                    // Размещаем тайл пола
                    FloorsTileMap.SetCell(MAP_LAYER, topWorldPos, FloorsSourceID, topFloorTile);

                    // Обновляем маску секции
                    if (x < MapWidth && y < MapHeight)
                    {
                        topSection.SectionMask[x, y] = TileType.Corridor;
                    }
                }

                // Устанавливаем проход из нижней секции (верхний край)
                for (int y = 0; y < 5; y++)
                {
                    // Рассчитываем смещенные координаты
                    Vector2I bottomPos = new Vector2I(x, y);
                    Vector2 bottomWorldOffset = bottomSection.WorldOffset;
                    Vector2I bottomWorldPos = new Vector2I((int)bottomWorldOffset.X + bottomPos.X, (int)bottomWorldOffset.Y + bottomPos.Y);

                    // Размещаем тайл пола
                    FloorsTileMap.SetCell(MAP_LAYER, bottomWorldPos, FloorsSourceID, bottomFloorTile);

                    // Обновляем маску секции
                    if (x < MapWidth && y < MapHeight)
                    {
                        bottomSection.SectionMask[x, y] = TileType.Corridor;
                    }
                }
            }

            Logger.Debug($"Connected sections vertically at X={passageX}", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting sections vertically: {e.Message}");
        }
    }

    // НОВОЕ: Метод для выбора секции для спавна игрока
    private void SelectSpawnSection()
    {
        if (_mapSections.Count == 0)
        {
            Logger.Error("No sections available for player spawn!");
            return;
        }

        // Выбираем случайную секцию для спавна игрока
        int sectionIndex = _random.Next(0, _mapSections.Count);
        MapSection spawnSection = _mapSections[sectionIndex];

        // Проверяем, есть ли у секции точка спавна
        if (spawnSection.SpawnPosition.HasValue)
        {
            // Рассчитываем мировые координаты точки спавна
            Vector2 localSpawnPos = spawnSection.SpawnPosition.Value;
            Vector2 worldOffset = spawnSection.WorldOffset;

            // Устанавливаем глобальную точку спавна
            _currentSpawnPosition = new Vector2(
                localSpawnPos.X + worldOffset.X,
                localSpawnPos.Y + worldOffset.Y
            );

            Logger.Debug($"Selected spawn position in section ({spawnSection.GridX}, {spawnSection.GridY})", true);
            Logger.Debug($"Local spawn position: {localSpawnPos}, World spawn position: {_currentSpawnPosition}", false);
        }
        else
        {
            Logger.Error($"Selected section at ({spawnSection.GridX}, {spawnSection.GridY}) has no spawn position!");
        }
    }

    // Метод для создания структуры секций
    private void CreateMapSections()
    {
        // Создаем секции по сетке
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                // Выбираем случайный биом для секции
                int biomeType = _random.Next(0, MaxBiomeTypes);

                // Создаем новую секцию
                MapSection section = new MapSection(biomeType, x, y, MapWidth, MapHeight);

                // Рассчитываем смещение в мировых координатах для каждой секции
                // Это смещение будет использоваться при размещении тайлов
                float offsetX = x * (MapWidth + SectionSpacing);
                float offsetY = y * (MapHeight + SectionSpacing);

                // Сохраняем смещение
                section.WorldOffset = new Vector2(offsetX, offsetY);

                // Добавляем секцию в список
                _mapSections.Add(section);

                Logger.Debug($"Created section at grid ({x},{y}) with biome {GetBiomeName(biomeType)}", false);
            }
        }
    }

    // Метод для поиска всех необходимых узлов
    private void FindRequiredNodes()
    {
        // Сначала ищем родительский IsometricTileset, если он не указан
        if (IsometricTileset == null)
        {
            IsometricTileset = GetNodeOrNull<Node2D>("../isometric_tileset");

            if (IsometricTileset == null)
            {
                IsometricTileset = FindNodeRecursive<Node2D>(GetTree().Root, "isometric_tileset");
                Logger.Debug($"Поиск IsometricTileset: {(IsometricTileset != null ? "Найден" : "Не найден")}", true);
            }
        }

        // Если нашли IsometricTileset, используем его для поиска дочерних узлов
        if (IsometricTileset != null)
        {
            // Ищем FloorsTileMap внутри IsometricTileset
            if (FloorsTileMap == null)
            {
                FloorsTileMap = IsometricTileset.GetNodeOrNull<Godot.TileMap>("Floors");
                Logger.Debug($"Поиск FloorsTileMap внутри IsometricTileset: {(FloorsTileMap != null ? "Найден" : "Не найден")}", true);
            }

            // Ищем WallsTileMap внутри IsometricTileset
            if (WallsTileMap == null)
            {
                WallsTileMap = IsometricTileset.GetNodeOrNull<Godot.TileMap>("Walls");
                Logger.Debug($"Поиск WallsTileMap внутри IsometricTileset: {(WallsTileMap != null ? "Найден" : "Не найден")}", true);
            }

            // Ищем YSortContainer внутри IsometricTileset
            if (YSortContainer == null)
            {
                YSortContainer = IsometricTileset.GetNodeOrNull<Node2D>("YSortContainer");
                Logger.Debug($"Поиск YSortContainer внутри IsometricTileset: {(YSortContainer != null ? "Найден" : "Не найден")}", true);
            }
        }
        else
        {
            // Если не нашли IsometricTileset, попробуем найти компоненты напрямую
            if (FloorsTileMap == null)
            {
                FloorsTileMap = FindNodeRecursive<Godot.TileMap>(GetTree().Root, "Floors");
            }

            if (WallsTileMap == null)
            {
                WallsTileMap = FindNodeRecursive<Godot.TileMap>(GetTree().Root, "Walls");
            }

            if (YSortContainer == null)
            {
                YSortContainer = FindNodeRecursive<Node2D>(GetTree().Root, "YSortContainer");
            }
        }
    }

    // Проверяем, что YSortContainer найден и его настройки
    private void EnsureSortingWorks()
    {
        if (YSortContainer is Node2D ysortNode2D)
        {
            // Явно включаем Y-сортировку для контейнера (только если это Node2D)
            ysortNode2D.YSortEnabled = true;
            Logger.Debug($"YSortContainer found, YSortEnabled set to: {ysortNode2D.YSortEnabled}", true);
        }
        else
        {
            Logger.Error("YSortContainer is not a Node2D or is not found!");
        }

        if (IsometricTileset is Node2D isometricNode)
        {
            // Устанавливаем YSortEnabled для IsometricTileset (только если это Node2D)
            isometricNode.YSortEnabled = true;
            Logger.Debug($"IsometricTileset found, YSortEnabled set to: {isometricNode.YSortEnabled}", true);
        }
    }

    // Рекурсивный поиск узла по типу и имени
    private T FindNodeRecursive<T>(Node node, string nodeName = null) where T : class
    {
        foreach (var child in node.GetChildren())
        {
            // Проверяем совпадение типа и, если указано, имени
            if (child is T result && (string.IsNullOrEmpty(nodeName) || child.Name == nodeName))
            {
                return result;
            }

            // Рекурсивно проверяем дочерние узлы
            var found = FindNodeRecursive<T>(child, nodeName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // Генерация уровня со случайным биомом
    public void GenerateRandomLevel()
    {
        try
        {
            // Выключаем мульти-секционный режим для обычной генерации
            UseMultiSectionMap = false;

            // Выбираем случайный биом
            int randomBiome = new Random().Next(0, MaxBiomeTypes);
            BiomeType = randomBiome;

            // Добавляем отладочную информацию
            Logger.Debug($"ВЫБРАН БИОМ: {GetBiomeName(BiomeType)} (Тип {BiomeType})", true);

            // Генерируем уровень
            GenerateLevel();

            // Показываем информацию о сгенерированном биоме
            DisplayBiomeInfo();
        }
        catch (Exception e)
        {
            Logger.Error($"Error generating random level: {e.Message}");
        }
    }

    // Отображение информации о текущем биоме
    private void DisplayBiomeInfo()
    {
        string biomeName = GetBiomeName(BiomeType);
        Logger.Debug($"Generated new level with biome: {biomeName} (Type {BiomeType})", true);
    }

    // Получение названия биома по его типу
    private string GetBiomeName(int biomeType)
    {
        switch (biomeType)
        {
            case 1: return "Forest";
            case 2: return "Desert";
            case 3: return "Ice";
            case 4: return "Techno";
            case 5: return "Anomal";
            case 6: return "Lava Springs";
            default: return "Grassland";
        }
    }

    // Метод для начала генерации
    public void GenerateLevel(int? seedValue = null)
    {
        try
        {
            // Устанавливаем конкретное семя, если оно указано
            if (seedValue.HasValue)
            {
                _random = new Random(seedValue.Value);
                Logger.Debug($"Using seed: {seedValue.Value}", true);
            }
            else
            {
                // Иначе используем текущее время в качестве случайного семени
                int seed = (int)DateTime.Now.Ticks;
                _random = new Random(seed);
                Logger.Debug($"Generated seed: {seed}", true);
            }

            // Очищаем предыдущую карту и список комнат
            ClearAllLayers();
            _rooms.Clear();

            // Сбрасываем маску карты
            ResetMapMask();

            // Выбираем фоновый тайл для данного биома
            _backgroundTile = GetBackgroundTileForBiome();

            // Заполняем базовый пол по всей карте
            FillBaseFloor();

            // Генерируем комнаты
            GenerateRooms();

            // Соединяем комнаты коридорами
            ConnectRooms();

            // Заполняем всю карту фоновыми тайлами
            FillMapWithBackgroundTiles();

            // Добавляем стены вокруг комнат и коридоров
            AddWalls();

            // Добавляем декорации и препятствия
            AddDecorationsAndObstacles();

            // Добавляем опасные зоны (вода/лава)
            AddHazards();

            // Вычисляем позицию спавна игрока
            _currentSpawnPosition = GetPlayerSpawnPosition();

            // Устанавливаем флаг, что уровень был сгенерирован
            _levelGenerated = true;

            Logger.Debug($"Level generated with {_rooms.Count} rooms", true);

            // Вызываем сигнал о завершении генерации
            EmitSignal("LevelGenerated", _currentSpawnPosition);

            // Спавним или перемещаем игрока
            if (CreatePlayerOnGeneration)
            {
                HandlePlayerSpawn();
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error during level generation: {e.Message}\n{e.StackTrace}");
        }
    }

    // НОВОЕ: Метод для сброса маски секции
    private void ResetSectionMask(MapSection section)
    {
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                section.SectionMask[x, y] = TileType.None;
            }
        }
    }

    // НОВОЕ: Метод для заполнения базового пола секции
    private void FillSectionBaseFloor(MapSection section)
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome(section.BiomeType);
        int tilesAdded = 0;
        Vector2 worldOffset = section.WorldOffset;

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                try
                {
                    // Рассчитываем мировые координаты тайла
                    Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                    // Размещаем базовый тайл пола на всей секции
                    FloorsTileMap.SetCell(MAP_LAYER, worldPos, FloorsSourceID, backgroundTile);
                    section.SectionMask[x, y] = TileType.Background;
                    tilesAdded++;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error setting base floor at section ({section.GridX},{section.GridY}), pos ({x}, {y}): {e.Message}", false);
                }
            }
        }

        Logger.Debug($"Section base floor filled with {tilesAdded} tiles for biome {GetBiomeName(section.BiomeType)}", false);
    }

    // НОВОЕ: Метод для генерации комнат в секции
    private void GenerateSectionRooms(MapSection section)
    {
        // Создаем комнаты
        int attempts = 0;
        int createdRooms = 0;
        Vector2 worldOffset = section.WorldOffset;

        while (createdRooms < MaxRooms && attempts < MaxRooms * 5)
        {
            attempts++;

            // Определяем случайный размер комнаты
            int width = _random.Next(MinRoomSize, MaxRoomSize + 1);
            int height = _random.Next(MinRoomSize, MaxRoomSize + 1);

            // Определяем случайную позицию комнаты (с отступом от краев)
            int x = _random.Next(2, MapWidth - width - 2);
            int y = _random.Next(2, MapHeight - height - 2);

            // Создаем прямоугольник комнаты
            Rect2I newRoom = new Rect2I(x, y, width, height);

            // Проверяем, пересекается ли новая комната с существующими
            bool roomOverlaps = false;
            foreach (var room in section.Rooms)
            {
                // Добавляем буфер в MinRoomDistance тайлов для избежания слишком близких комнат
                Rect2I expandedRoom = new Rect2I(
                    room.Position - new Vector2I(MinRoomDistance, MinRoomDistance),
                    room.Size + new Vector2I(MinRoomDistance * 2, MinRoomDistance * 2)
                );

                if (expandedRoom.Intersects(newRoom))
                {
                    roomOverlaps = true;
                    break;
                }
            }

            // Если комната не пересекается, добавляем её
            if (!roomOverlaps)
            {
                CreateSectionRoom(section, newRoom);
                section.Rooms.Add(newRoom);
                createdRooms++;

                Logger.Debug($"Created room in section ({section.GridX},{section.GridY}) at ({x},{y}) with size {width}x{height}", false);
            }
        }

        Logger.Debug($"Generated {section.Rooms.Count} rooms in section ({section.GridX},{section.GridY}) after {attempts} attempts", false);
    }

    // НОВОЕ: Метод для создания комнаты в секции
    private void CreateSectionRoom(MapSection section, Rect2I room)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        // Размещаем тайлы пола внутри комнаты
        for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
        {
            for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
            {
                try
                {
                    // Рассчитываем мировые координаты тайла
                    Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                    // Размещаем тайл пола
                    FloorsTileMap.SetCell(MAP_LAYER, worldPos, FloorsSourceID, floorTile);
                    section.SectionMask[x, y] = TileType.Room;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error creating room tile at section ({section.GridX},{section.GridY}), pos ({x},{y}): {e.Message}", false);
                }
            }
        }
    }

    // НОВОЕ: Метод для соединения комнат в секции коридорами
    private void ConnectSectionRooms(MapSection section)
    {
        if (section.Rooms.Count < 2)
        {
            Logger.Debug($"Not enough rooms to connect in section ({section.GridX},{section.GridY})", false);
            return;
        }

        // Сначала сортируем комнаты по положению (слева направо, сверху вниз)
        section.Rooms.Sort((a, b) => {
            return (a.Position.X + a.Position.Y).CompareTo(b.Position.X + b.Position.Y);
        });

        // Соединяем каждую комнату с следующей и с одной случайной
        for (int i = 0; i < section.Rooms.Count; i++)
        {
            // Соединяем с следующей комнатой (по кругу)
            int nextIndex = (i + 1) % section.Rooms.Count;
            ConnectSectionTwoRooms(section, section.Rooms[i], section.Rooms[nextIndex]);

            // С небольшой вероятностью соединяем с еще одной случайной комнатой
            if (_random.Next(0, 100) < 30)  // 30% шанс дополнительного соединения
            {
                int randomIndex = _random.Next(0, section.Rooms.Count);
                // Избегаем соединения с самой собой или с той, с которой уже соединены
                if (randomIndex != i && randomIndex != nextIndex)
                {
                    ConnectSectionTwoRooms(section, section.Rooms[i], section.Rooms[randomIndex]);
                }
            }
        }
    }

    // НОВОЕ: Метод для соединения двух комнат в секции
    private void ConnectSectionTwoRooms(MapSection section, Rect2I roomA, Rect2I roomB)
    {
        try
        {
            // Получаем центры комнат
            Vector2I centerA = roomA.Position + roomA.Size / 2;
            Vector2I centerB = roomB.Position + roomB.Size / 2;

            // С вероятностью 50% сначала идем по горизонтали, потом по вертикали
            // Иначе - сначала по вертикали, потом по горизонтали
            if (_random.Next(0, 2) == 0)
            {
                CreateSectionHorizontalTunnel(section, centerA.X, centerB.X, centerA.Y);
                CreateSectionVerticalTunnel(section, centerA.Y, centerB.Y, centerB.X);
            }
            else
            {
                CreateSectionVerticalTunnel(section, centerA.Y, centerB.Y, centerA.X);
                CreateSectionHorizontalTunnel(section, centerA.X, centerB.X, centerB.Y);
            }

            Logger.Debug($"Connected rooms in section ({section.GridX},{section.GridY}) at ({roomA.Position.X},{roomA.Position.Y}) and ({roomB.Position.X},{roomB.Position.Y})", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting rooms in section ({section.GridX},{section.GridY}): {e.Message}");
        }
    }

    // НОВОЕ: Метод для создания горизонтального тоннеля в секции
    private void CreateSectionHorizontalTunnel(MapSection section, int x1, int x2, int y)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        // Определяем начальную и конечную точку тоннеля
        int start = Math.Min(x1, x2);
        int end = Math.Max(x1, x2);

        // Создаем тоннель с шириной CorridorWidth
        for (int x = start; x <= end; x++)
        {
            for (int offset = 0; offset < CorridorWidth; offset++)
            {
                int yPos = y - (CorridorWidth / 2) + offset;

                // Проверяем, находится ли позиция в пределах секции
                if (yPos >= 0 && yPos < MapHeight && x >= 0 && x < MapWidth)
                {
                    try
                    {
                        // Рассчитываем мировые координаты тайла
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + yPos);

                        // Размещаем тайл коридора
                        FloorsTileMap.SetCell(MAP_LAYER, worldPos, FloorsSourceID, floorTile);
                        if (section.SectionMask[x, yPos] != TileType.Room)  // Не перезаписываем комнаты
                        {
                            section.SectionMask[x, yPos] = TileType.Corridor;
                        }
                    }
                    catch (Exception)
                    {
                        // Игнорируем ошибки при установке тайлов
                    }
                }
            }
        }
    }

    // НОВОЕ: Метод для создания вертикального тоннеля в секции
    private void CreateSectionVerticalTunnel(MapSection section, int y1, int y2, int x)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        // Определяем начальную и конечную точку тоннеля
        int start = Math.Min(y1, y2);
        int end = Math.Max(y1, y2);

        // Создаем тоннель с шириной CorridorWidth
        for (int y = start; y <= end; y++)
        {
            for (int offset = 0; offset < CorridorWidth; offset++)
            {
                int xPos = x - (CorridorWidth / 2) + offset;

                // Проверяем, находится ли позиция в пределах секции
                if (xPos >= 0 && xPos < MapWidth && y >= 0 && y < MapHeight)
                {
                    try
                    {
                        // Рассчитываем мировые координаты тайла
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + xPos, (int)worldOffset.Y + y);

                        // Размещаем тайл коридора
                        FloorsTileMap.SetCell(MAP_LAYER, worldPos, FloorsSourceID, floorTile);
                        if (section.SectionMask[xPos, y] != TileType.Room)  // Не перезаписываем комнаты
                        {
                            section.SectionMask[xPos, y] = TileType.Corridor;
                        }
                    }
                    catch (Exception)
                    {
                        // Игнорируем ошибки при установке тайлов
                    }
                }
            }
        }
    }

    // НОВОЕ: Метод для добавления фоновых тайлов в секции
    private void FillSectionWithBackgroundTiles(MapSection section)
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome(section.BiomeType);
        int tilesAdded = 0;
        Vector2 worldOffset = section.WorldOffset;

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Добавляем декоративный фоновый тайл только если клетка не является комнатой или коридором
                if (section.SectionMask[x, y] != TileType.Room && section.SectionMask[x, y] != TileType.Corridor)
                {
                    try
                    {
                        // Рассчитываем мировые координаты тайла
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                        // Размещаем фоновый тайл
                        WallsTileMap.SetCell(MAP_LAYER, worldPos, WallsSourceID, backgroundTile);
                        if (section.SectionMask[x, y] == TileType.None)
                        {
                            section.SectionMask[x, y] = TileType.Background;
                        }
                        tilesAdded++;
                    }
                    catch (Exception e)
                    {
                        Logger.Debug($"Error setting background tile in section ({section.GridX},{section.GridY}) at ({x}, {y}): {e.Message}", false);
                    }
                }
            }
        }

        Logger.Debug($"Section filled with {tilesAdded} background tiles for biome {GetBiomeName(section.BiomeType)}", false);
    }

    // НОВОЕ: Метод для добавления стен в секции
    private void AddSectionWalls(MapSection section)
    {
        // Создаем временный список для хранения позиций, где нужно разместить стены
        List<Vector2I> wallPositions = new List<Vector2I>();
        Vector2 worldOffset = section.WorldOffset;

        // Проверяем каждую ячейку секции для добавления стен
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Если текущая клетка - фон (не комната и не коридор)
                if (section.SectionMask[x, y] == TileType.Background)
                {
                    // Проверяем все соседние клетки
                    bool shouldBeWall = false;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            // Пропускаем диагональные и текущую клетку
                            if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0))
                                continue;

                            int neighborX = x + dx;
                            int neighborY = y + dy;

                            // Проверяем, находится ли соседняя клетка на карте
                            if (neighborX >= 0 && neighborX < MapWidth &&
                                neighborY >= 0 && neighborY < MapHeight)
                            {
                                // Если сосед - комната или коридор, текущая клетка становится стеной
                                if (section.SectionMask[neighborX, neighborY] == TileType.Room ||
                                    section.SectionMask[neighborX, neighborY] == TileType.Corridor)
                                {
                                    shouldBeWall = true;
                                    break;
                                }
                            }
                        }

                        if (shouldBeWall)
                            break;
                    }

                    // Если клетка должна быть стеной, добавляем ее в список
                    if (shouldBeWall)
                    {
                        // Локальная позиция внутри секции
                        Vector2I localPos = new Vector2I(x, y);

                        // Глобальная позиция с учетом смещения секции
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                        // Добавляем позиции в список
                        wallPositions.Add(new Vector2I(x, y));
                        section.SectionMask[x, y] = TileType.Wall;
                    }
                }
            }
        }

        // Размещаем стены в собранных позициях
        foreach (var localPos in wallPositions)
        {
            try
            {
                // Рассчитываем мировые координаты тайла
                Vector2I worldPos = new Vector2I((int)worldOffset.X + localPos.X, (int)worldOffset.Y + localPos.Y);

                // Получаем тайл стены соответствующий биому
                Vector2I primaryWallTile = GetWallTileForBiome(section.BiomeType, localPos);

                // Размещаем стену
                WallsTileMap.SetCell(MAP_LAYER, worldPos, WallsSourceID, primaryWallTile);
            }
            catch (Exception e)
            {
                Logger.Debug($"Error placing wall in section ({section.GridX},{section.GridY}) at {localPos}: {e.Message}", false);
            }
        }

        Logger.Debug($"Added {wallPositions.Count} wall tiles in section ({section.GridX},{section.GridY})", false);
    }

    // НОВОЕ: Метод для добавления декораций в секции
    private void AddSectionDecorationsAndObstacles(MapSection section)
    {
        // Выбор тайла декорации в зависимости от биома
        Vector2I decorationTile = GetDecorationTileForBiome(section.BiomeType);
        Vector2 worldOffset = section.WorldOffset;

        // Добавляем декорации в комнаты секции
        foreach (var room in section.Rooms)
        {
            // Пропускаем маленькие комнаты
            if (room.Size.X <= 5 || room.Size.Y <= 5)
                continue;

            // Определяем количество декораций для этой комнаты
            int roomArea = room.Size.X * room.Size.Y;
            int maxDecorations = roomArea / 16; // Примерно 1 декорация на каждые 16 клеток
            int numDecorations = _random.Next(1, maxDecorations + 1);

            for (int i = 0; i < numDecorations; i++)
            {
                try
                {
                    // Выбираем случайную позицию внутри комнаты, но не у самых краев
                    int x = _random.Next(room.Position.X + 1, room.Position.X + room.Size.X - 1);
                    int y = _random.Next(room.Position.Y + 1, room.Position.Y + room.Size.Y - 1);

                    // Чтобы не ставить декорации слишком плотно, проверяем соседние клетки
                    bool canPlaceDecoration = true;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int checkX = x + dx;
                            int checkY = y + dy;

                            if (checkX >= 0 && checkX < MapWidth && checkY >= 0 && checkY < MapHeight)
                            {
                                if (section.SectionMask[checkX, checkY] == TileType.Decoration)
                                {
                                    canPlaceDecoration = false;
                                    break;
                                }
                            }
                        }

                        if (!canPlaceDecoration)
                            break;
                    }

                    if (canPlaceDecoration)
                    {
                        // Рассчитываем мировые координаты тайла
                        Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                        // Размещаем декорацию
                        WallsTileMap.SetCell(MAP_LAYER, worldPos, WallsSourceID, decorationTile);
                        section.SectionMask[x, y] = TileType.Decoration;
                    }
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error placing decoration in section ({section.GridX},{section.GridY}): {e.Message}", false);
                }
            }
        }

        Logger.Debug($"Added decorations for section ({section.GridX},{section.GridY}) with biome {GetBiomeName(section.BiomeType)}", false);
    }

    // НОВОЕ: Метод для добавления опасных зон в секции
    private void AddSectionHazards(MapSection section)
    {
        // Определяем тип опасности в зависимости от биома
        Vector2I hazardTile;
        Vector2 worldOffset = section.WorldOffset;

        switch (section.BiomeType)
        {
            case 1: // Forest
                hazardTile = Water; // (5, 0)
                break;
            case 2: // Desert
                hazardTile = Water; // (5, 0) - Оазисы
                break;
            case 3: // Ice
                hazardTile = Water; // (5, 0) - Проломы во льду
                break;
            case 4: // Techno
                hazardTile = Lava; // (1, 1) - Энергетические поля
                break;
            case 5: // Anomal
                hazardTile = Lava; // (1, 1) - Аномальные зоны
                break;
            case 6: // Lava Springs
                hazardTile = Lava; // (1, 1) - Лавовые источники
                break;
            default:
                hazardTile = Water; // (5, 0)
                break;
        }

        // Решаем, добавлять ли опасные зоны в этот биом (шанс 20-70% в зависимости от биома)
        int hazardChance = 20 + (section.BiomeType * 10);

        // Если случайное число не попадает в диапазон или нет комнат, выходим
        if (_random.Next(0, 100) >= hazardChance || section.Rooms.Count == 0)
            return;

        try
        {
            // Выбираем случайную комнату для размещения опасности    
            int roomIndex = _random.Next(0, section.Rooms.Count);
            Rect2I room = section.Rooms[roomIndex];

            // Не размещаем опасности в слишком маленьких комнатах
            if (room.Size.X <= 5 || room.Size.Y <= 5)
                return;

            // Определяем размер опасной зоны (меньше чем раньше)
            int hazardWidth = _random.Next(2, Math.Min(room.Size.X - 3, 4));
            int hazardHeight = _random.Next(2, Math.Min(room.Size.Y - 3, 4));

            // Оставляем место для прохода в комнате
            int maxStartX = room.Position.X + room.Size.X - hazardWidth - 2;
            int maxStartY = room.Position.Y + room.Size.Y - hazardHeight - 2;

            int startX = _random.Next(room.Position.X + 2, maxStartX);
            int startY = _random.Next(room.Position.Y + 2, maxStartY);

            // Размещаем опасную зону
            for (int x = startX; x < startX + hazardWidth; x++)
            {
                for (int y = startY; y < startY + hazardHeight; y++)
                {
                    // Рассчитываем мировые координаты тайла
                    Vector2I worldPos = new Vector2I((int)worldOffset.X + x, (int)worldOffset.Y + y);

                    // Размещаем опасный тайл
                    FloorsTileMap.SetCell(MAP_LAYER, worldPos, FloorsSourceID, hazardTile);

                    // Опасные зоны (лава) непроходимы, а вода проходима
                    if (hazardTile == Lava)
                    {
                        // Для лавы создаем визуальное представление для отображения эффекта
                        WallsTileMap.SetCell(MAP_LAYER, worldPos, WallsSourceID, hazardTile);
                    }
                }
            }

            Logger.Debug($"Added {hazardWidth}x{hazardHeight} hazard area in section ({section.GridX},{section.GridY})", false);
        }
        catch (Exception e)
        {
            Logger.Debug($"Error adding hazards in section ({section.GridX},{section.GridY}): {e.Message}", false);
        }
    }

    // НОВОЕ: Метод для получения точки спавна в секции
    private Vector2 GetSectionSpawnPosition(MapSection section)
    {
        if (section.Rooms.Count == 0)
        {
            Logger.Error($"No rooms available for spawn in section ({section.GridX},{section.GridY})!");
            return Vector2.Zero;
        }

        // Выбираем случайную комнату
        int roomIndex = _random.Next(0, section.Rooms.Count);
        Rect2I spawnRoom = section.Rooms[roomIndex];

        // Получаем центр комнаты
        Vector2I center = spawnRoom.Position + spawnRoom.Size / 2;

        // Преобразуем координаты тайла в мировые координаты
        Vector2 worldPos = MapTileToIsometricWorld(center);

        Logger.Debug($"Selected spawn position in section ({section.GridX},{section.GridY}) at room {roomIndex}, tile ({center.X}, {center.Y}), world pos: {worldPos}", false);

        return worldPos;
    }

    // Метод для получения позиции спавна игрока
    public Vector2 GetPlayerSpawnPosition()
    {
        if (_rooms.Count == 0)
        {
            Logger.Error("No rooms available for player spawn!");
            return Vector2.Zero;
        }

        // Выбираем случайную комнату
        int roomIndex = _random.Next(0, _rooms.Count);
        Rect2I spawnRoom = _rooms[roomIndex];

        // Получаем центр комнаты
        Vector2I center = spawnRoom.Position + spawnRoom.Size / 2;

        // Преобразуем координаты тайла в мировые координаты
        Vector2 worldPos = MapTileToIsometricWorld(center);

        Logger.Debug($"Selected spawn position at room {roomIndex}, tile ({center.X}, {center.Y}), world pos: {worldPos}", true);

        return worldPos;
    }

    // Обработка спавна игрока
    private void HandlePlayerSpawn()
    {
        if (!_levelGenerated && _mapSections.Count == 0)
        {
            Logger.Debug("Level not generated yet, cannot spawn player", true);
            return;
        }

        // Ищем существующего игрока
        Node2D existingPlayer = FindPlayer();

        if (existingPlayer != null && TeleportExistingPlayer)
        {
            // Перемещаем существующего игрока
            _currentPlayer = existingPlayer;
            _currentPlayer.Position = _currentSpawnPosition;
            Logger.Debug($"Teleported existing player to spawn position: {_currentSpawnPosition}", true);
        }
        else if (PlayerScene != null)
        {
            // Создаем нового игрока
            SpawnNewPlayer();
        }
        else
        {
            Logger.Error("Cannot spawn player: PlayerScene is not set and no existing player found");
        }

        // Центрируем камеру на игроке
        CenterCameraOnPlayer();
    }

    // Поиск существующего игрока
    private Node2D FindPlayer()
    {
        var players = GetTree().GetNodesInGroup(PlayerGroup);
        if (players.Count > 0 && players[0] is Node2D player)
        {
            return player;
        }
        return null;
    }

    // Создание нового игрока
    private void SpawnNewPlayer()
    {
        try
        {
            // Если текущий игрок существует, удаляем его
            if (_currentPlayer != null && IsInstanceValid(_currentPlayer))
            {
                _currentPlayer.QueueFree();
            }

            // Создаем нового игрока
            _currentPlayer = PlayerScene.Instantiate<Node2D>();

            // Убедимся, что у игрока нет фиксированного Z-индекса
            if (_currentPlayer is Node2D playerNode)
            {
                // Для отладки
                Logger.Debug($"Created player node: {playerNode.Name}, ZIndex before: {playerNode.ZIndex}", true);

                // Сбрасываем Z-индекс для сортировки
                playerNode.ZIndex = 0;

                // Для отладки
                Logger.Debug($"Reset player ZIndex to 0", true);
            }

            _currentPlayer.Position = _currentSpawnPosition;

            // Добавляем игрока в группу для быстрого поиска
            if (!_currentPlayer.IsInGroup(PlayerGroup))
            {
                _currentPlayer.AddToGroup(PlayerGroup);
            }

            // Проверяем, что YSortContainer найден и включена Y сортировка
            if (YSortContainer != null)
            {
                // Убедимся, что Y-сортировка включена (если это Node2D)
                if (YSortContainer is Node2D ysortNode)
                {
                    ysortNode.YSortEnabled = true;

                    // Для отладки
                    Logger.Debug($"YSortContainer is Node2D, YSortEnabled set to: {ysortNode.YSortEnabled}", true);
                }

                // Добавляем игрока в YSortContainer для правильной сортировки по глубине
                YSortContainer.AddChild(_currentPlayer);
                Logger.Debug($"Spawned new player at {_currentSpawnPosition} in YSortContainer", true);
            }
            else
            {
                // Запасной вариант - добавляем как обычно к родителю
                GetParent().AddChild(_currentPlayer);
                Logger.Error($"YSortContainer not found. Spawned player at {_currentSpawnPosition} in parent node");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error spawning player: {e.Message}");
        }
    }

    // Центрирование камеры на игроке
    private void CenterCameraOnPlayer()
    {
        if (_currentPlayer == null)
            return;

        // Проверяем наличие CameraController
        var cameraControllers = GetTree().GetNodesInGroup("Camera");
        foreach (var cam in cameraControllers)
        {
            if (cam is CameraController cameraController)
            {
                cameraController.CenterOnPlayer();
                Logger.Debug("Camera centered on player using CameraController", false);
                return;
            }
        }

        // Если не нашли контроллер, пробуем найти обычную камеру
        var camera = GetViewport().GetCamera2D();
        if (camera != null)
        {
            camera.Position = _currentPlayer.Position;
            Logger.Debug("Camera centered on player using GetCamera2D", false);
        }
    }

    // Сброс маски карты
    private void ResetMapMask()
    {
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                _mapMask[x, y] = TileType.None;
            }
        }
    }

    // Метод для очистки всех слоев карты
    private void ClearAllLayers()
    {
        try
        {
            if (FloorsTileMap != null)
            {
                FloorsTileMap.Clear();
                Logger.Debug("FloorsTileMap cleared successfully", false);
            }

            if (WallsTileMap != null)
            {
                WallsTileMap.Clear();
                Logger.Debug("WallsTileMap cleared successfully", false);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error clearing TileMaps: {e.Message}");
        }
    }

    // НОВОЕ: Перегруженный метод для получения фонового тайла в зависимости от типа биома
    private Vector2I GetBackgroundTileForBiome(int biomeType)
    {
        switch (biomeType)
        {
            case 1: // Forest
                return ForestFloor;
            case 2: // Desert
                return Stone;
            case 3: // Ice
                return Ice;
            case 4: // Techno
                return Techno;
            case 5: // Anomal
                return Anomal;
            case 6: // Lava Springs
                return Lava;
            default: // Grassland
                return Grass;
        }
    }

    // Выбор фонового тайла в зависимости от биома
    private Vector2I GetBackgroundTileForBiome()
    {
        return GetBackgroundTileForBiome(BiomeType);
    }

    // Метод для заполнения базового пола (вся карта)
    // Обновленный метод для базового пола
    private void FillBaseFloor()
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome();
        int tilesAdded = 0;

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                try
                {
                    // Размещаем базовый тайл пола на ВСЕЙ карте 
                    FloorsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), FloorsSourceID, backgroundTile);
                    _mapMask[x, y] = TileType.Background;
                    tilesAdded++;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error setting base floor at ({x}, {y}): {e.Message}", false);
                }
            }
        }

        Logger.Debug($"Base floor filled with {tilesAdded} tiles", true);
    }

    // Метод для добавления декоративных фоновых тайлов только в пустых областях
    private void FillMapWithBackgroundTiles()
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome();
        int tilesAdded = 0;

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Добавляем декоративный фоновый тайл ТОЛЬКО если клетка НЕ является комнатой или коридором
                if (_mapMask[x, y] != TileType.Room && _mapMask[x, y] != TileType.Corridor)
                {
                    try
                    {
                        // Размещаем декоративный фоновый тайл на WallsTileMap
                        WallsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), WallsSourceID, backgroundTile);
                        if (_mapMask[x, y] == TileType.None)
                        {
                            _mapMask[x, y] = TileType.Background;
                        }
                        tilesAdded++;
                    }
                    catch (Exception e)
                    {
                        Logger.Debug($"Error setting background tile at ({x}, {y}): {e.Message}", false);
                    }
                }
            }
        }

        Logger.Debug($"Map filled with {tilesAdded} background tiles for biome type: {BiomeType}", true);
    }

    // Метод для генерации комнат
    private void GenerateRooms()
    {
        // Создаем комнаты
        int attempts = 0;
        int createdRooms = 0;

        while (createdRooms < MaxRooms && attempts < MaxRooms * 5)
        {
            attempts++;

            // Определяем случайный размер комнаты
            int width = _random.Next(MinRoomSize, MaxRoomSize + 1);
            int height = _random.Next(MinRoomSize, MaxRoomSize + 1);

            // Определяем случайную позицию комнаты (с отступом от краев)
            int x = _random.Next(2, MapWidth - width - 2);
            int y = _random.Next(2, MapHeight - height - 2);

            // Создаем прямоугольник комнаты
            Rect2I newRoom = new Rect2I(x, y, width, height);

            // Проверяем, пересекается ли новая комната с существующими
            bool roomOverlaps = false;
            foreach (var room in _rooms)
            {
                // Добавляем буфер в MinRoomDistance тайлов для избежания слишком близких комнат
                Rect2I expandedRoom = new Rect2I(
                    room.Position - new Vector2I(MinRoomDistance, MinRoomDistance),
                    room.Size + new Vector2I(MinRoomDistance * 2, MinRoomDistance * 2)
                );

                if (expandedRoom.Intersects(newRoom))
                {
                    roomOverlaps = true;
                    break;
                }
            }

            // Если комната не пересекается, добавляем её
            if (!roomOverlaps)
            {
                CreateRoom(newRoom);
                _rooms.Add(newRoom);
                createdRooms++;

                Logger.Debug($"Created room at ({x},{y}) with size {width}x{height}", false);
            }
        }

        Logger.Debug($"Generated {_rooms.Count} rooms after {attempts} attempts", true);
    }

    // Метод для создания комнаты на карте
    private void CreateRoom(Rect2I room)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome();

        // Размещаем тайлы пола внутри комнаты
        for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
        {
            for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
            {
                try
                {
                    // Проверяем, что используем FloorsTileMap (а не WallsTileMap)
                    FloorsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), FloorsSourceID, floorTile);
                    _mapMask[x, y] = TileType.Room;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error creating room tile at ({x},{y}): {e.Message}", false);
                }
            }
        }
    }

    // Метод для соединения комнат коридорами
    private void ConnectRooms()
    {
        if (_rooms.Count < 2)
        {
            Logger.Debug("Not enough rooms to connect", true);
            return;
        }

        // Сначала сортируем комнаты по положению (слева направо, сверху вниз)
        _rooms.Sort((a, b) => {
            return (a.Position.X + a.Position.Y).CompareTo(b.Position.X + b.Position.Y);
        });

        // Соединяем каждую комнату с следующей и с одной случайной
        for (int i = 0; i < _rooms.Count; i++)
        {
            // Соединяем с следующей комнатой (по кругу)
            int nextIndex = (i + 1) % _rooms.Count;
            ConnectTwoRooms(_rooms[i], _rooms[nextIndex]);

            // С небольшой вероятностью соединяем с еще одной случайной комнатой
            if (_random.Next(0, 100) < 30)  // 30% шанс дополнительного соединения
            {
                int randomIndex = _random.Next(0, _rooms.Count);
                // Избегаем соединения с самой собой или с той, с которой уже соединены
                if (randomIndex != i && randomIndex != nextIndex)
                {
                    ConnectTwoRooms(_rooms[i], _rooms[randomIndex]);
                }
            }
        }
    }

    // Соединение двух конкретных комнат
    private void ConnectTwoRooms(Rect2I roomA, Rect2I roomB)
    {
        try
        {
            // Получаем центры комнат
            Vector2I centerA = roomA.Position + roomA.Size / 2;
            Vector2I centerB = roomB.Position + roomB.Size / 2;

            // С вероятностью 50% сначала идем по горизонтали, потом по вертикали
            // Иначе - сначала по вертикали, потом по горизонтали
            if (_random.Next(0, 2) == 0)
            {
                CreateHorizontalTunnel(centerA.X, centerB.X, centerA.Y);
                CreateVerticalTunnel(centerA.Y, centerB.Y, centerB.X);
            }
            else
            {
                CreateVerticalTunnel(centerA.Y, centerB.Y, centerA.X);
                CreateHorizontalTunnel(centerA.X, centerB.X, centerB.Y);
            }

            Logger.Debug($"Connected room at ({roomA.Position.X},{roomA.Position.Y}) with room at ({roomB.Position.X},{roomB.Position.Y})", false);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting rooms: {e.Message}");
        }
    }

    // Метод для создания горизонтального тоннеля
    private void CreateHorizontalTunnel(int x1, int x2, int y)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome();

        // Определяем начальную и конечную точку тоннеля
        int start = Math.Min(x1, x2);
        int end = Math.Max(x1, x2);

        // Создаем тоннель с шириной CorridorWidth
        for (int x = start; x <= end; x++)
        {
            for (int offset = 0; offset < CorridorWidth; offset++)
            {
                int yPos = y - (CorridorWidth / 2) + offset;

                // Проверяем, находится ли позиция в пределах карты
                if (yPos >= 0 && yPos < MapHeight)
                {
                    try
                    {
                        // Размещаем тайл коридора на FloorsTileMap
                        FloorsTileMap.SetCell(MAP_LAYER, new Vector2I(x, yPos), FloorsSourceID, floorTile);
                        if (_mapMask[x, yPos] != TileType.Room)  // Не перезаписываем комнаты
                        {
                            _mapMask[x, yPos] = TileType.Corridor;
                        }
                    }
                    catch (Exception)
                    {
                        // Игнорируем ошибки при установке тайлов
                    }
                }
            }
        }
    }


    // Метод для создания вертикального тоннеля
    private void CreateVerticalTunnel(int y1, int y2, int x)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = GetFloorTileForBiome();

        // Определяем начальную и конечную точку тоннеля
        int start = Math.Min(y1, y2);
        int end = Math.Max(y1, y2);

        // Создаем тоннель с шириной CorridorWidth
        for (int y = start; y <= end; y++)
        {
            for (int offset = 0; offset < CorridorWidth; offset++)
            {
                int xPos = x - (CorridorWidth / 2) + offset;

                // Проверяем, находится ли позиция в пределах карты
                if (xPos >= 0 && xPos < MapWidth)
                {
                    try
                    {
                        // Размещаем тайл коридора на FloorsTileMap
                        FloorsTileMap.SetCell(MAP_LAYER, new Vector2I(xPos, y), FloorsSourceID, floorTile);
                        if (_mapMask[xPos, y] != TileType.Room)  // Не перезаписываем комнаты
                        {
                            _mapMask[xPos, y] = TileType.Corridor;
                        }
                    }
                    catch (Exception)
                    {
                        // Игнорируем ошибки при установке тайлов
                    }
                }
            }
        }
    }


    // НОВОЕ: Перегруженный метод для получения тайла пола на основе типа биома
    private Vector2I GetFloorTileForBiome(int biomeType)
    {
        switch (biomeType)
        {
            case 1: // Forest
                return Grass; // Изменено с ForestFloor на Grass
            case 2: // Desert
                return Sand; // Без изменений 
            case 3: // Ice
                return Snow; // Без изменений
            case 4: // Techno
                return Stone; // Изменено с Techno на Stone
            case 5: // Anomal
                return Ground; // Изменено с Anomal на Ground
            case 6: // Lava Springs
                return Ground;
            default: // Grassland
                return ForestFloor; // Изменено с Grass на ForestFloor
        }
    }

    // Метод для получения тайла пола в зависимости от биома
    private Vector2I GetFloorTileForBiome()
    {
        return GetFloorTileForBiome(BiomeType);
    }

    // Метод для добавления стен вокруг проходимых областей
    private void AddWalls()
    {
        // Создаем временный список для хранения позиций, где нужно разместить стены
        List<Vector2I> wallPositions = new List<Vector2I>();

        // Проверяем каждую ячейку карты для добавления стен
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Если текущая клетка - фон (не комната и не коридор)
                if (_mapMask[x, y] == TileType.Background)
                {
                    // Проверяем все соседние клетки
                    bool shouldBeWall = false;

                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            // Пропускаем диагональные и текущую клетку
                            if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0))
                                continue;

                            int neighborX = x + dx;
                            int neighborY = y + dy;

                            // Проверяем, находится ли соседняя клетка на карте
                            if (neighborX >= 0 && neighborX < MapWidth &&
                                neighborY >= 0 && neighborY < MapHeight)
                            {
                                // Если сосед - комната или коридор, текущая клетка становится стеной
                                if (_mapMask[neighborX, neighborY] == TileType.Room ||
                                    _mapMask[neighborX, neighborY] == TileType.Corridor)
                                {
                                    shouldBeWall = true;
                                    break;
                                }
                            }
                        }

                        if (shouldBeWall)
                            break;
                    }

                    // Если клетка должна быть стеной, добавляем ее в список
                    if (shouldBeWall)
                    {
                        wallPositions.Add(new Vector2I(x, y));
                        _mapMask[x, y] = TileType.Wall;
                    }
                }
            }
        }

        // Размещаем стены в собранных позициях
        foreach (var pos in wallPositions)
        {
            try
            {
                // Получаем тайл стены соответствующий биому
                Vector2I primaryWallTile = GetWallTileForBiome(BiomeType, pos);

                // Визуальная часть стены на WallsTileMap
                WallsTileMap.SetCell(MAP_LAYER, pos, WallsSourceID, primaryWallTile);

                // Добавляем коллизию для стен
                if (_random.Next(0, 100) < 70) // 70% шанс сделать стену непроходимой
                {
                    // В данном случае коллизия создается автоматически,
                    // так как WallsTileMap имеет настроенные коллизии
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"Error placing wall at {pos}: {e.Message}", false);
            }
        }

        Logger.Debug($"Added {wallPositions.Count} wall tiles specific to biome {GetBiomeName(BiomeType)}", true);
    }


    // НОВОЕ: Перегруженный метод для получения тайла стены в зависимости от типа биома
    private Vector2I GetWallTileForBiome(int biomeType, Vector2I position)
    {
        // Если включена вариативность и это не опорная стена, добавляем вариации
        bool useVariation = UseVariedWalls && _random.Next(0, 100) < 30; // 30% шанс вариации

        switch (biomeType)
        {
            case 1: // Forest
                if (useVariation && _random.Next(0, 100) < 50)
                    return Snow; // Вкрапления Snow
                return ForestFloor; // Основная стена ForestFloor

            case 2: // Desert
                if (useVariation && _random.Next(0, 100) < 40)
                    return Ground; // Вкрапления Ground
                return Stone; // Основная стена Stone

            case 3: // Ice
                if (useVariation && _random.Next(0, 100) < 40)
                    return Stone; // Вкрапления Stone
                return Ice; // Основная стена Ice

            case 4: // Techno
                if (useVariation && _random.Next(0, 100) < 40)
                    return Anomal; // Вкрапления Anomal
                return Techno; // Основная стена Techno

            case 5: // Anomal
                if (useVariation)
                {
                    int variationType = _random.Next(0, 100);
                    if (variationType < 40)
                        return Lava; // Вкрапления Lava
                    return Anomal;
                }
                return Anomal; // Основная стена Anomal

            case 6: // Lava Springs
                if (useVariation && _random.Next(0, 100) < 40)
                    return Stone; // Вкрапления Stone
                return Lava; // Основная стена Lava

            default: // Grassland
                if (useVariation && _random.Next(0, 100) < 40)
                    return Ground; // Вкрапления Ground
                return Grass; // Основная стена Grass
        }
    }

    // Получение тайла стены в зависимости от биома
    private Vector2I GetWallTileForBiome(Vector2I position)
    {
        return GetWallTileForBiome(BiomeType, position);
    }

    // НОВОЕ: Перегруженный метод для получения тайла декорации в зависимости от типа биома
    private Vector2I GetDecorationTileForBiome(int biomeType)
    {
        switch (biomeType)
        {
            case 1: // Forest
                return _random.Next(0, 100) < 50 ? Snow : ForestFloor;
            case 2: // Desert
                return _random.Next(0, 100) < 60 ? Sand : Stone;
            case 3: // Ice
                return _random.Next(0, 100) < 70 ? Snow : Ice;
            case 4: // Techno
                return _random.Next(0, 100) < 50 ? Stone : Techno;
            case 5: // Anomal
                return _random.Next(0, 100) < 50 ? Anomal : Lava;
            case 6: // Lava Springs
                return _random.Next(0, 100) < 60 ? Lava : Stone;
            default: // Grassland
                return _random.Next(0, 100) < 70 ? Grass : Ground;
        }
    }

    // Получение тайла второго уровня стены
    private Vector2I GetSecondaryWallTileForBiome(Vector2I position)
    {
        // Второй уровень стены может отличаться от первого для более интересного вида
        switch (BiomeType)
        {
            case 1: // Forest
                return _random.Next(0, 100) < 30 ? Snow : ForestFloor;
            case 2: // Desert
                return _random.Next(0, 100) < 30 ? Ground : Stone;
            case 3: // Ice
                return _random.Next(0, 100) < 30 ? Stone : Ice;
            case 4: // Techno
                return _random.Next(0, 100) < 30 ? Anomal : Techno;
            case 5: // Anomal
                return _random.Next(0, 100) < 40 ? Lava : Anomal;
            case 6: // Lava Springs
                return _random.Next(0, 100) < 30 ? Stone : Lava;
            default: // Grassland
                return _random.Next(0, 100) < 30 ? Ground : Grass;
        }
    }

    // Метод для выбора тайла декорации в зависимости от биома
    private Vector2I GetDecorationTileForBiome()
    {
        return GetDecorationTileForBiome(BiomeType);
    }

    // Метод для добавления декораций и препятствий
    private void AddDecorationsAndObstacles()
    {
        // Выбор тайла декорации в зависимости от биома
        Vector2I decorationTile = GetDecorationTileForBiome();

        // Добавляем декорации в комнаты
        foreach (var room in _rooms)
        {
            // Пропускаем маленькие комнаты
            if (room.Size.X <= 5 || room.Size.Y <= 5)
                continue;

            // Определяем количество декораций для этой комнаты
            int roomArea = room.Size.X * room.Size.Y;
            int maxDecorations = roomArea / 16; // Примерно 1 декорация на каждые 16 клеток
            int numDecorations = _random.Next(1, maxDecorations + 1);

            for (int i = 0; i < numDecorations; i++)
            {
                try
                {
                    // Выбираем случайную позицию внутри комнаты, но не у самых краев
                    int x = _random.Next(room.Position.X + 1, room.Position.X + room.Size.X - 1);
                    int y = _random.Next(room.Position.Y + 1, room.Position.Y + room.Size.Y - 1);

                    // Чтобы не ставить декорации слишком плотно, проверяем соседние клетки
                    bool canPlaceDecoration = true;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int checkX = x + dx;
                            int checkY = y + dy;

                            if (checkX >= 0 && checkX < MapWidth && checkY >= 0 && checkY < MapHeight)
                            {
                                if (_mapMask[checkX, checkY] == TileType.Decoration)
                                {
                                    canPlaceDecoration = false;
                                    break;
                                }
                            }
                        }

                        if (!canPlaceDecoration)
                            break;
                    }

                    if (canPlaceDecoration)
                    {
                        // Размещаем декорацию на WallsTileMap
                        WallsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), WallsSourceID, decorationTile);
                        _mapMask[x, y] = TileType.Decoration;

                        // Некоторые декорации делаем непроходимыми через коллизии
                        // Они будут непроходимыми автоматически благодаря настройкам WallsTileMap
                    }
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error placing decoration: {e.Message}", false);
                }
            }
        }

        // Также добавляем некоторые декорации в коридоры
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Только для коридоров, и только с малой вероятностью
                if (_mapMask[x, y] == TileType.Corridor && _random.Next(0, 100) < 5) // 5% шанс декорации в коридоре
                {
                    try
                    {
                        WallsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), WallsSourceID, decorationTile);
                        _mapMask[x, y] = TileType.Decoration;
                    }
                    catch (Exception e)
                    {
                        Logger.Debug($"Error placing corridor decoration: {e.Message}", false);
                    }
                }
            }
        }

        Logger.Debug($"Added decorations for biome {GetBiomeName(BiomeType)}", true);
    }

    // Метод для добавления опасных участков (вода/лава и т.д.)
    private void AddHazards()
    {
        // Определяем тип опасности в зависимости от биома
        Vector2I hazardTile;

        switch (BiomeType)
        {
            case 1: // Forest
                hazardTile = Water; // (5, 0)
                break;
            case 2: // Desert
                hazardTile = Water; // (5, 0) - Оазисы
                break;
            case 3: // Ice
                hazardTile = Water; // (5, 0) - Проломы во льду
                break;
            case 4: // Techno
                hazardTile = Lava; // (1, 1) - Энергетические поля
                break;
            case 5: // Anomal
                hazardTile = Lava; // (1, 1) - Аномальные зоны
                break;
            case 6: // Lava Springs
                hazardTile = Lava; // (1, 1) - Лавовые источники
                break;
            default:
                hazardTile = Water; // (5, 0)
                break;
        }

        // Решаем, добавлять ли опасные зоны в этот биом (шанс 20-70% в зависимости от биома)
        int hazardChance = 20 + (BiomeType * 10);

        // Если случайное число не попадает в диапазон или нет комнат, выходим
        if (_random.Next(0, 100) >= hazardChance || _rooms.Count == 0)
            return;

        try
        {
            // Выбираем случайную комнату для размещения опасности    
            int roomIndex = _random.Next(0, _rooms.Count);
            Rect2I room = _rooms[roomIndex];

            // Не размещаем опасности в слишком маленьких комнатах
            if (room.Size.X <= 5 || room.Size.Y <= 5)
                return;

            // Определяем размер опасной зоны (меньше чем раньше)
            int hazardWidth = _random.Next(2, Math.Min(room.Size.X - 3, 4));
            int hazardHeight = _random.Next(2, Math.Min(room.Size.Y - 3, 4));

            // Оставляем место для прохода в комнате
            int maxStartX = room.Position.X + room.Size.X - hazardWidth - 2;
            int maxStartY = room.Position.Y + room.Size.Y - hazardHeight - 2;

            int startX = _random.Next(room.Position.X + 2, maxStartX);
            int startY = _random.Next(room.Position.Y + 2, maxStartY);

            // Размещаем опасную зону
            for (int x = startX; x < startX + hazardWidth; x++)
            {
                for (int y = startY; y < startY + hazardHeight; y++)
                {
                    // Размещаем опасный тайл на пол (FloorsTileMap)
                    FloorsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), FloorsSourceID, hazardTile);

                    // Опасные зоны (лава) непроходимы, а вода проходима
                    if (hazardTile == Lava)
                    {
                        // Для лавы создаем визуальное представление на WallsTileMap для отображения эффекта
                        WallsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), WallsSourceID, hazardTile);
                    }
                }
            }

            Logger.Debug($"Added {hazardWidth}x{hazardHeight} hazard area in room {roomIndex}", false);
        }
        catch (Exception e)
        {
            Logger.Debug($"Error adding hazards: {e.Message}", false);
        }
    }

    // Вспомогательный метод для установки проходимости тайла
    private void SetTileWalkable(int x, int y, bool isWalkable)
    {
        try
        {
            // Получаем данные тайла
            TileData tileData = FloorsTileMap.GetCellTileData(MAP_LAYER, new Vector2I(x, y));
            if (tileData != null)
            {
                // Устанавливаем пользовательские данные
                tileData.SetCustomData("is_walkable", isWalkable);

                // Обновляем физическую коллизию через WallsTileMap
                if (isWalkable)
                {
                    // Убираем тайл из WallsTileMap, чтобы сделать проходимым
                    WallsTileMap.EraseCell(MAP_LAYER, new Vector2I(x, y));
                }
                else
                {
                    // Добавляем блокирующий тайл в WallsTileMap
                    WallsTileMap.SetCell(MAP_LAYER, new Vector2I(x, y), WallsSourceID, Empty);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Debug($"Error setting tile walkability at ({x}, {y}): {e.Message}", false);
        }
    }

    // Вспомогательный метод для преобразования координат тайла в мировые координаты
    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // Получаем размер ячейки из TileMap, если возможно
        Vector2I tileSize = FloorsTileMap?.TileSet?.TileSize ?? new Vector2I(64, 32);

        // Для изометрии 2:1 (обычное соотношение в изометрических играх)
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;

        return new Vector2(x, y);
    }

    // Получить текущую позицию спавна
    public Vector2 GetCurrentSpawnPosition()
    {
        return _currentSpawnPosition;
    }

    // Публичный метод для телепортации игрока в указанную комнату
    public void TeleportPlayerToRoom(int roomIndex)
    {
        if (roomIndex < 0 || roomIndex >= _rooms.Count)
        {
            Logger.Error($"Invalid room index: {roomIndex}. Valid range: 0-{_rooms.Count - 1}");
            return;
        }

        // Получаем центр комнаты
        Rect2I room = _rooms[roomIndex];
        Vector2I center = room.Position + room.Size / 2;

        // Преобразуем в мировые координаты
        Vector2 worldPos = MapTileToIsometricWorld(center);

        // Находим игрока и телепортируем
        Node2D player = FindPlayer();
        if (player != null)
        {
            player.Position = worldPos;
            Logger.Debug($"Player teleported to room {roomIndex} at position {worldPos}", true);

            // Центрируем камеру
            CenterCameraOnPlayer();
        }
        else
        {
            Logger.Error("Cannot teleport player: Player not found");
        }
    }

    // НОВОЕ: Метод телепортации игрока в указанную секцию
    public void TeleportPlayerToSection(int sectionX, int sectionY)
    {
        // Находим секцию по координатам сетки
        MapSection section = _mapSections.Find(s => s.GridX == sectionX && s.GridY == sectionY);

        if (section == null)
        {
            Logger.Error($"Cannot find section at grid coordinates ({sectionX}, {sectionY})");
            return;
        }

        if (!section.SpawnPosition.HasValue)
        {
            Logger.Error($"Section at ({sectionX}, {sectionY}) has no spawn position");
            return;
        }

        // Рассчитываем мировые координаты точки спавна
        Vector2 localSpawnPos = section.SpawnPosition.Value;
        Vector2 worldOffset = section.WorldOffset;
        Vector2 worldSpawnPos = new Vector2(localSpawnPos.X + worldOffset.X, localSpawnPos.Y + worldOffset.Y);

        // Находим игрока и телепортируем
        Node2D player = FindPlayer();
        if (player != null)
        {
            player.Position = worldSpawnPos;
            Logger.Debug($"Player teleported to section ({sectionX}, {sectionY}) at position {worldSpawnPos}", true);

            // Центрируем камеру
            CenterCameraOnPlayer();
        }
        else
        {
            Logger.Error("Cannot teleport player: Player not found");
        }
    }

    // НОВОЕ: Получить информацию о всех секциях для дебага
    public string GetSectionsInfo()
    {
        string info = $"Multi-section map: {_mapSections.Count} sections in {GridWidth}x{GridHeight} grid\n";

        foreach (var section in _mapSections)
        {
            info += $"Section ({section.GridX}, {section.GridY}): Biome {GetBiomeName(section.BiomeType)}, " +
                   $"Rooms: {section.Rooms.Count}, " +
                   $"Offset: {section.WorldOffset}\n";
        }

        info += $"Current spawn position: {_currentSpawnPosition}";

        return info;
    }
}