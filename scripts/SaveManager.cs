using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // Счетчик сохранений для контроля логирования
    private int _saveCounter = 0;

    // Интервал логирования (каждое N-ое сохранение)
    private const int LOG_INTERVAL = 10;

    // Уровень детализации логов (0-выключены, 1-только важные, 2-стандартные, 3-отладочные)
    [Export] public int LogLevel { get; set; } = 1;

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

        LogInfo($"SaveManager initialized, save path: {_savePath}", 1, true);
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Логирует информационное сообщение с учетом уровня подробности
    /// </summary>
    private void LogInfo(string message, int level, bool forceDisplay = false)
    {
        if (level <= LogLevel || forceDisplay)
        {
            Logger.Debug(message, false);
        }
    }

    /// <summary>
    /// Логирует ошибку (всегда отображается)
    /// </summary>
    private void LogError(string message)
    {
        Logger.Error(message);
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

        LogInfo($"Save file path: {_savePath}", 3);
    }

    /// <summary>
    /// Сохраняет текущее состояние игры
    /// </summary>
    /// <returns>Успешность операции</returns>
    public bool SaveGame()
    {
        try
        {
            // Увеличиваем счетчик сохранений
            _saveCounter++;
            bool shouldLog = _saveCounter % LOG_INTERVAL == 0;

            // Собираем данные для сохранения
            _currentSaveData = CollectSaveData(shouldLog);

            // Сохраняем данные в файл
            SaveToFile(_currentSaveData, shouldLog);

            // Отправляем сигнал о завершении сохранения
            EmitSignal("SaveCompleted");

            if (shouldLog)
            {
                LogInfo("Game saved successfully", 1);
            }
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Error saving game: {ex.Message}");
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
                LogInfo("Save file not found", 1);
                return false;
            }

            // Загружаем данные из файла
            _currentSaveData = LoadFromFile();
            if (_currentSaveData == null)
            {
                LogError("Failed to load save data");
                return false;
            }

            // Применяем данные к игре
            bool success = ApplySaveData(_currentSaveData);
            if (!success)
            {
                LogError("Failed to apply save data");
                return false;
            }

            // Отправляем сигнал о завершении загрузки
            EmitSignal("LoadCompleted");

            LogInfo("Game loaded successfully", 1);
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Error loading game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загружает игру напрямую, минуя проблемную десериализацию SaveData
    /// </summary>
    public bool LoadGameDirectly()
    {
        try
        {
            // Проверяем, существует ли файл сохранения
            if (!File.Exists(_savePath))
            {
                LogInfo("Save file not found", 1);
                return false;
            }

            // Читаем данные напрямую из файла как строку
            string jsonData = File.ReadAllText(_savePath, Encoding.UTF8);
            LogInfo($"Read save file with length: {jsonData.Length}", 3);

            // Парсим JSON напрямую
            using (JsonDocument document = JsonDocument.Parse(jsonData))
            {
                JsonElement root = document.RootElement;
                var gameManager = GetNode<GameManager>("/root/GameManager");

                if (gameManager == null)
                {
                    LogError("GameManager not found for direct loading");
                    return false;
                }

                // Обрабатываем PlayerData
                if (root.TryGetProperty("PlayerData", out JsonElement playerData))
                {
                    LogInfo("Processing PlayerData section directly", 3);

                    // Позиция игрока
                    if (playerData.TryGetProperty("Position", out JsonElement positionData))
                    {
                        float x = positionData.GetProperty("X").GetSingle();
                        float y = positionData.GetProperty("Y").GetSingle();
                        Vector2 position = new Vector2(x, y);
                        gameManager.SetData("LastWorldPosition", position);
                        LogInfo($"Set player position: {position}", 3);
                    }

                    // Текущая сцена
                    if (playerData.TryGetProperty("CurrentScene", out JsonElement sceneData))
                    {
                        string scene = sceneData.GetString();
                        gameManager.SetData("CurrentScene", scene);
                        LogInfo($"Set current scene: {scene}", 3);
                    }

                    // Здоровье
                    if (playerData.TryGetProperty("Health", out JsonElement healthData) &&
                        playerData.TryGetProperty("MaxHealth", out JsonElement maxHealthData))
                    {
                        float health = healthData.GetSingle();
                        float maxHealth = maxHealthData.GetSingle();
                        gameManager.SetData("PlayerHealth", health);
                        gameManager.SetData("PlayerMaxHealth", maxHealth);
                        LogInfo($"Set player health: {health}/{maxHealth}", 3);
                    }
                }

                // Обрабатываем InventoryData
                if (root.TryGetProperty("InventoryData", out JsonElement inventoryData))
                {
                    LogInfo("Processing InventoryData section directly", 3);

                    // Вместо десериализации сохраняем JSON как строку, затем парсим
                    string inventoryJson = inventoryData.GetRawText();

                    // Парсим инвентарь в отдельный словарь
                    using (JsonDocument invDoc = JsonDocument.Parse(inventoryJson))
                    {
                        Dictionary<string, object> inventoryDict = new Dictionary<string, object>();

                        // Обрабатываем базовые свойства
                        if (invDoc.RootElement.TryGetProperty("max_slots", out JsonElement maxSlotsEl))
                        {
                            inventoryDict["max_slots"] = maxSlotsEl.GetInt32();
                        }

                        if (invDoc.RootElement.TryGetProperty("max_weight", out JsonElement maxWeightEl))
                        {
                            inventoryDict["max_weight"] = maxWeightEl.GetSingle();
                        }

                        // Ключевая часть - обработка предметов
                        if (invDoc.RootElement.TryGetProperty("items", out JsonElement itemsEl) &&
                            itemsEl.ValueKind == JsonValueKind.Array)
                        {
                            // Создаем список предметов
                            var itemsList = new List<Dictionary<string, object>>();
                            int itemCount = 0;

                            // Обрабатываем каждый предмет
                            foreach (JsonElement item in itemsEl.EnumerateArray())
                            {
                                var itemDict = new Dictionary<string, object>();

                                // Обрабатываем все свойства предмета
                                foreach (JsonProperty prop in item.EnumerateObject())
                                {
                                    switch (prop.Value.ValueKind)
                                    {
                                        case JsonValueKind.String:
                                            itemDict[prop.Name] = prop.Value.GetString();
                                            break;
                                        case JsonValueKind.Number:
                                            // Пробуем получить как int, если не получится, то как float
                                            try
                                            {
                                                itemDict[prop.Name] = prop.Value.GetInt32();
                                            }
                                            catch
                                            {
                                                itemDict[prop.Name] = prop.Value.GetSingle();
                                            }
                                            break;
                                        case JsonValueKind.True:
                                            itemDict[prop.Name] = true;
                                            break;
                                        case JsonValueKind.False:
                                            itemDict[prop.Name] = false;
                                            break;
                                            // Обработка других типов по необходимости
                                    }
                                }

                                // Добавляем предмет в список
                                itemsList.Add(itemDict);
                                itemCount++;

                                // Выводим информацию о предмете только на высоком уровне логирования
                                if (LogLevel >= 3)
                                {
                                    string name = itemDict.ContainsKey("display_name") ? itemDict["display_name"].ToString() : "Unknown";
                                    int qty = itemDict.ContainsKey("quantity") ? Convert.ToInt32(itemDict["quantity"]) : 0;
                                    string id = itemDict.ContainsKey("id") ? itemDict["id"].ToString() : "Unknown";
                                    LogInfo($"Manually processed item: {name} x{qty} (ID: {id})", 3);
                                }
                            }

                            // Добавляем список предметов в словарь инвентаря
                            inventoryDict["items"] = itemsList;
                            LogInfo($"Manually processed {itemCount} items for inventory", 2);
                        }
                        else
                        {
                            // Если предметов нет, создаем пустой список
                            inventoryDict["items"] = new List<Dictionary<string, object>>();
                            LogInfo("No items found in inventory data, creating empty list", 2);
                        }

                        // Сохраняем обработанный инвентарь в GameManager
                        gameManager.SetData("PlayerInventorySaved", inventoryDict);
                        gameManager.SetData("PlayerInventoryLastSaveTime", DateTime.Now.ToString());
                        LogInfo($"Directly saved inventory with {(inventoryDict["items"] as List<Dictionary<string, object>>)?.Count ?? 0} items", 2);
                    }
                }

                // Обрабатываем StationData
                if (root.TryGetProperty("StationData", out JsonElement stationData) &&
                    stationData.TryGetProperty("StorageData", out JsonElement storageData))
                {
                    LogInfo("Processing StationData section directly", 3);

                    // Выводим ключи только при высоком уровне логирования
                    if (LogLevel >= 3)
                    {
                        List<string> storageKeys = new List<string>();
                        foreach (JsonProperty storage in storageData.EnumerateObject())
                        {
                            storageKeys.Add(storage.Name);
                        }
                        LogInfo($"Found {storageKeys.Count} storage keys: {string.Join(", ", storageKeys)}", 3);
                    }

                    int processedStorages = 0;
                    foreach (JsonProperty storage in storageData.EnumerateObject())
                    {
                        string storageId = storage.Name;
                        string storageJson = storage.Value.GetRawText();

                        // Подробное логирование только на высоком уровне
                        if (LogLevel >= 3)
                        {
                            LogInfo($"Processing storage with ID '{storageId}'", 3);
                        }

                        // Парсим хранилище в отдельный словарь
                        using (JsonDocument storageDoc = JsonDocument.Parse(storageJson))
                        {
                            Dictionary<string, object> storageDict = new Dictionary<string, object>();

                            // Обрабатываем базовые свойства
                            if (storageDoc.RootElement.TryGetProperty("max_slots", out JsonElement maxSlotsEl))
                            {
                                storageDict["max_slots"] = maxSlotsEl.GetInt32();
                            }

                            if (storageDoc.RootElement.TryGetProperty("max_weight", out JsonElement maxWeightEl))
                            {
                                storageDict["max_weight"] = maxWeightEl.GetSingle();
                            }

                            // Обрабатываем предметы в хранилище
                            if (storageDoc.RootElement.TryGetProperty("items", out JsonElement itemsEl) &&
                                itemsEl.ValueKind == JsonValueKind.Array)
                            {
                                // Создаем список предметов
                                var itemsList = new List<Dictionary<string, object>>();
                                int itemCount = 0;

                                // Обрабатываем каждый предмет
                                foreach (JsonElement item in itemsEl.EnumerateArray())
                                {
                                    var itemDict = new Dictionary<string, object>();

                                    // Обрабатываем все свойства предмета
                                    foreach (JsonProperty prop in item.EnumerateObject())
                                    {
                                        switch (prop.Value.ValueKind)
                                        {
                                            case JsonValueKind.String:
                                                itemDict[prop.Name] = prop.Value.GetString();
                                                break;
                                            case JsonValueKind.Number:
                                                // Пробуем получить как int, если не получится, то как float
                                                try
                                                {
                                                    itemDict[prop.Name] = prop.Value.GetInt32();
                                                }
                                                catch
                                                {
                                                    itemDict[prop.Name] = prop.Value.GetSingle();
                                                }
                                                break;
                                            case JsonValueKind.True:
                                                itemDict[prop.Name] = true;
                                                break;
                                            case JsonValueKind.False:
                                                itemDict[prop.Name] = false;
                                                break;
                                                // Обработка других типов по необходимости
                                        }
                                    }

                                    // Добавляем предмет в список
                                    itemsList.Add(itemDict);
                                    itemCount++;

                                    // Детальное логирование только при высоком уровне логирования
                                    if (LogLevel >= 3)
                                    {
                                        string displayName = itemDict.ContainsKey("display_name") ? itemDict["display_name"].ToString() : "Unknown";
                                        int quantity = itemDict.ContainsKey("quantity") ? Convert.ToInt32(itemDict["quantity"]) : 0;
                                        LogInfo($"Found item in storage '{storageId}': {displayName} x{quantity}", 3);
                                    }
                                }

                                // Добавляем список предметов в словарь хранилища
                                storageDict["items"] = itemsList;
                                LogInfo($"Processed {itemCount} items for storage '{storageId}'", 3);
                            }
                            else
                            {
                                // Если предметов нет, создаем пустой список
                                storageDict["items"] = new List<Dictionary<string, object>>();
                                LogInfo($"No items found in storage '{storageId}', creating empty list", 3);
                            }

                            // Сохраняем обработанное хранилище в GameManager
                            string saveKey = $"StorageInventory_{storageId}";
                            gameManager.SetData(saveKey, storageDict);
                            LogInfo($"Saved storage '{storageId}' with key '{saveKey}'", 3);

                            // Дополнительно сохраняем под ключом StorageContainer для совместимости
                            if (storageId != "StorageContainer")
                            {
                                string containerKey = "StorageInventory_StorageContainer";
                                gameManager.SetData(containerKey, storageDict);
                                LogInfo($"Also saved with container key for compatibility", 3);
                            }

                            processedStorages++;
                        }
                    }

                    // Сводка по хранилищам (средний уровень логирования)
                    LogInfo($"Processed {processedStorages} storage containers", 2);
                }
                else
                {
                    LogInfo("No StationData or StorageData found in save file", 2);
                }

                // Обрабатываем ProgressData
                if (root.TryGetProperty("ProgressData", out JsonElement progressData))
                {
                    LogInfo("Processing ProgressData section directly", 3);

                    // Обрабатываем статистику
                    if (progressData.TryGetProperty("Stats", out JsonElement statsData))
                    {
                        if (statsData.TryGetProperty("playtime_seconds", out JsonElement playtimeData))
                        {
                            float playtime = playtimeData.GetSingle();
                            gameManager.SetData("PlayTime", playtime);
                            LogInfo($"Set play time: {playtime} seconds", 3);
                        }
                    }
                }
            }

            // Сообщаем об успешной загрузке
            LogInfo("Direct load completed successfully", 1);

            EmitSignal("LoadCompleted");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Error in direct load: {ex.Message}");
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
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    private void SaveToFile(SaveData saveData, bool shouldLog)
    {
        // Сериализуем данные в JSON
        string jsonData = JsonSerializer.Serialize(saveData, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Сохраняем в файл
        File.WriteAllText(_savePath, jsonData, Encoding.UTF8);

        if (shouldLog)
        {
            LogInfo($"Save data written to file: {_savePath}", 2);
        }
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
            LogError($"Save file not found: {_savePath}");
            return null;
        }

        try
        {
            // Читаем данные из файла
            string jsonData = File.ReadAllText(_savePath, Encoding.UTF8);
            LogInfo($"Read save file content: {jsonData.Length} bytes", 2);

            // Создаем опции для десериализации
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // Десериализуем JSON в динамический объект для анализа структуры
            using (JsonDocument document = JsonDocument.Parse(jsonData))
            {
                LogInfo("Successfully parsed JSON document", 3);

                // Проверяем корневые элементы только при высоком уровне логирования
                if (LogLevel >= 3)
                {
                    JsonElement root = document.RootElement;

                    // Проверяем корневые элементы
                    bool hasInventoryData = root.TryGetProperty("InventoryData", out JsonElement inventoryElement);
                    LogInfo($"Save file has InventoryData section: {hasInventoryData}", 3);

                    if (hasInventoryData)
                    {
                        bool hasItems = inventoryElement.TryGetProperty("items", out JsonElement itemsElement);
                        LogInfo($"InventoryData has items array: {hasItems}", 3);

                        if (hasItems && itemsElement.ValueKind == JsonValueKind.Array)
                        {
                            int itemsCount = itemsElement.GetArrayLength();
                            LogInfo($"Items array contains {itemsCount} elements", 3);

                            // Выводим информацию только о первом предмете для отладки
                            if (itemsCount > 0)
                            {
                                JsonElement item = itemsElement[0];
                                string displayName = "Unknown";
                                int quantity = 0;
                                string id = "Unknown";

                                if (item.TryGetProperty("display_name", out JsonElement nameElement))
                                {
                                    displayName = nameElement.GetString();
                                }

                                if (item.TryGetProperty("quantity", out JsonElement qtyElement))
                                {
                                    quantity = qtyElement.GetInt32();
                                }

                                if (item.TryGetProperty("id", out JsonElement idElement))
                                {
                                    id = idElement.GetString();
                                }

                                LogInfo($"First item in save file: {displayName} x{quantity} (ID: {id})", 3);
                            }
                        }
                    }
                }
            }

            // Теперь пробуем десериализовать в объект SaveData
            try
            {
                // Используем прямую десериализацию из строки
                var saveData = JsonSerializer.Deserialize<SaveData>(jsonData, options);

                if (saveData == null)
                {
                    LogError("Failed to deserialize save data - result is null");
                    return null;
                }

                LogInfo($"Successfully deserialized save data (Version: {saveData.Version}, SaveDate: {saveData.SaveDate})", 2);

                // Проверяем версию сохранения
                if (saveData.Version != SAVE_VERSION)
                {
                    LogInfo($"Save version mismatch: file version {saveData.Version}, current version {SAVE_VERSION}", 2);
                    // В будущем здесь может быть логика миграции данных между версиями
                }

                return saveData;
            }
            catch (JsonException jsonEx)
            {
                LogError($"JSON deserialization error: {jsonEx.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error loading save file '{_savePath}': {ex.Message}");
            if (LogLevel >= 3)
            {
                LogInfo($"Exception details: {ex.ToString()}", 3);
            }
            return null;
        }
    }

    /// <summary>
    /// Собирает все данные для сохранения
    /// </summary>
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    /// <returns>Структура данных сохранения</returns>
    private SaveData CollectSaveData(bool shouldLog)
    {
        // Создаем новый объект данных сохранения
        SaveData saveData = new SaveData
        {
            Version = SAVE_VERSION,
            SaveDate = DateTime.Now,
            PlayTime = GetCurrentPlayTime()
        };

        // Собираем данные игрока
        CollectPlayerData(saveData, shouldLog);

        // Собираем данные инвентаря
        CollectInventoryData(saveData, shouldLog);

        // Собираем данные станции
        CollectSpaceStationData(saveData, shouldLog);

        // Собираем данные прогресса
        CollectProgressData(saveData, shouldLog);

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
            LogError($"Error applying save data: {ex.Message}");
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
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    private void CollectPlayerData(SaveData saveData, bool shouldLog)
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

            if (shouldLog)
            {
                LogInfo($"Collected player data: HP {saveData.PlayerData.Health}/{saveData.PlayerData.MaxHealth}, Pos {player.GlobalPosition}", 2);
            }
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

                if (shouldLog)
                {
                    LogInfo($"Collected player data from GameManager: Pos {lastPosition}", 2);
                }
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

                if (shouldLog)
                {
                    LogInfo("Created default player data", 2);
                }
            }
        }
    }

    /// <summary>
    /// Собирает данные инвентаря
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    private void CollectInventoryData(SaveData saveData, bool shouldLog)
    {
        // Находим игрока для получения инвентаря
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Сериализуем инвентарь
            saveData.InventoryData = player.PlayerInventory.Serialize();

            if (shouldLog)
            {
                LogInfo($"Collected player inventory: {player.PlayerInventory.Items.Count} items", 2);
            }
        }
        else
        {
            // Если инвентарь не найден, пытаемся получить из GameManager
            var gameManager = GetNode<GameManager>("/root/GameManager");
            if (gameManager != null && gameManager.HasData("PlayerInventorySaved"))
            {
                saveData.InventoryData = gameManager.GetData<Dictionary<string, object>>("PlayerInventorySaved");

                if (shouldLog)
                {
                    LogInfo("Collected player inventory from GameManager", 2);
                }
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

                if (shouldLog)
                {
                    LogInfo("Created empty inventory data", 2);
                }
            }
        }
    }

    /// <summary>
    /// Собирает данные космической станции
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    private void CollectSpaceStationData(SaveData saveData, bool shouldLog)
    {
        // Создаем данные станции
        saveData.StationData = new SpaceStationData();

        // Получаем данные хранилищ из GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Создаем словарь для хранения данных хранилищ
            saveData.StationData.StorageData = new Dictionary<string, Dictionary<string, object>>();

            // Получаем список всех ключей (для отладки)
            List<string> allKeys = new List<string>();
            if (LogLevel >= 3)
            {
                // Получаем список всех ключей через рефлексию
                var keysMethod = gameManager.GetType().GetMethod("GetAllKeys");
                if (keysMethod != null)
                {
                    var keys = keysMethod.Invoke(gameManager, null) as IEnumerable<string>;
                    if (keys != null)
                    {
                        allKeys.AddRange(keys);
                        LogInfo($"All keys in GameManager: {string.Join(", ", allKeys)}", 3);
                    }
                }
            }

            // ПРЯМАЯ ПРОВЕРКА: Проверяем конкретные ключи хранилищ
            string[] specificIds = new string[] { "main_storage", "second_storage", "StorageContainer" };
            foreach (var id in specificIds)
            {
                string key = $"StorageInventory_{id}";
                if (gameManager.HasData(key))
                {
                    var inventoryData = gameManager.GetData<Dictionary<string, object>>(key);
                    if (inventoryData != null)
                    {
                        saveData.StationData.StorageData[id] = inventoryData;

                        if (LogLevel >= 3)
                        {
                            int itemCount = 0;
                            if (inventoryData.ContainsKey("items") &&
                                inventoryData["items"] is List<Dictionary<string, object>> items)
                            {
                                itemCount = items.Count;
                            }

                            LogInfo($"Added storage '{id}' with {itemCount} items to save data", 3);
                        }
                    }
                }
            }

            // НОВЫЙ СПОСОБ: Находим все ключи, начинающиеся с "StorageInventory_"
            string prefix = "StorageInventory_";
            foreach (string key in allKeys)
            {
                if (key.StartsWith(prefix))
                {
                    string storageId = key.Substring(prefix.Length);

                    // Пропускаем дубликаты
                    if (saveData.StationData.StorageData.ContainsKey(storageId))
                        continue;

                    var data = gameManager.GetData<Dictionary<string, object>>(key);
                    if (data != null)
                    {
                        saveData.StationData.StorageData[storageId] = data;

                        if (LogLevel >= 3)
                        {
                            int itemCount = 0;
                            if (data.ContainsKey("items") &&
                                data["items"] is List<Dictionary<string, object>> items)
                            {
                                itemCount = items.Count;
                            }

                            LogInfo($"Found and added storage '{storageId}' with {itemCount} items", 3);
                        }
                    }
                }
            }

            if (shouldLog)
            {
                LogInfo($"Collected data for {saveData.StationData.StorageData.Count} storage modules", 2);
            }
        }
    }

    /// <summary>
    /// Собирает данные прогресса игры
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    /// <param name="shouldLog">Нужно ли логировать процесс</param>
    private void CollectProgressData(SaveData saveData, bool shouldLog)
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

        if (shouldLog)
        {
            LogInfo("Collected progress data", 2);
        }
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
            gameManager.SetData("CurrentScene", saveData.PlayerData.CurrentScene);

            // Здоровье игрока (сохраняем для применения после создания)
            gameManager.SetData("PlayerHealth", saveData.PlayerData.Health);
            gameManager.SetData("PlayerMaxHealth", saveData.PlayerData.MaxHealth);

            LogInfo($"Applied player data to GameManager", 2);
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
                LogInfo($"Set player health to {health}/{maxHealth}", 2);
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
        {
            LogInfo("Cannot apply inventory data - InventoryData is null", 2);
            return;
        }

        // Проверяем количество предметов в данных (только для высокого уровня логов)
        if (LogLevel >= 3)
        {
            int itemsCount = 0;
            List<Dictionary<string, object>> itemsList = null;

            if (saveData.InventoryData.ContainsKey("items") &&
                saveData.InventoryData["items"] is List<Dictionary<string, object>> items)
            {
                itemsCount = items.Count;
                itemsList = items;

                LogInfo($"ApplyInventoryData: Found {itemsCount} items in save data", 3);

                // Вывод первых нескольких предметов для отладки
                for (int i = 0; i < Math.Min(items.Count, 3); i++)
                {
                    var item = items[i];
                    string name = item.ContainsKey("display_name") ? item["display_name"].ToString() : "Unknown";
                    int qty = item.ContainsKey("quantity") ? Convert.ToInt32(item["quantity"]) : 0;
                    string id = item.ContainsKey("id") ? item["id"].ToString() : "Unknown";
                    LogInfo($"Item to apply: {name} x{qty} (ID: {id})", 3);
                }
            }
            else
            {
                LogInfo("ApplyInventoryData: No valid items list found in save data", 3);
            }
        }

        // Сохраняем данные инвентаря в GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // ВАЖНО: Создаем глубокую копию данных инвентаря
            Dictionary<string, object> inventoryCopy = new Dictionary<string, object>();

            foreach (var key in saveData.InventoryData.Keys)
            {
                if (key == "items" && saveData.InventoryData[key] is List<Dictionary<string, object>> originalItems)
                {
                    // Копируем список предметов
                    var itemsCopy = new List<Dictionary<string, object>>();

                    foreach (var originalItem in originalItems)
                    {
                        var itemCopy = new Dictionary<string, object>();
                        foreach (var itemKey in originalItem.Keys)
                        {
                            itemCopy[itemKey] = originalItem[itemKey];
                        }
                        itemsCopy.Add(itemCopy);
                    }

                    inventoryCopy[key] = itemsCopy;
                }
                else
                {
                    inventoryCopy[key] = saveData.InventoryData[key];
                }
            }

            // Проверяем итоговое количество предметов в копии (только для логов высокого уровня)
            if (LogLevel >= 3)
            {
                int copyItemsCount = 0;
                if (inventoryCopy.ContainsKey("items") &&
                    inventoryCopy["items"] is List<Dictionary<string, object>> copyItems)
                {
                    copyItemsCount = copyItems.Count;
                }

                LogInfo($"Saving inventory data to GameManager, items count: {copyItemsCount}", 3);
            }

            gameManager.SetData("PlayerInventorySaved", inventoryCopy);
            gameManager.SetData("PlayerInventoryLastSaveTime", DateTime.Now.ToString());
            LogInfo("Applied inventory data to GameManager", 2);
        }
        else
        {
            LogError("GameManager not found for applying inventory data");
        }

        // Если игрок уже существует, применяем данные напрямую
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Десериализуем инвентарь напрямую
            try
            {
                player.PlayerInventory.Deserialize(saveData.InventoryData);
                LogInfo($"Applied inventory data directly to player", 2);
            }
            catch (Exception ex)
            {
                LogError($"Error applying inventory data to player: {ex.Message}");
            }
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

                LogInfo($"Applied storage data for '{storageId}'", 3);
            }

            // Общий лог по всем хранилищам
            LogInfo($"Applied data for {saveData.StationData.StorageData.Count} storage modules", 2);
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

            LogInfo("Applied progress data to GameManager", 2);
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