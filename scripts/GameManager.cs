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
    /// Очищает все данные
    /// </summary>
    public void ClearData()
    {
        _data.Clear();
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
}