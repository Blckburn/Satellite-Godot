using Godot;
using System;

/// <summary>
/// Компонент для автоматического сохранения игры через определенные промежутки времени.
/// Прикрепите этот узел к корневому узлу игровой сцены.
/// </summary>
public partial class AutoSave : Node
{
    // Интервал автосохранения в секундах
    [Export] public float SaveInterval { get; set; } = 300f; // 5 минут по умолчанию

    // Минимальный интервал между сохранениями при изменениях (защита от спама сохранений)
    [Export] public float MinChangeInterval { get; set; } = 30f; // 30 секунд

    // Время до первого автосохранения после загрузки сцены
    [Export] public float InitialDelay { get; set; } = 60f; // 60 секунд

    // Сохранять при переходе между сценами
    [Export] public bool SaveOnSceneChange { get; set; } = true;

    // Сохранять при выходе из игры
    [Export] public bool SaveOnQuit { get; set; } = true;

    // Таймер для регулярных сохранений
    private Timer _autoSaveTimer;

    // Таймер для сохранений при изменениях
    private Timer _changeTimer;

    // Флаг, указывающий на необходимость сохранения из-за изменений
    private bool _changesPending = false;

    // Время последнего сохранения
    private float _lastSaveTime = 0f;

    // Начальное состояние загружено
    private bool _initialStateLoaded = false;

    public override void _Ready()
    {
        // Создаем и настраиваем таймер автосохранения
        _autoSaveTimer = new Timer
        {
            WaitTime = SaveInterval,
            OneShot = false,
            Autostart = false
        };
        _autoSaveTimer.Timeout += OnAutoSaveTimerTimeout;
        AddChild(_autoSaveTimer);

        // Создаем и настраиваем таймер для отложенного сохранения при изменениях
        _changeTimer = new Timer
        {
            WaitTime = MinChangeInterval,
            OneShot = true,
            Autostart = false
        };
        _changeTimer.Timeout += OnChangeTimerTimeout;
        AddChild(_changeTimer);

        // Подключаемся к событию выхода из дерева сцен
        GetTree().Connect("tree_exiting", Callable.From(OnTreeExiting));

        // Запускаем первое автосохранение с задержкой
        StartInitialSave();

        Logger.Debug($"AutoSave initialized with interval: {SaveInterval}s", true);
    }

    /// <summary>
    /// Запускает первое автосохранение с задержкой
    /// </summary>
    private void StartInitialSave()
    {
        // Создаем одноразовый таймер для первого сохранения
        var initialTimer = new Timer
        {
            WaitTime = InitialDelay,
            OneShot = true,
            Autostart = true
        };
        initialTimer.Timeout += () =>
        {
            // Загружаем начальное состояние, если оно еще не загружено
            if (!_initialStateLoaded)
            {
                LoadInitialState();
            }

            // Запускаем регулярное автосохранение
            _autoSaveTimer.Start();

            // Выполняем первое сохранение
            PerformSave();

            // Удаляем временный таймер
            initialTimer.QueueFree();
        };
        AddChild(initialTimer);

        Logger.Debug($"Initial save scheduled in {InitialDelay}s", true);
    }

