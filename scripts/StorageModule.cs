using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Модуль хранилища для космической станции.
/// Позволяет хранить предметы и ресурсы между рейдами.
/// </summary>
public partial class StorageModule : BaseStationModule
{
    // Вместимость хранилища (количество слотов)
    [Export] public int StorageCapacity { get; set; } = 50;

    // Отображаемое название модуля
    [Export] public string StorageName { get; set; } = "Central Storage";

    // Ссылка на внутренний контейнер, управляющий хранением предметов
    private Container _storageContainer;

    // Путь к визуальной модели хранилища
    [Export] public NodePath StorageModelPath { get; set; }

    // Визуальная модель
    private Node2D _storageModel;

    // Анимация открытия/закрытия хранилища
    private AnimationPlayer _animationPlayer;

    // Флаг для контроля состояния контейнера
    private bool _isContainerOpen = false;

    // Ссылка на UI контейнера (если он создан)
    private ContainerUI _containerUI;

    // Наблюдатель для отслеживания жизненного цикла UI
    private Timer _containerWatcher;

    [Export] public bool CloseOnDistanceExceeded { get; set; } = true;
    [Export] public float MaxInteractionDistance { get; set; } = 2.0f;

    [Export] public string StorageID { get; set; } = "main_storage";

    private const string STORAGE_INVENTORY_KEY = "StorageModuleInventory";

