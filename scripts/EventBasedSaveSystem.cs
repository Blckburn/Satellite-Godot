using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Модификации для класса AutoSave, которые нужно будет внести.
/// Этот код НЕ ЯВЛЯЕТСЯ самостоятельным классом - это просто примеры
/// для добавления в существующий AutoSave.cs!
/// 
/// Ниже представлены методы, которые нужно добавить в AutoSave.cs.
/// </summary>
// ПРИМЕЧАНИЕ: Не используйте этот класс напрямую! Модифицируйте существующий AutoSave.cs!
public partial class EventBasedSaveSystemExample : Node
{
    // Минимальный интервал между сохранениями (чтобы избежать спама сохранений)
    [Export] public float MinSaveInterval { get; set; } = 1.0f; // 1 секунда

    // Максимальная задержка после изменения перед сохранением
    [Export] public float SaveDelay { get; set; } = 0.2f; // 200 мс

    // Показывать ли небольшой индикатор при сохранении
    [Export] public bool ShowSaveIndicator { get; set; } = true;

    // Сохранять при выходе из игры
    [Export] public bool SaveOnQuit { get; set; } = true;

    // Время последнего сохранения
    private float _lastSaveTime = 0f;

    // Флаг, указывающий на необходимость сохранения
    private bool _pendingSave = false;

    // Таймер отложенного сохранения
    private Timer _saveDelayTimer;

    // Очередь изменений для сохранения (для будущего расширения)
    private HashSet<string> _changedSystems = new HashSet<string>();

    // Индикатор сохранения (UI)
    private Panel _saveIndicatorPanel;
    private Label _saveIndicatorLabel;
    private Timer _indicatorTimer;

    // Количество активных процессов сохранения
    private int _activeSaveCount = 0;

    public override void _Ready()
    {
        // Создаем таймер для отложенного сохранения
        _saveDelayTimer = new Timer();
        _saveDelayTimer.WaitTime = SaveDelay;
        _saveDelayTimer.OneShot = true;
        _saveDelayTimer.Timeout += OnSaveDelayTimeout;
        AddChild(_saveDelayTimer);

        // Таймер для скрытия индикатора
        _indicatorTimer = new Timer();
        _indicatorTimer.WaitTime = 1.0f;
        _indicatorTimer.OneShot = true;
        _indicatorTimer.Timeout += () => {
            if (_saveIndicatorPanel != null)
                _saveIndicatorPanel.Visible = false;
        };
        AddChild(_indicatorTimer);

        // Создаем индикатор сохранения, если включен
        if (ShowSaveIndicator)
        {
            CreateSaveIndicator();
        }

        // Подключаемся к сигналу выхода из дерева сцен
        GetTree().Connect("tree_exiting", Callable.From(OnTreeExiting));

        // Подписываемся на события изменения состояния игры
        ConnectToInventorySignals();
        ConnectToContainerSignals();

        Logger.Debug("EventBasedSaveSystem initialized", true);

        // Отложенное подключение сигналов (после полной загрузки сцены)
        CallDeferred("ConnectSignalsDeferred");
    }

    /// <summary>
    /// Подключает сигналы с небольшой задержкой для обеспечения полной загрузки сцены
    /// </summary>
    private void ConnectSignalsDeferred()
    {
        // Дополнительное подключение к сигналам, которые могут появиться после загрузки
        ConnectToInventorySignals();
        ConnectToContainerSignals();
    }

    /// <summary>
    /// Подключается к сигналам изменения инвентаря
    /// </summary>
    private void ConnectToInventorySignals()
    {
        // Подписываемся на сигналы изменения инвентаря игрока
        var players = GetTree().GetNodesInGroup("Player");
        foreach (var playerNode in players)
        {
            if (playerNode is Player player)
            {
                if (!player.IsConnected(Player.SignalName.PlayerInventoryChanged, Callable.From(OnInventoryChanged)))
                {
                    player.Connect(Player.SignalName.PlayerInventoryChanged, Callable.From(OnInventoryChanged));
                    Logger.Debug("Connected to player inventory signals", false);
                }
            }
        }
    }

