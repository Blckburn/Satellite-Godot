using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Синглтон для управления глобальным состоянием игры.
/// Добавляется как автозагружаемый сценарий.
/// </summary>
public partial class GameManager : Node
{
    // Синглтон для удобного доступа
    public static GameManager Instance { get; private set; }

    // Хранилище данных
    private Dictionary<string, object> _data = new Dictionary<string, object>();

    // Текущая сцена
    private string _currentScene = "";

    // Флаг инициализации
    private bool _initialized = false;

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
        {
            Instance = this;
            // Делаем этот узел персистентным, чтобы он не удалялся при смене сцены
            ProcessMode = ProcessModeEnum.Always;
        }
        else
        {
            // Если синглтон уже существует, удаляем этот экземпляр
            QueueFree();
            return;
        }

        Logger.Debug("GameManager initialized", true);

        // Инициализация
        Initialize();

        InitializeSaveSystem();
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Инициализирует GameManager
    /// </summary>
    private void Initialize()
    {
        if (_initialized)
            return;

        // Подключаемся к сигналу смены сцены
        GetTree().Root.Connect("scene_changed", Callable.From(() => OnSceneChanged()));

        _initialized = true;
    }

    /// <summary>
    /// Обработчик изменения сцены
    /// </summary>
    private void OnSceneChanged()
    {
        // Сохраняем важные данные перед сменой сцены
        ProtectKeyData();

        // Обновляем информацию о текущей сцене
        var currentSceneRoot = GetTree().CurrentScene;
        if (currentSceneRoot != null)
        {
            _currentScene = currentSceneRoot.SceneFilePath;
            Logger.Debug($"Scene changed to: {_currentScene}", false);
        }

        // Восстанавливаем защищенные данные после смены сцены
        CallDeferred("RestoreProtectedData");
    }

    /// <summary>
    /// Сохраняет данные в словарь
    /// </summary>
    public void SetData<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
            return;

        _data[key] = value;
    }

    /// <summary>
    /// Получает данные из словаря
    /// </summary>
    public T GetData<T>(string key)
    {
        if (string.IsNullOrEmpty(key) || !_data.ContainsKey(key))
            return default;

        try
        {
            return (T)_data[key];
        }
        catch (Exception ex)
        {
            Logger.Error($"Error getting data for key {key}: {ex.Message}");
            return default;
        }
    }

    /// <summary>
    /// Проверяет, существуют ли данные с указанным ключом
    /// </summary>
    public bool HasData(string key)
    {
        return !string.IsNullOrEmpty(key) && _data.ContainsKey(key);
    }

    /// <summary>
    /// Удаляет данные с указанным ключом
    /// </summary>
    public void RemoveData(string key)
    {
        if (string.IsNullOrEmpty(key) || !_data.ContainsKey(key))
            return;

        _data.Remove(key);
    }

