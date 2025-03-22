using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

/// <summary>
/// Управляет сохранением и загрузкой игровых данных.
/// </summary>
public partial class SaveManager : Node
{
    // Синглтон для удобного доступа
    public static SaveManager Instance { get; private set; }

    // Имя файла сохранения
    private const string SAVE_FILE_NAME = "game_save.json";

    // Путь к файлу сохранения
    private string _savePath;

    // Версия формата сохранения
    public const int SAVE_VERSION = 1;

    // Текущие данные сохранения
    private SaveData _currentSaveData;

    // Сигналы
    [Signal] public delegate void SaveCompletedEventHandler();
    [Signal] public delegate void LoadCompletedEventHandler();

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

        // Инициализация пути к файлу сохранения
        InitializeSavePath();

        Logger.Debug($"SaveManager initialized, save path: {_savePath}", true);
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Инициализирует путь к файлу сохранения
    /// </summary>
    private void InitializeSavePath()
    {
        string savesDir = Path.Combine(OS.GetUserDataDir(), "saves");

        // Создаем директорию, если она не существует
        if (!Directory.Exists(savesDir))
        {
            Directory.CreateDirectory(savesDir);
        }

        // Полный путь к файлу сохранения
        _savePath = Path.Combine(savesDir, SAVE_FILE_NAME);

        Logger.Debug($"Save file path: {_savePath}", false);
    }

