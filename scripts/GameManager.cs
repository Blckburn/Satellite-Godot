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
        // Обновляем информацию о текущей сцене
        var currentSceneRoot = GetTree().CurrentScene;
        if (currentSceneRoot != null)
        {
            _currentScene = currentSceneRoot.SceneFilePath;
            Logger.Debug($"Scene changed to: {_currentScene}", false);
        }
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
    /// Обработчик завершения загрузки
    /// </summary>
    private void OnLoadCompleted()
    {
        Logger.Debug("Load completed successfully", true);

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




}