/// <summary>
/// Очищает все сохраненные данные в GameManager
/// </summary>
public void ClearData()
{
    _data.Clear();
    Logger.Debug("GameManager data cleared", true);
}


    /// <summary>
    /// Получает текущую сцену
    /// </summary>
    public string GetCurrentScene()
    {
        return _currentScene;
    }

    /// <summary>
    /// Сохраняет данные о телепортации
    /// </summary>
    public void SetTeleportDestination(Vector2 position, string scene)
    {
        SetData("TeleportPosition", position);
        SetData("TeleportScene", scene);
    }

    /// <summary>
    /// Получает позицию телепортации
    /// </summary>
    public Vector2 GetTeleportPosition()
    {
        return GetData<Vector2>("TeleportPosition");
    }

    /// <summary>
    /// Получает сцену телепортации
    /// </summary>
    public string GetTeleportScene()
    {
        return GetData<string>("TeleportScene");
    }

    /// <summary>
    /// Получает список всех сохраненных хранилищ
    /// </summary>
    public List<string> GetAllStorageIds()
    {
        List<string> storageIds = new List<string>();
        string prefix = "StorageInventory_";

        // Проходим по всем ключам и ищем те, что начинаются с префикса хранилища
        foreach (var key in _data.Keys)
        {
            if (key.StartsWith(prefix))
            {
                string storageId = key.Substring(prefix.Length);
                storageIds.Add(storageId);
            }
        }

        return storageIds;
    }

    /// <summary>
    /// Проверяет, существует ли сохраненное хранилище с указанным ID
    /// </summary>
    public bool HasStorageInventory(string storageId)
    {
        string key = $"StorageInventory_{storageId}";
        return HasData(key);
    }

    /// <summary>
    /// Инициализирует интеграцию с SaveManager
    /// </summary>
    public void InitializeSaveSystem()
    {
        // Проверяем, подключен ли уже SaveManager
        if (HasData("SaveManagerConnected"))
            return;

        // Подключаемся к сигналам SaveManager
        var saveManager = GetNode<SaveManager>("/root/SaveManager");
        if (saveManager != null)
        {
            saveManager.Connect("SaveCompleted", Callable.From(OnSaveCompleted));
            saveManager.Connect("LoadCompleted", Callable.From(OnLoadCompleted));

            // Устанавливаем флаг подключения
            SetData("SaveManagerConnected", true);

            Logger.Debug("GameManager connected to SaveManager", true);
        }
        else
        {
            Logger.Error("SaveManager not found");
        }
    }

    /// <summary>
    /// Обработчик завершения сохранения
    /// </summary>
    private void OnSaveCompleted()
    {
        Logger.Debug("Save completed successfully", true);

        // Здесь можно добавить любую логику, которая должна выполняться после сохранения
        // Например, показать уведомление пользователю
    }

    /// <summary>
    /// Принудительно проверяет и восстанавливает данные инвентаря игрока
    /// </summary>
    public void EnsurePlayerInventoryLoaded()
    {
        // Проверяем наличие данных инвентаря
        if (!HasData("PlayerInventorySaved"))
        {
            Logger.Debug("GameManager: No player inventory data to restore", true);
            return;
        }

        // Получаем всех игроков в сцене
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count == 0)
        {
            Logger.Debug("GameManager: No player found in scene to restore inventory", true);
            return;
        }

        // Для каждого игрока восстанавливаем инвентарь
        foreach (var playerNode in players)
        {
            if (playerNode is Player player)
            {
                // Если инвентарь пуст или его нет, загружаем сохраненный
                if (player.PlayerInventory == null || player.PlayerInventory.Items.Count == 0)
                {
                    Logger.Debug("GameManager: Player with empty inventory found, restoring...", true);
                    bool result = player.LoadInventory();
                    Logger.Debug($"GameManager: Inventory restore result: {result}", true);

                    // Принудительно обновляем UI инвентаря
                    var inventoryUIs = GetTree().GetNodesInGroup("InventoryUI");
                    foreach (var uiNode in inventoryUIs)
                    {
                        if (uiNode is InventoryUI ui)
                        {
                            ui.UpdateInventoryUI();
                            Logger.Debug("GameManager: Forced inventory UI update", true);
                        }
                    }
                }
                else
                {
                    Logger.Debug($"GameManager: Player already has inventory with {player.PlayerInventory.Items.Count} items", true);
                }
            }
        }
    }

    /// <summary>
    /// Принудительно проверяет и восстанавливает данные хранилищ спутника
    /// </summary>
    public void EnsureStorageModulesLoaded()
    {
        Logger.Debug("GameManager: Starting storage modules data check", true);

        // Список всех обнаруженных данных хранилищ и их ключей
        Dictionary<string, Dictionary<string, object>> storageData = new Dictionary<string, Dictionary<string, object>>();

        // Собираем все данные хранилищ
        foreach (string key in _data.Keys)
        {
            if (key.StartsWith("StorageInventory_"))
            {
                string storageKey = key.Substring("StorageInventory_".Length);

                // Получаем данные инвентаря
                Dictionary<string, object> inventoryData = GetData<Dictionary<string, object>>(key);
                if (inventoryData != null)
                {
                    storageData[storageKey] = inventoryData;

                    // Отображаем количество предметов для отладки
                    int itemCount = 0;
                    if (inventoryData.ContainsKey("items") &&
                        inventoryData["items"] is List<Dictionary<string, object>> items)
                    {
                        itemCount = items.Count;
                    }

                    Logger.Debug($"GameManager: Found storage data for '{storageKey}' with {itemCount} items", true);
                }
            }
        }

        if (storageData.Count == 0)
        {
            Logger.Debug("GameManager: No storage data found to restore", true);
            return;
        }

        // Находим все модули хранилищ в текущей сцене
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        if (storageModules.Count == 0)
        {
            Logger.Debug("GameManager: No storage modules found in scene", true);
            return;
        }

        Logger.Debug($"GameManager: Found {storageModules.Count} storage modules in scene", true);

        // Для каждого модуля хранилища пытаемся загрузить данные
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule storageModule)
            {
                // Получаем ID хранилища и имя контейнера
                string storageId = storageModule.StorageID;
                string containerName = "StorageContainer"; // Имя по умолчанию для контейнера

                // Пытаемся получить фактическое имя контейнера
                var container = storageModule.GetNode<Container>("StorageContainer");
                if (container != null)
                {
                    containerName = container.Name;
                    Logger.Debug($"GameManager: Found container '{containerName}' in module '{storageId}'", true);
                }

                // Проверяем все возможные ключи, по которым могут быть сохранены данные
                Dictionary<string, object> dataToLoad = null;
                string usedKey = null;

                // Проверяем сначала StorageID, затем имя контейнера, а потом все остальные ключи
                if (storageData.ContainsKey(storageId))
                {
                    dataToLoad = storageData[storageId];
                    usedKey = storageId;
                }
                else if (storageData.ContainsKey(containerName))
                {
                    dataToLoad = storageData[containerName];
                    usedKey = containerName;
                }
                else
                {
                    // Ищем любое хранилище, которое ещё не использовалось
                    foreach (var entry in storageData)
                    {
                        if (!string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                        {
                            dataToLoad = entry.Value;
                            usedKey = entry.Key;
                            break;
                        }
                    }
                }

                // Если нашли подходящие данные, загружаем их напрямую в контейнер
                if (dataToLoad != null && container != null && container.ContainerInventory != null)
                {
                    Logger.Debug($"GameManager: Loading data for storage '{storageId}' from key '{usedKey}'", true);

                    // Очищаем текущий инвентарь контейнера
                    container.ContainerInventory.Clear();

                    // Загружаем данные
                    container.ContainerInventory.Deserialize(dataToLoad);

                    // Подсчитываем количество загруженных предметов
                    int loadedItems = container.ContainerInventory.Items.Count;
                    Logger.Debug($"GameManager: Successfully loaded {loadedItems} items into storage '{storageId}'", true);

                    // Обновляем UI, если хранилище открыто
                    storageModule.ForceUpdateContainerUI();

                    // Удаляем использованные данные, чтобы не использовать их повторно
                    storageData.Remove(usedKey);
                }
                else
                {
                    Logger.Debug($"GameManager: No suitable data found for storage module '{storageId}'", true);
                }
            }
        }

        // Если остались неиспользованные данные хранилищ, выводим информацию об этом
        if (storageData.Count > 0)
        {
            Logger.Debug($"GameManager: {storageData.Count} storage data entries were not applied to any module", true);
        }

        Logger.Debug("GameManager: Storage modules data check completed", true);
    }



    /// <summary>
    /// Обработчик завершения загрузки
    /// </summary>
    private void OnLoadCompleted()
    {
        Logger.Debug("Load completed successfully", true);

        // Принудительно восстанавливаем инвентарь игрока
        EnsurePlayerInventoryLoaded();

        // НОВАЯ СТРОКА: Принудительно восстанавливаем данные хранилищ
        // Получаем GameManager и вызываем его метод напрямую
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Вызываем метод GameManager для загрузки хранилищ
            gameManager.EnsureStorageModulesLoaded();
        }

        // Здесь логика, которая должна выполняться после загрузки
        // Например, переключение сцены на сохраненную локацию
        if (HasData("CurrentScene"))
        {
            string currentScene = GetData<string>("CurrentScene");
            if (!string.IsNullOrEmpty(currentScene))
            {
                // Проверяем, не находимся ли мы уже на этой сцене
                if (GetTree().CurrentScene.SceneFilePath != currentScene)
                {
                    // Переключаемся на сцену из сохранения
                    GetTree().ChangeSceneToFile(currentScene);
                    Logger.Debug($"Changing scene to: {currentScene}", true);
                }
            }
        }
    }

    /// <summary>
    /// Сохраняет игру
    /// </summary>
    /// <returns>Успешность операции</returns>
    public bool SaveGame()
    {
        var saveManager = GetNode<SaveManager>("/root/SaveManager");
        if (saveManager != null)
        {
            return saveManager.SaveGame();
        }
        else
        {
            Logger.Error("SaveManager not found for saving game");
            return false;
        }
    }

    /// <summary>
    /// Загружает игру
    /// </summary>
    /// <returns>Успешность операции</returns>
    public bool LoadGame()
    {
        var saveManager = GetNode<SaveManager>("/root/SaveManager");
        if (saveManager != null)
        {
            return saveManager.LoadGame();
        }
        else
        {
            Logger.Error("SaveManager not found for loading game");
            return false;
        }
    }

    /// <summary>
    /// Проверяет, существует ли сохранение
    /// </summary>
    /// <returns>True, если сохранение существует</returns>
    public bool SaveExists()
    {
        var saveManager = GetNode<SaveManager>("/root/SaveManager");
        if (saveManager != null)
        {
            return saveManager.SaveExists();
        }
        else
        {
            Logger.Error("SaveManager not found for checking save existence");
            return false;
        }
    }

    /// <summary>
    /// Защищает ключевые данные от потери при смене сцен
    /// </summary>
    public void ProtectKeyData()
    {
        // Список критически важных ключей данных
        string[] protectedKeys = new string[]
        {
        "PlayerInventorySaved",
        "PlayerInventoryLastSaveTime",
        "LastWorldPosition",
        "CurrentScene",
        };

        // Создаем резервные копии данных
        Dictionary<string, object> backupData = new Dictionary<string, object>();

        foreach (string key in protectedKeys)
        {
            if (HasData(key))
            {
                backupData[key] = _data[key];
                Logger.Debug($"GameManager: Protected data '{key}'", true);
            }
        }

        // Резервное копирование данных хранилищ
        List<string> storageKeys = new List<string>();
        foreach (string key in _data.Keys)
        {
            if (key.StartsWith("StorageInventory_"))
            {
                backupData[key] = _data[key];
                storageKeys.Add(key);
            }
        }

        if (storageKeys.Count > 0)
        {
            Logger.Debug($"GameManager: Protected {storageKeys.Count} storage inventories", true);
        }

        // Сохраняем резервные копии в специальное хранилище
        SetData("_BackupData", backupData);
        SetData("_BackupTime", DateTime.Now.ToString());
        Logger.Debug("GameManager: Created data backup", true);
    }

    /// <summary>
    /// Восстанавливает ключевые данные после смены сцены
    /// </summary>
    public void RestoreProtectedData()
    {
        if (!HasData("_BackupData"))
        {
            Logger.Debug("GameManager: No backup data found", true);
            return;
        }

        var backupData = GetData<Dictionary<string, object>>("_BackupData");
        if (backupData == null || backupData.Count == 0)
        {
            Logger.Debug("GameManager: Backup data is empty", true);
            return;
        }

        string backupTime = HasData("_BackupTime") ? GetData<string>("_BackupTime") : "unknown";
        Logger.Debug($"GameManager: Restoring backup data from {backupTime}", true);

        // Восстанавливаем данные
        int restoredCount = 0;
        foreach (var entry in backupData)
        {
            if (entry.Value != null)
            {
                _data[entry.Key] = entry.Value;
                restoredCount++;
            }
        }

        Logger.Debug($"GameManager: Restored {restoredCount} data entries", true);

        // Очищаем резервные копии
        RemoveData("_BackupData");
        RemoveData("_BackupTime");
    }



}