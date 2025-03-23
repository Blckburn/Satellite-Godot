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
    /// Загружает игру напрямую, минуя проблемную десериализацию SaveData
    /// </summary>
    public bool LoadGameDirectly()
    {
        try
        {
            // Проверяем, существует ли файл сохранения
            if (!File.Exists(_savePath))
            {
                Logger.Debug("Save file not found", true);
                return false;
            }

            // Читаем данные напрямую из файла как строку
            string jsonData = File.ReadAllText(_savePath, Encoding.UTF8);
            Logger.Debug($"Read save file with length: {jsonData.Length}", true);

            // Парсим JSON напрямую
            using (JsonDocument document = JsonDocument.Parse(jsonData))
            {
                JsonElement root = document.RootElement;
                var gameManager = GetNode<GameManager>("/root/GameManager");

                if (gameManager == null)
                {
                    Logger.Error("GameManager not found for direct loading");
                    return false;
                }

                // Обрабатываем PlayerData
                if (root.TryGetProperty("PlayerData", out JsonElement playerData))
                {
                    Logger.Debug("Processing PlayerData section directly", true);

                    // Позиция игрока
                    if (playerData.TryGetProperty("Position", out JsonElement positionData))
                    {
                        float x = positionData.GetProperty("X").GetSingle();
                        float y = positionData.GetProperty("Y").GetSingle();
                        Vector2 position = new Vector2(x, y);
                        gameManager.SetData("LastWorldPosition", position);
                        Logger.Debug($"Set player position: {position}", true);
                    }

                    // Текущая сцена
                    if (playerData.TryGetProperty("CurrentScene", out JsonElement sceneData))
                    {
                        string scene = sceneData.GetString();
                        gameManager.SetData("CurrentScene", scene);
                        Logger.Debug($"Set current scene: {scene}", true);
                    }

                    // Здоровье
                    if (playerData.TryGetProperty("Health", out JsonElement healthData) &&
                        playerData.TryGetProperty("MaxHealth", out JsonElement maxHealthData))
                    {
                        float health = healthData.GetSingle();
                        float maxHealth = maxHealthData.GetSingle();
                        gameManager.SetData("PlayerHealth", health);
                        gameManager.SetData("PlayerMaxHealth", maxHealth);
                        Logger.Debug($"Set player health: {health}/{maxHealth}", true);
                    }
                }

                // Обрабатываем InventoryData - критическое место
                if (root.TryGetProperty("InventoryData", out JsonElement inventoryData))
                {
                    Logger.Debug("Processing InventoryData section directly", true);

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

                                // Выводим информацию о предмете
                                string name = itemDict.ContainsKey("display_name") ? itemDict["display_name"].ToString() : "Unknown";
                                int qty = itemDict.ContainsKey("quantity") ? Convert.ToInt32(itemDict["quantity"]) : 0;
                                string id = itemDict.ContainsKey("id") ? itemDict["id"].ToString() : "Unknown";
                                Logger.Debug($"Manually processed item: {name} x{qty} (ID: {id})", true);
                            }

                            // Добавляем список предметов в словарь инвентаря
                            inventoryDict["items"] = itemsList;
                            Logger.Debug($"Manually processed {itemCount} items for inventory", true);
                        }
                        else
                        {
                            // Если предметов нет, создаем пустой список
                            inventoryDict["items"] = new List<Dictionary<string, object>>();
                            Logger.Debug("No items found in inventory data, creating empty list", true);
                        }

                        // Сохраняем обработанный инвентарь в GameManager
                        gameManager.SetData("PlayerInventorySaved", inventoryDict);
                        gameManager.SetData("PlayerInventoryLastSaveTime", DateTime.Now.ToString());
                        Logger.Debug($"Directly saved inventory with {(inventoryDict["items"] as List<Dictionary<string, object>>)?.Count ?? 0} items", true);
                    }
                }

                // Обрабатываем StationData
                if (root.TryGetProperty("StationData", out JsonElement stationData) &&
                    stationData.TryGetProperty("StorageData", out JsonElement storageData))
                {
                    Logger.Debug("Processing StationData section directly", true);

                    // Выводим все ключи для отладки
                    List<string> storageKeys = new List<string>();
                    foreach (JsonProperty storage in storageData.EnumerateObject())
                    {
                        storageKeys.Add(storage.Name);
                    }
                    Logger.Debug($"GameManager: Found {storageKeys.Count} storage keys: {string.Join(", ", storageKeys)}", true);

                    foreach (JsonProperty storage in storageData.EnumerateObject())
                    {
                        string storageId = storage.Name;
                        string storageJson = storage.Value.GetRawText();

                        Logger.Debug($"GameManager: Processing storage with ID '{storageId}'", true);

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

                                    // Добавим логирование для отладки
                                    string displayName = itemDict.ContainsKey("display_name") ? itemDict["display_name"].ToString() : "Unknown";
                                    int quantity = itemDict.ContainsKey("quantity") ? Convert.ToInt32(itemDict["quantity"]) : 0;
                                    Logger.Debug($"GameManager: Found item in storage '{storageId}': {displayName} x{quantity}", true);
                                }

                                // Добавляем список предметов в словарь хранилища
                                storageDict["items"] = itemsList;
                                Logger.Debug($"GameManager: Processed {itemCount} items for storage '{storageId}'", true);
                            }
                            else
                            {
                                // Если предметов нет, создаем пустой список
                                storageDict["items"] = new List<Dictionary<string, object>>();
                                Logger.Debug($"GameManager: No items found in storage '{storageId}', creating empty list", true);
                            }

                            // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Сохраняем обработанное хранилище в GameManager
                            // ВАЖНО: Используем правильный формат ключа StorageInventory_{storageId}
                            string saveKey = $"StorageInventory_{storageId}";
                            gameManager.SetData(saveKey, storageDict);
                            Logger.Debug($"GameManager: Saved storage '{storageId}' to GameManager with key '{saveKey}'", true);

                            // Дополнительно сохраняем под ключом StorageContainer для совместимости
                            // Это важно, так как некоторые модули ищут данные по имени контейнера
                            if (storageId != "StorageContainer")
                            {
                                string containerKey = "StorageInventory_StorageContainer";
                                gameManager.SetData(containerKey, storageDict);
                                Logger.Debug($"GameManager: Also saved storage '{storageId}' with container key '{containerKey}' for compatibility", true);
                            }
                        }
                    }
                }
                else
                {
                    Logger.Debug("GameManager: No StationData or StorageData found in save file", true);
                }

                // Обрабатываем ProgressData
                if (root.TryGetProperty("ProgressData", out JsonElement progressData))
                {
                    Logger.Debug("Processing ProgressData section directly", true);

                    // Обрабатываем статистику
                    if (progressData.TryGetProperty("Stats", out JsonElement statsData))
                    {
                        if (statsData.TryGetProperty("playtime_seconds", out JsonElement playtimeData))
                        {
                            float playtime = playtimeData.GetSingle();
                            gameManager.SetData("PlayTime", playtime);
                            Logger.Debug($"Set play time: {playtime} seconds", true);
                        }
                    }
                }
            }

            // Сообщаем об успешной загрузке
            Logger.Debug("Direct load completed successfully", true);

            EmitSignal("LoadCompleted");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error in direct load: {ex.Message}");
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
            Logger.Debug($"Read save file content: {jsonData.Length} bytes", true);

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
                Logger.Debug("Successfully parsed JSON document", true);
                JsonElement root = document.RootElement;

                // Проверяем корневые элементы
                bool hasInventoryData = root.TryGetProperty("InventoryData", out JsonElement inventoryElement);
                Logger.Debug($"Save file has InventoryData section: {hasInventoryData}", true);

                if (hasInventoryData)
                {
                    bool hasItems = inventoryElement.TryGetProperty("items", out JsonElement itemsElement);
                    Logger.Debug($"InventoryData has items array: {hasItems}", true);

                    if (hasItems && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        int itemsCount = itemsElement.GetArrayLength();
                        Logger.Debug($"Items array contains {itemsCount} elements", true);

                        // Выводим информацию о первых предметах для проверки
                        int maxToShow = Math.Min(itemsCount, 3);
                        for (int i = 0; i < maxToShow; i++)
                        {
                            JsonElement item = itemsElement[i];
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

                            Logger.Debug($"Item {i + 1} in save file: {displayName} x{quantity} (ID: {id})", true);
                        }
                    }
                }

                // Проверяем раздел StationData
                bool hasStationData = root.TryGetProperty("StationData", out JsonElement stationElement);
                Logger.Debug($"Save file has StationData section: {hasStationData}", true);

                if (hasStationData && stationElement.TryGetProperty("StorageData", out JsonElement storageDataElement))
                {
                    Logger.Debug("StationData has StorageData section", true);

                    // Проверяем объекты хранилищ
                    if (storageDataElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty storageProperty in storageDataElement.EnumerateObject())
                        {
                            string storageId = storageProperty.Name;
                            JsonElement storageElement = storageProperty.Value;

                            if (storageElement.TryGetProperty("items", out JsonElement storageItems) &&
                                storageItems.ValueKind == JsonValueKind.Array)
                            {
                                int storageItemsCount = storageItems.GetArrayLength();
                                Logger.Debug($"Storage '{storageId}' has {storageItemsCount} items in save file", true);
                            }
                            else
                            {
                                Logger.Debug($"Storage '{storageId}' has no items array or it's invalid", true);
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
                    Logger.Error("Failed to deserialize save data - result is null");
                    return null;
                }

                Logger.Debug($"Successfully deserialized save data (Version: {saveData.Version}, SaveDate: {saveData.SaveDate})", true);

                // Проверяем версию сохранения
                if (saveData.Version != SAVE_VERSION)
                {
                    Logger.Debug($"Save version mismatch: file version {saveData.Version}, current version {SAVE_VERSION}", true);
                    // В будущем здесь может быть логика миграции данных между версиями
                }

                return saveData;
            }
            catch (JsonException jsonEx)
            {
                Logger.Error($"JSON deserialization error: {jsonEx.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading save file '{_savePath}': {ex.Message}");
            Logger.Debug($"Exception details: {ex.ToString()}", true);
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
    /// <summary>
    /// Собирает данные космической станции
    /// </summary>
    /// <param name="saveData">Структура данных сохранения</param>
    private void CollectSpaceStationData(SaveData saveData)
    {
        // Создаем данные станции
        saveData.StationData = new SpaceStationData();

        // ОТЛАДКА: Проверяем все существующие ключи в GameManager
        Logger.Debug("Checking all keys in GameManager for storage data...", true);
        var gameManager = GetNode<GameManager>("/root/GameManager");
        List<string> allKeys = new List<string>();
        string prefix = "StorageInventory_";

        if (gameManager != null)
        {
            // Получаем список всех ключей через рефлексию или другой метод
            var keysMethod = gameManager.GetType().GetMethod("GetAllKeys");
            if (keysMethod != null)
            {
                var keys = keysMethod.Invoke(gameManager, null) as IEnumerable<string>;
                if (keys != null)
                {
                    allKeys.AddRange(keys);
                    Logger.Debug($"All keys in GameManager: {string.Join(", ", allKeys)}", true);

                    // Теперь ищем ключи хранилищ
                    List<string> storageKeys = new List<string>();
                    foreach (var key in allKeys)
                    {
                        if (key.StartsWith(prefix))
                        {
                            storageKeys.Add(key);
                        }
                    }

                    Logger.Debug($"Found {storageKeys.Count} storage keys: {string.Join(", ", storageKeys)}", true);
                }
            }
        }

        // Получаем данные хранилищ из GameManager
        if (gameManager != null)
        {
            // Создаем словарь для хранения данных хранилищ
            saveData.StationData.StorageData = new Dictionary<string, Dictionary<string, object>>();

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

                        int itemCount = 0;
                        if (inventoryData.ContainsKey("items") &&
                            inventoryData["items"] is List<Dictionary<string, object>> items)
                        {
                            itemCount = items.Count;
                        }

                        Logger.Debug($"Added storage '{id}' with {itemCount} items to save data", true);
                    }
                }
            }

            // НОВЫЙ СПОСОБ: Находим все ключи, начинающиеся с "StorageInventory_"
            if (saveData.StationData.StorageData.Count == 0)
            {
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

                            int itemCount = 0;
                            if (data.ContainsKey("items") &&
                                data["items"] is List<Dictionary<string, object>> items)
                            {
                                itemCount = items.Count;
                            }

                            Logger.Debug($"Found and added storage '{storageId}' with {itemCount} items", true);
                        }
                    }
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
            gameManager.SetData("CurrentScene", saveData.PlayerData.CurrentScene);

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
        {
            Logger.Debug("Cannot apply inventory data - InventoryData is null", true);
            return;
        }

        // Проверим количество предметов в данных
        int itemsCount = 0;
        List<Dictionary<string, object>> itemsList = null;

        if (saveData.InventoryData.ContainsKey("items") &&
            saveData.InventoryData["items"] is List<Dictionary<string, object>> items)
        {
            itemsCount = items.Count;
            itemsList = items;

            Logger.Debug($"ApplyInventoryData: Found {itemsCount} items in save data", true);

            // Вывод первых нескольких предметов для отладки
            for (int i = 0; i < Math.Min(items.Count, 3); i++)
            {
                var item = items[i];
                string name = item.ContainsKey("display_name") ? item["display_name"].ToString() : "Unknown";
                int qty = item.ContainsKey("quantity") ? Convert.ToInt32(item["quantity"]) : 0;
                string id = item.ContainsKey("id") ? item["id"].ToString() : "Unknown";
                Logger.Debug($"Item to apply: {name} x{qty} (ID: {id})", true);
            }
        }
        else
        {
            Logger.Debug("ApplyInventoryData: No valid items list found in save data", true);
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

            // Проверяем итоговое количество предметов в копии
            int copyItemsCount = 0;
            if (inventoryCopy.ContainsKey("items") &&
                inventoryCopy["items"] is List<Dictionary<string, object>> copyItems)
            {
                copyItemsCount = copyItems.Count;
            }

            Logger.Debug($"SaveManager: Saving inventory data to GameManager, items count: {copyItemsCount}", true);

            gameManager.SetData("PlayerInventorySaved", inventoryCopy);
            gameManager.SetData("PlayerInventoryLastSaveTime", DateTime.Now.ToString());
            Logger.Debug("Applied inventory data to GameManager with timestamp", true);
        }
        else
        {
            Logger.Error("GameManager not found for applying inventory data");
        }

        // Если игрок уже существует, применяем данные напрямую
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Десериализуем инвентарь напрямую
            try
            {
                player.PlayerInventory.Deserialize(saveData.InventoryData);
                Logger.Debug($"Applied inventory data directly to player, items count: {player.PlayerInventory.Items.Count}", true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying inventory data to player: {ex.Message}");
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