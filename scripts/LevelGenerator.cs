using Godot;
using System;
using System.Collections.Generic;

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
    private EntitySpawner _entitySpawner; // пока не используется для сохранения поведения
    private CorridorCarver _corridorCarver; // постепенный вынос карвинга
    private SectionConnector _sectionConnector; // постепенный вынос межсекционных связей
    private Decorator _decorator; // постепенный вынос декора
    private MultiSectionCoordinator _multiSectionCoordinator; // постепенный вынос мультисекции

    public override void _Ready()
    {
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

        Logger.Debug($"TileMapLayer найдены: Floors: {FloorsTileMap?.Name}, Walls: {WallsTileMap?.Name}, YSort: {YSortContainer?.Name}", true);

        // Уберём визуальные швы: используем padding в атласе (включено) и nearest-фильтр на слое
        if (FloorsTileMap != null)
        {
            FloorsTileMap.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        }

        // Генерируем мульти-секционную карту сразу с задержкой 0.5 секунды
        GetTree().CreateTimer(0.5).Timeout += () => {
            Logger.Debug("Automatically generating multi-section map on startup", true);
            GenerateMultiSectionMap();
        };

        // Инициализируем генератор ресурсов
        if (ResourceNodeScene != null)
        {
            _resourceGenerator = new ResourceGenerator(ResourceNodeScene, MaxResourcesPerRoom, ResourceDensity);
            Logger.Debug("ResourceGenerator initialized", true);
        }
        else
        {
            Logger.Error("ResourceNodeScene is not set in LevelGenerator!");
        }

        if (ContainerScene != null)
        {
            _containerGenerator = new ContainerGenerator(ContainerScene, MaxContainersPerRoom, ContainerDensity);
            Logger.Debug("ContainerGenerator initialized", true);
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

        // EntitySpawner подготавливаем, но не используем (сохраняем текущее поведение)
        _entitySpawner = new EntitySpawner(_resourceGenerator, _containerGenerator);
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
        int containersPlaced = 0;

            Logger.Debug($"Added {containersPlaced} containers to single-section map with biome {GetBiomeName(BiomeType)}", true);
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
        Vector2I tileSize = new Vector2I(64, 32);

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

            // WorldBiomes: каждая секция становится частью одного общего мира
            GenerateSectionLevelWorldBiomes(section);

            Logger.Debug($"Generated section at ({section.GridX},{section.GridY}) with biome {GetBiomeName(section.BiomeType)}", false);
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
        var path = FindWorldPathOrganic(pa, pb);
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
    private System.Collections.Generic.List<Vector2I> FindWorldPathOrganic(Vector2I startWp, Vector2I goalWp)
    {
        var open = new System.Collections.Generic.SortedSet<(int,int,Vector2I)>(System.Collections.Generic.Comparer<(int,int,Vector2I)>.Create((a,b)=> a.Item1!=b.Item1? a.Item1-b.Item1 : a.Item2!=b.Item2? a.Item2-b.Item2 : a.Item3.X!=b.Item3.X? a.Item3.X-b.Item3.X : a.Item3.Y-b.Item3.Y));
        var came = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        var gScore = new System.Collections.Generic.Dictionary<Vector2I, int>();
        int H(Vector2I p) => System.Math.Abs(p.X - goalWp.X) + System.Math.Abs(p.Y - goalWp.Y);
        open.Add((H(startWp), 0, startWp)); gScore[startWp] = 0;
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            var p = cur.Item3;
            if (p == goalWp)
            {
                var path = new System.Collections.Generic.List<Vector2I>();
                while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                path.Reverse(); return path;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                // в мировых координатах ограничимся рамками всёй мультикарты
                if (n.X < 0 || n.Y < 0) continue;
                // разрешаем идти по любым клеткам — мост прорежет
                int ng = cur.Item2 + 1;
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                {
                    gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n));
                }
            }
        }
        return null;
    }

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

        // Подготовим общий холст: размеры мира в тайлах (используем WorldWidth/WorldHeight секций по MapWidth/MapHeight)
        int worldTilesX = System.Math.Max(1, WorldWidth) * MapWidth;
        int worldTilesY = System.Math.Max(1, WorldHeight) * MapHeight;

        // 1) Выберем центры регионов (простейшая Poisson-замена: отбраковка по минимальному расстоянию)
        var rng = _random;

        // Мягкая рандомизация параметров (если включена). Не влияет на инспектор напрямую.
        if (RandomizeWorldParams)
        {
            int rivers = rng.Next(System.Math.Max(1, RiverCount - 1), RiverCount + 2); // минимум 1
            RiverCount = System.Math.Max(1, rivers);
            RiverWidth = System.Math.Clamp(RiverWidth + rng.Next(-1, 2), 4, 10);
            CarveGlobalTrailsWidth = System.Math.Clamp(CarveGlobalTrailsWidth + rng.Next(-1, 2), 3, 8);
            BiomeHallRadius = System.Math.Clamp(BiomeHallRadius + rng.Next(-2, 3), 8, 14);
            LocalCorridorWidth = System.Math.Clamp(LocalCorridorWidth + rng.Next(-1, 2), 2, 5);
            // варьируем форму русел
            RiverNoiseFreq = Math.Clamp(RiverNoiseFreq + (float)((rng.NextDouble()-0.5)*0.01), 0.02f, 0.08f);
            RiverNoiseAmp  = Math.Clamp(RiverNoiseAmp  + (float)((rng.NextDouble()-0.5)*2.0), 6f, 12f);
            // Немного варьируем открытость
            WorldOpenTarget = System.Math.Clamp(WorldOpenTarget + (float)((rng.NextDouble()-0.5)*0.06), 0.30f, 0.50f);
        }
        var centers = new System.Collections.Generic.List<(Vector2I pos, int biome)>();
        int attempts = 0; int maxAttempts = WorldBiomeCount * 200;
        int spacing = System.Math.Max(2, BiomeMinSpacing);
        while (centers.Count < WorldBiomeCount && attempts++ < maxAttempts)
        {
            int x = rng.Next(4, worldTilesX - 4);
            int y = rng.Next(4, worldTilesY - 4);
            bool ok = true;
            foreach (var c in centers)
            {
                int dx = c.pos.X - x, dy = c.pos.Y - y;
                if (dx*dx + dy*dy < spacing * spacing) { ok = false; break; }
            }
            if (!ok) continue;
            int biome = rng.Next(0, MaxBiomeTypes);
            centers.Add((new Vector2I(x, y), biome));

            // если всё ещё не достигаем нужного количества, постепенно ослабляем минимальный разнос
            if (attempts % (WorldBiomeCount * 20) == 0 && spacing > 4) spacing -= 2;
        }
        if (centers.Count == 0)
        {
            centers.Add((new Vector2I(worldTilesX/2, worldTilesY/2), 0));
        }

        // 2) Voronoi по L1: ближайший центр — биом
        var worldBiome = new int[worldTilesX, worldTilesY];
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            int best = int.MaxValue; int b = 0;
            foreach (var c in centers)
            {
                int d = System.Math.Abs(c.pos.X - x) + System.Math.Abs(c.pos.Y - y);
                if (d < best) { best = d; b = c.biome; }
            }
            worldBiome[x, y] = b;
        }

        // 3) Инициализация надежной маски мира шумом внутри каждого региона + «залы» вокруг центров
        var worldMask = new TileType[worldTilesX, worldTilesY];
        var waterMask = new bool[worldTilesX, worldTilesY]; // отмечаем клетки воды/льда для мостов
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            bool inRegion = true; // вся карта разбита на регионы
            if (inRegion && rng.NextDouble() < CaveInitialFill)
                worldMask[x, y] = TileType.Room;
            else
                worldMask[x, y] = TileType.Background;
        }

        // Залы вокруг центров
        foreach (var c in centers)
        {
            int r = System.Math.Max(2, BiomeHallRadius);
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                int x = c.pos.X + dx, y = c.pos.Y + dy;
                if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY) continue;
                if (dx*dx + dy*dy <= r*r && worldBiome[x,y]==c.biome)
                    worldMask[x, y] = TileType.Room;
            }
        }

        // 4) Сглаживание с учётом границ биомов (сосед другого биома считаем стеной) + самонастройка под WorldOpenTarget
        for (int step = 0; step < CaveSmoothSteps; step++)
        {
            var next = new TileType[worldTilesX, worldTilesY];
            for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
            {
                int walls = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) { walls++; continue; }
                    if (worldBiome[nx, ny] != worldBiome[x, y] || worldMask[nx, ny] != TileType.Room) walls++;
                }
                if (worldMask[x, y] != TileType.Room)
                    next[x, y] = (walls >= CaveDeathLimit+1) ? TileType.Background : TileType.Room;
                else
                    next[x, y] = (walls > CaveBirthLimit+1) ? TileType.Background : TileType.Room;
            }
            worldMask = next;
        }

        // 4b) Подстройка под целевую долю свободного пространства
        int openCount = 0; for (int x=0;x<worldTilesX;x++) for (int y=0;y<worldTilesY;y++) if (worldMask[x,y]==TileType.Room) openCount++;
        float openRatio = (float)openCount / (worldTilesX*worldTilesY);
        if (openRatio < WorldOpenTarget)
        {
            // разрежаем стены: второй проход, где пороги уменьшаем
            var next = new TileType[worldTilesX, worldTilesY];
            for (int x = 0; x < worldTilesX; x++)
            for (int y = 0; y < worldTilesY; y++)
            {
                int walls = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= worldTilesX || ny < 0 || ny >= worldTilesY) { walls++; continue; }
                    if (worldBiome[nx, ny] != worldBiome[x, y] || worldMask[nx, ny] != TileType.Room) walls++;
                }
                if (worldMask[x, y] != TileType.Room)
                    next[x, y] = (walls >= CaveDeathLimit-1) ? TileType.Background : TileType.Room;
                else
                    next[x, y] = (walls > CaveBirthLimit+2) ? TileType.Background : TileType.Room;
            }
            worldMask = next;
        }

        // 5) Отрисуем: пол = Room, фон = BackTile каждого биома при WorldBlendBorders
        for (int x = 0; x < worldTilesX; x++)
        for (int y = 0; y < worldTilesY; y++)
        {
            int biome = worldBiome[x, y];
            var wp = new Vector2I(x, y);
            if (worldMask[x, y] == TileType.Room)
            {
                Vector2I tile = _biome.GetFloorTileForBiome(biome);
                FloorsTileMap.SetCell(wp, FloorsSourceID, tile);
                WallsTileMap.EraseCell(wp);
            }
            else if (WorldBlendBorders)
            {
                var wallTile = _biome.GetWallTileForBiome(biome, wp);
                WallsTileMap.SetCell(wp, WallsSourceID, wallTile);
            }
        }

        // 4) Глобальные тропы между центрами (по MST)
        var centersIdx = new System.Collections.Generic.List<int>(); for (int i=0;i<centers.Count;i++) centersIdx.Add(i);
        var edges = new System.Collections.Generic.List<(int a,int b,int w)>();
        for (int i=0;i<centers.Count;i++)
        for (int j=i+1;j<centers.Count;j++)
        {
            int dx = centers[i].pos.X - centers[j].pos.X; int dy = centers[i].pos.Y - centers[j].pos.Y;
            edges.Add((i,j,dx*dx+dy*dy));
        }
        edges.Sort((e1,e2)=>e1.w.CompareTo(e2.w));
        var parent = new int[centers.Count]; for (int i=0;i<parent.Length;i++) parent[i]=i;
        int FindP(int x){ while (parent[x]!=x) x=parent[x]=parent[parent[x]]; return x; }
        bool UnionP(int x,int y){ x=FindP(x); y=FindP(y); if (x==y) return false; parent[y]=x; return true; }
        var chosen = new System.Collections.Generic.List<(int a,int b)>();
        foreach (var e in edges) if (UnionP(e.a,e.b)) chosen.Add((e.a,e.b));

        foreach (var c in chosen)
        {
            var path = FindWorldPathOrganic(centers[c.a].pos, centers[c.b].pos);
            if (path == null) continue;
            var tile = _biome.GetBridgeTile(true, CarveGlobalTrailsWidth);
            foreach (var wp in path)
            {
                for (int w = -(CarveGlobalTrailsWidth/2); w <= (CarveGlobalTrailsWidth/2); w++)
                {
                    foreach (var d in new[]{new Vector2I(1,0), new Vector2I(0,1)})
                    {
                        var p = new Vector2I(wp.X + d.X*w, wp.Y + d.Y*w);
                        FloorsTileMap.SetCell(p, FloorsSourceID, tile);
                        WallsTileMap.EraseCell(p);
                    }
                }
            }
        }

        // 4b) Локальные связки: из центральной «залы» каждого биома в близкие комнаты
        // Локальная функция A* с ограничением на тот же биом
        System.Collections.Generic.List<Vector2I> FindWorldPathConstrainedLocal(Vector2I start, Vector2I goal, int allowedBiome)
        {
            var open = new System.Collections.Generic.SortedSet<(int,int,Vector2I)>(System.Collections.Generic.Comparer<(int,int,Vector2I)>.Create((a,b)=> a.Item1!=b.Item1? a.Item1-b.Item1 : a.Item2!=b.Item2? a.Item2-b.Item2 : a.Item3.X!=b.Item3.X? a.Item3.X-b.Item3.X : a.Item3.Y-b.Item3.Y));
            var came = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
            var gScore = new System.Collections.Generic.Dictionary<Vector2I, int>();
            int H(Vector2I p) => System.Math.Abs(p.X - goal.X) + System.Math.Abs(p.Y - goal.Y);
            open.Add((H(start), 0, start)); gScore[start] = 0;
            var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                var p = cur.Item3;
                if (p == goal)
                {
                    var path = new System.Collections.Generic.List<Vector2I>();
                    while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                    path.Reverse(); return path;
                }
                foreach (var d in dirs)
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= worldTilesX || n.Y < 0 || n.Y >= worldTilesY) continue;
                    if (worldBiome[n.X, n.Y] != allowedBiome) continue; // ходим только внутри своего биома
                    int ng = cur.Item2 + 1;
                    if (!gScore.TryGetValue(n, out var old) || ng < old)
                    {
                        gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n));
                    }
                }
            }
            return null;
        }

        foreach (var c in centers)
        {
            var hub = c.pos;
            int searchR = System.Math.Max(8, BiomeHallRadius + 18);
            for (int x = System.Math.Max(0, hub.X - searchR); x < System.Math.Min(worldTilesX, hub.X + searchR); x++)
            {
                for (int y = System.Math.Max(0, hub.Y - searchR); y < System.Math.Min(worldTilesY, hub.Y + searchR); y++)
                {
                    if (worldBiome[x, y] != c.biome) continue;
                    if (worldMask[x, y] != TileType.Room) continue;
                    int dx0 = x - hub.X, dy0 = y - hub.Y; if (dx0*dx0 + dy0*dy0 <= BiomeHallRadius*BiomeHallRadius) continue;
                    // Редкий отбор, чтобы не перегружать
                    if (((x + y) % 11) != 0) continue;
                    var path = FindWorldPathConstrainedLocal(hub, new Vector2I(x, y), c.biome);
                    if (path == null) continue;
                    // Если это травяной биом (0), используем улучшенные Wang‑варианты 12..23
                    var tile = _biome.GetFloorTileForBiome(c.biome);
                    foreach (var wp in path)
                    {
                        for (int w = -(LocalCorridorWidth/2); w <= (LocalCorridorWidth/2); w++)
                        {
                            foreach (var d in new[]{new Vector2I(1,0), new Vector2I(0,1)})
                            {
                                var p = new Vector2I(wp.X + d.X*w, wp.Y + d.Y*w);
                                FloorsTileMap.SetCell(p, FloorsSourceID, tile);
                                WallsTileMap.EraseCell(p);
                                if (p.X >= 0 && p.X < worldTilesX && p.Y >= 0 && p.Y < worldTilesY)
                                    worldMask[p.X, p.Y] = TileType.Room;
                            }
                        }
                    }
                }
            }
        }

        // 5b) Реки (вода/лёд на полу): синусоиды по миру. Для снежного биома используем лёд (0,1)
        for (int ri = 0; ri < RiverCount; ri++)
        {
            // случайная ориентация
            bool horizontal = rng.NextDouble() < 0.5;
            if (horizontal)
            {
                int y0 = rng.Next(worldTilesY);
                for (int x = 0; x < worldTilesX; x++)
                {
                    int y = y0 + (int)(System.Math.Sin(x * RiverNoiseFreq) * RiverNoiseAmp);
                    for (int w = -RiverWidth/2; w <= RiverWidth/2; w++)
                    {
                        int yy = y + w; if (yy < 0 || yy >= worldTilesY) continue;
                        // Если клетка принадлежит снежному биому — рисуем лёд (0,1), иначе воду (5,0)
                        var liquidTile = (worldBiome[x, yy] == 3 /* Ice */) ? new Vector2I(0,1) : new Vector2I(5,0);
                        FloorsTileMap.SetCell(new Vector2I(x, yy), FloorsSourceID, liquidTile);
                        WallsTileMap.EraseCell(new Vector2I(x, yy));
                        worldMask[x, yy] = TileType.Background; // непроходимо
                        waterMask[x, yy] = true;
                    }
                }
            }
            else
            {
                int x0 = rng.Next(worldTilesX);
                for (int y = 0; y < worldTilesY; y++)
                {
                    int x = x0 + (int)(System.Math.Sin(y * RiverNoiseFreq) * RiverNoiseAmp);
                    for (int w = -RiverWidth/2; w <= RiverWidth/2; w++)
                    {
                        int xx = x + w; if (xx < 0 || xx >= worldTilesX) continue;
                        var liquidTile = (worldBiome[xx, y] == 3 /* Ice */) ? new Vector2I(0,1) : new Vector2I(5,0);
                        FloorsTileMap.SetCell(new Vector2I(xx, y), FloorsSourceID, liquidTile);
                        WallsTileMap.EraseCell(new Vector2I(xx, y));
                        worldMask[xx, y] = TileType.Background;
                        waterMask[xx, y] = true;
                    }
                }
            }
        }

        // 5c) Мосты поверх рек: только в точках реального пересечения
        foreach (var c in chosen)
        {
            var path = FindWorldPathOrganic(centers[c.a].pos, centers[c.b].pos);
            if (path == null) continue;
            var tile = _biome.GetBridgeTile(false, CarveGlobalTrailsWidth);
            for (int i = 0; i < path.Count; i++)
            {
                var wp = path[i];
                if (wp.X < 1 || wp.X >= worldTilesX-1 || wp.Y < 1 || wp.Y >= worldTilesY-1) continue;
                if (!waterMask[wp.X, wp.Y] && !waterMask[wp.X+1, wp.Y] && !waterMask[wp.X-1, wp.Y] && !waterMask[wp.X, wp.Y+1] && !waterMask[wp.X, wp.Y-1])
                    continue; // нет воды рядом — мост не нужен

                // Определим направление реки (продольная ось воды) по локальному окружению
                int waterRunX = 0; for (int dx=-6; dx<=6; dx++) if (wp.X+dx>=0 && wp.X+dx<worldTilesX && waterMask[wp.X+dx, wp.Y]) waterRunX++;
                int waterRunY = 0; for (int dy=-6; dy<=6; dy++) if (wp.Y+dy>=0 && wp.Y+dy<worldTilesY && waterMask[wp.X, wp.Y+dy]) waterRunY++;
                bool riverVertical = waterRunY >= waterRunX; // если больше по Y — река идёт вертикально ⇒ мост горизонтальный

                int halfBridge = System.Math.Max((CarveGlobalTrailsWidth+2)/2, 3);
                int halfSpan = System.Math.Max(RiverWidth/2 + 2, 5); // перекрыть всю реку с запасом

                if (riverVertical)
                {
                    // мост горизонтальный: расширяем по X через всю ширину реки
                    for (int ox = -halfSpan; ox <= halfSpan; ox++)
                    for (int w = -halfBridge; w <= halfBridge; w++)
                    {
                        var p = new Vector2I(wp.X + ox, wp.Y + w);
                        if (p.X < 0 || p.X >= worldTilesX || p.Y < 0 || p.Y >= worldTilesY) continue;
                        FloorsTileMap.SetCell(p, FloorsSourceID, tile);
                        WallsTileMap.EraseCell(p);
                        worldMask[p.X, p.Y] = TileType.Room; waterMask[p.X, p.Y] = false;
                    }
                }
                else
                {
                    // мост вертикальный: расширяем по Y
                    for (int oy = -halfSpan; oy <= halfSpan; oy++)
                    for (int w = -halfBridge; w <= halfBridge; w++)
                    {
                        var p = new Vector2I(wp.X + w, wp.Y + oy);
                        if (p.X < 0 || p.X >= worldTilesX || p.Y < 0 || p.Y >= worldTilesY) continue;
                        FloorsTileMap.SetCell(p, FloorsSourceID, tile);
                        WallsTileMap.EraseCell(p);
                        worldMask[p.X, p.Y] = TileType.Room; waterMask[p.X, p.Y] = false;
                    }
                }
            }
        }

        // Генерируем ресурсы и контейнеры напрямую по мировой маске (новый подход для WorldBiomes)
        GenerateWorldResources(worldMask, worldBiome, worldTilesX, worldTilesY);
        GenerateWorldContainers(worldMask, worldBiome, worldTilesX, worldTilesY);

        // Выбираем точку спавна игрока в центральной области мира
        _currentSpawnPosition = FindWorldSpawnPosition(worldMask, worldTilesX, worldTilesY);
        
        Logger.Debug($"WorldBiomes generation completed. Spawn position: {_currentSpawnPosition}", true);
    }

    // Удалено: GenerateVirtualRoomsFromWorldMask - заменено на прямую генерацию по мировой маске

    // Находит подходящую точку спавна игрока в сгенерированном мире
    private Vector2 FindWorldSpawnPosition(TileType[,] worldMask, int worldTilesX, int worldTilesY)
    {
        // Начинаем поиск из центра мира
        int centerX = worldTilesX / 2;
        int centerY = worldTilesY / 2;
        
        // Ищем ближайшую проходимую клетку от центра мира
        for (int radius = 0; radius < Math.Max(worldTilesX, worldTilesY) / 2; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Проверяем только клетки на текущем радиусе (граница круга)
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius && radius > 0)
                        continue;
                        
                    int x = centerX + dx;
                    int y = centerY + dy;
                    
                    // Проверяем границы
                    if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY)
                        continue;
                    
                    // Проверяем, что клетка проходима
                    if (worldMask[x, y] == TileType.Room)
                    {
                        // Проверяем, что вокруг есть достаточно места (3x3 область)
                        bool hasSpace = true;
                        for (int sx = -1; sx <= 1 && hasSpace; sx++)
                        {
                            for (int sy = -1; sy <= 1 && hasSpace; sy++)
                            {
                                int checkX = x + sx;
                                int checkY = y + sy;
                                if (checkX >= 0 && checkX < worldTilesX && checkY >= 0 && checkY < worldTilesY)
                                {
                                    if (worldMask[checkX, checkY] != TileType.Room)
                                        hasSpace = false;
                                }
                            }
                        }
                        
                        if (hasSpace)
                        {
                            // Преобразуем тайловые координаты в мировые пиксельные координаты
                            // Для изометрии: каждый тайл = 64x32 пикселя
                            Vector2 worldPosition = MapTileToIsometricWorld(new Vector2I(x, y));
                            Logger.Debug($"Found spawn position at tile ({x}, {y}) -> world {worldPosition}", false);
                            return worldPosition;
                        }
                    }
                }
            }
        }
        
        // Если не нашли подходящего места, используем центр мира
        Vector2 fallbackPosition = MapTileToIsometricWorld(new Vector2I(centerX, centerY));
        Logger.Debug($"Could not find safe spawn position, using center: {fallbackPosition}", true);
        return fallbackPosition;
    }

    // Новый метод генерации ресурсов для WorldBiomes - работает напрямую с мировой маской
    private void GenerateWorldResources(TileType[,] worldMask, int[,] worldBiome, int worldTilesX, int worldTilesY)
    {
        if (_resourceGenerator == null)
        {
            Logger.Error("ResourceGenerator is not initialized!");
            return;
        }

        int resourcesPlaced = 0;
        int resourceAttempts = 0;
        int maxResources = (worldTilesX * worldTilesY) / 100; // Примерно 1% тайлов могут содержать ресурсы
        
        Logger.Debug($"Starting world resource generation. World size: {worldTilesX}x{worldTilesY}, target resources: {maxResources}", true);

        // Проходим по всему миру и размещаем ресурсы
        for (int x = 0; x < worldTilesX && resourcesPlaced < maxResources; x += 4) // Шаг 4 для разреженности
        {
            for (int y = 0; y < worldTilesY && resourcesPlaced < maxResources; y += 4)
            {
                resourceAttempts++;
                
                // Проверяем, что это проходимая область
                if (worldMask[x, y] != TileType.Room)
                    continue;
                
                // Проверяем, что вокруг тоже есть место (3x3 область)
                if (!IsAreaWalkable(worldMask, x, y, worldTilesX, worldTilesY, 1))
                    continue;
                
                // Получаем биом в этой точке
                int biome = worldBiome[x, y];
                
                // Вероятность размещения ресурса зависит от биома
                float spawnChance = GetResourceSpawnChance(biome);
                if (_random.NextDouble() > spawnChance)
                    continue;
                
                // Размещаем ресурс
                if (PlaceWorldResource(x, y, biome))
                {
                    resourcesPlaced++;
                    Logger.Debug($"Placed resource {resourcesPlaced} at ({x}, {y}) in biome {GetBiomeName(biome)}", false);
                }
            }
        }
        
        Logger.Debug($"World resource generation completed. Placed {resourcesPlaced} resources from {resourceAttempts} attempts", true);
    }

    // Новый метод генерации контейнеров для WorldBiomes
    private void GenerateWorldContainers(TileType[,] worldMask, int[,] worldBiome, int worldTilesX, int worldTilesY)
    {
        if (_containerGenerator == null)
        {
            Logger.Error("ContainerGenerator is not initialized!");
            return;
        }

        int containersPlaced = 0;
        int maxContainers = (worldTilesX * worldTilesY) / 200; // Примерно 0.5% тайлов могут содержать контейнеры
        
        Logger.Debug($"Starting world container generation. Target containers: {maxContainers}", true);

        // Размещаем контейнеры реже чем ресурсы
        for (int x = 0; x < worldTilesX && containersPlaced < maxContainers; x += 6) // Шаг 6 для большей разреженности
        {
            for (int y = 0; y < worldTilesY && containersPlaced < maxContainers; y += 6)
            {
                // Проверяем, что это проходимая область
                if (worldMask[x, y] != TileType.Room)
                    continue;
                
                // Проверяем, что вокруг есть достаточно места (5x5 область для контейнеров)
                if (!IsAreaWalkable(worldMask, x, y, worldTilesX, worldTilesY, 2))
                    continue;
                
                // Получаем биом в этой точке
                int biome = worldBiome[x, y];
                
                // Вероятность размещения контейнера
                if (_random.NextDouble() > 0.3) // 30% шанс
                    continue;
                
                // Размещаем контейнер
                if (PlaceWorldContainer(x, y, biome))
                {
                    containersPlaced++;
                    Logger.Debug($"Placed container {containersPlaced} at ({x}, {y}) in biome {GetBiomeName(biome)}", false);
                }
            }
        }
        
        Logger.Debug($"World container generation completed. Placed {containersPlaced} containers", true);
    }

    // Проверяет, что область вокруг точки проходима
    private bool IsAreaWalkable(TileType[,] worldMask, int centerX, int centerY, int worldTilesX, int worldTilesY, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                
                if (x < 0 || x >= worldTilesX || y < 0 || y >= worldTilesY)
                    return false;
                
                if (worldMask[x, y] != TileType.Room)
                    return false;
            }
        }
        return true;
    }

    // Получает вероятность появления ресурса для биома
    private float GetResourceSpawnChance(int biome)
    {
        switch (biome)
        {
            case 0: return 0.15f; // Grassland - умеренно
            case 1: return 0.20f; // Forest - больше органических ресурсов
            case 2: return 0.18f; // Desert - металлы и кристаллы
            case 3: return 0.12f; // Ice - редкие ресурсы
            case 4: return 0.25f; // Techno - много ресурсов
            case 5: return 0.22f; // Anomal - необычные ресурсы
            case 6: return 0.16f; // Lava Springs - специальные ресурсы
            default: return 0.10f;
        }
    }

    // Размещает ресурс в мировых координатах
    private bool PlaceWorldResource(int worldX, int worldY, int biome)
    {
        try
        {
            if (ResourceNodeScene == null)
            {
                Logger.Error("ResourceNodeScene is not set!");
                return false;
            }
            
            // Создаем экземпляр ресурса напрямую из сцены
            ResourceNode resourceNode = ResourceNodeScene.Instantiate<ResourceNode>();
            
            if (resourceNode != null)
            {
                // Определяем тип ресурса на основе биома
                ResourceType resourceType = SelectResourceTypeForBiome(biome);
                
                // Настраиваем ресурс
                resourceNode.Type = resourceType;
                resourceNode.ResourceAmount = _random.Next(1, 4); // Случайное количество от 1 до 3
                
                // Преобразуем мировые тайловые координаты в изометрические пиксельные
                Vector2 worldPosition = MapTileToIsometricWorld(new Vector2I(worldX, worldY));
                worldPosition.Y += 16; // Смещение для правильного отображения
                
                resourceNode.Position = worldPosition;
                
                // Добавляем в YSortContainer
                if (YSortContainer != null)
                {
                    YSortContainer.AddChild(resourceNode);
                    Logger.Debug($"Successfully placed {resourceType} resource at world ({worldX}, {worldY})", false);
                    return true;
                }
                else
                {
                    Logger.Error("YSortContainer not found for resource placement");
                    resourceNode.QueueFree();
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error placing world resource at ({worldX}, {worldY}): {e.Message}");
        }
        
        return false;
    }

    // Размещает контейнер в мировых координатах
    private bool PlaceWorldContainer(int worldX, int worldY, int biome)
    {
        try
        {
            if (ContainerScene == null)
            {
                Logger.Error("ContainerScene is not set!");
                return false;
            }
            
            // Создаем экземпляр контейнера напрямую из сцены
            Container containerNode = ContainerScene.Instantiate<Container>();
            
            if (containerNode != null)
            {
                // Преобразуем мировые тайловые координаты в изометрические пиксельные
                Vector2 worldPosition = MapTileToIsometricWorld(new Vector2I(worldX, worldY));
                worldPosition.Y += 16; // Смещение для правильного отображения
                
                containerNode.Position = worldPosition;
                
                // Добавляем в YSortContainer
                if (YSortContainer != null)
                {
                    YSortContainer.AddChild(containerNode);
                    Logger.Debug($"Successfully placed container at world ({worldX}, {worldY}) in biome {GetBiomeName(biome)}", false);
                    return true;
                }
                else
                {
                    Logger.Error("YSortContainer not found for container placement");
                    containerNode.QueueFree();
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error placing world container at ({worldX}, {worldY}): {e.Message}");
        }
        
        return false;
    }

    // Выбирает тип ресурса на основе биома
    private ResourceType SelectResourceTypeForBiome(int biome)
    {
        // Используем биом-специфичные вероятности
        switch (biome)
        {
            case 0: // Grassland - сбалансированно
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Metal;
                    if (rand < 0.6f) return ResourceType.Crystal;
                    return ResourceType.Organic;
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
                    return ResourceType.Energy;
                }
            case 3: // Ice - кристаллы
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.5f) return ResourceType.Crystal;
                    if (rand < 0.8f) return ResourceType.Metal;
                    return ResourceType.Energy;
                }
            case 4: // Techno - энергия и композиты
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Energy;
                    if (rand < 0.6f) return ResourceType.Metal;
                    if (rand < 0.8f) return ResourceType.Composite;
                    return ResourceType.Crystal;
                }
            case 5: // Anomal - все редкие
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.4f) return ResourceType.Crystal;
                    if (rand < 0.6f) return ResourceType.Energy;
                    if (rand < 0.8f) return ResourceType.Composite;
                    return ResourceType.Metal;
                }
            case 6: // Lava Springs - металлы и энергия
                {
                    float rand = (float)_random.NextDouble();
                    if (rand < 0.5f) return ResourceType.Metal;
                    if (rand < 0.8f) return ResourceType.Energy;
                    return ResourceType.Crystal;
                }
            default:
                return ResourceType.Metal;
        }
    }

    private void PreserveLargestWalkableComponent(MapSection section)
    {
        var visited = new bool[MapWidth, MapHeight];
        int best = 0; System.Collections.Generic.List<Vector2I> bestCells = null;
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
        {
            if (visited[x, y] || section.SectionMask[x, y] != TileType.Room) continue;
            var comp = new System.Collections.Generic.List<Vector2I>();
            var q = new System.Collections.Generic.Queue<Vector2I>();
            q.Enqueue(new Vector2I(x, y)); visited[x, y] = true;
            while (q.Count > 0)
            {
                var p = q.Dequeue(); comp.Add(p);
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= MapWidth || n.Y < 0 || n.Y >= MapHeight) continue;
                    if (visited[n.X, n.Y]) continue;
                    if (section.SectionMask[n.X, n.Y] != TileType.Room) continue;
                    visited[n.X, n.Y] = true; q.Enqueue(n);
                }
            }
            if (comp.Count > best) { best = comp.Count; bestCells = comp; }
        }
        // Очищаем все, кроме best
        if (bestCells == null) return;
        var keep = new System.Collections.Generic.HashSet<Vector2I>(bestCells);
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
        {
            if (section.SectionMask[x, y] == TileType.Room && !keep.Contains(new Vector2I(x, y)))
                section.SectionMask[x, y] = TileType.Background;
        }
    }

    // Подключение изолированных комнатных компонентов к ближайшей тропе/коридору
    private void ConnectAllRoomComponentsToTrails(MapSection section)
    {
        // 1) Найдём все компоненты Room
        var visited = new bool[MapWidth, MapHeight];
        var components = new System.Collections.Generic.List<System.Collections.Generic.List<Vector2I>>();
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
        {
            if (visited[x, y] || section.SectionMask[x, y] != TileType.Room) continue;
            var comp = new System.Collections.Generic.List<Vector2I>();
            var q = new System.Collections.Generic.Queue<Vector2I>();
            q.Enqueue(new Vector2I(x, y)); visited[x, y] = true;
            while (q.Count > 0)
            {
                var p = q.Dequeue(); comp.Add(p);
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= MapWidth || n.Y < 0 || n.Y >= MapHeight) continue;
                    if (visited[n.X, n.Y]) continue;
                    if (section.SectionMask[n.X, n.Y] != TileType.Room) continue;
                    visited[n.X, n.Y] = true; q.Enqueue(n);
                }
            }
            components.Add(comp);
        }

        if (components.Count <= 1) return; // уже связно

        // 2) Соберём все клетки коридоров (троп) как цели
        var corridors = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
            if (section.SectionMask[x, y] == TileType.Corridor)
                corridors.Add(new Vector2I(x, y));

        // Если коридоров нет — нечем соединять
        if (corridors.Count == 0) return;

        // 3) Для каждой компоненты проведём короткую связь к ближайшему коридору
        foreach (var comp in components)
        {
            // если в компоненте уже есть контакт с коридором — пропуск
            bool touches = false;
            foreach (var p in comp)
            {
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    int nx = p.X + d.X, ny = p.Y + d.Y;
                    if (nx < 0 || nx >= MapWidth || ny < 0 || ny >= MapHeight) continue;
                    if (section.SectionMask[nx, ny] == TileType.Corridor) { touches = true; break; }
                }
                if (touches) break;
            }
            if (touches) continue;

            // выберем точку комп-ты и ближайшую цель
            Vector2I from = comp[comp.Count / 2];
            int best = int.MaxValue; Vector2I target = from;
            foreach (var c in corridors)
            {
                int dx = c.X - from.X; int dy = c.Y - from.Y; int d2 = dx*dx + dy*dy;
                if (d2 < best) { best = d2; target = c; }
            }

            // Проложим путь по непроходимым (фон/стены), не разрушая другие комнаты
            var worldOffset = new Vector2I((int)section.WorldOffset.X, (int)section.WorldOffset.Y);
            var path = FindWorldPathOrganic(worldOffset + from, worldOffset + target);
            if (path == null) continue;
            var floorTile = _biome.GetFloorTileForBiome(section.BiomeType);
            foreach (var wp in path)
            {
                FloorsTileMap.SetCell(wp, FloorsSourceID, floorTile);
                WallsTileMap.EraseCell(wp);
                int lx = wp.X - worldOffset.X; int ly = wp.Y - worldOffset.Y;
                if (lx >= 0 && lx < MapWidth && ly >= 0 && ly < MapHeight)
                    section.SectionMask[lx, ly] = TileType.Corridor;
            }
        }
    }
    private System.Collections.Generic.List<Vector2I> PickTrailNodes(MapSection section, int count, int minSpacing)
    {
        var nodes = new System.Collections.Generic.List<Vector2I>();
        int attempts = 0; int maxAttempts = count * 50;
        while (nodes.Count < count && attempts++ < maxAttempts)
        {
            int x = _random.Next(2, MapWidth - 2);
            int y = _random.Next(2, MapHeight - 2);
            if (section.SectionMask[x, y] != TileType.Room) continue;
            bool far = true;
            foreach (var n in nodes)
                if ((n - new Vector2I(x, y)).LengthSquared() < minSpacing * minSpacing) { far = false; break; }
            if (far) nodes.Add(new Vector2I(x, y));
        }
        return nodes;
    }

    private void CarveTrailsBetweenNodes(MapSection section, System.Collections.Generic.List<Vector2I> nodes, int width)
    {
        if (nodes == null || nodes.Count < 2) return;

        // Строим MST по эвклидовой дистанции между узлами
        var edges = new System.Collections.Generic.List<(int a, int b, int w)>();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            int dx = nodes[i].X - nodes[j].X;
            int dy = nodes[i].Y - nodes[j].Y;
            int w2 = dx*dx + dy*dy;
            edges.Add((i, j, w2));
        }
        edges.Sort((e1,e2) => e1.w.CompareTo(e2.w));

        var parent = new int[nodes.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x){ while (parent[x]!=x) x = parent[x] = parent[parent[x]]; return x; }
        bool Union(int x,int y){ x=Find(x); y=Find(y); if (x==y) return false; parent[y]=x; return true; }

        var chosen = new System.Collections.Generic.List<(int a,int b)>();
        foreach (var e in edges)
            if (Union(e.a, e.b)) chosen.Add((e.a, e.b));

        // Доп. рёбра для вариативности
        int extras = System.Math.Min(TrailExtraEdges, edges.Count);
        int idx = 0;
        for (int k = 0; k < extras && idx < edges.Count; idx++)
        {
            var e = edges[idx];
            // пропускаем уже выбранные
            bool exists = false; foreach (var c in chosen) if ((c.a==e.a && c.b==e.b) || (c.a==e.b && c.b==e.a)) { exists = true; break; }
            if (exists) continue;
            chosen.Add((e.a, e.b)); k++;
        }

        // Карвим пути для выбранных пар
        foreach (var c in chosen)
        {
            var path = FindPathOverRooms(section, nodes[c.a], nodes[c.b]);
            if (path == null) continue;
            Vector2I worldOffset = new Vector2I((int)section.WorldOffset.X, (int)section.WorldOffset.Y);
            var floorTile = _biome.GetFloorTileForBiome(section.BiomeType);
            foreach (var p in path)
            {
                for (int w = -(width/2); w <= (width/2); w++)
                {
                    foreach (var dir in new[]{new Vector2I(1,0), new Vector2I(0,1)})
                    {
                        int cx = p.X + dir.X * w;
                        int cy = p.Y + dir.Y * w;
                        if (cx < 0 || cx >= MapWidth || cy < 0 || cy >= MapHeight) continue;
                        FloorsTileMap.SetCell(worldOffset + new Vector2I(cx, cy), FloorsSourceID, floorTile);
                        WallsTileMap.EraseCell(worldOffset + new Vector2I(cx, cy));
                        section.SectionMask[cx, cy] = TileType.Corridor;
                    }
                }
            }
        }
    }

    // A* по проходимым (Room) клеткам
    private System.Collections.Generic.List<Vector2I> FindPathOverRooms(MapSection section, Vector2I start, Vector2I goal)
    {
        var open = new System.Collections.Generic.SortedSet<(int,int,Vector2I)>(System.Collections.Generic.Comparer<(int,int,Vector2I)>.Create((a,b)=> a.Item1!=b.Item1? a.Item1-b.Item1 : a.Item2!=b.Item2? a.Item2-b.Item2 : a.Item3.X!=b.Item3.X? a.Item3.X-b.Item3.X : a.Item3.Y-b.Item3.Y));
        var came = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        var gScore = new System.Collections.Generic.Dictionary<Vector2I, int>();
        int H(Vector2I p) => System.Math.Abs(p.X - goal.X) + System.Math.Abs(p.Y - goal.Y);
        open.Add((H(start), 0, start)); gScore[start] = 0;
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            var p = cur.Item3;
            if (p == goal)
            {
                var path = new System.Collections.Generic.List<Vector2I>();
                while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                path.Reverse(); return path;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.X >= MapWidth || n.Y < 0 || n.Y >= MapHeight) continue;
                if (section.SectionMask[n.X, n.Y] != TileType.Room) continue;
                int ng = cur.Item2 + 1;
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                {
                    gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n));
                }
            }
        }
        return null;
    }

    // Удалено: AddSectionResources - заменено на GenerateWorldResources

    // Односекционный режим удалён

    // НОВОЕ: Метод для соединения соседних секций проходами
    // МОДИФИКАЦИЯ метода для соединения соседних секций
    private void ConnectAdjacentSections()
    {
        try
        {
            Logger.Debug("Connecting adjacent sections", true);

            // Соединяем секции по горизонтали (слева направо)
            for (int y = 0; y < GridHeight; y++)
            {
                for (int x = 0; x < GridWidth - 1; x++)
                {
                    MapSection leftSection = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                    MapSection rightSection = _mapSections.Find(s => s.GridX == x + 1 && s.GridY == y);

                    if (leftSection != null && rightSection != null)
                    {
                        Logger.Debug($"Connecting sections horizontally: ({leftSection.GridX},{leftSection.GridY}) to ({rightSection.GridX},{rightSection.GridY})", false);
                        ConnectSectionsHorizontally(leftSection, rightSection);
                    }
                }
            }

            // Соединяем секции по вертикали (сверху вниз)
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight - 1; y++)
                {
                    MapSection topSection = _mapSections.Find(s => s.GridX == x && s.GridY == y);
                    MapSection bottomSection = _mapSections.Find(s => s.GridX == x && s.GridY == y + 1);

                    if (topSection != null && bottomSection != null)
                    {
                        Logger.Debug($"Connecting sections vertically: ({topSection.GridX},{topSection.GridY}) to ({bottomSection.GridX},{bottomSection.GridY})", false);
                        ConnectSectionsVertically(topSection, bottomSection);
                    }
                }
            }

            Logger.Debug("All adjacent sections connected successfully", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting adjacent sections: {e.Message}\n{e.StackTrace}");
        }
    }


    // Метод для соединения двух секций по горизонтали
    // УЛУЧШЕННЫЙ метод для соединения секций по горизонтали
    private void ConnectSectionsHorizontally(MapSection leftSection, MapSection rightSection)
    {
        try
        {
            Logger.Debug($"Creating horizontal connection between sections ({leftSection.GridX},{leftSection.GridY}) and ({rightSection.GridX},{rightSection.GridY})", true);

            int passageY = MapHeight / 2;
            Vector2I leftFloorTile = _biome.GetFloorTileForBiome(leftSection.BiomeType);
            Vector2I rightFloorTile = _biome.GetFloorTileForBiome(rightSection.BiomeType);
            int tunnelWidth = Math.Max(3, ConnectorWidth);

            _multiSectionCoordinator.CreateHorizontalCorridorPart(
                leftSection,
                MapWidth - 10,
                MapWidth,
                passageY,
                tunnelWidth,
                leftFloorTile,
                MapWidth,
                MapHeight,
                FloorsTileMap,
                WallsTileMap,
                MAP_LAYER,
                FloorsSourceID,
                (section, x1, x2, y, width, floor) => _corridorCarver.FindAndConnectToNearbyRooms(
                    section, x1, x2, y, width, floor, true,
                    MapWidth, MapHeight,
                    (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(s, sx, ex, py, ft),
                    (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(s, px, sy, ey, ft)
                )
            );

            _multiSectionCoordinator.CreateHorizontalCorridorPart(
                rightSection,
                0,
                10,
                passageY,
                tunnelWidth,
                rightFloorTile,
                MapWidth,
                MapHeight,
                FloorsTileMap,
                WallsTileMap,
                MAP_LAYER,
                FloorsSourceID,
                (section, x1, x2, y, width, floor) => _corridorCarver.FindAndConnectToNearbyRooms(
                    section, x1, x2, y, width, floor, true,
                    MapWidth, MapHeight,
                    (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(s, sx, ex, py, ft),
                    (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(s, px, sy, ey, ft)
                )
            );

            if (SectionSpacing > 0)
            {
                _multiSectionCoordinator.FillHorizontalGap(
                    leftSection,
                    rightSection,
                    passageY,
                    tunnelWidth,
                    SectionSpacing,
                    MapWidth,
                    FloorsTileMap,
                    WallsTileMap,
                    MAP_LAYER,
                    FloorsSourceID,
                    biome => _biome.GetFloorTileForBiome(biome)
                );
            }

            _sectionConnector.AddDecorativeHorizontalWalls(
                leftSection,
                rightSection,
                passageY,
                tunnelWidth,
                MapWidth,
                MapHeight,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );

            _sectionConnector.AddWallsAroundHorizontalConnector(
                leftSection,
                rightSection,
                passageY,
                tunnelWidth,
                MapWidth,
                MapHeight,
                SectionSpacing,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );

            Logger.Debug($"Horizontal connection created between sections at Y={passageY}", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting sections horizontally: {e.Message}\n{e.StackTrace}");
        }
    }

    // Метод для соединения двух секций по вертикали
    // УЛУЧШЕННЫЙ метод для соединения секций по вертикали
    private void ConnectSectionsVertically(MapSection topSection, MapSection bottomSection)
    {
        try
        {
            Logger.Debug($"Creating vertical connection between sections ({topSection.GridX},{topSection.GridY}) and ({bottomSection.GridX},{bottomSection.GridY})", true);

            int passageX = MapWidth / 2;
            Vector2I topFloorTile = _biome.GetFloorTileForBiome(topSection.BiomeType);
            Vector2I bottomFloorTile = _biome.GetFloorTileForBiome(bottomSection.BiomeType);
            int tunnelWidth = Math.Max(3, ConnectorWidth);

            _multiSectionCoordinator.CreateVerticalCorridorPart(
                topSection,
                MapHeight - 10,
                MapHeight,
                passageX,
                tunnelWidth,
                topFloorTile,
                MapWidth,
                MapHeight,
                FloorsTileMap,
                WallsTileMap,
                MAP_LAYER,
                FloorsSourceID,
                (section, x, width, y1, y2, floor, isHorizontal) => _corridorCarver.FindAndConnectToNearbyRooms(
                    section, x, width, y1, y2, floor, false,
                    MapWidth, MapHeight,
                    (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(s, sx, ex, py, ft),
                    (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(s, px, sy, ey, ft)
                )
            );

            _multiSectionCoordinator.CreateVerticalCorridorPart(
                bottomSection,
                0,
                10,
                passageX,
                tunnelWidth,
                bottomFloorTile,
                MapWidth,
                MapHeight,
                FloorsTileMap,
                WallsTileMap,
                MAP_LAYER,
                FloorsSourceID,
                (section, x, width, y1, y2, floor, isHorizontal) => _corridorCarver.FindAndConnectToNearbyRooms(
                    section, x, width, y1, y2, floor, false,
                    MapWidth, MapHeight,
                    (s, sx, ex, py, ft) => CreateHorizontalConnectionToRoom(s, sx, ex, py, ft),
                    (s, px, sy, ey, ft) => CreateVerticalConnectionToRoom(s, px, sy, ey, ft)
                )
            );

            if (SectionSpacing > 0)
            {
                _multiSectionCoordinator.FillVerticalGap(
                    topSection,
                    bottomSection,
                    passageX,
                    tunnelWidth,
                    SectionSpacing,
                    MapHeight,
                    FloorsTileMap,
                    WallsTileMap,
                    MAP_LAYER,
                    FloorsSourceID,
                    biome => _biome.GetFloorTileForBiome(biome)
                );
            }

            _sectionConnector.AddDecorativeVerticalWalls(
                topSection,
                bottomSection,
                passageX,
                tunnelWidth,
                MapWidth,
                MapHeight,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );

            _sectionConnector.AddWallsAroundVerticalConnector(
                topSection,
                bottomSection,
                passageX,
                tunnelWidth,
                MapWidth,
                MapHeight,
                SectionSpacing,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );

            Logger.Debug($"Vertical connection created between sections at X={passageX}", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error connecting sections vertically: {e.Message}\n{e.StackTrace}");
        }
    }

    // Метод CreateHorizontalCorridorPart перенесён в MultiSectionCoordinator

    // Метод CreateVerticalCorridorPart перенесён в MultiSectionCoordinator

    // Метод FillHorizontalGap перенесён в MultiSectionCoordinator

    // Метод FillVerticalGap перенесён в MultiSectionCoordinator

    // Методы AddDecorativeHorizontalWalls/AddDecorativeVerticalWalls перенесены в SectionConnector

    // НОВЫЙ метод: Находит и соединяет коридор с ближайшими комнатами
    // Вынесено: CorridorCarver.FindAndConnectToNearbyRooms

    // НОВЫЙ метод: Создает вертикальное соединение между точками
    private void CreateVerticalConnectionToRoom(MapSection section, int x, int startY, int endY, Vector2I floorTile)
    {
        try
        {
            Vector2 worldOffset = section.WorldOffset;

            // Ширина прохода
            int width = 3; // Можно изменить для более узких/широких проходов

            for (int offsetX = -width / 2; offsetX <= width / 2; offsetX++)
            {
                int posX = x + offsetX;

                if (posX < 0 || posX >= MapWidth)
                    continue;

                // Выбираем направление (сверху вниз или снизу вверх)
                int yStart = Math.Min(startY, endY);
                int yEnd = Math.Max(startY, endY);

                for (int posY = yStart; posY <= yEnd; posY++)
                {
                    if (posY < 0 || posY >= MapHeight)
                        continue;

                    // Вычисляем мировую позицию
                    Vector2I worldPos = new Vector2I(
                        (int)worldOffset.X + posX,
                        (int)worldOffset.Y + posY
                    );

                    // Размещаем пол
                    FloorsTileMap.SetCell(worldPos, FloorsSourceID, floorTile);

                    // ВАЖНО: Удаляем все стены и препятствия
                    WallsTileMap.EraseCell(worldPos);

                    // Обновляем маску секции
                    if (posX < MapWidth && posY < MapHeight)
                    {
                        section.SectionMask[posX, posY] = TileType.Corridor;
                    }
                }
            }

            // Добавляем декоративные стены (перенесено в SectionConnector)
            _sectionConnector.AddDecorativeWallsForConnection(
                section,
                x,
                width,
                startY,
                endY,
                false,
                MapWidth,
                MapHeight,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );
        }
        catch (Exception e)
        {
            Logger.Error($"Error creating vertical connection: {e.Message}");
        }
    }

    // НОВЫЙ метод: Создает горизонтальное соединение между точками
    private void CreateHorizontalConnectionToRoom(MapSection section, int startX, int endX, int y, Vector2I floorTile)
    {
        try
        {
            Vector2 worldOffset = section.WorldOffset;

            // Ширина прохода
            int width = 3; // Можно изменить для более узких/широких проходов

            for (int offsetY = -width / 2; offsetY <= width / 2; offsetY++)
            {
                int posY = y + offsetY;

                if (posY < 0 || posY >= MapHeight)
                    continue;

                // Выбираем направление (слева направо или справа налево)
                int xStart = Math.Min(startX, endX);
                int xEnd = Math.Max(startX, endX);

                for (int posX = xStart; posX <= xEnd; posX++)
                {
                    if (posX < 0 || posX >= MapWidth)
                        continue;

                    // Вычисляем мировую позицию
                    Vector2I worldPos = new Vector2I(
                        (int)worldOffset.X + posX,
                        (int)worldOffset.Y + posY
                    );

                    // Размещаем пол
                    FloorsTileMap.SetCell(worldPos, FloorsSourceID, floorTile);

                    // ВАЖНО: Удаляем все стены и препятствия
                    WallsTileMap.EraseCell(worldPos);

                    // Обновляем маску секции
                    if (posX < MapWidth && posY < MapHeight)
                    {
                        section.SectionMask[posX, posY] = TileType.Corridor;
                    }
                }
            }

            // Добавляем декоративные стены (перенесено в SectionConnector)
            _sectionConnector.AddDecorativeWallsForConnection(
                section,
                y,
                width,
                startX,
                endX,
                true,
                MapWidth,
                MapHeight,
                WallsTileMap,
                MAP_LAYER,
                WallsSourceID,
                (biome, pos) => _biome.GetWallTileForBiome(biome, pos)
            );
        }
        catch (Exception e)
        {
            Logger.Error($"Error creating horizontal connection: {e.Message}");
        }
    }

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
    // Односекционный режим удалён

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
                    FloorsTileMap.SetCell(worldPos, FloorsSourceID, backgroundTile);
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
        // Делегируем расчёт прямоугольников комнат во вспомогательный класс,
        // а фактическое заполнение тайлов выполняем через существующий метод CreateSectionRoom,
        // чтобы не менять текущее визуальное поведение.
        int attempts = _roomPlacer.GenerateSectionRooms(section, (Rect2I roomRect) =>
        {
            CreateSectionRoom(section, roomRect);
            Logger.Debug($"Created room in section ({section.GridX},{section.GridY}) at ({roomRect.Position.X},{roomRect.Position.Y}) with size {roomRect.Size.X}x{roomRect.Size.Y}", false);
        });

        Logger.Debug($"Generated {section.Rooms.Count} rooms in section ({section.GridX},{section.GridY}) after {attempts} attempts", false);
    }

    // НОВОЕ: Метод для создания комнаты в секции
    private void CreateSectionRoom(MapSection section, Rect2I room)
    {
        // Выбор тайла пола в зависимости от биома
        Vector2I floorTile = _biome.GetFloorTileForBiome(section.BiomeType);
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
                    FloorsTileMap.SetCell(worldPos, FloorsSourceID, floorTile);
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
        var queue = new System.Collections.Generic.Queue<Vector2I>();
        var came = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        var visited = new System.Collections.Generic.HashSet<Vector2I>();

        foreach (var s in starts)
        {
            if (s.X < 0 || s.X >= MapWidth || s.Y < 0 || s.Y >= MapHeight) continue;
            if (section.SectionMask[s.X, s.Y] == TileType.Room) continue;
            queue.Enqueue(s);
            visited.Add(s);
        }

        Vector2I? goal = null;
        var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (section.SectionMask[p.X, p.Y] == TileType.Corridor)
            {
                goal = p; break;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.X >= MapWidth || n.Y < 0 || n.Y >= MapHeight) continue;
                if (visited.Contains(n)) continue;
                // Можно идти через фон/стены/декор/коридор, но не через другие комнаты
                var t = section.SectionMask[n.X, n.Y];
                if (t == TileType.Room) continue;
                visited.Add(n);
                came[n] = p;
                queue.Enqueue(n);
            }
        }

        if (goal == null) return null;

        var path = new System.Collections.Generic.List<Vector2I>();
        var cur = goal.Value;
        while (came.ContainsKey(cur))
        {
            path.Add(cur);
            cur = came[cur];
        }
        path.Reverse();
        return path;
    }

    // НОВОЕ: Метод для соединения двух комнат в секции
    // Локальные методы карвинга перенесены в CorridorCarver

    // НОВОЕ: Метод для создания горизонтального тоннеля в секции
    // Методы CreateSectionHorizontalTunnel/CreateSectionVerticalTunnel перенесены в CorridorCarver

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

                        // Размещаем стену
                        var wallTile = _biome.GetWallTileForBiome(section.BiomeType, worldPos);
                        WallsTileMap.SetCell(worldPos, WallsSourceID, wallTile);
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

    // Методы стен, декораций и опасных зон перенесены в Decorator.cs

    // НОВОЕ: Метод для получения безопасной точки спавна в секции (в ТАЙЛОВЫХ координатах секции)
    private Vector2 GetSectionSpawnPosition(MapSection section)
    {
        if (section.Rooms.Count == 0)
        {
            Logger.Error($"No rooms available for spawn in section ({section.GridX},{section.GridY})!");
            return Vector2.Zero;
        }

        // Случайная комната
        int roomIndex = _random.Next(0, section.Rooms.Count);
        Rect2I room = section.Rooms[roomIndex];

        // Стартуем с центра комнаты
        Vector2I center = room.Position + room.Size / 2;

        // Локальная функция проверки проходимости тайла
        bool IsWalkableTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= MapWidth || y >= MapHeight) return false;
            var t = section.SectionMask[x, y];
            return t == TileType.Room || t == TileType.Corridor || t == TileType.Background; // допустимые
        }

        // Функция проверки, что тайл имеет выход (минимум одного соседа-прохода)
        bool HasExit(int x, int y)
        {
            var dirs = new[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
            foreach (var d in dirs)
            {
                int nx = x + d.X;
                int ny = y + d.Y;
                if (nx < 0 || ny < 0 || nx >= MapWidth || ny >= MapHeight) continue;
                var t = section.SectionMask[nx, ny];
                if (t == TileType.Room || t == TileType.Corridor || t == TileType.Background)
                    return true;
            }
            return false;
        }

        // Если центр подходит и есть выход — используем его
        if (IsWalkableTile(center.X, center.Y) && HasExit(center.X, center.Y))
        {
            Logger.Debug($"Spawn tile chosen at room center ({center.X}, {center.Y}) in section ({section.GridX},{section.GridY})", false);
            return new Vector2(center.X, center.Y);
        }

        // Ищем ближайший проходимый тайл внутри комнаты (по расширяющимся квадратным слоям)
        int maxRadius = Math.Max(room.Size.X, room.Size.Y);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Пропускаем внутренние точки, оставляем только «кольцо» текущего радиуса
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;

                    int tx = center.X + dx;
                    int ty = center.Y + dy;

                    // Должно быть внутри комнаты
                    if (tx < room.Position.X || ty < room.Position.Y || tx >= room.Position.X + room.Size.X || ty >= room.Position.Y + room.Size.Y)
                        continue;

                    if (IsWalkableTile(tx, ty) && HasExit(tx, ty))
                    {
                        Logger.Debug($"Spawn tile adjusted to ({tx}, {ty}) in section ({section.GridX},{section.GridY})", false);
                        return new Vector2(tx, ty);
                    }
                }
            }
        }

        // Если подходящего тайла в комнате не нашли, пробуем выбрать ближайший тайл КОРИДОРА
        int bestDist = int.MaxValue;
        Vector2I bestCorridor = center;
        for (int x = room.Position.X; x < room.Position.X + room.Size.X; x++)
        for (int y = room.Position.Y; y < room.Position.Y + room.Size.Y; y++)
        {
            // Сканируем всю секцию на случай, если коридор вне комнаты
        }
        for (int x = 0; x < MapWidth; x++)
        for (int y = 0; y < MapHeight; y++)
        {
            if (section.SectionMask[x, y] == TileType.Corridor && HasExit(x, y))
            {
                int dx = x - center.X;
                int dy = y - center.Y;
                int d2 = dx*dx + dy*dy;
                if (d2 < bestDist)
                {
                    bestDist = d2;
                    bestCorridor = new Vector2I(x, y);
                }
            }
        }
        if (bestDist != int.MaxValue)
        {
            Logger.Debug($"Spawn moved to nearest corridor tile ({bestCorridor.X},{bestCorridor.Y}) in section ({section.GridX},{section.GridY})", false);
            return new Vector2(bestCorridor.X, bestCorridor.Y);
        }

        // Фолбэк: верхний левый угол комнаты (как тайл)
        Logger.Error($"No walkable tile found in room in section ({section.GridX},{section.GridY}). Falling back to room origin.");
        return new Vector2(room.Position.X, room.Position.Y);
    }

    // Метод для получения позиции спавна игрока
    // Односекционный режим удалён

    [Signal] public delegate void PlayerSpawnedEventHandler(Node2D player);

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

        // Эмитим сигнал для систем, которым нужен игрок (например, камера)
        if (_currentPlayer != null)
        {
            EmitSignal(SignalName.PlayerSpawned, _currentPlayer);
        }
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
            Logger.Debug($"Player teleported to room {roomIndex} at world position {worldPos}", true);
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