    public override void _Ready()
    {
        // Настройка основных параметров модуля
        ModuleName = "Storage Module";
        ModuleDescription = "Stores items and resources between raids";

        base._Ready();

        // Создаем таймер для наблюдения за UI
        _containerWatcher = new Timer();
        _containerWatcher.WaitTime = 0.1f; // Проверка каждые 0.1 секунды
        _containerWatcher.OneShot = false;
        _containerWatcher.Autostart = false;
        _containerWatcher.Timeout += CheckContainerUIStatus;
        AddChild(_containerWatcher);

        // Создаем контейнер для хранения предметов напрямую (без вызова OpenContainer)
        CreateStorageContainer();

        // Добавляем таймер для отложенной загрузки хранилища
        var timer = new Timer();
        timer.WaitTime = 0.5f;  // Достаточная задержка для инициализации
        timer.OneShot = true;
        timer.Timeout += () => {
            Logger.Debug($"Delayed loading of storage '{StorageID}'", true);
            bool loaded = LoadStorageInventory();
            Logger.Debug($"Storage '{StorageID}' loaded: {loaded}, Items: {_storageContainer?.ContainerInventory?.Items.Count ?? 0}", true);
            timer.QueueFree();
        };
        AddChild(timer);
        timer.Start();

        Logger.Debug($"Storage module '{Name}' initialized", true);

        // Находим визуальную модель
        if (!string.IsNullOrEmpty(StorageModelPath))
        {
            _storageModel = GetNodeOrNull<Node2D>(StorageModelPath);
        }

        // Если модель не найдена, ищем ее среди дочерних узлов
        if (_storageModel == null)
        {
            foreach (var child in GetChildren())
            {
                if (child is Node2D node && child.Name.ToString().IndexOf("Model", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _storageModel = node;
                    break;
                }
            }
        }

        // Находим анимацию, если она есть
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

        // Подключаем обработку эскейпа для закрытия хранилища
        Connect("tree_entered", Callable.From(() => {
            Logger.Debug("Adding input handling for storage module", true);
            SetProcess(true);
        }));

        Logger.Debug($"StorageModule '{Name}' initialized with {StorageCapacity} slots", true);
    }
    public override void _Process(double delta)
    {
        base._Process(delta);

        // Проверяем нажатие Esc для закрытия контейнера
        if (_isContainerOpen && Input.IsKeyPressed(Key.Escape))
        {
            Logger.Debug("Escape key detected, closing storage", true);
            CloseStorage();

        }

        // Если хранилище открыто и включена опция закрытия по расстоянию
        if (_isContainerOpen && CloseOnDistanceExceeded)
        {
            // Проверяем расстояние до игрока
            CheckPlayerDistance();
        }
        if (_storageContainer != null && _storageContainer.InventorySize != StorageCapacity)
        {
            UpdateContainerSize();
        }
    }

    private void UpdateContainerSize()
    {
        if (_storageContainer != null)
        {
            _storageContainer.InventorySize = StorageCapacity;

            // Если контейнер открыт, обновляем UI
            if (_isContainerOpen && _containerUI != null && IsInstanceValid(_containerUI))
            {
                // Закроем и снова откроем UI, чтобы обновить слоты
                _containerUI.CloseContainerUI();
                _containerUI.OpenContainerUI(_storageContainer);
                Logger.Debug($"Reopened container UI after size update to {StorageCapacity}", true);
            }

            Logger.Debug($"Updated container size to {StorageCapacity}", true);
        }
    }

    /// <summary>
    /// Метод для проверки статуса UI контейнера
    /// </summary>
    private void CheckContainerUIStatus()
    {
        var containerUIs = GetTree().GetNodesInGroup("ContainerUI");
        bool uiFound = containerUIs.Count > 0;

        Logger.Debug($"Container UI check: found {containerUIs.Count} UIs, _isContainerOpen={_isContainerOpen}", true);

        if (!uiFound && _isContainerOpen)
        {
            // UI исчез, но у нас флаг открытия - скорее всего произошло преждевременное закрытие
            Logger.Debug("Container UI disappeared unexpectedly, restoring...", true);
            _isContainerOpen = false;

            // Пробуем снова открыть контейнер с задержкой
            var reopenTimer = new Timer();
            reopenTimer.WaitTime = 0.2f;
            reopenTimer.OneShot = true;
            reopenTimer.Timeout += () => {
                Logger.Debug("Attempting to reopen container...", true);
                ManuallyOpenContainerUI();
            };
            AddChild(reopenTimer);
            reopenTimer.Start();
        }
        else if (uiFound && !_isContainerOpen)
        {
            // UI есть, но флаг закрыт - обновляем флаг
            _isContainerOpen = true;
            Logger.Debug("Container UI found, updating open flag", true);
        }
        else if (!uiFound && !_isContainerOpen)
        {
            // UI закрыт и флаг закрыт - можно остановить наблюдатель
            _containerWatcher.Stop();
            Logger.Debug("Container UI properly closed, stopping watcher", true);
        }
    }

    /// <summary>
    /// Создание внутреннего контейнера для хранения предметов
    /// </summary>
    private void CreateStorageContainer()
    {
        // Создаем контейнер, если его еще нет
        if (_storageContainer == null)
        {
            _storageContainer = new Container();
            _storageContainer.Name = "StorageContainer";
            _storageContainer.ContainerName = StorageName;
            _storageContainer.InventorySize = StorageCapacity; // Убедитесь, что эта строка есть

            // Инициализируем инвентарь контейнера
            _storageContainer.AddToGroup("Containers");

            // Добавляем контейнер как дочерний узел, но скрываем его
            AddChild(_storageContainer);
            _storageContainer.Position = Vector2.Zero;
            _storageContainer.Visible = false; // Скрываем визуальное представление контейнера

            Logger.Debug($"Storage container created with {StorageCapacity} slots", true);
        }
        else
        {
            // Добавьте эту проверку для уже созданного контейнера
            if (_storageContainer.InventorySize != StorageCapacity)
            {
                _storageContainer.InventorySize = StorageCapacity;
                Logger.Debug($"Updated storage container capacity to {StorageCapacity}", true);
            }
        }
        LoadStorageInventory();
    }


    /// <summary>
    /// Метод для создания и открытия UI контейнера напрямую
    /// </summary>
    private void ManuallyOpenContainerUI()
    {
        if (_isContainerOpen)
            return;

        Logger.Debug("Manually opening container UI", true);

        // Первый шаг - найдем или создадим ContainerUI
        _containerUI = null;
        var existingUIs = GetTree().GetNodesInGroup("ContainerUI");

        if (existingUIs.Count > 0 && existingUIs[0] is ContainerUI ui)
        {
            _containerUI = ui;
            Logger.Debug("Found existing ContainerUI", true);
        }
        else
        {
            // Загружаем сцену ContainerUI
            string containerUIPath = "res://scenes/ui/container_ui.tscn";

            try
            {
                PackedScene containerUIScene = ResourceLoader.Load<PackedScene>(containerUIPath);
                if (containerUIScene != null)
                {
                    Node instance = containerUIScene.Instantiate();
                    if (instance is ContainerUI newUI)
                    {
                        _containerUI = newUI;
                        // Добавляем в корень сцены или CanvasLayer для UI
                        var uiRoot = GetUIRoot();
                        uiRoot.AddChild(_containerUI);
                        Logger.Debug("Created new ContainerUI", true);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load ContainerUI: {ex.Message}");
                return;
            }
        }

        if (_containerUI != null)
        {
            // Важно: подключаем сигнал закрытия контейнера, если он еще не подключен
            if (_storageContainer != null && !_storageContainer.IsConnected(Container.SignalName.ContainerClosed, Callable.From<Container>(OnContainerClosed)))
            {
                _storageContainer.Connect(Container.SignalName.ContainerClosed, Callable.From<Container>(OnContainerClosed));
                Logger.Debug("Connected ContainerClosed signal", true);
            }

            // Открываем UI с нашим контейнером
            _containerUI.OpenContainerUI(_storageContainer);
            _isContainerOpen = true;

            // Запускаем наблюдатель
            _containerWatcher.Start();

            Logger.Debug("Container UI opened manually", true);
        }
        else
        {
            Logger.Error("Failed to open container UI - couldn't find or create UI");
        }
    }
    /// <summary>
    /// Обработчик сигнала открытия контейнера
    /// </summary>
    private void OnContainerOpened(Container container)
    {
        if (container == _storageContainer)
        {
            _isContainerOpen = true;
            // Запускаем наблюдатель
            _containerWatcher.Start();
            Logger.Debug("Container opened signal received", true);
        }
    }

    /// <summary>
    /// Обработчик сигнала закрытия контейнера
    /// </summary>
    private void OnContainerClosed(Container container)
    {
        // Проверяем, что это наш контейнер
        if (container == _storageContainer)
        {
            Logger.Debug("Container closed signal received", true);
            _isContainerOpen = false;
            _containerWatcher.Stop();

            // Воспроизводим анимацию закрытия, если она есть
            if (_animationPlayer != null && _animationPlayer.HasAnimation("close"))
            {
                _animationPlayer.Play("close");
            }
        }
    }


    /// <summary>
    /// Получает корневой узел для UI
    /// </summary>
    private CanvasLayer GetUIRoot()
    {
        // Сначала пытаемся найти CanvasLayer для UI
        var uiCanvas = GetTree().Root.GetNodeOrNull<CanvasLayer>("UICanvas");

        // Если не нашли, ищем существующий CanvasLayer
        if (uiCanvas == null)
        {
            foreach (var child in GetTree().Root.GetChildren())
            {
                if (child is CanvasLayer canvas)
                {
                    uiCanvas = canvas;
                    break;
                }
            }
        }

        // Если не нашли, создаем новый
        if (uiCanvas == null)
        {
            uiCanvas = new CanvasLayer();
            uiCanvas.Name = "UICanvas";
            GetTree().Root.AddChild(uiCanvas);
            Logger.Debug("Created new UICanvas for container UI", true);
        }

        return uiCanvas;
    }

    /// <summary>
    /// Открывает хранилище
    /// </summary>
    public void OpenStorage()
    {
        Logger.Debug("OpenStorage called", true);

        // Убедимся, что данные хранилища загружены
        if (_storageContainer != null && _storageContainer.ContainerInventory != null &&
            _storageContainer.ContainerInventory.Items.Count == 0)
        {
            LoadStorageInventory();
        }

        // Вместо обычного открытия контейнера, напрямую создаем UI
        ManuallyOpenContainerUI();

        // Воспроизводим анимацию открытия, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("open"))
        {
            _animationPlayer.Play("open");
        }
    }

    /// <summary>
    /// Закрывает хранилище
    /// </summary>
    public void CloseStorage()
    {
        Logger.Debug("CloseStorage called", true);

        if (_isContainerOpen)
        {
            // Сохраняем инвентарь перед закрытием
            SaveStorageInventory();
            // Воспроизводим анимацию закрытия, если она есть
            if (_animationPlayer != null && _animationPlayer.HasAnimation("close"))
            {
                _animationPlayer.Play("close");
            }

            // Если есть ContainerUI, закрываем его
            var containerUIs = GetTree().GetNodesInGroup("ContainerUI");
            if (containerUIs.Count > 0 && containerUIs[0] is ContainerUI ui)
            {
                ui.CloseContainerUI();
                Logger.Debug("Closed container UI directly from CloseStorage", true);
            }

            // Сбрасываем флаг
            _isContainerOpen = false;
            _containerWatcher.Stop();

            Logger.Debug("Storage closed successfully", true);
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        // Сохраняем инвентарь хранилища
        SaveStorageInventory();

        Logger.Debug("StorageModule saved inventory before exiting tree", true);
    }

    /// <summary>
    /// Реализация активации модуля
    /// </summary>
    public override void Activate()
    {
        base.Activate();

        // При активации модуля открываем хранилище
        OpenStorage();
    }

    /// <summary>
    /// Реализация деактивации модуля
    /// </summary>
    public override void Deactivate()
    {
        // Только если хранилище открыто
        if (_isContainerOpen)
        {
            CloseStorage();
        }

        base.Deactivate();
    }

    /// <summary>
    /// Обработчик входа тела в область взаимодействия
    /// </summary>
    protected override void OnBodyEnteredInteractionArea(Node2D body)
    {
        base.OnBodyEnteredInteractionArea(body);

        // Если это игрок, обновляем подсказку
        if (body is Player)
        {
            Logger.Debug($"Player entered storage module area", false);
        }
    }

    /// <summary>
    /// Обработчик выхода тела из области взаимодействия
    /// </summary>
    protected override void OnBodyExitedInteractionArea(Node2D body)
    {
        base.OnBodyExitedInteractionArea(body);

        // Если это игрок, убираем подсказку
        if (body is Player)
        {
            Logger.Debug($"Player exited storage module area", false);
        }
    }

    /// <summary>
    /// Добавление предмета в хранилище
    /// </summary>
    public bool AddItemToStorage(Item item)
    {
        if (_storageContainer != null)
        {
            bool result = _storageContainer.AddItemToContainer(item);
            if (result)
            {
                Logger.Debug($"Added {item.DisplayName} x{item.Quantity} to storage module", false);
            }
            return result;
        }
        return false;
    }

    /// <summary>
    /// Проверка наличия предмета в хранилище
    /// </summary>
    public bool HasItem(string itemId, int quantity = 1)
    {
        if (_storageContainer != null && _storageContainer.ContainerInventory != null)
        {
            return _storageContainer.ContainerInventory.HasItem(itemId, quantity);
        }
        return false;
    }

    /// <summary>
    /// Получение информации о содержимом хранилища
    /// </summary>
    public string GetStorageInfo()
    {
        if (_storageContainer != null)
        {
            return _storageContainer.GetContainerInfo();
        }
        return $"{StorageName} (Not initialized)";
    }

    /// <summary>
    /// Переопределение метода для получения подсказки взаимодействия
    /// </summary>
    public override string GetInteractionHint()
    {
        return $"Press E to access {StorageName}";
    }

    /// <summary>
/// Проверяет расстояние от хранилища до игрока и закрывает его,
/// если игрок ушел слишком далеко
/// </summary>
private void CheckPlayerDistance()
{
    var players = GetTree().GetNodesInGroup("Player");
    if (players.Count > 0 && players[0] is Node2D player)
    {
        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);

        // Если игрок ушел слишком далеко, закрываем хранилище
        if (distance > MaxInteractionDistance)
        {
            Logger.Debug($"Player moved too far ({distance:F2} > {MaxInteractionDistance:F2}), closing storage", true);
            CloseStorage();
        }
    }
}
    // Метод для генерации ключа хранения на основе ID
    private string GetStorageInventoryKey()
    {
        return $"StorageInventory_{StorageID}";
    }

    /// <summary>
    /// Сохраняет содержимое хранилища через GameManager
    /// </summary>
    private void SaveStorageInventory()
    {
        if (_storageContainer == null || _storageContainer.ContainerInventory == null)
        {
            Logger.Debug("Cannot save storage inventory - container or inventory is null", true);
            return;
        }

        // Получаем GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
        {
            Logger.Error("GameManager not found for saving storage inventory");
            return;
        }

        // Получаем ключ для текущего хранилища
        string storageKey = GetStorageInventoryKey();

        // Сериализуем инвентарь контейнера
        var inventoryData = _storageContainer.ContainerInventory.Serialize();

        // Сохраняем данные в GameManager
        gameManager.SetData(storageKey, inventoryData);

        Logger.Debug($"Storage inventory for '{StorageID}' saved successfully. Items count: {_storageContainer.ContainerInventory.Items.Count}", true);
    }

    /// <summary>
    /// Загружает содержимое хранилища из GameManager
    /// </summary>
    private bool LoadStorageInventory()
    {
        // Проверяем, что контейнер существует
        if (_storageContainer == null)
        {
            CreateStorageContainer();
        }

        if (_storageContainer.ContainerInventory == null)
        {
            Logger.Debug("Cannot load storage inventory - container inventory is null", true);
            return false;
        }

        // Получаем GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
        {
            Logger.Error("GameManager not found for loading storage inventory");
            return false;
        }

        // Получаем ключ для текущего хранилища
        string storageKey = GetStorageInventoryKey();

        // Подробное логирование
        Logger.Debug($"Attempting to load storage '{StorageID}' with key '{storageKey}'", true);

        // Проверяем наличие сохраненных данных
        if (!gameManager.HasData(storageKey))
        {
            Logger.Debug($"No saved storage inventory data found for '{StorageID}'", true);
            return false;
        }

        try
        {
            // Получаем сохраненные данные и десериализуем их
            var inventoryData = gameManager.GetData<Dictionary<string, object>>(storageKey);
            if (inventoryData != null)
            {
                // Подробное логирование для отладки
                int itemCount = 0;
                if (inventoryData.ContainsKey("items") && inventoryData["items"] is List<Dictionary<string, object>> items)
                {
                    itemCount = items.Count;
                    Logger.Debug($"Found {itemCount} items in saved storage '{StorageID}'", true);

                    // Вывод информации о каждом предмете для отладки
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        string name = item.ContainsKey("display_name") ? item["display_name"].ToString() : "Unknown";
                        int qty = item.ContainsKey("quantity") ? Convert.ToInt32(item["quantity"]) : 0;
                        Logger.Debug($"Storage item {i + 1}: {name} x{qty}", true);
                    }
                }

                _storageContainer.ContainerInventory.Deserialize(inventoryData);

                // Обновляем UI, если хранилище открыто
              //  ForceUpdateContainerUI();

                Logger.Debug($"Storage inventory for '{StorageID}' loaded successfully. Items count: {_storageContainer.ContainerInventory.Items.Count}", true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading storage inventory: {ex.Message}");
        }

        return false;
    }





}