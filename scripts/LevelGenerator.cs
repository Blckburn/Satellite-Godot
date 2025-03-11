using Godot;
using System;
using System.Collections.Generic;

public partial class LevelGenerator : Node
{
    // Сигнал о завершении генерации уровня с передачей точки спавна
    [Signal] public delegate void LevelGeneratedEventHandler(Vector2 spawnPosition);

    // Ссылка на TileMap для размещения тайлов
    [Export] public Godot.TileMap TileMap { get; set; }

    // Сцена игрока для спавна
    [Export] public PackedScene PlayerScene { get; set; }

    // Индексы слоев
    [Export] public int BaseLayerIndex { get; set; } = 0;  // Для пола
    [Export] public int DecorationLayerIndex { get; set; } = 1;  // Для первого уровня стен/декораций
    [Export] public int WallLayerIndex { get; set; } = 2;  // Для высоких стен/препятствий

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

    // ID источника тайлов в тайлсете
    [Export] public int SourceID { get; set; } = 0;

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

    // Типы тайлов для маски карты
    private enum TileType
    {
        None,
        Background,
        Room,
        Corridor,
        Wall,
        Decoration
    }

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

        // Поиск TileMap, если не указан
        if (TileMap == null)
        {
            // Сначала ищем узел isometric_tileset
            var isometricTileset = GetNode<Node2D>("../isometric_tileset");

            if (isometricTileset != null)
            {
                // Ищем TileMap внутри isometric_tileset
                TileMap = isometricTileset.GetNode<Godot.TileMap>("TileMap");

                if (TileMap != null)
                {
                    Logger.Debug("TileMap найден внутри isometric_tileset!", true);
                }
            }

            // Если не нашли TileMap через прямой путь, применяем рекурсивный поиск
            if (TileMap == null)
            {
                Logger.Debug("TileMap не найден через прямой путь, выполняется полный поиск...", true);
                TileMap = FindTileMapInScene();

                if (TileMap == null)
                {
                    Logger.Error("LevelGenerator: TileMap не найден нигде в сцене!");
                    return;
                }
            }
        }

        // Проверка тайлсета
        if (TileMap.TileSet == null)
        {
            Logger.Error("TileMap не имеет назначенного TileSet!");
            return;
        }

        Logger.Debug($"TileMap найден: {TileMap.Name}, TileSet OK", true);

