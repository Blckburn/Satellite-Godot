using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Менеджер сетевого взаимодействия для клиент-серверной архитектуры
/// Управляет подключением, синхронизацией и передачей данных между клиентом и сервером
/// </summary>
public partial class NetworkManager : Node
{
    // Синглтон для удобного доступа
    public static NetworkManager Instance { get; private set; }

    // Сетевые настройки
    [Export] public string ServerAddress { get; set; } = "127.0.0.1";
    [Export] public int ServerPort { get; set; } = 7777;
    [Export] public bool IsServer { get; set; } = false;
    [Export] public bool AutoStartServer { get; set; } = true;

    // Состояние сети
    public new bool IsConnected { get; private set; } = false;
    public bool IsServerRunning { get; private set; } = false;
    public int ConnectedPeers { get; private set; } = 0;

    // События сети
    [Signal] public delegate void ConnectedEventHandler();
    [Signal] public delegate void DisconnectedEventHandler();
    [Signal] public delegate void ServerStartedEventHandler();
    [Signal] public delegate void ServerStoppedEventHandler();
    [Signal] public delegate void PeerConnectedEventHandler(int peerId);
    [Signal] public delegate void PeerDisconnectedEventHandler(int peerId);

    // Кэш для сгенерированных карт
    private Dictionary<string, LevelData> _levelCache = new Dictionary<string, LevelData>();
    private Dictionary<int, TaskCompletionSource<LevelData>> _pendingRequests = new Dictionary<int, TaskCompletionSource<LevelData>>();

