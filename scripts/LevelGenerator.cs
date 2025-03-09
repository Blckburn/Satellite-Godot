using Godot;
using System;
using System.Collections.Generic;

public partial class LevelGenerator : Node
{
    // Ссылка на TileMap для размещения тайлов
    [Export] public Godot.TileMap TileMap { get; set; }

    // Индексы слоев
    [Export] public int BaseLayerIndex { get; set; } = 0;  // Для пола
    [Export] public int DecorationLayerIndex { get; set; } = 1;  // Для первого уровня стен/декораций
    [Export] public int WallLayerIndex { get; set; } = 2;  // Для высоких стен/препятствий

    // Настройки размера карты
    [Export] public int MapWidth { get; set; } = 50;
    [Export] public int MapHeight { get; set; } = 50;

    // Настройки комнат
    [Export] public int MinRoomSize { get; set; } = 5;  // Увеличено для более заметных комнат
    [Export] public int MaxRoomSize { get; set; } = 12; // Увеличено для более заметных комнат
    [Export] public int MaxRooms { get; set; } = 12;
    [Export] public int MinRoomDistance { get; set; } = 2;  // Минимальное расстояние между комнатами

    // Настройки коридоров
    [Export] public int CorridorWidth { get; set; } = 2;  // Ширина коридоров (1-3)

    // Настройки биомов
    [Export] public int BiomeType { get; set; } = 0; // 0 - Default, 1 - Forest, 2 - Desert, etc.
    [Export] public int MaxBiomeTypes { get; set; } = 6; // Общее количество типов биомов

    // ID источника тайлов в тайлсете (обычно 0 для первого атласа)
    [Export] public int SourceID { get; set; } = 0;

    // Клавиша для генерации нового уровня
    [Export] public Key GenerationKey { get; set; } = Key.G;

    // Настройки декорирования
    [Export] public int DecorationDensity { get; set; } = 25; // Процент шанса декорации в комнате (0-100)

    // Псевдослучайный генератор
    private Random _random;

    // Список сгенерированных комнат
    private List<Rect2I> _rooms = new List<Rect2I>();

    // Тайлы для фонового заполнения (вокруг комнат и коридоров)
    private Vector2I _backgroundTile;

    // ПРАВИЛЬНЫЕ КООРДИНАТЫ ТАЙЛОВ из AtlasCoordinates.txt и скриншотов
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

    // Маска карты для отслеживания тайлов
    private enum TileType
    {
        None,       // Пустой тайл
        Background, // Фоновый тайл
        Room,       // Тайл комнаты
        Corridor,   // Тайл коридора
        Wall,       // Тайл стены
        Decoration  // Тайл декорации
    }

    private TileType[,] _mapMask;

    // Флаг, указывающий, что уровень был сгенерирован хотя бы раз
    private bool _levelGenerated = false;

    public override void _Ready()
    {
        // Инициализируем генератор случайных чисел
        _random = new Random();

        // Инициализируем маску карты
        _mapMask = new TileType[MapWidth, MapHeight];

        // Если TileMap не указан, ищем его в дереве сцены
        if (TileMap == null)
        {
            TileMap = GetNode<Godot.TileMap>("../TileMap");

            if (TileMap == null)
            {
                Logger.Debug("LevelGenerator: TileMap not found in parent, searching in whole scene...", true);
                TileMap = FindTileMapInScene();

                if (TileMap == null)
                {
                    Logger.Error("LevelGenerator: TileMap not found!");
                    return;
                }
            }
        }

        // Проверяем, есть ли тайлсет
        if (TileMap.TileSet == null)
        {
            Logger.Error("TileMap does not have a TileSet assigned!");
            return;
        }

        Logger.Debug($"TileMap found: {TileMap.Name}, TileSet OK", true);

        // Генерируем начальный уровень при запуске с небольшой задержкой
        GetTree().CreateTimer(0.5).Timeout += () => GenerateRandomLevel();
    }

    // Метод для поиска TileMap в сцене
    private Godot.TileMap FindTileMapInScene()
    {
        // Поиск TileMap в корне сцены и ее дочерних узлах
        return FindTileMapInNode(GetTree().Root);
    }

    // Рекурсивный поиск TileMap в узле
    private Godot.TileMap FindTileMapInNode(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Godot.TileMap tileMap)
                return tileMap;

