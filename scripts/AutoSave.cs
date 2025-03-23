using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Усовершенствованный компонент для автоматического сохранения игры через определенные промежутки времени
/// и при важных изменениях игрового состояния с активным мониторингом состояний.
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

    // Минимальный интервал для событийных сохранений (чтобы не сохранять слишком часто)
    [Export] public float EventSaveMinInterval { get; set; } = 10f; // 10 секунд по умолчанию

    // Включить активный мониторинг изменений (регулярная проверка вместо ожидания сигналов)
    [Export] public bool EnableActiveMonitoring { get; set; } = true;

    // Интервал активного мониторинга
    [Export] public float ActiveMonitoringInterval { get; set; } = 1.0f; // 1 секунда

    // Максимальное время ожидания перед принудительным сохранением после обнаружения изменений
    [Export] public float MaxWaitTimeBeforeForceSave { get; set; } = 2.0f; // 2 секунды

    // Уровень детализации логов (0-выключены, 1-основные, 2-подробные, 3-отладка)
    [Export] public int LogLevel { get; set; } = 0;

    // Таймер для регулярных сохранений
    private Timer _autoSaveTimer;

    // Таймер для сохранений при изменениях
    private Timer _changeTimer;

    // Таймер для активного мониторинга
    private Timer _monitoringTimer;

    // Флаг, указывающий на необходимость сохранения из-за изменений
    private bool _changesPending = false;

    // Время последнего сохранения
    private float _lastSaveTime = 0f;

    // Время обнаружения первого изменения для принудительного сохранения
    private float _firstChangeTime = 0f;

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

    // Таймер для переподключения сигналов
    private Timer _signalRecheckTimer;

    // Флаг для блокировки сохранений во время телепортации
    private static bool _isTeleporting = false;

    // Группы для поиска объектов
    private readonly string[] _inventoryGroups = { "Player", "Containers", "StorageModules" };

    // Счетчик переподключений сигналов (для отладки)
    private int _signalReconnectCount = 0;

    // Кеши для мониторинга состояния
    private Dictionary<string, int> _inventoryItemCounts = new Dictionary<string, int>();
    private Dictionary<string, long> _inventoryHashCodes = new Dictionary<string, long>();

    // Счетчик обнаруженных изменений мониторингом
    private int _monitoringDetectedChanges = 0;

    // Статический экземпляр для доступа к методам из статических методов
    private static AutoSave Instance { get; set; }

    public override void _Ready()
    {
        // Установка статического экземпляра
        Instance = this;

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

        // Создаем таймер для периодической проверки и подключения сигналов
        _signalRecheckTimer = new Timer
        {
            WaitTime = 5.0f, // Проверяем каждые 5 секунд
            OneShot = false,
            Autostart = true
        };
        _signalRecheckTimer.Timeout += RefreshSignalConnections;
        AddChild(_signalRecheckTimer);

        // Создаем таймер для активного мониторинга, если включен
        if (EnableActiveMonitoring)
        {
            _monitoringTimer = new Timer
            {
                WaitTime = ActiveMonitoringInterval,
                OneShot = false,
                Autostart = true
            };
            _monitoringTimer.Timeout += MonitorGameState;
            AddChild(_monitoringTimer);
        }

        // Подключаемся к событию выхода из дерева сцен
        GetTree().Connect("tree_exiting", Callable.From(OnTreeExiting));

        // Создаем индикатор сохранения
        if (ShowNotification)
        {
            CreateSaveIndicator();
        }

        // Запускаем первое автосохранение с задержкой
        StartInitialSave();

        // Подключаемся к сигналам изменения состояния игры (инициально)
        ConnectToGameSignals();

        // Отложенное подключение сигналов (после полной загрузки сцены)
        CallDeferred("ConnectSignalsDeferred");

        // Инициализация кешей для мониторинга
        InitializeMonitoringCaches();

        Log($"AutoSave initialized with interval: {SaveInterval}s, active monitoring: {EnableActiveMonitoring}", 1, true);
    }

    public override void _ExitTree()
    {
        // Очистка статического экземпляра
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Логирует сообщение с учетом заданного уровня логирования
    /// </summary>
    /// <param name="message">Сообщение для вывода</param>
    /// <param name="level">Уровень сообщения (0-3)</param>
    /// <param name="forceDisplay">Принудительно показать даже при низком LogLevel</param>
    private void Log(string message, int level = 1, bool forceDisplay = false)
    {
        if (level <= LogLevel || forceDisplay)
        {
            // Используем низкий уровень вывода для сокращения сообщений
            Logger.Debug(message, false);
        }
    }

    /// <summary>
    /// Инициализирует кеши для активного мониторинга
    /// </summary>
    private void InitializeMonitoringCaches()
    {
        if (!EnableActiveMonitoring)
            return;

        _inventoryItemCounts.Clear();
        _inventoryHashCodes.Clear();

        // Отложенная инициализация для учета всех объектов
        CallDeferred("InitializeMonitoringCachesDeferred");
    }

    /// <summary>
    /// Отложенная инициализация кешей для активного мониторинга
    /// </summary>
    private void InitializeMonitoringCachesDeferred()
    {
        // Кешируем состояние инвентаря игрока
        var players = GetTree().GetNodesInGroup("Player");
        foreach (var playerNode in players)
        {
            if (playerNode is Player player && player.PlayerInventory != null)
            {
                CacheInventoryState(player.PlayerInventory, "Player");
            }
        }

        // Кешируем состояние контейнеров
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var containerNode in containers)
        {
            if (containerNode is Container container && container.ContainerInventory != null)
            {
                CacheInventoryState(container.ContainerInventory, container.Name);
            }
        }

        // Кешируем состояние модулей хранилищ
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule module)
            {
                var container = module.GetNode<Container>("StorageContainer");
                if (container != null && container.ContainerInventory != null)
                {
                    CacheInventoryState(container.ContainerInventory, module.StorageID);
                }
            }
        }

        Log($"Monitoring caches initialized with {_inventoryItemCounts.Count} inventories", 3);
    }

    /// <summary>
    /// Кеширует состояние инвентаря для отслеживания изменений
    /// </summary>
    private void CacheInventoryState(Inventory inventory, string inventoryId)
    {
        if (inventory == null)
            return;

        // Кешируем количество предметов
        _inventoryItemCounts[inventoryId] = inventory.Items.Count;

        // Создаем хеш-код для быстрого сравнения
        long hashCode = 0;
        foreach (var item in inventory.Items)
        {
            // Простой алгоритм хеширования
            hashCode += item.Quantity;
            hashCode += item.ID.GetHashCode();
        }
        _inventoryHashCodes[inventoryId] = hashCode;
    }

    /// <summary>
    /// Активный мониторинг состояния игры для обнаружения изменений
    /// </summary>
    private void MonitorGameState()
    {
        if (!EnableActiveMonitoring)
            return;

        bool changesDetected = false;

        // Проверка инвентаря игрока
        var players = GetTree().GetNodesInGroup("Player");
        foreach (var playerNode in players)
        {
            if (playerNode is Player player && player.PlayerInventory != null)
            {
                if (CheckInventoryChanged(player.PlayerInventory, "Player"))
                {
                    changesDetected = true;
                    _changedSystems.Add("PlayerInventory_Monitored");
                }
            }
        }

        // Проверка контейнеров
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var containerNode in containers)
        {
            if (containerNode is Container container && container.ContainerInventory != null)
            {
                if (CheckInventoryChanged(container.ContainerInventory, container.Name))
                {
                    changesDetected = true;
                    _changedSystems.Add("Container_Monitored");
                }
            }
        }

        // Проверка модулей хранилищ
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule module)
            {
                var container = module.GetNode<Container>("StorageContainer");
                if (container != null && container.ContainerInventory != null)
                {
                    if (CheckInventoryChanged(container.ContainerInventory, module.StorageID))
                    {
                        changesDetected = true;
                        _changedSystems.Add("StorageModule_Monitored");
                    }
                }
            }
        }

        // Если обнаружены изменения, инициируем сохранение
        if (changesDetected)
        {
            _monitoringDetectedChanges++;
            Log($"Active monitoring detected changes ({_monitoringDetectedChanges} total)", 2);

            // Получаем текущее время
            float currentTime = (float)(Time.GetTicksMsec() / 1000.0);

            // Если это первое обнаруженное изменение, запоминаем время
            if (_firstChangeTime == 0)
            {
                _firstChangeTime = currentTime;
            }

            // Проверяем, нужно ли выполнить принудительное сохранение
            if (currentTime - _firstChangeTime > MaxWaitTimeBeforeForceSave)
            {
                Log("Maximum wait time exceeded, forcing save", 1);
                _firstChangeTime = 0; // Сбрасываем таймер
                PerformSave(true);
            }
            else
            {
                // Обычное уведомление об изменении
                NotifyChange("ActiveMonitoring");
            }
        }
    }

    /// <summary>
    /// Проверяет, изменился ли инвентарь с момента последнего кеширования
    /// </summary>
    private bool CheckInventoryChanged(Inventory inventory, string inventoryId)
    {
        if (inventory == null)
            return false;

        // Проверяем по количеству предметов
        if (_inventoryItemCounts.TryGetValue(inventoryId, out int cachedCount))
        {
            if (cachedCount != inventory.Items.Count)
            {
                // Обновляем кеш при изменении
                CacheInventoryState(inventory, inventoryId);
                return true;
            }
        }
        else
        {
            // Если этого инвентаря еще нет в кеше, добавляем его
            CacheInventoryState(inventory, inventoryId);
            return false; // Первое добавление не считаем изменением
        }

        // Проверяем по хеш-коду
        if (_inventoryHashCodes.TryGetValue(inventoryId, out long cachedHash))
        {
            // Создаем новый хеш-код
            long newHashCode = 0;
            foreach (var item in inventory.Items)
            {
                newHashCode += item.Quantity;
                newHashCode += item.ID.GetHashCode();
            }

            if (newHashCode != cachedHash)
            {
                // Обновляем кеш при изменении
                _inventoryHashCodes[inventoryId] = newHashCode;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Подключает сигналы после полной загрузки сцены
    /// </summary>
    private void ConnectSignalsDeferred()
    {
        // Добавляем небольшую задержку, чтобы все объекты успели инициализироваться
        var timer = new Timer();
        timer.WaitTime = 1.0f;  // Даем сцене секунду на полную загрузку
        timer.OneShot = true;
        timer.Timeout += () => {
            ConnectToGameSignals();
            timer.QueueFree();
            Log("Deferred signal connections established", 3);
        };
        AddChild(timer);
        timer.Start();
    }

    /// <summary>
    /// Периодически обновляет подключения к сигналам
    /// </summary>
    private void RefreshSignalConnections()
    {
        // Проверяем есть ли активные объекты сцены
        if (GetTree() == null || GetTree().CurrentScene == null)
            return;

        // Подключаемся к сигналам снова, чтобы не пропустить новые объекты
        bool anyConnected = ConnectToGameSignals();

        if (anyConnected)
        {
            _signalReconnectCount++;
            Log($"Refreshed signal connections: {_signalReconnectCount}", 3);
        }
    }

    /// <summary>
    /// Блокирует сохранения во время телепортации
    /// </summary>
    public static void BlockDuringTeleportation(bool block)
    {
        _isTeleporting = block;
        // Используем метод Log вместо прямого вызова Logger
        if (Instance != null)
            Instance.Log($"AutoSave teleportation block: {(block ? "ON" : "OFF")}", 1);
        else
            Logger.Debug($"AutoSave teleportation block: {(block ? "ON" : "OFF")}", false);
    }

    /// <summary>
    /// Подключается к сигналам изменения состояния игры
    /// </summary>
    private bool ConnectToGameSignals()
    {
        bool anyConnected = false;

        // Подписываемся на сигналы изменения инвентаря игрока
        if (ConnectToPlayerInventorySignals())
            anyConnected = true;

        // Подписываемся на сигналы изменения контейнеров
        if (ConnectToContainerSignals())
            anyConnected = true;

        // Подписываемся на сигналы изменения модулей хранилищ
        if (ConnectToStorageModuleSignals())
            anyConnected = true;

        return anyConnected;
    }

    /// <summary>
    /// Подключается к сигналам изменения инвентаря игрока
    /// </summary>
    private bool ConnectToPlayerInventorySignals()
    {
        bool anyConnected = false;

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
                    Log("AutoSave: Connected to player inventory signals", 3);
                    anyConnected = true;
                }

                // Также подключаемся к инвентарю напрямую, если он есть
                if (player.PlayerInventory != null &&
                    !player.PlayerInventory.IsConnected("InventoryChanged", Callable.From(OnInventoryChanged)))
                {
                    player.PlayerInventory.Connect("InventoryChanged", Callable.From(OnInventoryChanged));
                    Log("AutoSave: Connected directly to player inventory", 3);
                    anyConnected = true;
                }
            }
        }

        return anyConnected;
    }

    /// <summary>
    /// Подключается к сигналам изменения контейнеров
    /// </summary>
    private bool ConnectToContainerSignals()
    {
        bool anyConnected = false;

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
                    Log($"AutoSave: Connected to container inventory signals: {container.Name}", 3);
                    anyConnected = true;
                }

                // Подключаемся к контейнеру напрямую
                if (container.HasSignal("ContainerChanged") &&
                    !container.IsConnected("ContainerChanged", Callable.From(OnContainerChanged)))
                {
                    container.Connect("ContainerChanged", Callable.From(OnContainerChanged));
                    Log($"AutoSave: Connected to container directly: {container.Name}", 3);
                    anyConnected = true;
                }
            }
        }

        return anyConnected;
    }

    /// <summary>
    /// Подключается к сигналам изменения модулей хранилищ
    /// </summary>
    private bool ConnectToStorageModuleSignals()
    {
        bool anyConnected = false;

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
                    Log($"AutoSave: Connected to storage module signals: {module.Name}", 3);
                    anyConnected = true;
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
                            Log($"AutoSave: Connected to storage module container signals: {module.Name}", 3);
                            anyConnected = true;
                        }
                    }
                }
            }
        }

        return anyConnected;
    }

    /// <summary>
    /// Обработчик изменения инвентаря игрока
    /// </summary>
    private void OnPlayerInventoryChanged()
    {
        Log("AutoSave: Player inventory changed", 2);
        _changedSystems.Add("PlayerInventory");
        NotifyChange();
    }

    /// <summary>
    /// Общий обработчик изменения инвентаря
    /// </summary>
    private void OnInventoryChanged()
    {
        Log("AutoSave: Raw inventory changed", 2);
        _changedSystems.Add("Inventory");
        NotifyChange();
    }

    /// <summary>
    /// Обработчик изменения инвентаря контейнера
    /// </summary>
    private void OnContainerInventoryChanged()
    {
        Log("AutoSave: Container inventory changed", 2);
        _changedSystems.Add("Containers");
        NotifyChange();
    }

    /// <summary>
    /// Обработчик прямых изменений контейнера
    /// </summary>
    private void OnContainerChanged()
    {
        Log("AutoSave: Container directly changed", 2);
        _changedSystems.Add("ContainerDirect");
        NotifyChange();
    }

    /// <summary>
    /// Обработчик изменения модуля хранилища
    /// </summary>
    private void OnStorageModuleChanged()
    {
        Log("AutoSave: Storage module changed", 2);
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
        _saveIndicatorPanel.Size = new Vector2(200, 40);
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
            _indicatorTimer.WaitTime = 1.5f; // 1.5 секунды отображения
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
            // Запускаем регулярное автосохранение
            _autoSaveTimer.Start();

            // Выполняем первое сохранение
            PerformSave();

            // Удаляем временный таймер
            initialTimer.QueueFree();
        };
        AddChild(initialTimer);

        Log($"Initial save scheduled in {InitialDelay}s", 2);
    }

    /// <summary>
    /// Метод для вызова из других скриптов при важных изменениях,
    /// которые следует сохранить в ближайшее время
    /// </summary>
    public void NotifyChange(string systemName = "Unknown")
    {
        if (systemName != "Unknown")
        {
            _changedSystems.Add(systemName);
        }

        // Сбрасываем время первого изменения, если не установлено
        if (_firstChangeTime == 0)
        {
            _firstChangeTime = (float)(Time.GetTicksMsec() / 1000.0);
        }

        if (!_changesPending)
        {
            _changesPending = true;

            // Если прошло достаточно времени с последнего сохранения, запускаем таймер
            float timeSinceLastSave = (float)(Time.GetTicksMsec() / 1000.0) - _lastSaveTime;

            // Проверяем, прошло ли минимальное время между событийными сохранениями
            if (timeSinceLastSave >= EventSaveMinInterval)
            {
                // Запускаем таймер с коротким интервалом для быстрого сохранения
                if (_changeTimer.IsStopped())
                {
                    _changeTimer.WaitTime = SaveDelay;
                    _changeTimer.Start();
                    Log($"Event save scheduled in {SaveDelay}s", 2);
                }
            }
            else if (timeSinceLastSave >= MinChangeInterval)
            {
                // Запускаем таймер с минимальным интервалом до следующего сохранения
                if (_changeTimer.IsStopped())
                {
                    float waitTime = EventSaveMinInterval - timeSinceLastSave;
                    _changeTimer.WaitTime = waitTime;
                    _changeTimer.Start();
                    Log($"Event save delayed by {waitTime}s (cooling down)", 2);
                }
            }
            else
            {
                Log($"Event save pending, but too soon after last save ({timeSinceLastSave}s < {MinChangeInterval}s)", 3);
            }
        }
    }

    /// <summary>
    /// Обработчик таймаута таймера автосохранения
    /// </summary>
    private void OnAutoSaveTimerTimeout()
    {
        Log("Regular auto-save timer expired", 2);
        PerformSave();
    }

    /// <summary>
    /// Обработчик таймаута таймера изменений
    /// </summary>
    private void OnChangeTimerTimeout()
    {
        if (_changesPending)
        {
            Log("Change timer expired, performing save", 2);
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

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Проверяем, нажатие клавиши сохранения (для отладки)
        if (Input.IsKeyPressed(Key.F9))
        {
            Log("Manual event save triggered (Ctrl+F9)", 1);
            _changedSystems.Add("ManualEventSave");
            NotifyChange();
        }

        // Проверяем время с первого изменения (защита от зависания сохранения)
        if (_firstChangeTime > 0)
        {
            float currentTime = (float)(Time.GetTicksMsec() / 1000.0);
            if (currentTime - _firstChangeTime > MaxWaitTimeBeforeForceSave)
            {
                Log("Maximum wait time for changes exceeded, forcing save", 1);
                _firstChangeTime = 0; // Сбрасываем таймер ожидания
                PerformSave(true);
            }
        }
    }

    /// <summary>
    /// Выполняет сохранение игры
    /// </summary>
    /// <param name="immediate">Выполнить сохранение немедленно, без проверок интервалов</param>
    private void PerformSave(bool immediate = false)
    {
        // Сбрасываем флаг ожидания первого изменения
        _firstChangeTime = 0;

        // Если идет телепортация, и это не принудительное сохранение, пропускаем
        if (_isTeleporting && !immediate)
        {
            Log("AutoSave: Skipping save during teleportation", 1);
            return;
        }

        // Если уже идет процесс сохранения и это не принудительное сохранение, пропускаем
        if (_activeSaveCount > 0 && !immediate)
        {
            _changesPending = true;
            Log("AutoSave: Save already in progress, deferring", 2);
            return;
        }

        // Проверяем минимальный интервал, если это не принудительное сохранение
        if (!immediate)
        {
            float timeSinceLastSave = (float)(Time.GetTicksMsec() / 1000.0) - _lastSaveTime;
            if (timeSinceLastSave < MinChangeInterval)
            {
                // Если прошло недостаточно времени, откладываем сохранение
                _changesPending = true;

                // Устанавливаем отложенное сохранение
                if (_changeTimer.IsStopped())
                {
                    float waitTime = MinChangeInterval - timeSinceLastSave;
                    _changeTimer.WaitTime = waitTime;
                    _changeTimer.Start();
                    Log($"Save deferred by {waitTime}s (minimum interval not reached)", 2);
                }

                return;
            }
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
            // Детальное логирование состояния изменений
            if (_changedSystems.Count > 0)
            {
                Log($"Saving due to changes in: {string.Join(", ", _changedSystems)}", 2);
            }

            try
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

                    // Только если это первый уровень логов или принудительное сохранение
                    if (immediate || LogLevel > 0)
                        Log($"Auto-save successful", 1);

                    // Обновляем индикатор
                    UpdateSaveIndicator(true);

                    // Обновляем кеши мониторинга
                    if (EnableActiveMonitoring)
                    {
                        // Используем отложенное обновление, чтобы процесс сохранения завершился
                        CallDeferred("UpdateMonitoringCaches");
                    }

                    // Сбрасываем список измененных систем
                    _changedSystems.Clear();
                    _changesPending = false;
                }
                else
                {
                    Logger.Error("Auto-save failed");

                    // Обновляем индикатор для ошибки
                    UpdateSaveIndicator(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during save: {ex.Message}");
                UpdateSaveIndicator(false);
            }
        }

        // Уменьшаем счетчик активных сохранений
        _activeSaveCount--;
    }

    /// <summary>
    /// Обновляет кеши мониторинга после сохранения
    /// </summary>
    private void UpdateMonitoringCaches()
    {
        if (!EnableActiveMonitoring)
            return;

        // Переинициализируем кеши мониторинга
        InitializeMonitoringCachesDeferred();
        Log("Monitoring caches updated after save", 3);
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
        if (!_changedSystems.Contains("PlayerInventory") &&
            !_changedSystems.Contains("Inventory") &&
            !_changedSystems.Contains("PlayerInventory_Monitored") &&
            _changedSystems.Count > 0 &&
            _changedSystems.Count < 5) // Если много систем изменилось, лучше сохранить все
            return;

        // Находим игрока для получения инвентаря
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            try
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

                Log($"Saved player inventory with {itemCount} items", 2);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving player inventory: {ex.Message}");
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

        // Сохраняем только если были изменения в хранилищах или это принудительное сохранение
        if (!_changedSystems.Contains("Containers") &&
            !_changedSystems.Contains("StorageModules") &&
            !_changedSystems.Contains("ContainerDirect") &&
            !_changedSystems.Contains("Inventory") &&
            !_changedSystems.Contains("Container_Monitored") &&
            !_changedSystems.Contains("StorageModule_Monitored") &&
            _changedSystems.Count > 0 &&
            _changedSystems.Count < 5) // Если много систем изменилось, лучше сохранить все
            return;

        // Ищем все модули хранилищ и сохраняем их состояние
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var module in storageModules)
        {
            if (module is StorageModule storageModule)
            {
                try
                {
                    Log($"Saving storage module: {storageModule.Name}", 3);

                    // Если у модуля есть метод SaveStorageInventory, вызываем его напрямую
                    storageModule.SaveStorageInventory();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error saving storage module {storageModule.Name}: {ex.Message}");
                }
            }
        }

        // Проверяем также независимые контейнеры
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var container in containers)
        {
            if (container is Container containerObj)
            {
                // Пропускаем контейнеры, которые принадлежат модулям хранения
                bool isModuleContainer = false;
                foreach (var module in storageModules)
                {
                    if (module is StorageModule storageModule &&
                        storageModule.IsAncestorOf(containerObj))
                    {
                        isModuleContainer = true;
                        break;
                    }
                }

                if (!isModuleContainer)
                {
                    try
                    {
                        // Получаем ID контейнера
                        string containerID = containerObj.Name;
                        if (containerObj.HasMethod("GetStorageID"))
                        {
                            containerID = (string)containerObj.Call("GetStorageID");
                        }

                        Log($"Saving independent container: {containerID}", 3);

                        // Если контейнер имеет инвентарь, сохраняем его
                        if (containerObj.ContainerInventory != null)
                        {
                            string key = $"StorageInventory_{containerID}";
                            gameManager.SetData(key, containerObj.ContainerInventory.Serialize());
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error saving container {containerObj.Name}: {ex.Message}");
                    }
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
                Log($"Auto-save interval changed to {interval}s", 1);
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
            if (enable && _autoSaveTimer.IsStopped())
            {
                _autoSaveTimer.Start();
                Log("Auto-save enabled", 1);
            }
            else if (!enable && !_autoSaveTimer.IsStopped())
            {
                _autoSaveTimer.Stop();
                Log("Auto-save disabled", 1);
            }
        }
    }

    /// <summary>
    /// Включает или выключает активный мониторинг
    /// </summary>
    public void SetActiveMonitoring(bool enable)
    {
        EnableActiveMonitoring = enable;

        if (enable && _monitoringTimer == null)
        {
            // Создаем таймер для активного мониторинга
            _monitoringTimer = new Timer
            {
                WaitTime = ActiveMonitoringInterval,
                OneShot = false,
                Autostart = true
            };
            _monitoringTimer.Timeout += MonitorGameState;
            AddChild(_monitoringTimer);

            // Инициализируем кеши
            InitializeMonitoringCaches();

            Log("Active monitoring enabled", 1);
        }
        else if (!enable && _monitoringTimer != null)
        {
            _monitoringTimer.Stop();
            _monitoringTimer.QueueFree();
            _monitoringTimer = null;
            Log("Active monitoring disabled", 1);
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
                _firstChangeTime = 0;
                Log("Forced save successful", 1);
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
        Log($"AutoSave: Important item pickup: {itemId}, forcing save", 1);
        _changedSystems.Add("ImportantPickup");

        // Принудительно сохраняем игру, игнорируя все таймеры и ограничения
        ForceSave();
    }
}