    /// <summary>
    /// Загружает начальное состояние игры при запуске
    /// </summary>
    private void LoadInitialState()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null && gameManager.SaveExists())
        {
            bool success = gameManager.LoadGame();
            _initialStateLoaded = success;
            Logger.Debug($"Initial state load: {(success ? "successful" : "failed")}", true);
        }
        else
        {
            _initialStateLoaded = true; // Отмечаем как загруженное, чтобы не пытаться загрузить снова
            Logger.Debug("No save exists, skipping initial load", true);
        }
    }

    /// <summary>
    /// Метод для вызова из других скриптов при важных изменениях,
    /// которые следует сохранить в ближайшее время
    /// </summary>
    public void NotifyChange()
    {
        if (!_changesPending)
        {
            _changesPending = true;

            // Если прошло достаточно времени с последнего сохранения, запускаем таймер
            float timeSinceLastSave = (float)(Time.GetTicksMsec() / 1000.0) - _lastSaveTime;
            if (timeSinceLastSave >= MinChangeInterval)
            {
                _changeTimer.Start();
            }

            Logger.Debug("Game changes detected, scheduling save", false);
        }
    }

    /// <summary>
    /// Обработчик таймаута таймера автосохранения
    /// </summary>
    private void OnAutoSaveTimerTimeout()
    {
        PerformSave();
    }

    /// <summary>
    /// Обработчик таймаута таймера изменений
    /// </summary>
    private void OnChangeTimerTimeout()
    {
        if (_changesPending)
        {
            PerformSave();
            _changesPending = false;
        }
    }

    /// <summary>
    /// Обработчик выхода из дерева сцен (закрытие игры)
    /// </summary>
    private void OnTreeExiting()
    {
        if (SaveOnQuit)
        {
            PerformSave();
        }
    }

    /// <summary>
    /// Выполняет сохранение игры
    /// </summary>
    private void PerformSave()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Явно сохраняем инвентарь игрока перед основным сохранением
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
            {
                Logger.Debug($"Saving player inventory: {player.PlayerInventory.Items.Count} items", false);

                // Сериализуем инвентарь
                var inventoryData = player.PlayerInventory.Serialize();

                // Сохраняем в GameManager
                gameManager.SetData("PlayerInventorySaved", inventoryData);
            }

            // Сохраняем данные хранилищ станции, если есть
            SaveStorageData(gameManager);

            // Выполняем общее сохранение
            bool success = gameManager.SaveGame();

            if (success)
            {
                // Обновляем время последнего сохранения
                _lastSaveTime = (float)(Time.GetTicksMsec() / 1000.0);
                Logger.Debug($"Auto-save successful at {DateTime.Now.ToString("HH:mm:ss")}", true);
            }
            else
            {
                Logger.Error("Auto-save failed");
            }
        }
    }
    /// <summary>
    /// Сохраняет данные хранилищ станции
    /// </summary>
    private void SaveStorageData(GameManager gameManager)
    {
        if (gameManager == null)
            return;

        // Ищем все контейнеры хранилищ и сохраняем их состояние
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var module in storageModules)
        {
            if (module is StorageModule storageModule)
            {
                Logger.Debug($"Saving storage module: {storageModule.Name}", false);

                // Если у модуля есть метод SaveStorageInventory, вызываем его
                storageModule.CallDeferred("SaveStorageInventory");
            }
        }

        // Проверяем также независимые контейнеры
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var container in containers)
        {
            if (container is Container containerObj)
            {
                // Получаем ID контейнера
                string containerID = containerObj.Name;
                if (containerObj.HasMethod("GetStorageID"))
                {
                    containerID = (string)containerObj.Call("GetStorageID");
                }

                Logger.Debug($"Saving container: {containerID}", false);

                // Если контейнер имеет инвентарь, сохраняем его
                if (containerObj.ContainerInventory != null)
                {
                    string key = $"StorageInventory_{containerID}";
                    gameManager.SetData(key, containerObj.ContainerInventory.Serialize());
                }
            }
        }
    }

    /// <summary>
    /// Устанавливает интервал автосохранения
    /// </summary>
    /// <param name="interval">Новый интервал в секундах</param>
    public void SetSaveInterval(float interval)
    {
        if (interval > 0)
        {
            SaveInterval = interval;

            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.WaitTime = interval;
                Logger.Debug($"Auto-save interval changed to {interval}s", true);
            }
        }
    }

    /// <summary>
    /// Включает или выключает автосохранение
    /// </summary>
    /// <param name="enable">Включить ли автосохранение</param>
    public void EnableAutoSave(bool enable)
    {
        if (_autoSaveTimer != null)
        {
            if (enable)
            {
                if (!_autoSaveTimer.IsStopped())
                {
                    _autoSaveTimer.Start();
                    Logger.Debug("Auto-save enabled", true);
                }
            }
            else
            {
                _autoSaveTimer.Stop();
                Logger.Debug("Auto-save disabled", true);
            }
        }
    }

    /// <summary>
    /// Принудительно сохраняет игру, игнорируя все таймеры и ограничения
    /// </summary>
    /// <returns>True, если сохранение успешно</returns>
    public bool ForceSave()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            bool success = gameManager.SaveGame();

            if (success)
            {
                _lastSaveTime = (float)(Time.GetTicksMsec() / 1000.0);
                _changesPending = false;
                Logger.Debug("Forced save successful", true);
            }
            else
            {
                Logger.Error("Forced save failed");
            }

            return success;
        }

        return false;
    }



}