    /// <summary>
    /// Подключается к сигналам изменения контейнеров
    /// </summary>
    private void ConnectToContainerSignals()
    {
        // Подписываемся на сигналы изменения хранилищ
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var containerNode in containers)
        {
            if (containerNode is Container container)
            {
                if (container.ContainerInventory != null &&
                    !container.ContainerInventory.IsConnected("InventoryChanged", Callable.From(OnContainerChanged)))
                {
                    container.ContainerInventory.Connect("InventoryChanged", Callable.From(OnContainerChanged));
                    Logger.Debug($"Connected to container signals: {container.Name}", false);
                }
            }
        }

        // Также подписываемся на сигналы изменения от модулей хранения
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule module)
            {
                // Проверяем наличие метода подключения к сигналам
                if (!module.IsConnected("ContainerInventoryChanged", Callable.From(OnStorageModuleChanged)))
                {
                    module.Connect("ContainerInventoryChanged", Callable.From(OnStorageModuleChanged));
                    Logger.Debug($"Connected to storage module signals: {module.Name}", false);
                }
            }
        }
    }

    /// <summary>
    /// Обрабатывает событие изменения инвентаря игрока
    /// </summary>
    private void OnInventoryChanged()
    {
        Logger.Debug("Player inventory changed, scheduling save", false);
        _changedSystems.Add("PlayerInventory");
        ScheduleSave();
    }

    /// <summary>
    /// Обрабатывает событие изменения контейнера
    /// </summary>
    private void OnContainerChanged()
    {
        Logger.Debug("Container changed, scheduling save", false);
        _changedSystems.Add("Containers");
        ScheduleSave();
    }

    /// <summary>
    /// Обрабатывает событие изменения модуля хранения
    /// </summary>
    private void OnStorageModuleChanged()
    {
        Logger.Debug("Storage module changed, scheduling save", false);
        _changedSystems.Add("StorageModules");
        ScheduleSave();
    }

    /// <summary>
    /// Обработчик таймаута для отложенного сохранения
    /// </summary>
    private void OnSaveDelayTimeout()
    {
        if (_pendingSave)
        {
            PerformSave();
        }
    }

    /// <summary>
    /// Обработчик выхода из дерева сцен (закрытие игры)
    /// </summary>
    private void OnTreeExiting()
    {
        if (SaveOnQuit)
        {
            // При выходе не используем задержку
            PerformSave(true);
        }
    }

    /// <summary>
    /// Планирует сохранение с задержкой
    /// </summary>
    private void ScheduleSave()
    {
        // Если есть активный процесс сохранения, откладываем новый
        if (_activeSaveCount > 0)
        {
            _pendingSave = true;
            return;
        }

        // Проверяем, прошло ли достаточно времени с предыдущего сохранения
        float currentTime = (float)(Time.GetTicksMsec() / 1000.0);
        if (currentTime - _lastSaveTime < MinSaveInterval)
        {
            _pendingSave = true;

            // Если таймер не активен, запускаем его
            if (_saveDelayTimer.IsStopped())
            {
                _saveDelayTimer.Start();
            }

            return;
        }

        // Запускаем таймер для отложенного сохранения
        _pendingSave = true;
        if (_saveDelayTimer.IsStopped())
        {
            _saveDelayTimer.Start();
        }
    }

    /// <summary>
    /// Создает визуальный индикатор сохранения
    /// </summary>
    private void CreateSaveIndicator()
    {
        // Создаем панель для индикатора
        _saveIndicatorPanel = new Panel();
        _saveIndicatorPanel.Name = "SaveIndicatorPanel";
        _saveIndicatorPanel.Position = new Vector2(20, 20);
        _saveIndicatorPanel.Size = new Vector2(150, 40);
        _saveIndicatorPanel.Visible = false;

        // Стиль для панели (полупрозрачный фон)
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);
        styleBox.CornerRadiusTopLeft = styleBox.CornerRadiusTopRight =
        styleBox.CornerRadiusBottomLeft = styleBox.CornerRadiusBottomRight = 5;
        _saveIndicatorPanel.AddThemeStyleboxOverride("panel", styleBox);

        // Текст индикатора
        _saveIndicatorLabel = new Label();
        _saveIndicatorLabel.Name = "SaveIndicatorLabel";
        _saveIndicatorLabel.Text = "Saving...";
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
    }

    /// <summary>
    /// Выполняет сохранение игры
    /// </summary>
    /// <param name="immediate">Выполнить сохранение немедленно, без проверок интервалов</param>
    private void PerformSave(bool immediate = false)
    {
        // Сбрасываем флаг ожидания сохранения
        _pendingSave = false;

        // Проверяем, прошло ли достаточно времени с предыдущего сохранения
        float currentTime = (float)(Time.GetTicksMsec() / 1000.0);
        if (!immediate && currentTime - _lastSaveTime < MinSaveInterval)
        {
            // Если прошло мало времени, откладываем сохранение
            ScheduleSave();
            return;
        }

        // Увеличиваем счетчик активных сохранений
        _activeSaveCount++;

        // Показываем индикатор сохранения, если включен
        if (ShowSaveIndicator && _saveIndicatorPanel != null)
        {
            _saveIndicatorPanel.Visible = true;
            _indicatorTimer.Stop(); // Останавливаем таймер, чтобы индикатор не исчез слишком рано
        }

        // Получаем GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Явное сохранение инвентаря игрока, если он изменился
            if (_changedSystems.Contains("PlayerInventory"))
            {
                SavePlayerInventory(gameManager);
            }

            // Сохранение контейнеров и модулей хранения, если они изменились
            if (_changedSystems.Contains("Containers") || _changedSystems.Contains("StorageModules"))
            {
                SaveStorageContainers(gameManager);
            }

            // Выполняем общее сохранение игры
            bool success = gameManager.SaveGame();

            if (success)
            {
                _lastSaveTime = currentTime;
                Logger.Debug($"Event-based save successful ({string.Join(", ", _changedSystems)})", true);

                // Обновляем текст индикатора
                if (_saveIndicatorLabel != null)
                {
                    _saveIndicatorLabel.Text = "Game Saved";
                }
            }
            else
            {
                Logger.Error("Event-based save failed");

                // Обновляем текст индикатора
                if (_saveIndicatorLabel != null)
                {
                    _saveIndicatorLabel.Text = "Save Failed";
                }
            }

            // Сбрасываем список измененных систем
            _changedSystems.Clear();
        }
        else
        {
            Logger.Error("GameManager not found for saving game");

            // Обновляем текст индикатора
            if (_saveIndicatorLabel != null)
            {
                _saveIndicatorLabel.Text = "Save Error";
            }
        }

        // Уменьшаем счетчик активных сохранений
        _activeSaveCount--;

        // Запускаем таймер для скрытия индикатора
        if (ShowSaveIndicator && _indicatorTimer != null)
        {
            _indicatorTimer.Start();
        }

        // Если есть отложенные изменения и нет активных сохранений, запускаем новое сохранение
        if (_pendingSave && _activeSaveCount == 0)
        {
            ScheduleSave();
        }
    }

    /// <summary>
    /// Сохраняет инвентарь игрока
    /// </summary>
    private void SavePlayerInventory(GameManager gameManager)
    {
        if (gameManager == null)
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
    /// Сохраняет контейнеры хранения
    /// </summary>
    private void SaveStorageContainers(GameManager gameManager)
    {
        if (gameManager == null)
            return;

        // Сохраняем модули хранения
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        foreach (var moduleNode in storageModules)
        {
            if (moduleNode is StorageModule storageModule)
            {
                string storageId = storageModule.StorageID;

                // Вызываем метод сохранения модуля
                storageModule.SaveStorageInventory();

                Logger.Debug($"Saved storage module: {storageId}", false);
            }
        }

        // Сохраняем независимые контейнеры
        var containers = GetTree().GetNodesInGroup("Containers");
        foreach (var containerNode in containers)
        {
            if (containerNode is Container container)
            {
                // Пропускаем контейнеры, которые принадлежат модулям хранения
                bool isModuleContainer = false;
                foreach (var module in storageModules)
                {
                    if (module is StorageModule storageModule &&
                        storageModule.IsAncestorOf(container))
                    {
                        isModuleContainer = true;
                        break;
                    }
                }

                if (!isModuleContainer && container.ContainerInventory != null)
                {
                    // Получаем ID контейнера
                    string containerID = container.Name;
                    if (container.HasMethod("GetStorageID"))
                    {
                        containerID = (string)container.Call("GetStorageID");
                    }

                    // Сохраняем инвентарь контейнера
                    string key = $"StorageInventory_{containerID}";
                    gameManager.SetData(key, container.ContainerInventory.Serialize());

                    Logger.Debug($"Saved independent container: {containerID}", false);
                }
            }
        }
    }

    /// <summary>
    /// Принудительно инициирует сохранение игры
    /// </summary>
    public void ForceSave()
    {
        PerformSave(true);
    }
}