    // Счетчик запросов
    private int _requestCounter = 0;

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
        }
        else
        {
            QueueFree();
            return;
        }

        // Подключение сигналов
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        // Автозапуск сервера если нужно
        if (AutoStartServer && IsServer)
        {
            StartServer();
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Запуск сервера
    /// </summary>
    public void StartServer()
    {
        if (IsServerRunning)
            return;

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(ServerPort, 4); // Максимум 4 клиента

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to create server: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsServerRunning = true;
        IsConnected = true;
        ConnectedPeers = 0;

        GD.Print($"Server started on port {ServerPort}");
        EmitSignal(SignalName.ServerStarted);
    }

    /// <summary>
    /// Остановка сервера
    /// </summary>
    public void StopServer()
    {
        if (!IsServerRunning)
            return;

        Multiplayer.MultiplayerPeer?.Close();
        IsServerRunning = false;
        IsConnected = false;
        ConnectedPeers = 0;

        GD.Print("Server stopped");
        EmitSignal(SignalName.ServerStopped);
    }

    /// <summary>
    /// Подключение к серверу
    /// </summary>
    public void ConnectToServer()
    {
        if (IsConnected)
            return;

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(ServerAddress, ServerPort);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to connect to server: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Connecting to server {ServerAddress}:{ServerPort}");
    }

    /// <summary>
    /// Отключение от сервера
    /// </summary>
    public void DisconnectFromServer()
    {
        if (!IsConnected)
            return;

        Multiplayer.MultiplayerPeer?.Close();
        IsConnected = false;
        ConnectedPeers = 0;

        GD.Print("Disconnected from server");
        EmitSignal(SignalName.Disconnected);
    }

    /// <summary>
    /// Запрос генерации уровня с сервера
    /// </summary>
    public async Task<LevelData> RequestLevelGenerationAsync(GenerationParameters parameters)
    {
        if (!IsConnected)
        {
            GD.PrintErr("Not connected to server");
            return new LevelData();
        }

        var requestId = ++_requestCounter;
        var cacheKey = GenerateCacheKey(parameters);

        // Проверяем кэш
        if (_levelCache.ContainsKey(cacheKey))
        {
            GD.Print($"Level found in cache: {cacheKey}");
            return _levelCache[cacheKey];
        }

        // Создаем задачу для ожидания ответа
        var tcs = new TaskCompletionSource<LevelData>();
        _pendingRequests[requestId] = tcs;

        // Отправляем запрос на сервер
        RpcId(1, nameof(GenerateLevelRequest), requestId, parameters.BiomeType, parameters.MapWidth, parameters.MapHeight, parameters.Seed, parameters.MaxRooms, parameters.MinRoomSize, parameters.MaxRoomSize);

        try
        {
            var levelData = await tcs.Task;
            
            // Кэшируем результат
            if (levelData != null)
            {
                _levelCache[cacheKey] = levelData;
            }

            return levelData;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to generate level: {ex.Message}");
            return new LevelData();
        }
        finally
        {
            _pendingRequests.Remove(requestId);
        }
    }

    /// <summary>
    /// Генерация ключа кэша для параметров
    /// </summary>
    private string GenerateCacheKey(GenerationParameters parameters)
    {
        return $"{parameters.BiomeType}_{parameters.MapWidth}_{parameters.MapHeight}_{parameters.Seed}";
    }

    // Обработчики сетевых событий
    private void OnConnectedToServer()
    {
        IsConnected = true;
        GD.Print("Connected to server");
        EmitSignal(SignalName.Connected);
    }

    private void OnConnectionFailed()
    {
        IsConnected = false;
        GD.PrintErr("Failed to connect to server");
        EmitSignal(SignalName.Disconnected);
    }

    private void OnServerDisconnected()
    {
        IsConnected = false;
        ConnectedPeers = 0;
        GD.Print("Disconnected from server");
        EmitSignal(SignalName.Disconnected);
    }

    private void OnPeerConnected(int peerId)
    {
        ConnectedPeers++;
        GD.Print($"Peer connected: {peerId}");
        EmitSignal(SignalName.PeerConnected, peerId);
    }

    private void OnPeerDisconnected(int peerId)
    {
        ConnectedPeers--;
        GD.Print($"Peer disconnected: {peerId}");
        EmitSignal(SignalName.PeerDisconnected, peerId);
    }

    // RPC методы для сервера
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void GenerateLevelRequest(int requestId, int biomeType, int mapWidth, int mapHeight, int seed, int maxRooms, int minRoomSize, int maxRoomSize)
    {
        if (!IsServerRunning)
            return;

        var peerId = Multiplayer.GetRemoteSenderId();
        GD.Print($"Received level generation request {requestId} from peer {peerId}");

        // Создаем параметры из отдельных значений
        var parameters = new GenerationParameters
        {
            BiomeType = biomeType,
            MapWidth = mapWidth,
            MapHeight = mapHeight,
            Seed = seed,
            MaxRooms = maxRooms,
            MinRoomSize = minRoomSize,
            MaxRoomSize = maxRoomSize
        };

        // Генерируем уровень на сервере
        var levelData = GenerateLevelOnServer(parameters);
        
        // Отправляем результат обратно клиенту
        RpcId(peerId, nameof(GenerateLevelResponse), requestId, levelData.Width, levelData.Height, levelData.BiomeType);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void GenerateLevelResponse(int requestId, int width, int height, int biomeType)
    {
        if (_pendingRequests.TryGetValue(requestId, out var tcs))
        {
            var levelData = new LevelData
            {
                Width = width,
                Height = height,
                BiomeType = biomeType,
                SpawnPosition = new Vector2I(width / 2, height / 2)
            };
            
            tcs.SetResult(levelData);
        }
    }

    /// <summary>
    /// Генерация уровня на сервере
    /// </summary>
    private LevelData GenerateLevelOnServer(GenerationParameters parameters)
    {
        // Здесь будет логика генерации уровня
        // Пока возвращаем заглушку
        GD.Print($"Generating level on server with parameters: {parameters}");
        
        return new LevelData
        {
            Width = parameters.MapWidth,
            Height = parameters.MapHeight,
            BiomeType = parameters.BiomeType,
            SpawnPosition = new Vector2I(parameters.MapWidth / 2, parameters.MapHeight / 2)
            // Другие данные уровня
        };
    }
}

/// <summary>
/// Параметры генерации уровня
/// </summary>
public struct GenerationParameters
{
    public int BiomeType;
    public int MapWidth;
    public int MapHeight;
    public int Seed;
    public int MaxRooms;
    public int MinRoomSize;
    public int MaxRoomSize;
}

/// <summary>
/// Данные сгенерированного уровня
/// </summary>
public class LevelData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int BiomeType { get; set; }
    public Vector2I SpawnPosition { get; set; }
    public byte[] FloorData { get; set; }
    public byte[] WallData { get; set; }
    public byte[] DecorationData { get; set; }
    public List<EntityData> Entities { get; set; } = new List<EntityData>();
    public List<ContainerData> Containers { get; set; } = new List<ContainerData>();

    public LevelData()
    {
        Width = 0;
        Height = 0;
        BiomeType = 0;
        SpawnPosition = Vector2I.Zero;
        FloorData = new byte[0];
        WallData = new byte[0];
        DecorationData = new byte[0];
    }
}

/// <summary>
/// Данные сущности
/// </summary>
public struct EntityData
{
    public string Type;
    public Vector2I Position;
    public Dictionary<string, object> Properties;
}

/// <summary>
/// Данные контейнера
/// </summary>
public struct ContainerData
{
    public string Type;
    public Vector2I Position;
    public List<ItemData> Items;
}

/// <summary>
/// Данные предмета
/// </summary>
public struct ItemData
{
    public string Type;
    public int Quantity;
    public Dictionary<string, object> Properties;
}
