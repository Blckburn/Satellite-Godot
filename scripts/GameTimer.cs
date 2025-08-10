using Godot;
using System;

/// <summary>
/// Синглтон для отслеживания времени игры.
/// </summary>
public partial class GameTimer : Node
{
    // Синглтон для удобного доступа
    public static GameTimer Instance { get; private set; }

    // Общее время игры в секундах
    private float _totalPlayTime = 0f;

    // Время текущей сессии
    private float _sessionTime = 0f;

    // Время последнего обновления
    private double _lastUpdateTime = 0;

    // Флаг паузы
    private bool _isPaused = false;

    // Сигналы
    [Signal] public delegate void PlayTimeUpdatedEventHandler(float totalTime, float sessionTime);

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

        // Загружаем сохраненное время игры, если оно есть
        LoadPlayTime();

        // Инициализируем время последнего обновления
        _lastUpdateTime = Time.GetUnixTimeFromSystem();

        Logger.Debug($"GameTimer initialized. Total play time: {FormatTime(_totalPlayTime)}", true);
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;

        // Сохраняем время игры
        SavePlayTime();
    }

    public override void _Process(double delta)
    {
        if (!_isPaused)
        {
            // Обновляем время игры
            double currentTime = Time.GetUnixTimeFromSystem();
            float deltaTime = (float)(currentTime - _lastUpdateTime);
            _lastUpdateTime = currentTime;

            // Добавляем время к счетчикам
            _sessionTime += deltaTime;
            _totalPlayTime += deltaTime;

            // Отправляем сигнал обновления времени (раз в секунду)
            if ((int)_sessionTime != (int)(_sessionTime - deltaTime))
            {
                // ИСПРАВЛЕНИЕ: Используем строковое имя сигнала вместо SignalName
                EmitSignal("PlayTimeUpdated", _totalPlayTime, _sessionTime);
            }
        }
    }

    /// <summary>
    /// Паузирует таймер игры
    /// </summary>
    public void Pause()
    {
        if (!_isPaused)
        {
            _isPaused = true;
            SavePlayTime();
            Logger.Debug("Game timer paused", false);
        }
    }

    /// <summary>
    /// Возобновляет таймер игры
    /// </summary>
    public void Resume()
    {
        if (_isPaused)
        {
            _isPaused = false;
            _lastUpdateTime = Time.GetUnixTimeFromSystem();
            Logger.Debug("Game timer resumed", false);
        }
    }

    /// <summary>
    /// Сбрасывает таймер текущей сессии
    /// </summary>
    public void ResetSessionTime()
    {
        _sessionTime = 0f;
        _lastUpdateTime = Time.GetUnixTimeFromSystem();
        Logger.Debug("Session time reset", false);
    }

    /// <summary>
    /// Получает общее время игры в секундах
    /// </summary>
    /// <returns>Общее время игры</returns>
    public float GetTotalPlayTime()
    {
        return _totalPlayTime;
    }

    /// <summary>
    /// Получает время текущей сессии в секундах
    /// </summary>
    /// <returns>Время текущей сессии</returns>
    public float GetSessionTime()
    {
        return _sessionTime;
    }

    /// <summary>
    /// Форматирует время в читаемый формат (чч:мм:сс)
    /// </summary>
    /// <param name="seconds">Время в секундах</param>
    /// <returns>Отформатированное время</returns>
    public static string FormatTime(float seconds)
    {
        int hours = (int)(seconds / 3600);
        int minutes = (int)((seconds % 3600) / 60);
        int secs = (int)(seconds % 60);

        return $"{hours:00}:{minutes:00}:{secs:00}";
    }

    /// <summary>
    /// Загружает сохраненное время игры
    /// </summary>
    private void LoadPlayTime()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null && gameManager.HasData("PlayTime"))
        {
            _totalPlayTime = gameManager.GetData<float>("PlayTime");
            Logger.Debug($"Loaded play time: {FormatTime(_totalPlayTime)}", false);
        }
    }

    /// <summary>
    /// Сохраняет время игры
    /// </summary>
    private void SavePlayTime()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            gameManager.SetData("PlayTime", _totalPlayTime);
            Logger.Debug($"Saved play time: {FormatTime(_totalPlayTime)}", false);
        }
    }
}