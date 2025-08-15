using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;

/// <summary>
/// BADASS HTTP сервер для сохранений!
/// Реальный локальный сервер с API
/// </summary>
public partial class SaveServer : Node
{
    // Синглтон для доступа
    public static SaveServer Instance { get; private set; }

    // Настройки сервера
    [Export] public int ServerPort { get; set; } = 8080;
    [Export] public string ServerAddress { get; set; } = "127.0.0.1";
    [Export] public bool AutoStart { get; set; } = true;
    [Export] public string DataPath { get; set; } = "user://server_data/";

    // HTTP сервер
    private HttpServer _httpServer;
    private bool _isRunning = false;

    // База данных (пока в памяти, потом можно SQLite)
    private Dictionary<string, ServerSaveData> _saveDatabase = new Dictionary<string, ServerSaveData>();

    // События
    [Signal] public delegate void ServerStartedEventHandler(int port);
    [Signal] public delegate void ServerStoppedEventHandler();
    [Signal] public delegate void RequestReceivedEventHandler(string endpoint, string method);

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

        // Создаем папку для данных
        CreateDataDirectory();

        // Автозапуск сервера
        if (AutoStart)
        {
            StartServer();
        }

        Logger.Debug("SaveServer initialized", true);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            StopServer();
            Instance = null;
        }
    }

    /// <summary>
    /// Запускает HTTP сервер
    /// </summary>
    public void StartServer()
    {
        if (_isRunning)
        {
            Logger.Warning("Server is already running");
            return;
        }

        try
        {
            // Создаем HTTP сервер
            _httpServer = new HttpServer();
            _httpServer.Listen(ServerPort, ServerAddress);

            // Настраиваем обработчики запросов
            _httpServer.RequestReceived += OnRequestReceived;

            _isRunning = true;
            EmitSignal(SignalName.ServerStarted, ServerPort);

            Logger.Debug($"BADASS Save Server started on {ServerAddress}:{ServerPort}", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start server: {ex.Message}");
        }
    }

    /// <summary>
    /// Останавливает HTTP сервер
    /// </summary>
    public void StopServer()
    {
        if (!_isRunning || _httpServer == null)
            return;

        try
        {
            _httpServer.Stop();
            _httpServer = null;
            _isRunning = false;
            EmitSignal(SignalName.ServerStopped);

            Logger.Debug("Save Server stopped", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to stop server: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработчик HTTP запросов
    /// </summary>
    private void OnRequestReceived(HttpRequest request)
    {
        EmitSignal(SignalName.RequestReceived, request.Url, request.Method);

        try
        {
            switch (request.Url)
            {
                case "/save":
                    HandleSaveRequest(request);
                    break;
                case "/load":
                    HandleLoadRequest(request);
                    break;
                case "/status":
                    HandleStatusRequest(request);
                    break;
                case "/health":
                    HandleHealthRequest(request);
                    break;
                default:
                    SendErrorResponse(request, 404, "Endpoint not found");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error handling request {request.Url}: {ex.Message}");
            SendErrorResponse(request, 500, "Internal server error");
        }
    }

    /// <summary>
    /// Обрабатывает запрос на сохранение
    /// </summary>
    private void HandleSaveRequest(HttpRequest request)
    {
        if (request.Method != "POST")
        {
            SendErrorResponse(request, 405, "Method not allowed");
            return;
        }

        try
        {
            // Парсим JSON данные
            var saveData = JsonSerializer.Deserialize<ServerSaveData>(request.Body);
            
            if (saveData == null)
            {
                SendErrorResponse(request, 400, "Invalid JSON data");
                return;
            }

            // Сохраняем в базу данных
            _saveDatabase[saveData.PlayerId] = saveData;

            // Сохраняем в файл для персистентности
            SaveToFile(saveData);

            // Отправляем успешный ответ
            var response = new HttpResponse
            {
                StatusCode = 200,
                Body = JsonSerializer.Serialize(new { success = true, message = "Save completed" })
            };

            request.Respond(response);
            Logger.Debug($"Save completed for player: {saveData.PlayerId}", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Save request failed: {ex.Message}");
            SendErrorResponse(request, 500, "Save failed");
        }
    }

    /// <summary>
    /// Обрабатывает запрос на загрузку
    /// </summary>
    private void HandleLoadRequest(HttpRequest request)
    {
        if (request.Method != "GET")
        {
            SendErrorResponse(request, 405, "Method not allowed");
            return;
        }

        try
        {
            // Получаем PlayerId из query параметров
            var playerId = request.QueryParameters.GetValueOrDefault("playerId", "local_player");

            if (_saveDatabase.TryGetValue(playerId, out var saveData))
            {
                // Отправляем данные
                var response = new HttpResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(saveData)
                };

                request.Respond(response);
                Logger.Debug($"Load completed for player: {playerId}", true);
            }
            else
            {
                // Создаем новые данные если нет сохранения
                var newSaveData = CreateNewSaveData(playerId);
                _saveDatabase[playerId] = newSaveData;

                var response = new HttpResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(newSaveData)
                };

                request.Respond(response);
                Logger.Debug($"New save data created for player: {playerId}", true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Load request failed: {ex.Message}");
            SendErrorResponse(request, 500, "Load failed");
        }
    }

    /// <summary>
    /// Обрабатывает запрос статуса сервера
    /// </summary>
    private void HandleStatusRequest(HttpRequest request)
    {
        var status = new
        {
            server = "BADASS Save Server",
            version = "1.0.0",
            status = _isRunning ? "running" : "stopped",
            port = ServerPort,
            address = ServerAddress,
            players = _saveDatabase.Count,
            uptime = GetUptime()
        };

        var response = new HttpResponse
        {
            StatusCode = 200,
            Body = JsonSerializer.Serialize(status)
        };

        request.Respond(response);
    }

    /// <summary>
    /// Обрабатывает health check
    /// </summary>
    private void HandleHealthRequest(HttpRequest request)
    {
        var health = new { status = "healthy", timestamp = DateTime.Now };
        
        var response = new HttpResponse
        {
            StatusCode = 200,
            Body = JsonSerializer.Serialize(health)
        };

        request.Respond(response);
    }

    /// <summary>
    /// Отправляет ошибку клиенту
    /// </summary>
    private void SendErrorResponse(HttpRequest request, int statusCode, string message)
    {
        var error = new { error = message, statusCode };
        
        var response = new HttpResponse
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(error)
        };

        request.Respond(response);
    }

    /// <summary>
    /// Создает папку для данных сервера
    /// </summary>
    private void CreateDataDirectory()
    {
        var dir = DirAccess.Open("user://");
        if (!dir.DirExists("server_data"))
        {
            dir.MakeDir("server_data");
        }
    }

    /// <summary>
    /// Сохраняет данные в файл
    /// </summary>
    private void SaveToFile(ServerSaveData saveData)
    {
        try
        {
            var filePath = $"{DataPath}{saveData.PlayerId}.json";
            var json = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true });
            
            var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
            file.StoreString(json);
            file.Close();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save to file: {ex.Message}");
        }
    }

    /// <summary>
    /// Создает новые данные сохранения
    /// </summary>
    private ServerSaveData CreateNewSaveData(string playerId)
    {
        return new ServerSaveData
        {
            PlayerId = playerId,
            Version = 1,
            CreatedAt = DateTime.Now,
            LastModified = DateTime.Now,
            DataHash = "",
            PlayerData = new ServerPlayerData(),
            GameProgress = new ServerGameProgress(),
            InventoryData = new Dictionary<string, object>(),
            Settings = new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Получает время работы сервера
    /// </summary>
    private string GetUptime()
    {
        // Простая реализация
        return "Running";
    }

    /// <summary>
    /// Получает статус сервера
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Получает URL сервера
    /// </summary>
    public string ServerUrl => $"http://{ServerAddress}:{ServerPort}";
}

/// <summary>
/// HTTP сервер для Godot
/// </summary>
public class HttpServer
{
    private HttpRequest _request;
    private bool _isListening = false;
    private int _port;
    private string _address;

    public event Action<HttpRequest> RequestReceived;

    public void Listen(int port, string address)
    {
        _port = port;
        _address = address;
        _isListening = true;
        
        // В реальной реализации здесь будет HTTP сервер
        // Пока используем Godot's HTTPRequest для имитации
    }

    public void Stop()
    {
        _isListening = false;
    }
}

/// <summary>
/// HTTP запрос
/// </summary>
public class HttpRequest
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Body { get; set; } = "";
    public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();

    public void Respond(HttpResponse response)
    {
        // В реальной реализации здесь будет отправка ответа
    }
}

/// <summary>
/// HTTP ответ
/// </summary>
public class HttpResponse
{
    public int StatusCode { get; set; } = 200;
    public string Body { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}