    /// <summary>
    /// Сохраняет текущее состояние игры
    /// </summary>
    /// <returns>Успешность операции</returns>
    public bool SaveGame()
    {
        try
        {
            // Собираем данные для сохранения
            _currentSaveData = CollectSaveData();

            // Сохраняем данные в файл
            SaveToFile(_currentSaveData);

            // Отправляем сигнал о завершении сохранения
            EmitSignal("SaveCompleted");

            Logger.Debug("Game saved successfully", true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error saving game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загружает состояние игры
    /// </summary>
    /// <returns>Успешность операции</returns>
    public bool LoadGame()
    {
        try
        {
            // Проверяем, существует ли файл сохранения
            if (!File.Exists(_savePath))
            {
                Logger.Debug("Save file not found", true);
                return false;
            }

            // Загружаем данные из файла
            _currentSaveData = LoadFromFile();
            if (_currentSaveData == null)
            {
                Logger.Error("Failed to load save data");
                return false;
            }

            // Применяем данные к игре
            bool success = ApplySaveData(_currentSaveData);
            if (!success)
            {
                Logger.Error("Failed to apply save data");
                return false;
            }

            // Отправляем сигнал о завершении загрузки
            EmitSignal("LoadCompleted");

            Logger.Debug("Game loaded successfully", true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Проверяет, существует ли файл сохранения
    /// </summary>
    /// <returns>True, если сохранение существует</returns>
    public bool SaveExists()
    {
        return File.Exists(_savePath);
    }

    /// <summary>
    /// Сохраняет данные в файл
    /// </summary>
    /// <param name="saveData">Данные для сохранения</param>
    private void SaveToFile(SaveData saveData)
    {
        // Сериализуем данные в JSON
        string jsonData = JsonSerializer.Serialize(saveData, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Сохраняем в файл
        File.WriteAllText(_savePath, jsonData, Encoding.UTF8);

        Logger.Debug($"Save data written to file: {_savePath}", false);
    }

    /// <summary>
    /// Загружает данные из файла
    /// </summary>
    /// <returns>Загруженные данные или null, если файл не найден или неверного формата</returns>
    private SaveData LoadFromFile()
    {
        // Проверяем, существует ли файл
        if (!File.Exists(_savePath))
        {
            Logger.Error($"Save file not found: {_savePath}");
            return null;
        }

        try
        {
            // Читаем данные из файла
            string jsonData = File.ReadAllText(_savePath, Encoding.UTF8);

            // Десериализуем JSON в объект SaveData
            SaveData saveData = JsonSerializer.Deserialize<SaveData>(jsonData);

            // Проверяем версию сохранения
            if (saveData.Version != SAVE_VERSION)
            {
                Logger.Debug($"Save version mismatch: file version {saveData.Version}, current version {SAVE_VERSION}", true);
                // В будущем здесь может быть логика миграции данных между версиями
            }

            return saveData;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading save file '{_savePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Собирает все данные для сохранения
    /// </summary>
    /// <returns>Структура данных сохранения</returns>
    private SaveData CollectSaveData()
    {
        // Создаем новый объект данных сохранения
        SaveData saveData = new SaveData
        {
            Version = SAVE_VERSION,
            SaveDate = DateTime.Now,
            PlayTime = GetCurrentPlayTime()
        };

        // Собираем данные игрока
        CollectPlayerData(saveData);

        // Собираем данные инвентаря
        CollectInventoryData(saveData);

        // Собираем данные станции
        CollectSpaceStationData(saveData);

        // Собираем данные прогресса
        CollectProgressData(saveData);

        return saveData;
    }

    /// <summary>
    /// Применяет данные сохранения к текущей игре
    /// </summary>
    /// <param name="saveData">Данные сохранения</param>
    /// <returns>Успешность операции</returns>
    private bool ApplySaveData(SaveData saveData)
    {
        try
        {
            // Применяем данные игрока
            ApplyPlayerData(saveData);

            // Применяем данные инвентаря
            ApplyInventoryData(saveData);

            // Применяем данные станции
            ApplySpaceStationData(saveData);

            // Применяем данные прогресса
            ApplyProgressData(saveData);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error applying save data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Получает текущее время игры
    /// </summary>
    /// <returns>Время игры в секундах</returns>
    private float GetCurrentPlayTime()
    {
        // Здесь логика получения времени игры
        // Можно использовать GameManager или другой синглтон для отслеживания времени
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null && gameManager.HasData("PlayTime"))
        {
            return gameManager.GetData<float>("PlayTime");
        }

        return 0f;
    }

    #region Data Collection Methods

    /// <summary>
    /// Собирает данные игрока
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    private void CollectPlayerData(SaveData saveData)
    {
        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Создаем объект данных игрока
            saveData.PlayerData = new PlayerData
            {
                Health = player.GetHealth(),
                MaxHealth = player.GetMaxHealth(),
                Position = new Vector2Data
                {
                    X = player.GlobalPosition.X,
                    Y = player.GlobalPosition.Y
                },
                CurrentScene = "res://scenes/station/space_station.tscn"
            };

            Logger.Debug($"Collected player data: HP {saveData.PlayerData.Health}/{saveData.PlayerData.MaxHealth}, Pos {player.GlobalPosition}", false);
        }
        else
        {
            // Если игрок не найден, используем данные из GameManager
            var gameManager = GetNode<GameManager>("/root/GameManager");
            if (gameManager != null)
            {
                Vector2 lastPosition = gameManager.HasData("LastWorldPosition")
                    ? gameManager.GetData<Vector2>("LastWorldPosition")
                    : Vector2.Zero;

                string currentScene = gameManager.HasData("CurrentScene")
                    ? gameManager.GetData<string>("CurrentScene")
                    : "";

                saveData.PlayerData = new PlayerData
                {
                    Health = 100, // Значение по умолчанию
                    MaxHealth = 100, // Значение по умолчанию
                    Position = new Vector2Data
                    {
                        X = lastPosition.X,
                        Y = lastPosition.Y
                    },
                    CurrentScene = currentScene
                };

                Logger.Debug($"Collected player data from GameManager: Pos {lastPosition}, Scene {currentScene}", false);
            }
            else
            {
                // Если нет и GameManager, создаем значения по умолчанию
                saveData.PlayerData = new PlayerData
                {
                    Health = 100,
                    MaxHealth = 100,
                    Position = new Vector2Data { X = 0, Y = 0 },
                    CurrentScene = ""
                };

                Logger.Debug("Created default player data", false);
            }
        }
    }

    /// <summary>
    /// Собирает данные инвентаря
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    private void CollectInventoryData(SaveData saveData)
    {
        // Находим игрока для получения инвентаря
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Сериализуем инвентарь
            saveData.InventoryData = player.PlayerInventory.Serialize();
            Logger.Debug($"Collected player inventory: {player.PlayerInventory.Items.Count} items", false);
        }
        else
        {
            // Если инвентарь не найден, пытаемся получить из GameManager
            var gameManager = GetNode<GameManager>("/root/GameManager");
            if (gameManager != null && gameManager.HasData("PlayerInventorySaved"))
            {
                saveData.InventoryData = gameManager.GetData<Dictionary<string, object>>("PlayerInventorySaved");
                Logger.Debug("Collected player inventory from GameManager", false);
            }
            else
            {
                // Создаем пустой инвентарь
                saveData.InventoryData = new Dictionary<string, object>
                {
                    ["max_slots"] = 20,
                    ["max_weight"] = 0,
                    ["items"] = new List<Dictionary<string, object>>()
                };

                Logger.Debug("Created empty inventory data", false);
            }
        }
    }

    /// <summary>
    /// Собирает данные космической станции
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    private void CollectSpaceStationData(SaveData saveData)
    {
        // Создаем данные станции
        saveData.StationData = new SpaceStationData();

        // Получаем данные хранилищ из GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Получаем список всех хранилищ
            var storageIds = gameManager.GetAllStorageIds();

            // Создаем словарь для хранения данных хранилищ
            saveData.StationData.StorageData = new Dictionary<string, Dictionary<string, object>>();

            // Собираем данные каждого хранилища
            foreach (var storageId in storageIds)
            {
                string key = $"StorageInventory_{storageId}";
                if (gameManager.HasData(key))
                {
                    var storageInventory = gameManager.GetData<Dictionary<string, object>>(key);
                    saveData.StationData.StorageData[storageId] = storageInventory;
                    Logger.Debug($"Collected storage data for '{storageId}'", false);
                }
            }

            Logger.Debug($"Collected data for {saveData.StationData.StorageData.Count} storage modules", false);
        }
    }

    /// <summary>
    /// Собирает данные прогресса игры
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    private void CollectProgressData(SaveData saveData)
    {
        // Создаем данные прогресса
        saveData.ProgressData = new ProgressData
        {
            UnlockedModules = new List<string>(),
            CompletedMissions = new List<string>(),
            DiscoveredPlanets = new List<string>(),
            VisitedLocations = new List<string>(),
            Stats = new Dictionary<string, float>()
        };

        // Получаем данные из GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Добавляем базовую статистику
            saveData.ProgressData.Stats["playtime_seconds"] = GetCurrentPlayTime();

            // Получаем другие данные прогресса из GameManager, если они есть
            if (gameManager.HasData("UnlockedModules"))
                saveData.ProgressData.UnlockedModules = gameManager.GetData<List<string>>("UnlockedModules");

            if (gameManager.HasData("CompletedMissions"))
                saveData.ProgressData.CompletedMissions = gameManager.GetData<List<string>>("CompletedMissions");

            if (gameManager.HasData("DiscoveredPlanets"))
                saveData.ProgressData.DiscoveredPlanets = gameManager.GetData<List<string>>("DiscoveredPlanets");

            if (gameManager.HasData("VisitedLocations"))
                saveData.ProgressData.VisitedLocations = gameManager.GetData<List<string>>("VisitedLocations");
        }

        Logger.Debug("Collected progress data", false);
    }

    #endregion

    #region Data Application Methods

    /// <summary>
    /// Применяет данные игрока
    /// </summary>
    /// <param name="saveData">Данные сохранения</param>
    private void ApplyPlayerData(SaveData saveData)
    {
        if (saveData.PlayerData == null)
            return;

        // Сохраняем данные в GameManager для использования при создании игрока
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Позиция игрока
            Vector2 position = new Vector2(
                saveData.PlayerData.Position.X,
                saveData.PlayerData.Position.Y
            );
            gameManager.SetData("LastWorldPosition", position);

            // Текущая сцена
            gameManager.SetData("CurrentScene", "res://scenes/station/space_station.tscn");

            // Здоровье игрока (сохраняем для применения после создания)
            gameManager.SetData("PlayerHealth", saveData.PlayerData.Health);
            gameManager.SetData("PlayerMaxHealth", saveData.PlayerData.MaxHealth);

            Logger.Debug($"Applied player data to GameManager: Pos {position}, Scene {saveData.PlayerData.CurrentScene}", false);
        }

        // Если игрок уже существует, применяем данные напрямую
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Устанавливаем здоровье
            float health = saveData.PlayerData.Health;
            float maxHealth = saveData.PlayerData.MaxHealth;

            // Вычисляем урон, чтобы установить правильное здоровье через метод TakeDamage
            float currentHealth = player.GetHealth();
            float damage = currentHealth - health;
            if (damage != 0)
            {
                player.TakeDamage(damage, player);
                Logger.Debug($"Set player health to {health}/{maxHealth}", false);
            }
        }
    }

    /// <summary>
    /// Применяет данные инвентаря
    /// </summary>
    /// <param name="saveData">Данные сохранения</param>
    private void ApplyInventoryData(SaveData saveData)
    {
        if (saveData.InventoryData == null)
            return;

        // Сохраняем данные инвентаря в GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            gameManager.SetData("PlayerInventorySaved", saveData.InventoryData);
            Logger.Debug("Applied inventory data to GameManager", false);
        }

        // Если игрок уже существует, применяем данные напрямую
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Десериализуем инвентарь
            player.PlayerInventory.Deserialize(saveData.InventoryData);
            Logger.Debug("Applied inventory data directly to player", false);
        }
    }

    /// <summary>
    /// Применяет данные космической станции
    /// </summary>
    /// <param name="saveData">Данные сохранения</param>
    private void ApplySpaceStationData(SaveData saveData)
    {
        if (saveData.StationData == null || saveData.StationData.StorageData == null)
            return;

        // Сохраняем данные хранилищ в GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            foreach (var storage in saveData.StationData.StorageData)
            {
                string storageId = storage.Key;
                var storageData = storage.Value;

                // Сохраняем данные в GameManager
                string key = $"StorageInventory_{storageId}";
                gameManager.SetData(key, storageData);

                Logger.Debug($"Applied storage data for '{storageId}'", false);
            }
        }
    }

    /// <summary>
    /// Применяет данные прогресса
    /// </summary>
    /// <param name="saveData">Данные сохранения</param>
    private void ApplyProgressData(SaveData saveData)
    {
        if (saveData.ProgressData == null)
            return;

        // Сохраняем данные прогресса в GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Время игры
            if (saveData.ProgressData.Stats.ContainsKey("playtime_seconds"))
            {
                gameManager.SetData("PlayTime", saveData.ProgressData.Stats["playtime_seconds"]);
            }

            // Сохраняем другие данные прогресса
            gameManager.SetData("UnlockedModules", saveData.ProgressData.UnlockedModules);
            gameManager.SetData("CompletedMissions", saveData.ProgressData.CompletedMissions);
            gameManager.SetData("DiscoveredPlanets", saveData.ProgressData.DiscoveredPlanets);
            gameManager.SetData("VisitedLocations", saveData.ProgressData.VisitedLocations);

            Logger.Debug("Applied progress data to GameManager", false);
        }
    }

