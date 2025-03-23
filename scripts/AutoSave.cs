using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Компонент для автоматического сохранения игры через определенные промежутки времени
/// и при важных изменениях игрового состояния (событийная модель).
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

    // Показывать ли уведомление при сохранении
    [Export] public bool ShowNotification { get; set; } = true;

    // Время задержки перед сохранением после изменения (для группировки изменений)
    [Export] public float SaveDelay { get; set; } = 0.2f; // 200 мс

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

    // Индикатор сохранения (UI)
    private Panel _saveIndicatorPanel;
    private Label _saveIndicatorLabel;
    private Timer _indicatorTimer;

    // Отслеживание изменений для оптимизации сохранений
    private HashSet<string> _changedSystems = new HashSet<string>();

    // Количество активных процессов сохранения
    private int _activeSaveCount = 0;

    private static bool _isTeleporting = false;

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
            WaitTime = SaveDelay, // Используем короткую задержку для быстрого сохранения
            OneShot = true,
            Autostart = false
        };
        _changeTimer.Timeout += OnChangeTimerTimeout;
        AddChild(_changeTimer);

        // Подключаемся к событию выхода из дерева сцен
        GetTree().Connect("tree_exiting", Callable.From(OnTreeExiting));

        // Создаем индикатор сохранения
        if (ShowNotification)
        {
            CreateSaveIndicator();
        }

        // Запускаем первое автосохранение с задержкой
        StartInitialSave();

        // Подключаемся к сигналам изменения состояния игры
        ConnectToGameSignals();

        // Отложенное подключение сигналов (после полной загрузки сцены)
        CallDeferred("ConnectSignalsDeferred");

        Logger.Debug($"AutoSave initialized with interval: {SaveInterval}s", true);
    }

    /// <summary>
    /// Подключает сигналы после полной загрузки сцены
    /// </summary>
    private void ConnectSignalsDeferred()
    {
        // Ждем полную загрузку сцены, чтобы найти все объекты
        ConnectToGameSignals();
    }

    public static void BlockDuringTeleportation(bool block)
    {
        _isTeleporting = block;
        Logger.Debug($"AutoSave teleportation block: {(block ? "ON" : "OFF")}", true);
    }


    /// <summary>
    /// Подключается к сигналам изменения состояния игры
    /// </summary>
    /// 

    private void ConnectToGameSignals()
    {
        // Подписываемся на сигналы изменения инвентаря игрока
        ConnectToPlayerInventorySignals();

        // Подписываемся на сигналы изменения контейнеров
        ConnectToContainerSignals();

        // Подписываемся на сигналы изменения модулей хранилищ
        ConnectToStorageModuleSignals();
    }

    /// <summary>
    /// Подключается к сигналам изменения инвентаря игрока
    /// </summary>
    private void ConnectToPlayerInventorySignals()
    {
        // Находим всех игроков в сцене
        var players = GetTree().GetNodesInGroup("Player");
        foreach (var playerNode in players)
        {
            if (playerNode is Player player)
            {
                // Проверяем, не подключены ли мы уже к сигналу
                if (!player.IsConnected(Player.SignalName.PlayerInventoryChanged, Callable.From(OnPlayerInventoryChanged)))
                {
                    player.Connect(Player.SignalName.PlayerInventoryChanged, Callable.From(OnPlayerInventoryChanged));
                    Logger.Debug("AutoSave: Connected to player inventory signals", false);
                }
            }
        }
    }

    /// <summary>
    /// Подключается к сигналам изменения контейнеров
    /// </summary>
    private void ConnectToContainerSignals()
    {
        // Находим все контейнеры в сцене
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var containerNode in containers)
        {
            if (containerNode is Container container && container.ContainerInventory != null)
            {
                // Проверяем, не подключены ли мы уже к сигналу
                if (!container.ContainerInventory.IsConnected("InventoryChanged", Callable.From(OnContainerInventoryChanged)))
                {
                    container.ContainerInventory.Connect("InventoryChanged", Callable.From(OnContainerInventoryChanged));
                    Logger.Debug($"AutoSave: Connected to container inventory signals: {container.Name}", false);
                }
            }
        }
    }

    /// <summary>
    /// Подключается к сигналам изменения модулей хранилищ
    /// </summary>
    private void ConnectToStorageModuleSignals()
    {
        // Находим все модули хранилищ в сцене
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule module)
            {
                // Проверяем наличие сигнала StorageChanged
                if (module.HasSignal("StorageChanged") &&
                    !module.IsConnected("StorageChanged", Callable.From(OnStorageModuleChanged)))
                {
                    module.Connect("StorageChanged", Callable.From(OnStorageModuleChanged));
                    Logger.Debug($"AutoSave: Connected to storage module signals via StorageChanged: {module.Name}", false);
                }
                // Если сигнала нет, находим контейнер внутри модуля и подключаемся к его инвентарю
                else
                {
                    var container = module.GetNode<Container>("StorageContainer");
                    if (container != null && container.ContainerInventory != null)
                    {
                        if (!container.ContainerInventory.IsConnected("InventoryChanged", Callable.From(OnContainerInventoryChanged)))
                        {
                            container.ContainerInventory.Connect("InventoryChanged", Callable.From(OnContainerInventoryChanged));
                            Logger.Debug($"AutoSave: Connected to storage module container signals: {module.Name}", false);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Обработчик изменения инвентаря игрока
    /// </summary>
    private void OnPlayerInventoryChanged()
    {
        Logger.Debug("AutoSave: Player inventory changed, scheduling save", false);
        _changedSystems.Add("PlayerInventory");
        NotifyChange();
    }

    /// <summary>
    /// Обработчик изменения инвентаря контейнера
    /// </summary>
    private void OnContainerInventoryChanged()
    {
        Logger.Debug("AutoSave: Container inventory changed, scheduling save", false);
        _changedSystems.Add("Containers");
        NotifyChange();
    }

    /// <summary>
    /// Обработчик изменения модуля хранилища
    /// </summary>
    private void OnStorageModuleChanged()
    {
        Logger.Debug("AutoSave: Storage module changed, scheduling save", false);
        _changedSystems.Add("StorageModules");
        NotifyChange();
    }

    /// <summary>
    /// Создает визуальный индикатор сохранения
    /// </summary>
    private void CreateSaveIndicator()
    {
        // Создаем панель для индикатора (если уже не создана в другом месте)
        if (_saveIndicatorPanel != null)
            return;

        _saveIndicatorPanel = new Panel();
        _saveIndicatorPanel.Name = "SaveIndicatorPanel";
        _saveIndicatorPanel.Position = new Vector2(20, 60); // Чуть ниже, чтобы не перекрывать другие уведомления
        _saveIndicatorPanel.Size = new Vector2(150, 40);
        _saveIndicatorPanel.Visible = false;

        // Стиль для панели (полупрозрачный фон)
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.3f, 0.7f); // Синеватый, чтобы отличался от других уведомлений
        styleBox.CornerRadiusTopLeft = styleBox.CornerRadiusTopRight =
        styleBox.CornerRadiusBottomLeft = styleBox.CornerRadiusBottomRight = 5;
        _saveIndicatorPanel.AddThemeStyleboxOverride("panel", styleBox);

        // Текст индикатора
        _saveIndicatorLabel = new Label();
        _saveIndicatorLabel.Name = "SaveIndicatorLabel";
        _saveIndicatorLabel.Text = "Auto-Saving...";
        _saveIndicatorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _saveIndicatorLabel.VerticalAlignment = VerticalAlignment.Center;
        _saveIndicatorLabel.Size = _saveIndicatorPanel.Size;

        // Стиль для текста
        _saveIndicatorLabel.AddThemeColorOverride("font_color", Colors.White);
        _saveIndicatorLabel.AddThemeFontSizeOverride("font_size", 14);

        // Добавляем текст к панели
        _saveIndicatorPanel.AddChild(_saveIndicatorLabel);

        // Создаем канвас для отображения поверх всего
        var canvas = new CanvasLayer();
        canvas.Name = "SaveIndicatorCanvas";
        canvas.Layer = 10; // Высокий слой для отображения поверх UI

        // Добавляем панель к канвасу
        canvas.AddChild(_saveIndicatorPanel);

        // Добавляем канвас к ноде
        AddChild(canvas);

        // Создаем таймер для скрытия уведомления, если его еще нет
        if (_indicatorTimer == null)
        {
            _indicatorTimer = new Timer();
            _indicatorTimer.WaitTime = 1.0f; // 1 секунда отображения
            _indicatorTimer.OneShot = true;
            _indicatorTimer.Timeout += () => {
                if (_saveIndicatorPanel != null)
                    _saveIndicatorPanel.Visible = false;
            };
            AddChild(_indicatorTimer);
        }
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
           // if (!_initialStateLoaded)
         //   {
            //    LoadInitialState();
          //  }

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
            PerformSave(true); // Принудительное сохранение при выходе
        }
    }

    /// <summary>
    /// Выполняет сохранение игры
    /// </summary>
    /// <param name="immediate">Выполнить сохранение немедленно, без проверок интервалов</param>
    private void PerformSave(bool immediate = false)
    {
        // Если идет телепортация, и это не принудительное сохранение, пропускаем
        if (_isTeleporting && !immediate)
        {
            Logger.Debug("AutoSave: Skipping save during teleportation", true);
            return;
        }
        // Увеличиваем счетчик активных сохранений
        _activeSaveCount++;

        // Показываем индикатор сохранения, если включен
        if (ShowNotification && _saveIndicatorPanel != null)
        {
            _saveIndicatorPanel.Visible = true;
            _saveIndicatorLabel.Text = "Auto-Saving...";
            if (_indicatorTimer != null)
                _indicatorTimer.Stop(); // Останавливаем таймер, чтобы индикатор не исчез слишком рано
        }

        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Явно сохраняем инвентарь игрока перед основным сохранением
            SavePlayerInventory(gameManager);

            // Сохраняем данные хранилищ станции, если есть
            SaveStorageData(gameManager);

            // Выполняем общее сохранение
            bool success = gameManager.SaveGame();

            if (success)
            {
                // Обновляем время последнего сохранения
                _lastSaveTime = (float)(Time.GetTicksMsec() / 1000.0);
                Logger.Debug($"Auto-save successful at {DateTime.Now.ToString("HH:mm:ss")}", true);

                // Обновляем индикатор
                UpdateSaveIndicator(true);

                // Сбрасываем список измененных систем
                _changedSystems.Clear();
            }
            else
            {
                Logger.Error("Auto-save failed");

                // Обновляем индикатор для ошибки
                UpdateSaveIndicator(false);
            }
        }

        // Уменьшаем счетчик активных сохранений
        _activeSaveCount--;
    }

    /// <summary>
    /// Обновляет вид индикатора сохранения
    /// </summary>
    /// <param name="success">Было ли сохранение успешным</param>
    private void UpdateSaveIndicator(bool success)
    {
        if (!ShowNotification || _saveIndicatorPanel == null)
            return;

        if (success)
        {
            _saveIndicatorLabel.Text = "Game Saved";
            var styleBox = _saveIndicatorPanel.GetThemeStylebox("panel", "Panel") as StyleBoxFlat;
            if (styleBox != null)
            {
                styleBox.BgColor = new Color(0.1f, 0.3f, 0.1f, 0.7f); // Зеленоватый для успеха
            }
        }
        else
        {
            _saveIndicatorLabel.Text = "Save Failed";
            var styleBox = _saveIndicatorPanel.GetThemeStylebox("panel", "Panel") as StyleBoxFlat;
            if (styleBox != null)
            {
                styleBox.BgColor = new Color(0.3f, 0.1f, 0.1f, 0.7f); // Красноватый для ошибки
            }
        }

        // Запускаем таймер для скрытия индикатора
        if (_indicatorTimer != null)
        {
            _indicatorTimer.Start();
        }
    }

    /// <summary>
    /// Сохраняет инвентарь игрока
    /// </summary>
    private void SavePlayerInventory(GameManager gameManager)
    {
        if (gameManager == null)
            return;

        // Сохраняем только если были изменения в инвентаре или это принудительное сохранение
        if (!_changedSystems.Contains("PlayerInventory") && _changedSystems.Count > 0)
            return;

        // Находим игрока для получения инвентаря
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            // Сериализуем инвентарь
            var inventoryData = player.PlayerInventory.Serialize();

            // Сохраняем в GameManager
            gameManager.SetData("PlayerInventorySaved", inventoryData);

            // Добавляем метку времени для отслеживания актуальности данных
            gameManager.SetData("PlayerInventoryLastSaveTime", DateTime.Now.ToString());

            int itemCount = 0;
            if (inventoryData.ContainsKey("items") &&
                inventoryData["items"] is List<Dictionary<string, object>> items)
            {
                itemCount = items.Count;
            }

            Logger.Debug($"Saved player inventory with {itemCount} items", false);
        }
    }

    /// <summary>
    /// Сохраняет данные хранилищ станции
    /// </summary>
    private void SaveStorageData(GameManager gameManager)
    {
        if (gameManager == null)
            return;

        // Сохраняем только если были изменения в хранилищах или это принудительное сохранение
        if (!_changedSystems.Contains("Containers") && !_changedSystems.Contains("StorageModules") && _changedSystems.Count > 0)
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
        // Выполняем сохранение немедленно
        PerformSave(true);

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

    /// <summary>
    /// Дополнительный метод для вызова извне (например, при подборе важных предметов)
    /// </summary>
    /// <param name="itemId">ID подобранного предмета</param>
    public void SaveAfterImportantPickup(string itemId)
    {
        // Запоминаем, что произошло важное событие
        Logger.Debug($"AutoSave: Important item pickup: {itemId}, forcing save", true);
        _changedSystems.Add("ImportantPickup");

        // Принудительно сохраняем игру, игнорируя все таймеры и ограничения
        ForceSave();
    }
}