            var result = FindTileMapInNode(child);
            if (result != null)
                return result;
        }

        return null;
    }

    // Обработка ввода для генерации нового уровня по нажатию клавиши
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == GenerationKey)
        {
            GenerateRandomLevel();
        }
    }

    // Генерация уровня со случайным биомом
    public void GenerateRandomLevel()
    {
        try
        {
            // Выбираем случайный биом
            int randomBiome = new Random().Next(0, MaxBiomeTypes);
            BiomeType = randomBiome;

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

            // Заполняем всю карту фоновыми тайлами
            FillMapWithBackgroundTiles();

            // Генерируем комнаты
            GenerateRooms();

            // Соединяем комнаты коридорами
            ConnectRooms();

            // Добавляем стены вокруг комнат и коридоров
            AddWalls();

            // Добавляем декорации и препятствия
            AddDecorationsAndObstacles();

            // Добавляем опасные зоны (вода/лава)
            AddHazards();

            // Устанавливаем флаг, что уровень был сгенерирован
            _levelGenerated = true;

            Logger.Debug($"Level generated with {_rooms.Count} rooms", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error during level generation: {e.Message}\n{e.StackTrace}");
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
        if (TileMap != null)
        {
            try
            {
                TileMap.Clear();
                Logger.Debug("TileMap cleared successfully", false);
            }
            catch (Exception e)
            {
                Logger.Error($"Error clearing TileMap: {e.Message}");
            }
        }
    }

    // Выбор фонового тайла в зависимости от биома
    private Vector2I GetBackgroundTileForBiome()
    {
        switch (BiomeType)
        {
            case 1: return Stone; // Forest с каменным фоном
            case 2: return Stone; // Desert с каменным фоном
            case 3: return Stone; // Ice с каменным фоном
            case 4: return Stone; // Techno с каменным фоном
            case 5: return Stone; // Anomal с каменным фоном
            default: return Stone; // Default с каменным фоном
        }
    }

    // Метод для заполнения карты фоновыми тайлами
    private void FillMapWithBackgroundTiles()
    {
        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                try
                {
                    TileMap.SetCell(BaseLayerIndex, new Vector2I(x, y), SourceID, _backgroundTile);
                    _mapMask[x, y] = TileType.Background;
                }
                catch (Exception e)
                {
                    Logger.Debug($"Error setting tile at ({x}, {y}): {e.Message}", false);
                }
            }
        }

        Logger.Debug($"Map filled with background tiles for biome type: {BiomeType}", true);
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
                    TileMap.SetCell(BaseLayerIndex, new Vector2I(x, y), SourceID, floorTile);
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
                        TileMap.SetCell(BaseLayerIndex, new Vector2I(x, yPos), SourceID, floorTile);
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
                        TileMap.SetCell(BaseLayerIndex, new Vector2I(xPos, y), SourceID, floorTile);
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

    // Метод для получения тайла пола в зависимости от биома
    private Vector2I GetFloorTileForBiome()
    {
        switch (BiomeType)
        {
            case 1: // Forest
                return ForestFloor; // (2, 1)
            case 2: // Desert
                return Sand; // (4, 0)
            case 3: // Ice
                return Snow; // (3, 0)
            case 4: // Techno
                return Techno; // (3, 1)
            case 5: // Anomal
                return Anomal; // (4, 1)
            default: // Default
                return Grass; // (0, 0)
        }
    }

    // Метод для добавления стен вокруг проходимых областей
    private void AddWalls()
    {
        // Выбор тайла стены в зависимости от биома
        Vector2I wallTile = GetWallTileForBiome();

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
                // Первый уровень стены на DecorationLayer
                TileMap.SetCell(DecorationLayerIndex, pos, SourceID, wallTile);

                // Второй уровень стены на WallLayer (для некоторых стен)
                if (_random.Next(0, 100) < 70) // 70% шанс добавить второй уровень
                {
                    TileMap.SetCell(WallLayerIndex, pos, SourceID, wallTile);
                }
            }
            catch
            {
                // Игнорируем ошибки при установке тайлов
            }
        }

        Logger.Debug($"Added {wallPositions.Count} wall tiles", true);
    }

    // Получение тайла стены в зависимости от биома
    private Vector2I GetWallTileForBiome()
    {
        switch (BiomeType)
        {
            case 1: // Forest
                return Stone; // (1, 0)
            case 2: // Desert
                return Stone; // (1, 0)
            case 3: // Ice
                return Ice; // (0, 1)
            case 4: // Techno
                return Stone; // (1, 0)
            case 5: // Anomal
                return Stone; // (1, 0)
            default:
                return Stone; // (1, 0)
        }
    }

    // Метод для добавления декораций и препятствий в комнаты и коридоры
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
                        // Размещаем декорацию
                        TileMap.SetCell(DecorationLayerIndex, new Vector2I(x, y), SourceID, decorationTile);
                        _mapMask[x, y] = TileType.Decoration;

                        // Некоторые декорации добавляем и на верхний слой для объемности
                        if (_random.Next(0, 100) < 40) // 40% шанс 
                        {
                            TileMap.SetCell(WallLayerIndex, new Vector2I(x, y), SourceID, decorationTile);
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки при размещении декораций
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
                        TileMap.SetCell(DecorationLayerIndex, new Vector2I(x, y), SourceID, decorationTile);
                        _mapMask[x, y] = TileType.Decoration;
                    }
                    catch
                    {
                        // Игнорируем ошибки
                    }
                }
            }
        }
    }

    // Получение тайла декорации в зависимости от биома
    private Vector2I GetDecorationTileForBiome()
    {
        switch (BiomeType)
        {
            case 1: // Forest
                return Stone; // Камни в лесу
            case 2: // Desert
                return Stone; // Камни в пустыне
            case 3: // Ice
                return Stone; // Камни во льду
            case 4: // Techno
                return Stone; // Технические блоки
            case 5: // Anomal
                return Stone; // Аномальные объекты
            default:
                return Stone; // Обычные камни
        }
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
                    // Размещаем опасный тайл на базовом слое
                    TileMap.SetCell(BaseLayerIndex, new Vector2I(x, y), SourceID, hazardTile);
                }
            }

            Logger.Debug($"Added {hazardWidth}x{hazardHeight} hazard area in room {roomIndex}", false);
        }
        catch (Exception e)
        {
            Logger.Debug($"Error adding hazards: {e.Message}", false);
        }
    }
}