    #endregion
}

#region Save Data Classes

/// <summary>
/// Класс для хранения полной информации о сохранении
/// </summary>
public class SaveData
{
    // Основная информация
    public int Version { get; set; }
    public DateTime SaveDate { get; set; }
    public float PlayTime { get; set; }

    // Данные игрока
    public PlayerData PlayerData { get; set; }

    // Данные инвентаря
    public Dictionary<string, object> InventoryData { get; set; }

    // Данные космической станции
    public SpaceStationData StationData { get; set; }

    // Данные прогресса
    public ProgressData ProgressData { get; set; }
}

/// <summary>
/// Класс для хранения данных игрока
/// </summary>
public class PlayerData
{
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public Vector2Data Position { get; set; }
    public string CurrentScene { get; set; }
}

/// <summary>
/// Класс для хранения двумерного вектора
/// </summary>
public class Vector2Data
{
    public float X { get; set; }
    public float Y { get; set; }
}

/// <summary>
/// Класс для хранения данных космической станции
/// </summary>
public class SpaceStationData
{
    public Dictionary<string, Dictionary<string, object>> StorageData { get; set; }
}

/// <summary>
/// Класс для хранения данных прогресса
/// </summary>
public class ProgressData
{
    public List<string> UnlockedModules { get; set; }
    public List<string> CompletedMissions { get; set; }
    public List<string> DiscoveredPlanets { get; set; }
    public List<string> VisitedLocations { get; set; }
    public Dictionary<string, float> Stats { get; set; }
}

#endregion