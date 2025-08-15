using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Современный серверно-ориентированный менеджер сохранений
/// Защищает данные от модификаций и готов к интеграции с Steam
/// </summary>
public partial class ServerSaveManager : Node
{
    // Синглтон
    public static ServerSaveManager Instance { get; private set; }

    // Настройки
    [Export] public bool AutoConnectToServer { get; set; } = true;
    [Export] public bool EnableDataProtection { get; set; } = true;
    [Export] public int SaveIntervalSeconds { get; set; } = 30;
    [Export] public bool EnableAutoSave { get; set; } = true;

    // Состояние
    public bool IsConnectedToServer { get; private set; } = false;
    public bool IsSaving { get; private set; } = false;
    public bool IsLoading { get; private set; } = false;
    public DateTime LastSaveTime { get; private set; }
    public string CurrentPlayerId { get; private set; } = "local_player"; // В будущем будет Steam ID

    // Данные
    private ServerSaveData _currentSaveData;
    private Timer _autoSaveTimer;

    // События
    [Signal] public delegate void SaveCompletedEventHandler(bool success, string message);
    [Signal] public delegate void LoadCompletedEventHandler(bool success, string message);
    [Signal] public delegate void ServerConnectionChangedEventHandler(bool connected);
    [Signal] public delegate void DataIntegrityViolationEventHandler(string details);

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

        // Инициализация
        InitializeSaveData();
        SetupAutoSave();

        // Автоподключение к серверу
        if (AutoConnectToServer)
        {
            ConnectToServer();
        }

        Logger.Debug("ServerSaveManager initialized", true);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Подключается к серверу сохранений
    /// </summary>
    public async void ConnectToServer()
    {
        try
        {
            Logger.Debug("Connecting to save server...", true);
            
            // Имитация подключения к серверу
            await Task.Delay(1000); // В реальности здесь будет HTTP запрос
            
            IsConnectedToServer = true;
            EmitSignal(SignalName.ServerConnectionChanged, true);
            
            Logger.Debug("Connected to save server successfully", true);
            
            // Загружаем последнее сохранение с сервера
            await LoadFromServerAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to connect to save server: {ex.Message}");
            IsConnectedToServer = false;
            EmitSignal(SignalName.ServerConnectionChanged, false);
        }
    }

    /// <summary>
    /// Отключается от сервера
    /// </summary>
    public void DisconnectFromServer()
    {
        IsConnectedToServer = false;
        EmitSignal(SignalName.ServerConnectionChanged, false);
        Logger.Debug("Disconnected from save server", true);
    }

    /// <summary>
    /// Сохраняет данные на сервер
    /// </summary>
    public async Task<bool> SaveToServerAsync()
    {
        if (IsSaving)
        {
            Logger.Warning("Save already in progress");
            return false;
        }

        IsSaving = true;
        bool success = false;
        string message = "";

        try
        {
            Logger.Debug("Saving to server...", true);

            // Обновляем данные перед сохранением
            UpdateSaveData();

            // Защищаем данные если включено
            if (EnableDataProtection)
            {
                ProtectSaveData();
            }

            // Имитация отправки на сервер
            await Task.Delay(500); // В реальности здесь будет HTTP POST

            LastSaveTime = DateTime.Now;
            success = true;
            message = "Save completed successfully";

            Logger.Debug("Save to server completed", true);
        }
        catch (Exception ex)
        {
            message = $"Save failed: {ex.Message}";
            Logger.Error(message);
        }
        finally
        {
            IsSaving = false;
            EmitSignal(SignalName.SaveCompleted, success, message);
        }

        return success;
    }

    /// <summary>
    /// Загружает данные с сервера
    /// </summary>
    public async Task<bool> LoadFromServerAsync()
    {
        if (IsLoading)
        {
            Logger.Warning("Load already in progress");
            return false;
        }

        IsLoading = true;
        bool success = false;
        string message = "";

        try
        {
            Logger.Debug("Loading from server...", true);

            // Имитация загрузки с сервера
            await Task.Delay(300); // В реальности здесь будет HTTP GET

            // Проверяем целостность данных если включено
            if (EnableDataProtection && _currentSaveData != null)
            {
                if (!VerifyDataIntegrity())
                {
                    throw new Exception("Data integrity check failed - possible tampering detected");
                }
            }

            // Применяем загруженные данные
            ApplySaveData();

            success = true;
            message = "Load completed successfully";

            Logger.Debug("Load from server completed", true);
        }
        catch (Exception ex)
        {
            message = $"Load failed: {ex.Message}";
            Logger.Error(message);
            
            if (ex.Message.Contains("tampering"))
            {
                EmitSignal(SignalName.DataIntegrityViolation, ex.Message);
            }
        }
        finally
        {
            IsLoading = false;
            EmitSignal(SignalName.LoadCompleted, success, message);
        }

        return success;
    }