        // Генерируем начальный уровень с задержкой
        GetTree().CreateTimer(0.5).Timeout += () => GenerateRandomLevel();
    }

    // Обработка ввода для генерации нового уровня
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == GenerationKey)
        {
            GenerateRandomLevel();
        }
    }

    // Метод для поиска TileMap в сцене
    private Godot.TileMap FindTileMapInScene()
    {
        return FindTileMapInNode(GetTree().Root);
    }

    // Рекурсивный поиск TileMap в узле
    private Godot.TileMap FindTileMapInNode(Node node)
    {
        // Выводим отладочную информацию о каждом проверяемом узле
        Logger.Debug($"Проверяем узел: {node.Name} (Тип: {node.GetType().Name})", false);

        // Перебираем все дочерние узлы
        foreach (var child in node.GetChildren())
        {
            // Если узел - TileMap, возвращаем его
            if (child is Godot.TileMap tileMap)
            {
                Logger.Debug($"Найден TileMap в узле: {node.Name}/{child.Name}", true);
                return tileMap;
            }

            // Если узел - isometric_tileset, проверяем его напрямую на наличие TileMap
            if (child.Name == "isometric_tileset")
            {
                var possibleTileMap = child.GetNodeOrNull<Godot.TileMap>("TileMap");
                if (possibleTileMap != null)
                {
                    Logger.Debug($"Найден TileMap внутри isometric_tileset!", true);
                    return possibleTileMap;
                }
            }

            // Рекурсивно проверяем дочерние узлы
            var result = FindTileMapInNode(child);
            if (result != null)
                return result;
        }

        return null;
    }

    // Генерация уровня со случайным биомом
    public void GenerateRandomLevel()
    {
        try
        {
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

            // Вычисляем позицию спавна игрока
            _currentSpawnPosition = GetPlayerSpawnPosition();

            // Устанавливаем флаг, что уровень был сгенерирован
            _levelGenerated = true;

            Logger.Debug($"Level generated with {_rooms.Count} rooms", true);

            // Вызываем сигнал о завершении генерации
            EmitSignal(SignalName.LevelGenerated, _currentSpawnPosition);

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
        if (!_levelGenerated)
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
            _currentPlayer.Position = _currentSpawnPosition;

            // Добавляем игрока в группу для быстрого поиска
            if (!_currentPlayer.IsInGroup(PlayerGroup))
            {
                _currentPlayer.AddToGroup(PlayerGroup);
            }

            // Найти контейнер с Y-сортировкой
            var ySortContainer = GetParent().GetNodeOrNull<Node2D>("YSortContainer");

            if (ySortContainer != null)
            {
                // Добавляем игрока в YSortContainer вместо корневого узла
                ySortContainer.AddChild(_currentPlayer);
                Logger.Debug($"Spawned new player at {_currentSpawnPosition} in YSortContainer", true);
            }
            else
            {
                // Запасной вариант - добавляем как обычно к родителю
                GetParent().AddChild(_currentPlayer);
                Logger.Debug($"YSortContainer not found. Spawned player at {_currentSpawnPosition} in parent node", true);
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

    // Метод для заполнения карты фоновыми тайлами
    private void FillMapWithBackgroundTiles()
    {
        Vector2I backgroundTile = GetBackgroundTileForBiome();
        int tilesAdded = 0;

        for (int x = 0; x < MapWidth; x++)
        {
            for (int y = 0; y < MapHeight; y++)
            {
                // Добавляем фоновый тайл только если клетка имеет тип None или Background
                // и на ней нет никаких тайлов на этом слое
                if (_mapMask[x, y] == TileType.None || _mapMask[x, y] == TileType.Background)
                {
                    // Проверяем, есть ли уже тайлы на этих координатах в DecorationLayerIndex
                    if (TileMap.GetCellSourceId(DecorationLayerIndex, new Vector2I(x, y)) == -1)
                    {
                        try
                        {
                            // Размещаем фоновый тайл на DecorationLayerIndex (Level1)
                            TileMap.SetCell(DecorationLayerIndex, new Vector2I(x, y), SourceID, backgroundTile);
                            _mapMask[x, y] = TileType.Background;

                            tilesAdded++;
                        }
                        catch (Exception e)
                        {
                            Logger.Debug($"Error setting tile at ({x}, {y}): {e.Message}", false);
                        }
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
                    // Размещаем тайл пола
                    TileMap.SetCell(BaseLayerIndex, new Vector2I(x, y), SourceID, floorTile);
                    _mapMask[x, y] = TileType.Room;

                    // Для пола удаляем коллизию, чтобы сделать его проходимым
                    TileMap.EraseCell(WallLayerIndex, new Vector2I(x, y));
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

                        // Удаляем коллизию для коридоров
                        TileMap.EraseCell(WallLayerIndex, new Vector2I(x, yPos));
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

                        // Удаляем коллизию для коридоров
                        TileMap.EraseCell(WallLayerIndex, new Vector2I(xPos, y));
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
                Vector2I primaryWallTile = GetWallTileForBiome(pos);
                Vector2I secondaryWallTile = GetSecondaryWallTileForBiome(pos);

                // Первый уровень стены на DecorationLayer
                TileMap.SetCell(DecorationLayerIndex, pos, SourceID, primaryWallTile);

                // Второй уровень стены на WallLayer (для некоторых стен)
                if (_random.Next(0, 100) < 70) // 70% шанс добавить второй уровень
                {
                    // Ключевое изменение: добавляем тайл на WallLayer чтобы создать коллизию
                    TileMap.SetCell(WallLayerIndex, pos, SourceID, secondaryWallTile);
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"Error placing wall at {pos}: {e.Message}", false);
            }
        }

        Logger.Debug($"Added {wallPositions.Count} wall tiles specific to biome {GetBiomeName(BiomeType)}", true);
    }


    // Получение тайла стены в зависимости от биома
    private Vector2I GetWallTileForBiome(Vector2I position)
    {
        // Если включена вариативность и это не опорная стена, добавляем вариации
        bool useVariation = UseVariedWalls && _random.Next(0, 100) < 30; // 30% шанс вариации

        switch (BiomeType)
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
        switch (BiomeType)
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
                        TileMap.SetCell(DecorationLayerIndex, new Vector2I(x, y), SourceID, decorationTile);
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
                    // Размещаем опасный тайл на базовом слое
                    TileMap.SetCell(BaseLayerIndex, new Vector2I(x, y), SourceID, hazardTile);

                    // Опасные зоны (лава) непроходимы, а вода проходима
                    if (hazardTile == Lava)
                    {
                        // Для лавы создаем коллизию
                        TileMap.SetCell(WallLayerIndex, new Vector2I(x, y), SourceID, hazardTile);
                    }
                    else
                    {
                        // Для воды удаляем коллизию, чтобы сделать ее проходимой
                        TileMap.EraseCell(WallLayerIndex, new Vector2I(x, y));
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
            TileData tileData = TileMap.GetCellTileData(BaseLayerIndex, new Vector2I(x, y));
            if (tileData != null)
            {
                // Устанавливаем пользовательские данные
                tileData.SetCustomData("is_walkable", isWalkable);

                // Обновляем физическую коллизию на основе проходимости
                if (isWalkable)
                    TileMap.EraseCell(WallLayerIndex, new Vector2I(x, y));
                else
                    TileMap.SetCell(WallLayerIndex, new Vector2I(x, y), SourceID, Empty);
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
        Vector2I tileSize = TileMap.TileSet?.TileSize ?? new Vector2I(64, 32);

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
}