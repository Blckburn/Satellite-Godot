using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class LevelGenerator : Node
{
    public enum GenerationAlgorithm { WorldBiomes = 0 }
    // Сигнал о завершении генерации уровня с передачей точки спавна
    [Signal] public delegate void LevelGeneratedEventHandler(Vector2 spawnPosition);

    // Сигнал о завершении генерации всей мульти-секционной карты
    [Signal] public delegate void MultiSectionMapGeneratedEventHandler();

    // Ссылки на раздельные TileMap и контейнеры
    [Export] public Godot.TileMapLayer FloorsTileMap { get; set; } // Для пола
    [Export] public Godot.TileMapLayer WallsTileMap { get; set; }  // Для стен и декораций
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
    [Export] public int WallsSourceID { get; set; } = 4;  // Source ID для тайлсета стен (spritesheet.png)
    [Export] public int FloorsSourceID { get; set; } = 4;  // Source ID для тайлсета пола (spritesheet.png)

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
    
    // Настройки отображения координат
    [Export] public bool ShowCoordinateLabels { get; set; } = false;  // По умолчанию выкл. для производительности
    [Export] public int GridWidth { get; set; } = 2;  // Количество секций по горизонтали
    [Export] public int GridHeight { get; set; } = 2;  // Количество секций по вертикали
    [Export] public int SectionSpacing { get; set; } = 10;  // Расстояние между секциями в тайлах

    // НОВОЕ: Настройка для соединения секций проходами
    [Export] public bool ConnectSections { get; set; } = true;  // Соединять ли секции проходами
    [Export] public int ConnectorWidth { get; set; } = 3;  // Ширина проходов между секциями

    // Клавиша для генерации мульти-секционной карты
    [Export] public Key MultiSectionGenerationKey { get; set; } = Key.M;

    // Алгоритм всегда WorldBiomes (убраны другие варианты из инспектора)
    private readonly GenerationAlgorithm Algorithm = GenerationAlgorithm.WorldBiomes;

    // Cave (Cellular Automata) params (оставлены как внутренние, без экспорта)
    public float CaveInitialFill { get; set; } = 0.42f;
    public int CaveSmoothSteps { get; set; } = 5;
    public int CaveBirthLimit { get; set; } = 4;
    public int CaveDeathLimit { get; set; } = 3;
    public bool CavePreserveLargest { get; set; } = true;

    // Trails params (внутренние, без экспорта)
    public int TrailNodeCount { get; set; } = 8;
    public int TrailMinSpacing { get; set; } = 6;
    public int TrailWidth { get; set; } = 3;
    public bool TrailConnectAllComponents { get; set; } = true;
    public int TrailExtraEdges { get; set; } = 2;

    // WorldBiomes params
    [Export] public int WorldBiomeCount { get; set; } = 6; // сколько регионов биомов
    [Export] public int WorldWidth { get; set; } = 3;      // секции по X (временно переиспользуем сетку как холст)
    [Export] public int WorldHeight { get; set; } = 3;     // секции по Y
    [Export] public int BiomeMinSpacing { get; set; } = 12;
    [Export] public bool WorldBlendBorders { get; set; } = true;
    [Export(PropertyHint.Range, "0,1,0.01")] public float WorldOpenTarget { get; set; } = 0.38f; // целевая доля проходимых тайлов внутри мира
    [Export] public int CarveGlobalTrailsWidth { get; set; } = 4; // ширина глобальных троп (МСТ)
    [Export] public int BiomeHallRadius { get; set; } = 10;       // радиус «зала» вокруг центра биома
    [Export] public int RiverCount { get; set; } = 3;             // кол-во «рек/лавы» как открытых полос
    [Export] public int RiverWidth { get; set; } = 6;             // ширина полосы
    [Export(PropertyHint.Range, "0,0.2,0.005")] public float RiverNoiseFreq { get; set; } = 0.045f; // частота синус-шума
    [Export] public float RiverNoiseAmp { get; set; } = 8f;       // амплитуда синус-шума (в тайлах)
    [Export] public int LocalCorridorWidth { get; set; } = 3;     // ширина локальных связок «комнаты → центр биома»
    [Export] public bool RandomizeWorldParams { get; set; } = true; // лёгкая рандомизация параметров при каждой генерации
    [Export] public int RandomSeed { get; set; } = -1;              // -1 = случайный сид, иначе фиксированный

    // Псевдослучайный генератор
    private Random _random;
    private BiomePalette _biome;
    private NodeLocator _nodeLocator;

    // Удалено: локальный список комнат больше не используется в мультисекции (оставлено для совместимости, но не используется)
    // private readonly List<Rect2I> _rooms = new List<Rect2I>();

    // Тайлы для фонового заполнения
    private Vector2I _backgroundTile;

    private ContainerGenerator _containerGenerator;
    [Export] public PackedScene ContainerScene { get; set; }
    [Export] public int MaxContainersPerRoom { get; set; } = 1;
    [Export] public float ContainerDensity { get; set; } = 0.3f;

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

    private ResourceGenerator _resourceGenerator;
    [Export] public PackedScene ResourceNodeScene { get; set; }
    [Export] public int MaxResourcesPerRoom { get; set; } = 3;
    [Export] public float ResourceDensity { get; set; } = 0.5f;

    // Новые вспомогательные классы (подготовка к декомпозиции)
    private RoomPlacer _roomPlacer;
    private CorridorCarver _corridorCarver; // постепенный вынос карвинга
    private SectionConnector _sectionConnector; // постепенный вынос межсекционных связей
    private Decorator _decorator; // постепенный вынос декора
    private MultiSectionCoordinator _multiSectionCoordinator; // постепенный вынос мультисекции

    public override void _Ready()
    {
        // 📁 ИНИЦИАЛИЗИРУЕМ ФАЙЛОВОЕ ЛОГИРОВАНИЕ ПЕРВЫМ!
        Logger.InitializeFileLogging();
        Logger.Info("🚀 LevelGenerator starting up...");
        
        // Инициализируем генератор случайных чисел
        if (RandomSeed >= 0)
            _random = new Random(RandomSeed);
        else
            _random = new Random();

        // Инициализируем маску карты
        _mapMask = new TileType[MapWidth, MapHeight];

        // Поиск необходимых сцен компонентов, если они не указаны
        _nodeLocator = new NodeLocator();
        _nodeLocator.FindRequiredNodes(this, IsometricTileset, FloorsTileMap, WallsTileMap, YSortContainer);
        // Обновляем ссылки с найденными узлами
        IsometricTileset = _nodeLocator.IsometricTileset;
        FloorsTileMap = _nodeLocator.FloorsTileMap;
        WallsTileMap = _nodeLocator.WallsTileMap;
        YSortContainer = _nodeLocator.YSortContainer;

        // Logger.Debug($"TileMapLayer найдены: Floors: {FloorsTileMap?.Name}, Walls: {WallsTileMap?.Name}, YSort: {YSortContainer?.Name}", true); // СПАМ ОТКЛЮЧЕН

        // Уберём визуальные швы: используем padding в атласе (включено) и nearest-фильтр на слое
        if (FloorsTileMap != null)
        {
            FloorsTileMap.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        }

        // Генерируем мульти-секционную карту сразу с задержкой 0.5 секунды
        GetTree().CreateTimer(0.5).Timeout += () => {
            // Logger.Debug("Automatically generating multi-section map on startup", true); // СПАМ ОТКЛЮЧЕН
            GenerateMultiSectionMap();
        };

        // Инициализируем генератор ресурсов
        if (ResourceNodeScene != null)
        {
            _resourceGenerator = new ResourceGenerator(ResourceNodeScene, MaxResourcesPerRoom, ResourceDensity);
            // Logger.Debug("ResourceGenerator initialized", true); // СПАМ ОТКЛЮЧЕН
        }
        else
        {
            Logger.Error("ResourceNodeScene is not set in LevelGenerator!");
        }

        if (ContainerScene != null)
        {
            _containerGenerator = new ContainerGenerator(ContainerScene, MaxContainersPerRoom, ContainerDensity);
            // Logger.Debug("ContainerGenerator initialized", true); // СПАМ ОТКЛЮЧЕН
        }
        else
        {
            Logger.Error("LevelGenerator: ContainerScene is not set!");
        }

        // Подготовка вспомогательных модулей
        _roomPlacer = new RoomPlacer(
            _random,
            MapWidth,
            MapHeight,
            MinRoomSize,
            MaxRoomSize,
            MaxRooms,
            MinRoomDistance
        );

        // EntitySpawner удалён как неиспользуемый (ресурсы/контейнеры создаются напрямую)
        _corridorCarver = new CorridorCarver(_random);
        _sectionConnector = new SectionConnector(_random);
        _decorator = new Decorator(_random);
        _multiSectionCoordinator = new MultiSectionCoordinator(_random);
        // Используем исходные TileSet источники floors/walls из проекта без автогенерации
        _biome = new BiomePalette(_random, () => UseVariedWalls);

    }


    private void AddContainers()
        {
            // Собираем позиции всех размещенных ресурсов для избежания пересечений
            List<Vector2I> resourcePositions = GetResourcePositions();

        // Single-map mode removed: no-op
        // int containersPlaced = 0; // оставлено для возможной отладки, не используется

            // Logger.Debug($"Added {containersPlaced} containers to single-section map with biome {GetBiomeName(BiomeType)}", true); // СПАМ ОТКЛЮЧЕН
    }

    // Добавьте этот метод в класс для поддержки мульти-секций
    // Удалено: AddSectionContainers - заменено на GenerateWorldContainers

    // Вспомогательный метод для сбора позиций ресурсов
    private List<Vector2I> GetResourcePositions()
    {
        List<Vector2I> positions = new List<Vector2I>();

        // Получаем все узлы ResourceNode
        var resourceNodes = GetTree().GetNodesInGroup("ResourceNodes");

        // Преобразуем мировые координаты в координаты тайлов
        foreach (var node in resourceNodes)
        {
            if (node is Node2D resourceNode)
            {
                // Преобразуем мировые координаты в координаты тайлов
                Vector2I tilePos = WorldToMapTile(resourceNode.GlobalPosition);
                positions.Add(tilePos);
            }
        }

        return positions;
    }

    // Вспомогательный метод для сбора позиций ресурсов в секции
    private List<Vector2I> GetSectionResourcePositions(MapSection section)
    {
        List<Vector2I> positions = new List<Vector2I>();
        Vector2 worldOffset = section.WorldOffset;

        // Получаем все узлы ResourceNode
        var resourceNodes = GetTree().GetNodesInGroup("ResourceNodes");

        // Проверяем, какие из них находятся в текущей секции
        foreach (var node in resourceNodes)
        {
            if (node is Node2D resourceNode)
            {
                // Преобразуем мировые координаты в координаты тайлов
                Vector2I tilePos = WorldToMapTile(resourceNode.GlobalPosition);

                // Вычисляем локальные координаты в секции
                Vector2I localPos = new Vector2I(
                    tilePos.X - (int)worldOffset.X,
                    tilePos.Y - (int)worldOffset.Y
                );

                // Если координаты в пределах секции, добавляем
                if (localPos.X >= 0 && localPos.X < MapWidth &&
                    localPos.Y >= 0 && localPos.Y < MapHeight)
                {
                    positions.Add(localPos);
                }
            }
        }

        return positions;
    }

    // Вспомогательный метод для преобразования мировых координат в координаты тайлов
    private Vector2I WorldToMapTile(Vector2 worldPos)
    {
        // Размер тайла (должен соответствовать используемому в проекте)
        Vector2I tileSize = new Vector2I(32, 16);

        // Обратная формула преобразования для изометрии 2:1
        float tempX = worldPos.X / (tileSize.X / 2.0f);
        float tempY = worldPos.Y / (tileSize.Y / 2.0f);

        int tileX = (int)Math.Round((tempX + tempY) / 2.0f);
        int tileY = (int)Math.Round((tempY - tempX) / 2.0f);

        return new Vector2I(tileX, tileY);
    }

    // Обработка ввода для генерации нового уровня
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == MultiSectionGenerationKey)
            {
                GenerateMultiSectionMap();
            }
            // Переключение отображения координат по клавише C
            else if (keyEvent.Keycode == Key.C)
            {
                ToggleCoordinateLabels();
            }
        }
    }

    // Метод для генерации мульти-секционной карты
    public void GenerateMultiSectionMap()
    {
        try
        {
            // Logger.Debug("Starting generation of multi-section map", true); // СПАМ ОТКЛЮЧЕН

            // Включаем мульти-секционный режим
            UseMultiSectionMap = true;

            // Очищаем предыдущие секции
            _mapSections.Clear();

            // Очищаем карту
            ClearAllLayers();

            // Создаем секции в сетке через координатор
            _multiSectionCoordinator.CreateMapSections(
                GridWidth,
                GridHeight,
                MapWidth,
                MapHeight,
                SectionSpacing,
                MaxBiomeTypes,
                _mapSections,
                (biome) => GetBiomeName(biome)
            );

            // Генерируем все секции карты
            GenerateAllSections();

            // Соединяем секции (WorldBiomes используют собственные глобальные тропы/мосты)

            // Выбираем секцию для спавна игрока через координатор (получаем МИРОВЫЕ пиксели)
            _multiSectionCoordinator.SelectSpawnSection(_mapSections, out _currentSpawnPosition);

            // Logger.Debug($"Multi-section map generated with {_mapSections.Count} sections", true); // СПАМ ОТКЛЮЧЕН

            // Эмитим сигнал о завершении генерации мульти-секции
            EmitSignal("MultiSectionMapGenerated");
            
            // 🚀 ЭМИТИМ ГЛАВНЫЙ СИГНАЛ О ЗАВЕРШЕНИИ ГЕНЕРАЦИИ УРОВНЯ!
            // Logger.Debug($"ABOUT TO EMIT LevelGenerated signal from multi-section with spawn: {_currentSpawnPosition}", true); // СПАМ ОТКЛЮЧЕН
            
            // ПРОВЕРЯЕМ что спавн не нулевой!
            if (_currentSpawnPosition == Vector2.Zero)
            {
                Logger.Error("❌ CRITICAL: Multi-section spawn position is ZERO! Using emergency fallback!");
                _currentSpawnPosition = new Vector2(MapWidth * 16, MapHeight * 8);
            }
            
            // PlayerSpawner подхватит этот сигнал и создаст игрока в правильном месте
            EmitSignal(SignalName.LevelGenerated, _currentSpawnPosition);
            // Logger.Debug($"✅ LevelGenerated signal emitted from multi-section generation with spawn: {_currentSpawnPosition}", true); // СПАМ ОТКЛЮЧЕН
            
            // УБИРАЕМ старый HandlePlayerSpawn() - теперь PlayerSpawner сделает это через сигнал!
        }
        catch (Exception e)
        {
            Logger.Error($"Error generating multi-section map: {e.Message}\n{e.StackTrace}");
        }
    }

    // НОВОЕ: Метод для генерации всех секций
    private void GenerateAllSections()
    {
        // Logger.Debug("Generating all map sections", true); // СПАМ ОТКЛЮЧЕН

        // Проходим по всем секциям и генерируем для каждой уровень
        foreach (var section in _mapSections)
        {
            // Устанавливаем текущую секцию
            _currentSection = section;

            // Устанавливаем тип биома для генерации
            BiomeType = section.BiomeType;

            // WorldBiomes: каждая секция становится частью одного общего мира
            GenerateSectionLevelWorldBiomes(section);

            // Logger.Debug($"Generated section at ({section.GridX},{section.GridY}) with biome {GetBiomeName(section.BiomeType)}", false); // СПАМ ОТКЛЮЧЕН
        }
    }

    // Новый способ межсекционных проходов для CaveTrails: короткие органичные перемычки между ближайшими проходимыми плитками на границе секций
    private void ConnectSectionsCaveStyle()
    {
        // Горизонтальные соседи
        for (int y = 0; y < GridHeight; y++)
        {
            for (int x = 0; x < GridWidth - 1; x++)
            {
                var left = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                var right = _mapSections.Find(s => s.GridX == x + 1 && s.GridY == y);
                if (left == null || right == null) continue;
                CarveOrganicBridge(left, right, horizontal: true);
            }
        }
        // Вертикальные соседи
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight - 1; y++)
            {
                var top = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                var bottom = _mapSections.Find(s => s.GridX == x && s.GridY == y + 1);
                if (top == null || bottom == null) continue;
                CarveOrganicBridge(top, bottom, horizontal: false);
            }
        }
    }

    private void CarveOrganicBridge(MapSection a, MapSection b, bool horizontal)
    {
        // соберем кандидаты вдоль общей границы: ближайшие к проходимым клеткам секций
        var candidatesA = new System.Collections.Generic.List<Vector2I>();
        var candidatesB = new System.Collections.Generic.List<Vector2I>();
        if (horizontal)
        {
            int ax = MapWidth - 2; // внутренняя колонка у правой границы левой секции
            int bx = 1;           // внутренняя колонка у левой границы правой секции
            for (int ty = 2; ty < MapHeight - 2; ty++)
            {
                if (a.SectionMask[ax, ty] == TileType.Room) candidatesA.Add(new Vector2I((int)a.WorldOffset.X + ax, (int)a.WorldOffset.Y + ty));
                if (b.SectionMask[bx, ty] == TileType.Room) candidatesB.Add(new Vector2I((int)b.WorldOffset.X + bx, (int)b.WorldOffset.Y + ty));
            }
        }
        else
        {
            int ay = MapHeight - 2; // внутренняя строка у нижней границы верхней секции
            int by = 1;             // внутренняя строка у верхней границы нижней секции
            for (int tx = 2; tx < MapWidth - 2; tx++)
            {
                if (a.SectionMask[tx, ay] == TileType.Room) candidatesA.Add(new Vector2I((int)a.WorldOffset.X + tx, (int)a.WorldOffset.Y + ay));
                if (b.SectionMask[tx, by] == TileType.Room) candidatesB.Add(new Vector2I((int)b.WorldOffset.X + tx, (int)b.WorldOffset.Y + by));
            }
        }
        if (candidatesA.Count == 0 || candidatesB.Count == 0) return;

        // найдём ближайшую пару
        int best = int.MaxValue; Vector2I pa = default, pb = default;
        foreach (var va in candidatesA)
        foreach (var vb in candidatesB)
        {
            int dx = (int)(va.X - vb.X); int dy = (int)(va.Y - vb.Y);
            int d2 = dx*dx + dy*dy; if (d2 < best) { best = d2; pa = va; pb = vb; }
        }

        // проложим короткий A* путь по мировым тайлам, где допускаем прорезание через фон/стены
        var path = WorldPathfinder.FindWorldPathOrganic(pa, pb);
        if (path == null) return;
        var floorTileA = _biome.GetFloorTileForBiome(a.BiomeType);
        var floorTileB = _biome.GetFloorTileForBiome(b.BiomeType);
        var floorTile = floorTileA; // можно смешивать, пока возьмём левую/верхнюю секцию
        foreach (var wp in path)
        {
            FloorsTileMap.SetCell(wp, FloorsSourceID, floorTile);
            WallsTileMap.EraseCell(wp);
            // обновим локальные маски соответствующих секций
            foreach (var s in new[]{a,b})
            {
                int lx = wp.X - (int)s.WorldOffset.X; int ly = wp.Y - (int)s.WorldOffset.Y;
                if (lx >= 0 && lx < MapWidth && ly >= 0 && ly < MapHeight)
                    s.SectionMask[lx, ly] = TileType.Room;
            }
        }
    }

    // Поиск пути в мировых тайлах с разрешением проходить через всё, кроме чужих «комнат», чтобы мост был органичным
    // Перенесено в WorldPathfinder.FindWorldPathOrganic

    // НОВОЕ: Метод для генерации уровня для конкретной секции - использует актуальный WorldBiomes генератор

    // Актуальный генератор: WorldBiomes

    // Черновой каркас WorldBiomes: одна большая карта на сетке секций; размещаем центры биомов и для каждого региона вызываем Cave+Trails с его параметрами
    private void GenerateSectionLevelWorldBiomes(MapSection section)
    {
        // В этом режиме реальная генерация идёт из (0,0) секции, остальные секции пропускают отрисовку
        if (!(section.GridX == 0 && section.GridY == 0))
        {
            // только очистим маску/слои на всякий случай
            ResetSectionMask(section);
            return;
        }

        // Новый делегат генерации «большого мира»: перенос тяжёлой логики во внешний класс
        try
        {
            var generator = new WorldBiomesGenerator(_random, _biome, FloorsTileMap, WallsTileMap, FloorsSourceID, WallsSourceID);
            LevelGenerator.TileType[,] wm;
            int[,] wb;
            generator.GenerateWorld(
                MapWidth, MapHeight, WorldWidth, WorldHeight, MaxBiomeTypes,
                CaveInitialFill, CaveSmoothSteps, CaveBirthLimit, CaveDeathLimit, WorldOpenTarget,
                CarveGlobalTrailsWidth, BiomeHallRadius, RiverCount, RiverWidth, RiverNoiseFreq, RiverNoiseAmp,
                LocalCorridorWidth, RandomizeWorldParams, WorldBlendBorders,
                out wm, out wb,
                (tl, tlW, trW, blW, brW) =>
                {
                    int wx = System.Math.Max(1, WorldWidth) * MapWidth;
                    int wy = System.Math.Max(1, WorldHeight) * MapHeight;
                    Logger.Info($"🗺️ КАРТА: {wx}x{wy}");
                    UIManager.SetMapCorners(
                        tl,
                        new Vector2I(wx - 1, 0),
                        new Vector2I(0, wy - 1),
                        new Vector2I(wx - 1, wy - 1),
                        tlW, trW, blW, brW
                    );
                }
            );

            int worldTilesX = System.Math.Max(1, WorldWidth) * MapWidth;
            int worldTilesY = System.Math.Max(1, WorldHeight) * MapHeight;

            // Ресурсы временно отключены для чистого визуала биомов
            // WorldResourcePlacer.GenerateResources(
            //     new WorldResourcePlacer.Context
            //     {
            //         ResourceNodeScene = ResourceNodeScene,
            //         YSortContainer = YSortContainer,
            //         Random = _random,
            //         MapTileToIsometricWorld = MapTileToIsometricWorld
            //     },
            //     wm, wb, worldTilesX, worldTilesY);

            // Контейнеры тоже временно отключены
            // WorldContainerPlacer.GenerateContainers(
            //     new WorldContainerPlacer.Context
            //     {
            //         ContainerScene = ContainerScene,
            //         YSortContainer = YSortContainer,
            //         Random = _random,
            //         MapTileToIsometricWorld = MapTileToIsometricWorld
            //     },
            //     wm, wb, worldTilesX, worldTilesY);

            _levelGenerated = true;

            // Создание спавн‑поинтов и игрока
            int[,] compId;
            int[] compSizes;
            int centerCompId;
            SpawnPlanner.BuildConnectivityComponents(wm, worldTilesX, worldTilesY, out compId, out compSizes, out centerCompId);
            CreateCornerSpawnPointsAndPlayer(wm, worldTilesX, worldTilesY, compId, compSizes, centerCompId);

            // Завершаем метод, не исполняя старый монолитный код ниже
            return;
        }
        catch (Exception ex)
        {
            Logger.Error($"WorldBiomes generation failed in delegate: {ex.Message}. Falling back to legacy path.");
        }
    }

    // 🚀 РЕВОЛЮЦИОННАЯ СИСТЕМА: Создание SpawnPoint узлов в углах карты!
    private void CreateCornerSpawnPointsAndPlayer(
        TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int[] componentSizes,
        int centerComponentId)
    {
        // Logger.Debug("🚀 Creating BADASS corner spawn point system!", true); // СПАМ ОТКЛЮЧЕН
        
        // Создаем 4 SpawnPoint узла в углах карты
        var spawnPoints = new List<(string name, Vector2 position, bool isValid)>();
        
        // Определяем 4 угловые зоны с ПРАВИЛЬНОЙ логикой
        // ⚠️ КРИТИЧНО: borderOffset должен быть БОЛЬШЕ чем WALL_THICKNESS!
        const int WALL_THICKNESS = 1; // ИСПРАВЛЕНО: использовать то же значение что в AddBiomeBasedBorderWalls!
        int borderOffset = WALL_THICKNESS + 5; // ОТСТУП ОТ OUTER WALLS + запас безопасности!
        int cornerSize = Math.Max(15, Math.Min(worldTilesX, worldTilesY) / 4); // Больше зона поиска
        
        // Logger.Debug($"🛡️ SAFE SPAWN ZONES: borderOffset={borderOffset} (walls+5), cornerSize={cornerSize}", true); // СПАМ ОТКЛЮЧЕН
        
        var cornerDefs = new List<(string name, int startX, int startY, int endX, int endY)>
        {
            ("TopLeft", borderOffset, borderOffset, borderOffset + cornerSize, borderOffset + cornerSize),
            ("TopRight", worldTilesX - borderOffset - cornerSize, borderOffset, worldTilesX - borderOffset, borderOffset + cornerSize),
            ("BottomLeft", borderOffset, worldTilesY - borderOffset - cornerSize, borderOffset + cornerSize, worldTilesY - borderOffset),
            ("BottomRight", worldTilesX - borderOffset - cornerSize, worldTilesY - borderOffset - cornerSize, worldTilesX - borderOffset, worldTilesY - borderOffset)
        };
        
        Vector2I? bestSpawn = null;
        string bestCornerName = "";
        
        // Собираем ВСЕ валидные углы для РАНДОМНОГО выбора! 🎲
        var validSpawns = new List<(string name, Vector2I tilePos, Vector2 worldPos)>();
        
        foreach (var corner in cornerDefs)
        {
            // Logger.Debug($"🔍 Searching for spawn in corner: {corner.name} ({corner.startX},{corner.startY}) to ({corner.endX},{corner.endY})", false); // СПАМ ОТКЛЮЧЕН
            
            Vector2I? cornerSpawn = SpawnPlanner.FindBestSpawnInCorner(
                worldMask,
                corner.startX, corner.startY,
                corner.endX, corner.endY,
                worldTilesX, worldTilesY,
                componentId,
                centerComponentId,
                componentSizes
            );
            
            if (cornerSpawn.HasValue)
            {
                Vector2 worldPos = MapTileToIsometricWorld(cornerSpawn.Value);
                spawnPoints.Add((corner.name, worldPos, true));
                validSpawns.Add((corner.name, cornerSpawn.Value, worldPos));
                
                // Logger.Debug($"✅ Valid spawn found in {corner.name}: tile ({cornerSpawn.Value.X}, {cornerSpawn.Value.Y}) -> world {worldPos}", true); // СПАМ ОТКЛЮЧЕН
            }
            else
            {
                // Создаем резервный спавн в центре угловой зоны
                int centerX = (corner.startX + corner.endX) / 2;
                int centerY = (corner.startY + corner.endY) / 2;
                Vector2 fallbackPos = MapTileToIsometricWorld(new Vector2I(centerX, centerY));
                spawnPoints.Add((corner.name, fallbackPos, false));
                
                // Logger.Debug($"❌ No valid spawn in {corner.name}, created fallback at ({centerX}, {centerY}) -> {fallbackPos}", false); // СПАМ ОТКЛЮЧЕН
            }
        }
        
        // 🎲 РАНДОМНО выбираем один из ВАЛИДНЫХ углов!
        if (validSpawns.Count > 0)
        {
            // Используем уже инициализированный генератор случайных чисел
            int randomIndex = _random.Next(validSpawns.Count);
            var selectedSpawn = validSpawns[randomIndex];
            
            bestSpawn = selectedSpawn.tilePos;
            bestCornerName = selectedSpawn.name;
            
            // Убираем детальный debug для скорости
            // Logger.Debug($"🎲 RANDOM SELECTION PROCESS:", true);
            // Logger.Debug($"  Ticks: {ticks}", true);
            // Logger.Debug($"  Seed: {seed}", true);
            // Logger.Debug($"  Random index: {randomIndex} (from 0-{validSpawns.Count-1})", true);
            // Logger.Debug($"🎯 SELECTED CORNER: {bestCornerName} at {selectedSpawn.worldPos}", true); // СПАМ ОТКЛЮЧЕН
        }
        else
        {
            Logger.Error("🚨 NO VALID SPAWN CORNERS FOUND! This should not happen!");
        }
        
        // Создаем физические SpawnPoint узлы в сцене
        SpawnPlacement.CreateSpawnPointNodes(
            new SpawnPlacement.Context
            {
                Owner = this,
                YSortContainer = YSortContainer,
                PlayerScene = PlayerScene,
                MapTileToIsometricWorld = MapTileToIsometricWorld
            },
            spawnPoints);
        
        // Создаем игрока в ЛУЧШЕМ найденном углу
        if (bestSpawn.HasValue)
        {
            Vector2 finalSpawnPos = MapTileToIsometricWorld(bestSpawn.Value);
            Logger.Info($"🎯 ИГРОК: {bestCornerName} {bestSpawn.Value} -> {finalSpawnPos}");
            SpawnPlacement.CreatePlayerAtPosition(
                new SpawnPlacement.Context
                {
                    Owner = this,
                    YSortContainer = YSortContainer,
                    PlayerScene = PlayerScene,
                    MapTileToIsometricWorld = MapTileToIsometricWorld
                },
                finalSpawnPos);
        }
        else
        {
            // 🚨 АВАРИЙНАЯ СИСТЕМА: ищем ЛЮБУЮ безопасную позицию на всей карте!
            Logger.Error("🚨 No valid corner spawns found! Activating EMERGENCY spawn system!");
            Vector2I? emergencySpawn = FindEmergencySpawnPosition(worldMask, worldTilesX, worldTilesY, componentId, centerComponentId);
            
            if (emergencySpawn.HasValue)
            {
                Vector2 emergencyPos = MapTileToIsometricWorld(emergencySpawn.Value);
                Logger.Info($"🆘 EMERGENCY spawn found at tile {emergencySpawn.Value} -> world {emergencyPos}");
                SpawnPlacement.CreatePlayerAtPosition(
                    new SpawnPlacement.Context
                    {
                        Owner = this,
                        YSortContainer = YSortContainer,
                        PlayerScene = PlayerScene,
                        MapTileToIsometricWorld = MapTileToIsometricWorld
                    },
                    emergencyPos);
            }
            else
            {
                // Последняя инстанция - принудительный спавн в центре с очисткой зоны
                Vector2 centerPos = ForceCreateSafeSpawnInCenter(worldMask, worldTilesX, worldTilesY);
                Logger.Error($"🔥 FORCED spawn in center at {centerPos} - cleared area for safety!");
                SpawnPlacement.CreatePlayerAtPosition(
                    new SpawnPlacement.Context
                    {
                        Owner = this,
                        YSortContainer = YSortContainer,
                        PlayerScene = PlayerScene,
                        MapTileToIsometricWorld = MapTileToIsometricWorld
                    },
                    centerPos);
            }
        }
    }
    
    // 🔥 ЖЕЛЕЗОБЕТОННАЯ система поиска лучшей точки спавна! 
    private Vector2I? FindBestSpawnInCorner(
        TileType[,] worldMask,
        int startX,
        int startY,
        int endX,
        int endY,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int centerComponentId,
        int[] componentSizes)
    {
        // Logger.Debug($"💪 HARDCORE spawn search in corner ({startX},{startY}) to ({endX},{endY})", true); // СПАМ ОТКЛЮЧЕН
        
        var validPositions = new List<(Vector2I pos, int score)>();
        
        // Ищем от краев угла к центру (приоритет углам)
        for (int radius = 0; radius < Math.Max(endX - startX, endY - startY); radius++)
        {
            for (int x = startX; x < endX; x++)
            {
                for (int y = startY; y < endY; y++)
                {
                    // Проверяем только клетки на текущем радиусе
                    int distanceFromEdge = Math.Min(
                        Math.Min(x - startX, endX - 1 - x),
                        Math.Min(y - startY, endY - 1 - y)
                    );
                    
                    if (distanceFromEdge != radius) continue;
                    
                    // Проверяем границы
                    if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) continue;
                    
                    // 🛡️ ЖЕЛЕЗОБЕТОННАЯ ПРОВЕРКА БЕЗОПАСНОСТИ (без BFS — используем предвычисленные компоненты)!
                    Vector2I candidate = new Vector2I(x, y);
                    int safetyScore = EvaluateSpawnSafety(worldMask, candidate, worldTilesX, worldTilesY, componentId, centerComponentId, componentSizes);
                    
                    if (safetyScore > 0)
                    {
                        validPositions.Add((candidate, safetyScore));
                        // Logger.Debug($"🎯 VALID SPAWN: ({x}, {y}) score={safetyScore}", false); // СПАМ ОТКЛЮЧЕН
                    }
                }
            }
        }
        
        // Возвращаем позицию с наивысшим рейтингом безопасности
        if (validPositions.Count > 0)
        {
            var bestSpawn = validPositions.OrderByDescending(p => p.score).First();
            Logger.Info($"🏆 SPAWN: {bestSpawn.pos} score={bestSpawn.score}");
            return bestSpawn.pos;
        }
        
        // Logger.Debug("❌ No safe spawn found in this corner!", true); // СПАМ ОТКЛЮЧЕН
        return null; // Не нашли подходящую точку
    }
    
    // 🛡️ ЖЕЛЕЗОБЕТОННАЯ система оценки безопасности позиции спавна!
    private int EvaluateSpawnSafety(
        TileType[,] worldMask,
        Vector2I position,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int centerComponentId,
        int[] componentSizes)
    {
        int x = position.X;
        int y = position.Y;
        int safetyScore = 0;
        
        // Logger.Debug($"🔍 Evaluating spawn safety at ({x}, {y})", false); // СПАМ ОТКЛЮЧЕН
        
        // 1. ОСНОВНАЯ ПРОВЕРКА: позиция должна быть проходимой (ЗЕМЛЯ!)
        if (worldMask[x, y] != TileType.Room)
        {
            // Logger.Debug($"❌ Position ({x}, {y}) is NOT walkable (type: {worldMask[x, y]})", false); // СПАМ ОТКЛЮЧЕН
            return 0; // DISQUALIFIED!
        }
        safetyScore += 10; // Базовые очки за проходимость
        
        // 2. ПРОВЕРКА ОКРУЖЕНИЯ: убеждаемся что вокруг нет стен (3x3 зона)
        int walkableNeighbors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < worldTilesX && ny >= 0 && ny < worldTilesY)
                {
                    if (worldMask[nx, ny] == TileType.Room)
                    {
                        walkableNeighbors++;
                    }
                }
            }
        }
        
        if (walkableNeighbors < 5) // Минимум 5 из 9 клеток должны быть проходимыми
        {
            // Logger.Debug($"❌ Position ({x}, {y}) has only {walkableNeighbors}/9 walkable neighbors - too crowded!", false); // СПАМ ОТКЛЮЧЕН
            return 0; // DISQUALIFIED!
        }
        safetyScore += walkableNeighbors * 2; // Очки за свободное пространство
        
        // 3. РАСШИРЕННАЯ ПРОВЕРКА: большая область 5x5 должна быть относительно свободной
        int wideAreaWalkable = 0;
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < worldTilesX && ny >= 0 && ny < worldTilesY)
                {
                    if (worldMask[nx, ny] == TileType.Room)
                    {
                        wideAreaWalkable++;
                    }
                }
            }
        }
        safetyScore += wideAreaWalkable; // Очки за просторность
        
        // 4. ПРОВЕРКА СВЯЗНОСТИ: принадлежность той же компоненте, что и центр
        if (componentId[x, y] <= 0 || componentId[x, y] != centerComponentId)
        {
            return 0;
        }
        safetyScore += 50; // МЕГА-очки за связность с центром!
        
        // 5. БОНУСНАЯ ПРОВЕРКА: расстояние от краев карты (чем дальше от стен - тем лучше)
        int distanceFromEdges = Math.Min(
            Math.Min(x, worldTilesX - 1 - x),
            Math.Min(y, worldTilesY - 1 - y)
        );
        safetyScore += distanceFromEdges * 3; // Очки за удаленность от краев
        
        // 6. ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА: проверяем связность с несколькими ключевыми точками
        var testPoints = new List<Vector2I>
        {
            new Vector2I(worldTilesX / 4, worldTilesY / 4),
            new Vector2I(3 * worldTilesX / 4, worldTilesY / 4),
            new Vector2I(worldTilesX / 4, 3 * worldTilesY / 4),
            new Vector2I(3 * worldTilesX / 4, 3 * worldTilesY / 4)
        };
        int reachableQuadrants = 0;
        foreach (var tp in testPoints)
        {
            int cx = Math.Clamp(tp.X, 0, worldTilesX - 1);
            int cy = Math.Clamp(tp.Y, 0, worldTilesY - 1);
            if (worldMask[cx, cy] == TileType.Room && componentId[cx, cy] == centerComponentId)
            {
                reachableQuadrants++;
            }
        }
        safetyScore += reachableQuadrants * 15; // Большие очки за доступность разных зон карты
        
        // Выводим детальную оценку только для лучших позиций (высокий score)
        if (safetyScore > 80)
        {
            // Logger.Debug($"🎯 HIGH SCORE Position ({x}, {y}): " +
                        // $"walkable={walkableNeighbors}/9, wide={wideAreaWalkable}/25, " +
                        // $"edgeDist={distanceFromEdges}, reachable={reachableQuadrants}/4, " + // СПАМ ОТКЛЮЧЕН
                        // $"TOTAL SCORE={safetyScore}", false); // СПАМ ОТКЛЮЧЕН
        }
        
        return safetyScore;
    }
    
    // 🆘 АВАРИЙНАЯ система поиска ЛЮБОЙ безопасной позиции на всей карте
    private Vector2I? FindEmergencySpawnPosition(
        TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY,
        int[,] componentId,
        int centerComponentId)
    {
        // Logger.Debug("🆘 EMERGENCY SPAWN SEARCH across entire map!", true); // СПАМ ОТКЛЮЧЕН
        
        // Начинаем поиск от центра карты и идем спиралью наружу
        int centerX = worldTilesX / 2;
        int centerY = worldTilesY / 2;
        
        var bestCandidates = new List<(Vector2I pos, int score)>();
        
        // Поиск спиралью от центра
        int maxRadius = Math.Max(worldTilesX, worldTilesY) / 2;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int y = centerY - radius; y <= centerY + radius; y++)
                {
                    // Проверяем только клетки на границе текущего радиуса
                    if (Math.Abs(x - centerX) != radius && Math.Abs(y - centerY) != radius)
                        continue;
                    
                    // Проверяем границы карты
                    if (x < 3 || x >= worldTilesX - 3 || y < 3 || y >= worldTilesY - 3)
                        continue;
                    
                    Vector2I candidate = new Vector2I(x, y);
                    int safetyScore = EvaluateSpawnSafety(worldMask, candidate, worldTilesX, worldTilesY, componentId, centerComponentId, null);
                    
                    if (safetyScore > 30) // Пониженные требования для аварийного режима
                    {
                        bestCandidates.Add((candidate, safetyScore));
                        // Logger.Debug($"🆘 Emergency candidate: ({x}, {y}) score={safetyScore}", false); // СПАМ ОТКЛЮЧЕН
                    }
                }
            }
            
            // Если нашли достаточно кандидатов, выбираем лучшего
            if (bestCandidates.Count >= 3)
                break;
        }
        
        // Возвращаем лучшего кандидата или null
        if (bestCandidates.Count > 0)
        {
            var bestEmergency = bestCandidates.OrderByDescending(c => c.score).First();
            // Logger.Debug($"🆘 EMERGENCY SPAWN SELECTED: {bestEmergency.pos} score={bestEmergency.score}", true); // СПАМ ОТКЛЮЧЕН
            return bestEmergency.pos;
        }
        
        Logger.Error("🆘 NO EMERGENCY SPAWN FOUND! Map might be completely blocked!");
        return null;
    }

    // Строит компоненты связности по проходимым тайлам (TileType.Room).
    // Возвращает:
    // - componentId[x,y] = идентификатор компоненты (>=1) или 0 для непроходимых клеток
    // - componentSizes[compId] = размер соответствующей компоненты
    // - centerComponentId = id компоненты, к которой принадлежит ближайшая проходимая клетка к центру карты
    private void BuildConnectivityComponents(
        TileType[,] worldMask,
        int worldTilesX,
        int worldTilesY,
        out int[,] componentId,
        out int[] componentSizes,
        out int centerComponentId)
    {
        componentId = new int[worldTilesX, worldTilesY];
        var sizes = new List<int> { 0 }; // индекс 0 зарезервирован
        int currentId = 0;

        var directions = new Vector2I[]
        {
            new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1)
        };

        var queue = new Queue<Vector2I>();

        for (int y = 0; y < worldTilesY; y++)
        {
            for (int x = 0; x < worldTilesX; x++)
            {
                if (worldMask[x, y] != TileType.Room || componentId[x, y] != 0) continue;
                currentId++;
                int count = 0;
                componentId[x, y] = currentId;
                queue.Clear();
                queue.Enqueue(new Vector2I(x, y));

                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    count++;
                    foreach (var d in directions)
                    {
                        int nx = p.X + d.X, ny = p.Y + d.Y;
                        if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) continue;
                        if (worldMask[nx, ny] != TileType.Room) continue;
                        if (componentId[nx, ny] != 0) continue;
                        componentId[nx, ny] = currentId;
                        queue.Enqueue(new Vector2I(nx, ny));
                    }
                }

                sizes.Add(count);
            }
        }

        componentSizes = sizes.ToArray();

        // Определяем компоненту центра (ближайшую проходимую к центру)
        Vector2I center = new Vector2I(worldTilesX / 2, worldTilesY / 2);
        centerComponentId = 0;
        if (worldTilesX > 0 && worldTilesY > 0)
        {
            if (center.X >= 0 && center.X < worldTilesX && center.Y >= 0 && center.Y < worldTilesY &&
                componentId[center.X, center.Y] != 0)
            {
                centerComponentId = componentId[center.X, center.Y];
            }
            else
            {
                // Найти ближайшую клетку комнаты к центру (ограничимся разумным радиусом)
                int maxR = Math.Max(worldTilesX, worldTilesY);
                for (int r = 1; r <= maxR && centerComponentId == 0; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                            int cx = center.X + dx, cy = center.Y + dy;
                            if (cx < 0 || cx >= worldTilesX || cy < 0 || cy >= worldTilesY) continue;
                            if (componentId[cx, cy] != 0) { centerComponentId = componentId[cx, cy]; break; }
                        }
                        if (centerComponentId != 0) break;
                    }
                }
            }
        }
    }
    
    // 🔥 ПРИНУДИТЕЛЬНОЕ создание безопасного спавна в центре с очисткой области
    private Vector2 ForceCreateSafeSpawnInCenter(TileType[,] worldMask, int worldTilesX, int worldTilesY)
    {
        Logger.Error("🔥 FORCING safe spawn in center - CLEARING AREA!");
        
        int centerX = worldTilesX / 2;
        int centerY = worldTilesY / 2;
        
        // Принудительно очищаем область 7x7 в центре карты
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dy = -3; dy <= 3; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                
                if (x >= 0 && x < worldTilesX && y >= 0 && y < worldTilesY)
                {
                    // Принудительно делаем все клетки проходимыми
                    worldMask[x, y] = TileType.Room;
                    
                    // Убираем стены из TileMap
                    if (WallsTileMap != null)
                    {
                        WallsTileMap.EraseCell(new Vector2I(x, y));
                    }
                    
                    // Устанавливаем тайл пола
                    if (FloorsTileMap != null)
                    {
                        Vector2I grassTile = new Vector2I(0, 0); // Стандартная трава
                        FloorsTileMap.SetCell(new Vector2I(x, y), FloorsSourceID, grassTile);
                    }
                }
            }
        }
        
        Vector2 forcedSpawn = MapTileToIsometricWorld(new Vector2I(centerX, centerY));
        Logger.Error($"🔥 FORCED SPAWN created at center: tile ({centerX}, {centerY}) -> world {forcedSpawn}");
        
        return forcedSpawn;
    }
    
    
    
    // Удалено: GenerateVirtualRoomsFromWorldMask - заменено на прямую генерацию по мировой маске

    // Внешние стены вынесены в BorderWallsBuilder
    
    // ===== 🎯 МЕТОД ДЛЯ СОЗДАНИЯ КООРДИНАТНЫХ МЕТОК =====
    private void CreateCoordinateLabel(Vector2I tilePos, string text)
    {
        HudDebugHelpers.CreateCoordinateLabel(this, YSortContainer, MapTileToIsometricWorld, ShowCoordinateLabels, tilePos, text);
    }
    
    // Метод для очистки всех координатных меток
    private void ClearCoordinateLabels()
    {
        HudDebugHelpers.ClearCoordinateLabels(this, YSortContainer);
    }
    
    // Метод для переключения отображения координат
    public void ToggleCoordinateLabels()
    {
        ShowCoordinateLabels = !ShowCoordinateLabels;
        if (!ShowCoordinateLabels)
        {
            ClearCoordinateLabels();
        }
        // Logger.Debug($"Coordinate labels visibility: {ShowCoordinateLabels}", true); // СПАМ ОТКЛЮЧЕН
    }
    

    
    // Находит ближайший биом для НАРУЖНОЙ стены (проецируется к краю игровой области)
    private int GetNearestBiomeForOuterWall(int[,] worldBiome, int wallX, int wallY, int worldTilesX, int worldTilesY)
    {
        // Находим ближайшую точку на границе игровой области
        int nearestX = Math.Max(0, Math.Min(worldTilesX - 1, wallX));
        int nearestY = Math.Max(0, Math.Min(worldTilesY - 1, wallY));
        
        // Возвращаем биом этой ближайшей точки
        int foundBiome = worldBiome[nearestX, nearestY];
        // Logger.Debug($"Outer wall at ({wallX}, {wallY}) -> nearest map point ({nearestX}, {nearestY}) biome {foundBiome}", false); // СПАМ!
        return foundBiome;
    }
    
    // Находит ближайший биом для стены (СТАРЫЙ метод, оставляем для совместимости)
    private int GetNearestBiomeForWall(int[,] worldBiome, int wallX, int wallY, int worldTilesX, int worldTilesY, int wallThickness)
    {
        // Ищем ближайшую НЕ-стеновую клетку внутри карты
        for (int radius = 1; radius <= wallThickness + 5; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Проверяем только клетки на текущем радиусе
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius && radius > 1)
                        continue;
                    
                    int checkX = wallX + dx;
                    int checkY = wallY + dy;
                    
                    // Проверяем границы
                    if (checkX < 0 || checkX >= worldTilesX || checkY < 0 || checkY >= worldTilesY)
                        continue;
                    
                    // Проверяем, что это НЕ стена (достаточно далеко от края)
                    int distanceFromEdge = Math.Min(
                        Math.Min(checkX, worldTilesX - 1 - checkX),
                        Math.Min(checkY, worldTilesY - 1 - checkY)
                    );
                    
                    if (distanceFromEdge >= wallThickness)
                    {
                        // Нашли биом внутри карты!
                        int foundBiome = worldBiome[checkX, checkY];
                        // Logger.Debug($"Wall at ({wallX}, {wallY}) -> nearest biome {foundBiome} at ({checkX}, {checkY})", false); // СПАМ ОТКЛЮЧЕН
                        return foundBiome;
                    }
                }
            }
        }
        
        // Если не нашли, используем биом по умолчанию (Grassland)
        // Logger.Debug($"Wall at ({wallX}, {wallY}) -> fallback to default biome 0 (Grassland)", false); // СПАМ ОТКЛЮЧЕН
        return 0;
    }
    
    // Устаревшие методы поиска спавна в углах удалены (переведены на SpawnPlanner и CreateCornerSpawnPointsAndPlayer)
    
    // Перенесено в SpawnPlanner.IsPathToTargetExists
    
    // Перенесено в SpawnPlanner.FindWorldSpawnPosition


    // Выбирает тип ресурса на основе биома (ТОЛЬКО реально существующие типы: Metal, Crystal, Organic)
    private ResourceType SelectResourceTypeForBiome(int biome)
    {
        // ВНИМАНИЕ: У нас есть только 3 типа ресурсов в проекте: Metal, Crystal, Organic
        // Energy и Composite отсутствуют в scenes/resources/items/
        switch (biome)
        {
            case 0: // Grassland - сбалансированно
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Metal;
                    if (rand < 0.7f) return ResourceType.Organic;
                    return ResourceType.Crystal;
                }
            case 1: // Forest - больше органики
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.6f) return ResourceType.Organic;
                    if (rand < 0.8f) return ResourceType.Metal;
                    return ResourceType.Crystal;
                }
            case 2: // Desert - металлы и кристаллы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.5f) return ResourceType.Metal;
                    if (rand < 0.8f) return ResourceType.Crystal;
                    return ResourceType.Organic; // Заменяем Energy на Organic
                }
            case 3: // Ice - кристаллы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.5f) return ResourceType.Crystal;
                    if (rand < 0.8f) return ResourceType.Metal;
                    return ResourceType.Organic; // Заменяем Energy на Organic
                }
            case 4: // Techno - металлы и кристаллы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Metal;
                    if (rand < 0.7f) return ResourceType.Crystal;
                    return ResourceType.Organic; // Заменяем Energy/Composite на Organic
                }
            case 5: // Anomal - редкие кристаллы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Crystal;
                    if (rand < 0.7f) return ResourceType.Metal;
                    return ResourceType.Organic; // Заменяем Composite/Energy на Organic
                }
            case 6: // Lava Springs - металлы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.5f) return ResourceType.Metal;
                    if (rand < 0.8f) return ResourceType.Crystal;
                    return ResourceType.Organic; // Заменяем Energy/Composite на Organic
                }
            default:
                return ResourceType.Metal;
        }
    }

    private void PreserveLargestWalkableComponent(MapSection section)
    {
        SectionConnectivityTools.PreserveLargestWalkableComponent(section.SectionMask, MapWidth, MapHeight);
    }

    private void ConnectAllRoomComponentsToTrails(MapSection section)
    {
        Vector2I worldOffset = new Vector2I((int)section.WorldOffset.X, (int)section.WorldOffset.Y);
        SectionConnectivityTools.ConnectAllRoomComponentsToTrails(
            section.SectionMask,
            MapWidth,
            MapHeight,
            p => section.SectionMask[p.X, p.Y] == TileType.Corridor,
            (from, target) => WorldPathfinder.FindWorldPathOrganic(worldOffset + from, worldOffset + target),
            wp => {
                FloorsTileMap.SetCell(wp, FloorsSourceID, _biome.GetFloorTileForBiome(section.BiomeType));
                WallsTileMap.EraseCell(wp);
                int lx = wp.X - worldOffset.X; int ly = wp.Y - worldOffset.Y;
                if (lx >= 0 && lx < MapWidth && ly >= 0 && ly < MapHeight)
                {
                    section.SectionMask[lx, ly] = TileType.Corridor;
                }
            }
        );
    }
    private System.Collections.Generic.List<Vector2I> PickTrailNodes(MapSection section, int count, int minSpacing)
    {
        return SectionConnectivityTools.PickTrailNodes(_random, section.SectionMask, MapWidth, MapHeight, count, minSpacing);
    }

    private void CarveTrailsBetweenNodes(MapSection section, System.Collections.Generic.List<Vector2I> nodes, int width)
    {
        Vector2I worldOffset = new Vector2I((int)section.WorldOffset.X, (int)section.WorldOffset.Y);
        SectionConnectivityTools.CarveTrailsBetweenNodes(
            section.SectionMask,
            MapWidth,
            MapHeight,
            nodes,
            width,
            local => {
                FloorsTileMap.SetCell(worldOffset + local, FloorsSourceID, _biome.GetFloorTileForBiome(section.BiomeType));
                WallsTileMap.EraseCell(worldOffset + local);
            }
        );
    }

    // A* по проходимым (Room) клеткам
    // Перенесено в SectionConnectivityTools.FindPathOverRooms

    // Удалено: AddSectionResources - заменено на GenerateWorldResources

    // Односекционный режим удалён

    // НОВОЕ: Метод для соединения соседних секций проходами
    // МОДИФИКАЦИЯ метода для соединения соседних секций
    private void ConnectAdjacentSections()
    {
        var ctx = new SectionConnectorOrchestrator.Context
        {
            MapWidth = MapWidth,
            MapHeight = MapHeight,
            GridWidth = GridWidth,
            GridHeight = GridHeight,
            ConnectorWidth = ConnectorWidth,
            SectionSpacing = SectionSpacing,
            FloorsTileMap = FloorsTileMap,
            WallsTileMap = WallsTileMap,
            FloorsSourceID = FloorsSourceID,
            WallsSourceID = WallsSourceID,
            MAP_LAYER = MAP_LAYER,
            GetBiomeFloorTile = biome => _biome.GetFloorTileForBiome(biome),
            GetBiomeWallTile = (biome, pos) => _biome.GetWallTileForBiome(biome, pos),
            MultiSection = _multiSectionCoordinator,
            CorridorCarver = _corridorCarver,
            SectionConnector = _sectionConnector
        };
        SectionConnectorOrchestrator.ConnectAdjacentSections(ctx, (System.Collections.Generic.List<LevelGenerator.MapSection>)_mapSections);
    }


    // Перенесено в SectionConnectorOrchestrator.ConnectSectionsHorizontally

    // Перенесено в SectionConnectorOrchestrator.ConnectSectionsVertically

    // Метод CreateHorizontalCorridorPart перенесён в MultiSectionCoordinator

    // Метод CreateVerticalCorridorPart перенесён в MultiSectionCoordinator

    // Метод FillHorizontalGap перенесён в MultiSectionCoordinator

    // Метод FillVerticalGap перенесён в MultiSectionCoordinator

    // Методы AddDecorativeHorizontalWalls/AddDecorativeVerticalWalls перенесены в SectionConnector

    // НОВЫЙ метод: Находит и соединяет коридор с ближайшими комнатами
    // Вынесено: CorridorCarver.FindAndConnectToNearbyRooms

    // НОВЫЙ метод: Создает вертикальное соединение между точками
    // Перенесено в SectionConnectorOrchestrator.CreateVerticalConnectionToRoom

    // НОВЫЙ метод: Создает горизонтальное соединение между точками
    // Перенесено в SectionConnectorOrchestrator.CreateHorizontalConnectionToRoom

    // НОВЫЙ метод: Добавляет декоративные стены вокруг соединения
    // Вынесено: SectionConnector.AddDecorativeWallsForConnection



    // Вынесено: SectionConnector.FindRoomNearBorder

    // Вынесено: SectionConnector.EnsurePathToRoomEdge



    // НОВЫЙ метод: Добавляет стены вокруг горизонтального прохода
    // Вынесено: SectionConnector

    // НОВЫЙ метод: Добавляет стены вокруг вертикального прохода
    // Вынесено: SectionConnector

    // НОВОЕ: Метод для выбора секции для спавна игрока
    // Метод SelectSpawnSection перенесён в MultiSectionCoordinator

    // Метод для создания структуры секций
    // Метод CreateMapSections перенесён в MultiSectionCoordinator

    // Поиск и настройка нод перенесены в NodeLocator

    // Генерация уровня со случайным биомом
    // Односекционный режим удалён

    // Отображение информации о текущем биоме
    private void DisplayBiomeInfo()
    {
        string biomeName = GetBiomeName(BiomeType);
        // Logger.Debug($"Generated new level with biome: {biomeName} (Type {BiomeType})", true);
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
    // Односекционный режим удалён

    // НОВОЕ: Метод для сброса маски секции
    private void ResetSectionMask(MapSection section)
    {
        SectionPainter.ResetSectionMask(section.SectionMask, MapWidth, MapHeight);
    }

    // НОВОЕ: Метод для заполнения базового пола секции
    private void FillSectionBaseFloor(MapSection section)
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome(section.BiomeType);
        SectionPainter.FillSectionBaseFloor(FloorsTileMap, FloorsSourceID, section.SectionMask, MapWidth, MapHeight, section.WorldOffset, backgroundTile);
    }

    // НОВОЕ: Метод для генерации комнат в секции
    private void GenerateSectionRooms(MapSection section)
    {
        // Делегируем расчёт прямоугольников комнат во вспомогательный класс,
        // а фактическое заполнение тайлов выполняем через существующий метод CreateSectionRoom,
        // чтобы не менять текущее визуальное поведение.
        int attempts = _roomPlacer.GenerateSectionRooms(section, (Rect2I roomRect) =>
        {
            CreateSectionRoom(section, roomRect);
            // Logger.Debug($"Created room in section ({section.GridX},{section.GridY}) at ({roomRect.Position.X},{roomRect.Position.Y}) with size {roomRect.Size.X}x{roomRect.Size.Y}", false);
        });

        // Logger.Debug($"Generated {section.Rooms.Count} rooms in section ({section.GridX},{section.GridY}) after {attempts} attempts", false);
    }

    // НОВОЕ: Метод для создания комнаты в секции
    private void CreateSectionRoom(MapSection section, Rect2I room)
    {
        Vector2I floorTile = _biome.GetFloorTileForBiome(section.BiomeType);
        SectionPainter.CreateSectionRoom(FloorsTileMap, FloorsSourceID, section.SectionMask, section.WorldOffset, MapWidth, MapHeight, room, floorTile);
    }

    // НОВОЕ: Метод для соединения комнат в секции коридорами
    private void ConnectSectionRooms(MapSection section)
    {
        _corridorCarver.ConnectSectionRooms(
            section,
            MapWidth,
            MapHeight,
            CorridorWidth,
            biome => _biome.GetFloorTileForBiome(biome),
            FloorsTileMap,
            MAP_LAYER,
            FloorsSourceID
        );
    }

    // Гарантия связности комнат с сетью коридоров
    private void EnsureSectionRoomConnectivity(MapSection section)
    {
        // Вычислим заранее: есть ли вообще коридоры в секции
        bool sectionHasCorridors = false;
        for (int cx = 0; cx < MapWidth && !sectionHasCorridors; cx++)
        for (int cy = 0; cy < MapHeight && !sectionHasCorridors; cy++)
            if (section.SectionMask[cx, cy] == TileType.Corridor) sectionHasCorridors = true;

        foreach (var room in section.Rooms)
        {
            bool connected = false;
            for (int x = room.Position.X; x < room.Position.X + room.Size.X && !connected; x++)
            {
                int topY = room.Position.Y - 1;
                int bottomY = room.Position.Y + room.Size.Y;
                if (topY >= 0 && section.SectionMask[x, topY] == TileType.Corridor) connected = true;
                if (bottomY < MapHeight && section.SectionMask[x, bottomY] == TileType.Corridor) connected = true;
            }
            for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y && !connected; y++)
            {
                int leftX = room.Position.X - 1;
                int rightX = room.Position.X + room.Size.X;
                if (leftX >= 0 && section.SectionMask[leftX, y] == TileType.Corridor) connected = true;
                if (rightX < MapWidth && section.SectionMask[rightX, y] == TileType.Corridor) connected = true;
            }

            if (connected) continue;

            // 1) Пытаемся провести выход от границы комнаты до ближайшего коридора через BFS по непроходимым для комнаты клеткам
            Vector2I worldOffset = new Vector2I((int)section.WorldOffset.X, (int)section.WorldOffset.Y);
            Vector2I floorTile = _biome.GetFloorTileForBiome(section.BiomeType);
            int halfWidth = Math.Max(1, CorridorWidth / 2);

            // Кандидатные старты: середины каждой стороны (на 1 тайл вне комнаты)
            var starts = new System.Collections.Generic.List<Vector2I>
            {
                new Vector2I(room.Position.X + room.Size.X/2, room.Position.Y - 1),
                new Vector2I(room.Position.X + room.Size.X/2, room.Position.Y + room.Size.Y),
                new Vector2I(room.Position.X - 1, room.Position.Y + room.Size.Y/2),
                new Vector2I(room.Position.X + room.Size.X, room.Position.Y + room.Size.Y/2),
            };

            System.Collections.Generic.List<Vector2I> bfsPath = FindPathToNearestCorridor(section, starts);
            bool carved = false;
            if (bfsPath != null && bfsPath.Count > 0)
            {
                carved = true;
                foreach (var cell in bfsPath)
                {
                    // Вычисляем ориентир по соседям, чтобы расширять в правильную сторону
                    foreach (var dir in new Vector2I[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                    {
                        for (int w = -halfWidth; w <= halfWidth; w++)
                        {
                            int cx = cell.X + (dir.Y != 0 ? w : 0);
                            int cy = cell.Y + (dir.X != 0 ? w : 0);
                            if (cx < 0 || cx >= MapWidth || cy < 0 || cy >= MapHeight) continue;
                            FloorsTileMap.SetCell(worldOffset + new Vector2I(cx, cy), FloorsSourceID, floorTile);
                            WallsTileMap.EraseCell(worldOffset + new Vector2I(cx, cy));
                            if (section.SectionMask[cx, cy] != TileType.Room)
                                section.SectionMask[cx, cy] = TileType.Corridor;
                        }
                        // Расширяем только в одном направлении
                        break;
                    }
                }
            }

            if (!carved)
            {
                // 2) Фолбэк: короткий L‑образный канал к ближайшему коридору (как раньше)
                Vector2I center = room.Position + room.Size / 2;
                int bestDist = int.MaxValue; Vector2I best = center;
                for (int x = 0; x < MapWidth; x++)
                for (int y = 0; y < MapHeight; y++)
                {
                    if (section.SectionMask[x, y] != TileType.Corridor) continue;
                    int dx = x - center.X, dy = y - center.Y; int d2 = dx*dx + dy*dy;
                    if (d2 < bestDist) { bestDist = d2; best = new Vector2I(x, y); }
                }

                if (bestDist != int.MaxValue)
                {
                    int sx = Math.Min(center.X, best.X);
                    int ex = Math.Max(center.X, best.X);
                    int yMid = center.Y;
                    for (int x = sx; x <= ex; x++)
                    {
                        for (int w = -halfWidth; w <= halfWidth; w++)
                        {
                            int yy = yMid + w;
                            if (x < 0 || x >= MapWidth || yy < 0 || yy >= MapHeight) continue;
                            FloorsTileMap.SetCell(worldOffset + new Vector2I(x, yy), FloorsSourceID, floorTile);
                            WallsTileMap.EraseCell(worldOffset + new Vector2I(x, yy));
                            if (section.SectionMask[x, yy] != TileType.Room)
                                section.SectionMask[x, yy] = TileType.Corridor;
                        }
                    }
                    int sy = Math.Min(yMid, best.Y);
                    int ey = Math.Max(yMid, best.Y);
                    int xMid = best.X;
                    for (int y = sy; y <= ey; y++)
                    {
                        for (int w = -halfWidth; w <= halfWidth; w++)
                        {
                            int xx = xMid + w;
                            if (xx < 0 || xx >= MapWidth || y < 0 || y >= MapHeight) continue;
                            FloorsTileMap.SetCell(worldOffset + new Vector2I(xx, y), FloorsSourceID, floorTile);
                            WallsTileMap.EraseCell(worldOffset + new Vector2I(xx, y));
                            if (section.SectionMask[xx, y] != TileType.Room)
                                section.SectionMask[xx, y] = TileType.Corridor;
                        }
                    }
                }
                else if (!sectionHasCorridors)
                {
                    // 3) В секции ещё нет коридоров — режем до ближайшей границы секции
                    // Выбираем направление к ближайшей стороне
                    Vector2I centerTile = room.Position + room.Size / 2;
                    var candidates = new (Vector2I start, Vector2I dir, int dist)[]
                    {
                        (new Vector2I(room.Position.X + room.Size.X/2, room.Position.Y - 1), new Vector2I(0,-1), centerTile.Y),
                        (new Vector2I(room.Position.X + room.Size.X/2, room.Position.Y + room.Size.Y), new Vector2I(0,1), MapHeight - 1 - centerTile.Y),
                        (new Vector2I(room.Position.X - 1, room.Position.Y + room.Size.Y/2), new Vector2I(-1,0), centerTile.X),
                        (new Vector2I(room.Position.X + room.Size.X, room.Position.Y + room.Size.Y/2), new Vector2I(1,0), MapWidth - 1 - centerTile.X),
                    };
                    Array.Sort(candidates, (a,b) => a.dist.CompareTo(b.dist));
                    foreach (var c in candidates)
                    {
                        System.Collections.Generic.List<Vector2I> path = new System.Collections.Generic.List<Vector2I>();
                        Vector2I p = c.start;
                        while (p.X >= 0 && p.X < MapWidth && p.Y >= 0 && p.Y < MapHeight)
                        {
                            if (section.SectionMask[p.X, p.Y] == TileType.Corridor) { path.Clear(); break; }
                            if (section.SectionMask[p.X, p.Y] == TileType.Room) { path.Clear(); break; }
                            path.Add(p);
                            // достигли края — достаточно
                            if (p.X == 0 || p.X == MapWidth-1 || p.Y == 0 || p.Y == MapHeight-1) break;
                            p += c.dir;
                        }
                        if (path.Count == 0) continue;
                        foreach (var cell in path)
                        {
                            for (int w = -halfWidth; w <= halfWidth; w++)
                            {
                                int cx = cell.X + (c.dir.Y != 0 ? w : 0);
                                int cy = cell.Y + (c.dir.X != 0 ? w : 0);
                                if (cx < 0 || cx >= MapWidth || cy < 0 || cy >= MapHeight) continue;
                                FloorsTileMap.SetCell(worldOffset + new Vector2I(cx, cy), FloorsSourceID, floorTile);
                                WallsTileMap.EraseCell(worldOffset + new Vector2I(cx, cy));
                                if (section.SectionMask[cx, cy] != TileType.Room)
                                    section.SectionMask[cx, cy] = TileType.Corridor;
                            }
                        }
                        break;
                    }
                }
            }
        }
    }

    // Поиск кратчайшего пути от множества стартов до ближайшего тайла коридора (BFS)
    private System.Collections.Generic.List<Vector2I> FindPathToNearestCorridor(MapSection section, System.Collections.Generic.IEnumerable<Vector2I> starts)
    {
        return SectionConnectivityTools.FindPathToNearestCorridor(section.SectionMask, MapWidth, MapHeight, starts);
    }

    // НОВОЕ: Метод для соединения двух комнат в секции
    // Локальные методы карвинга перенесены в CorridorCarver

    // НОВОЕ: Метод для создания горизонтального тоннеля в секции
    // Методы CreateSectionHorizontalTunnel/CreateSectionVerticalTunnel перенесены в CorridorCarver

    // НОВОЕ: Метод для добавления фоновых тайлов в секции
    private void FillSectionWithBackgroundTiles(MapSection section)
    {
        SectionPainter.FillSectionWithBackgroundTiles(
            WallsTileMap,
            WallsSourceID,
            section.SectionMask,
            MapWidth,
            MapHeight,
            section.WorldOffset,
            pos => _biome.GetWallTileForBiome(section.BiomeType, pos)
        );
    }

    // Методы стен, декораций и опасных зон перенесены в Decorator.cs

    // НОВОЕ: Метод для получения безопасной точки спавна в секции (в ТАЙЛОВЫХ координатах секции)
    private Vector2 GetSectionSpawnPosition(MapSection section)
    {
        var pos = SpawnPlanner.GetSectionSpawnPosition(section.SectionMask, section.Rooms, MapWidth, MapHeight, _random);
        if (pos == Vector2.Zero)
            Logger.Error($"No rooms available for spawn in section ({section.GridX},{section.GridY})!");
        return pos;
    }

    // Метод для получения позиции спавна игрока
    // Односекционный режим удалён

    [Signal] public delegate void PlayerSpawnedEventHandler(Node2D player);

    // Обработка спавна игрока
    private void HandlePlayerSpawn()
    {
        if (!_levelGenerated && _mapSections.Count == 0) return;
        SpawnPlacement.HandlePlayerSpawn(
            new SpawnPlacement.Context
            {
                Owner = this,
                YSortContainer = YSortContainer,
                PlayerScene = PlayerScene,
                MapTileToIsometricWorld = MapTileToIsometricWorld
            },
            _currentSpawnPosition,
            TeleportExistingPlayer,
            PlayerGroup
        );
        CenterCameraOnPlayer();
        if (_currentPlayer != null) EmitSignal(SignalName.PlayerSpawned, _currentPlayer);
    }

    // Поиск существующего игрока
    private Node2D FindPlayer()
    {
        return SpawnPlacement.FindPlayer(this, PlayerGroup);
    }

    // Создание нового игрока
    // Перенесено в SpawnPlacement.HandlePlayerSpawn / CreatePlayerAtPosition

    // Центрирование камеры на игроке
    private void CenterCameraOnPlayer()
    {
        if (_currentPlayer == null) return;
        CameraHelpers.CenterOnPlayer(this, _currentPlayer);
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
                // Logger.Debug("FloorsTileMap cleared successfully", false);
            }

            if (WallsTileMap != null)
            {
                WallsTileMap.Clear();
                // Logger.Debug("WallsTileMap cleared successfully", false);
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

    // Методы базового пола и декоративных тайлов теперь реализованы в секционном подходе

    // Методы создания комнат и коридоров перенесены в RoomPlacer и CorridorCarver


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
    // Вынесено: Decorator (single-map удалён)


    // НОВОЕ: Перегруженный метод для получения тайла стены в зависимости от типа биома
    // Вынесено: BiomePalette

    // Получение тайла стены в зависимости от биома
    // Вынесено: BiomePalette

    // НОВОЕ: Перегруженный метод для получения тайла декорации в зависимости от типа биома
    // Вынесено: BiomePalette

    // Получение тайла второго уровня стены
    // Вынесено/не используется

  // Метод для выбора тайла декорации в зависимости от биома
    // Вынесено: BiomePalette

    // Метод для добавления декораций и препятствий
    // Вынесено: Decorator

    // Метод для добавления опасных участков (вода/лава и т.д.)
    // Вынесено: Decorator

    // Вспомогательный метод для установки проходимости тайла
    private void SetTileWalkable(int x, int y, bool isWalkable)
    {
        try
        {
            // Получаем данные тайла
            TileData tileData = FloorsTileMap.GetCellTileData(new Vector2I(x, y));
            if (tileData != null)
            {
                // Устанавливаем пользовательские данные
                tileData.SetCustomData("is_walkable", isWalkable);

                // Обновляем физическую коллизию через WallsTileMap
                if (isWalkable)
                {
                    // Убираем тайл из WallsTileMap, чтобы сделать проходимым
                    WallsTileMap.EraseCell(new Vector2I(x, y));
                }
                else
                {
                    // Добавляем блокирующий тайл в WallsTileMap
                    WallsTileMap.SetCell(new Vector2I(x, y), WallsSourceID, Empty);
                }
            }
        }
        catch (Exception)
        {
            // Logger.Debug($"Error setting tile walkability at ({x}, {y}): {e.Message}", false);
        }
    }

    // Вспомогательный метод для преобразования координат тайла в мировые координаты
    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // 🔧 ИСПРАВЛЕННАЯ ИЗОМЕТРИЧЕСКАЯ ФОРМУЛА! Теперь согласованная с ResourceGenerator
        // Размер тайла для изометрии (стандартные значения из проекта)
        Vector2I tileSize = new Vector2I(32, 16);

        // Правильная формула преобразования для изометрии 2:1 
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;

        // Убираем спам логов для ускорения
        // Logger.Debug($"🔧 Tile ({tilePos.X}, {tilePos.Y}) -> World ({x}, {y})", false);
        
        return new Vector2(x, y);
    }

    // Получить текущую позицию спавна
    public Vector2 GetCurrentSpawnPosition()
    {
        return _currentSpawnPosition;
    }

    // Публичный метод для телепортации игрока в указанную комнату (сквозь все секции)
    public void TeleportPlayerToRoom(int roomIndex)
    {
        // Собираем общий список комнат по всем секциям
        var flattened = new List<(MapSection section, Rect2I room)>();
        foreach (var s in _mapSections)
        {
            foreach (var r in s.Rooms)
                flattened.Add((s, r));
        }

        if (roomIndex < 0 || roomIndex >= flattened.Count)
        {
            Logger.Error($"Invalid room index: {roomIndex}. Valid range: 0-{flattened.Count - 1}");
            return;
        }

        var target = flattened[roomIndex];
        Rect2I room = target.room;
        Vector2I center = room.Position + room.Size / 2;
        // Преобразуем в мировые тайловые координаты с учётом смещения секции
        Vector2I worldTile = new Vector2I(
            (int)target.section.WorldOffset.X + center.X,
            (int)target.section.WorldOffset.Y + center.Y
        );
        Vector2 worldPos = MapTileToIsometricWorld(worldTile);

        Node2D player = FindPlayer();
        if (player != null)
        {
            player.Position = worldPos;
            // Logger.Debug($"Player teleported to room {roomIndex} at world position {worldPos}", true);
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
        
        // Рассчитываем МИРОВЫЕ координаты (изометрические пиксели) точки спавна из тайловых координат + смещение секции
        Vector2 localSpawnTile = section.SpawnPosition.Value; // хранится в тайлах
        Vector2 worldOffsetTiles = section.WorldOffset;       // смещение секции в тайлах
        Vector2I worldTile = new Vector2I((int)(localSpawnTile.X + worldOffsetTiles.X), (int)(localSpawnTile.Y + worldOffsetTiles.Y));
        Vector2 worldSpawnPos = MapTileToIsometricWorld(worldTile);
        
        // Находим игрока и телепортируем
        Node2D player = FindPlayer();
        if (player != null)
        {
            player.Position = worldSpawnPos;
            // Logger.Debug($"Player teleported to section ({sectionX}, {sectionY}) at position {worldSpawnPos}", true);
            
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