    /// <summary>
    /// Инициализирует данные сохранения
    /// </summary>
    private void InitializeSaveData()
    {
        _currentSaveData = new ServerSaveData
        {
            PlayerId = CurrentPlayerId,
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
    /// Обновляет данные сохранения из текущего состояния игры
    /// </summary>
    private void UpdateSaveData()
    {
        if (_currentSaveData == null) return;

        _currentSaveData.LastModified = DateTime.Now;

        // Обновляем данные игрока
        var player = GetNodeOrNull<Player>("/root/Player");
        if (player != null)
        {
            _currentSaveData.PlayerData.Health = player.Health;
            _currentSaveData.PlayerData.MaxHealth = player.MaxHealth;
            _currentSaveData.PlayerData.Position = player.GlobalPosition;
            _currentSaveData.PlayerData.CurrentScene = GetTree().CurrentScene.SceneFilePath;
        }

        // Обновляем прогресс игры
        var gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            _currentSaveData.GameProgress.PlayTime = gameManager.GetData("PlayTime", 0f);
            _currentSaveData.GameProgress.UnlockedModules = gameManager.GetData("UnlockedModules", new List<string>());
            _currentSaveData.GameProgress.CompletedMissions = gameManager.GetData("CompletedMissions", new List<string>());
        }

        // Обновляем инвентарь
        var inventory = GetNodeOrNull<Inventory>("/root/Player/Inventory");
        if (inventory != null)
        {
            _currentSaveData.InventoryData = inventory.SerializeInventory();
        }
    }

    /// <summary>
    /// Применяет загруженные данные к игре
    /// </summary>
    private void ApplySaveData()
    {
        if (_currentSaveData == null) return;

        // Применяем данные игрока
        var player = GetNodeOrNull<Player>("/root/Player");
        if (player != null && _currentSaveData.PlayerData != null)
        {
            player.Health = _currentSaveData.PlayerData.Health;
            player.MaxHealth = _currentSaveData.PlayerData.MaxHealth;
            player.GlobalPosition = _currentSaveData.PlayerData.Position;
        }

        // Применяем прогресс игры
        var gameManager = GetNodeOrNull<GameManager>("/root/GameManager");
        if (gameManager != null && _currentSaveData.GameProgress != null)
        {
            gameManager.SetData("PlayTime", _currentSaveData.GameProgress.PlayTime);
            gameManager.SetData("UnlockedModules", _currentSaveData.GameProgress.UnlockedModules);
            gameManager.SetData("CompletedMissions", _currentSaveData.GameProgress.CompletedMissions);
        }

        // Применяем инвентарь
        var inventory = GetNodeOrNull<Inventory>("/root/Player/Inventory");
        if (inventory != null && _currentSaveData.InventoryData != null)
        {
            inventory.DeserializeInventory(_currentSaveData.InventoryData);
        }
    }

    /// <summary>
    /// Защищает данные сохранения (хеширование)
    /// </summary>
    private void ProtectSaveData()
    {
        if (_currentSaveData == null) return;

        // Создаем хеш данных
        var dataJson = JsonSerializer.Serialize(_currentSaveData, new JsonSerializerOptions { WriteIndented = false });
        _currentSaveData.DataHash = ComputeHash(dataJson);
    }

    /// <summary>
    /// Проверяет целостность данных
    /// </summary>
    private bool VerifyDataIntegrity()
    {
        if (_currentSaveData == null) return false;

        // Вычисляем хеш текущих данных
        var dataJson = JsonSerializer.Serialize(_currentSaveData, new JsonSerializerOptions { WriteIndented = false });
        var currentHash = ComputeHash(dataJson);

        // Сравниваем с сохраненным хешем
        return currentHash == _currentSaveData.DataHash;
    }

    /// <summary>
    /// Вычисляет SHA256 хеш строки
    /// </summary>
    private string ComputeHash(string input)
    {
        using (var sha256 = SHA256.Create())
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// Настраивает автосохранение
    /// </summary>
    private void SetupAutoSave()
    {
        if (!EnableAutoSave) return;

        _autoSaveTimer = new Timer();
        _autoSaveTimer.WaitTime = SaveIntervalSeconds;
        _autoSaveTimer.Timeout += OnAutoSaveTimer;
        AddChild(_autoSaveTimer);
        _autoSaveTimer.Start();

        Logger.Debug($"Auto-save enabled with {SaveIntervalSeconds}s interval", true);
    }

    /// <summary>
    /// Обработчик автосохранения
    /// </summary>
    private async void OnAutoSaveTimer()
    {
        if (IsConnectedToServer && !IsSaving)
        {
            await SaveToServerAsync();
        }
    }

    /// <summary>
    /// Устанавливает ID игрока (для будущей Steam интеграции)
    /// </summary>
    public void SetPlayerId(string playerId)
    {
        CurrentPlayerId = playerId;
        if (_currentSaveData != null)
        {
            _currentSaveData.PlayerId = playerId;
        }
        Logger.Debug($"Player ID set to: {playerId}", true);
    }

    /// <summary>
    /// Получает статистику сохранений
    /// </summary>
    public Dictionary<string, object> GetSaveStats()
    {
        return new Dictionary<string, object>
        {
            { "IsConnected", IsConnectedToServer },
            { "LastSaveTime", LastSaveTime },
            { "IsSaving", IsSaving },
            { "IsLoading", IsLoading },
            { "PlayerId", CurrentPlayerId },
            { "DataProtected", EnableDataProtection }
        };
    }
}

/// <summary>
/// Защищенные данные сохранения для сервера
/// </summary>
public class ServerSaveData
{
    public string PlayerId { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public string DataHash { get; set; } // Хеш для проверки целостности
    public ServerPlayerData PlayerData { get; set; }
    public ServerGameProgress GameProgress { get; set; }
    public Dictionary<string, object> InventoryData { get; set; }
    public Dictionary<string, object> Settings { get; set; }
}

/// <summary>
/// Данные игрока для сервера
/// </summary>
public class ServerPlayerData
{
    public float Health { get; set; } = 100f;
    public float MaxHealth { get; set; } = 100f;
    public Vector2 Position { get; set; } = Vector2.Zero;
    public string CurrentScene { get; set; } = "";
}

/// <summary>
/// Прогресс игры для сервера
/// </summary>
public class ServerGameProgress
{
    public float PlayTime { get; set; } = 0f;
    public List<string> UnlockedModules { get; set; } = new List<string>();
    public List<string> CompletedMissions { get; set; } = new List<string>();
    public List<string> DiscoveredPlanets { get; set; } = new List<string>();
    public Dictionary<string, float> Stats { get; set; } = new Dictionary<string, float>();
}
