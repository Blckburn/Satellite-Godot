using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Класс для управления UI контейнера, отображающий два инвентаря рядом - игрока и контейнера
/// </summary>
public partial class ContainerUI : Control
{
    // Ссылка на инвентарь игрока
    private Inventory _playerInventory;

    // Ссылка на инвентарь контейнера
    private Inventory _containerInventory;

    // Текущий открытый контейнер
    private Container _currentContainer;

    // UI контейнеры для слотов
    private GridContainer _playerSlotsContainer;
    private GridContainer _containerSlotsContainer;

    // Метки для заголовков
    private Label _playerInventoryLabel;
    private Label _containerInventoryLabel;

    // Шаблон слота инвентаря
    [Export] public PackedScene SlotTemplate { get; set; }

    // Ссылка на тултип
    private ItemTooltip _itemTooltip;

    // Ссылка на контекстное меню
    private InventoryContextMenu _contextMenu;

    // Текущие слоты
    private List<Control> _playerSlots = new List<Control>();
    private List<Control> _containerSlots = new List<Control>();

    // Выбранные слоты
    private Control _selectedPlayerSlot;
    private Control _selectedContainerSlot;
    private int _selectedPlayerSlotIndex = -1;
    private int _selectedContainerSlotIndex = -1;

    // Видимость UI
    private bool _isVisible = false;

    // Клавиша для закрытия контейнера
    [Export] public Key CloseKey { get; set; } = Key.Escape;

    // Путь к тултипу
    [Export] public string TooltipScenePath { get; set; } = "res://scenes/ui/item_tooltip.tscn";

    public override void _Ready()
    {
        // Ищем контейнеры для слотов
        _playerSlotsContainer = GetNodeOrNull<GridContainer>("%PlayerSlotsContainer");
        _containerSlotsContainer = GetNodeOrNull<GridContainer>("%ContainerSlotsContainer");

        // Ищем метки заголовков
        _playerInventoryLabel = GetNodeOrNull<Label>("%PlayerInventoryLabel");
        _containerInventoryLabel = GetNodeOrNull<Label>("%ContainerInventoryLabel");

        // Изначально скрываем UI
        Visible = false;
        _isVisible = false;

        // Создаем тултип
        CreateTooltip();

        // Создаем контекстное меню
        CreateContextMenu();

        // Добавляем в группу
        AddToGroup("ContainerUI");

        Logger.Debug("ContainerUI initialized", true);
    }

    public override void _Input(InputEvent @event)
    {
        // Проверяем нажатие клавиши для закрытия контейнера
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && (Key)keyEvent.Keycode == CloseKey)
        {
            if (_isVisible && _currentContainer != null)
            {
                _currentContainer.CloseContainer();
            }
        }
    }

    // Метод открытия UI контейнера
    public void OpenContainerUI(Container container)
    {
        if (container == null || container.ContainerInventory == null)
        {
            Logger.Error("Cannot open container UI: container or its inventory is null");
            return;
        }

        _currentContainer = container;
        _containerInventory = container.ContainerInventory;

        // Находим инвентарь игрока
        FindPlayerInventory();

        // Создаем слоты для обоих инвентарей
        CreatePlayerSlots();
        CreateContainerSlots();

        // Обновляем названия инвентарей
        if (_playerInventoryLabel != null)
            _playerInventoryLabel.Text = "Player Inventory";

        if (_containerInventoryLabel != null)
            _containerInventoryLabel.Text = container.ContainerName;

        // Обновляем содержимое слотов
        UpdatePlayerInventoryUI();
        UpdateContainerInventoryUI();

        // Показываем UI
        Visible = true;
        _isVisible = true;

        Logger.Debug($"ContainerUI opened for container '{container.Name}'", true);
    }

    // Метод закрытия UI контейнера
    public void CloseContainerUI()
    {
        Logger.Debug("CloseContainerUI called", true);

        // Скрываем UI
        Visible = false;
        _isVisible = false;

        // Очищаем ссылки
        _currentContainer = null;
        _containerInventory = null;

        // Скрываем тултип
        if (_itemTooltip != null)
        {
            _itemTooltip.HideTooltip();
        }

        Logger.Debug("ContainerUI closed", true);
    }

    // Поиск инвентаря игрока
    private void FindPlayerInventory()
    {
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Инициализируем инвентарь, если его нет
            if (player.PlayerInventory == null)
            {
                Logger.Debug("Player inventory is null, initializing new inventory", true);
                player.InitializeInventory();
            }

            // Получаем ссылку на инвентарь
            _playerInventory = player.PlayerInventory;

            if (_playerInventory != null)
            {
                Logger.Debug("Successfully connected to player inventory", true);

                // Отписываемся от предыдущих сигналов, чтобы избежать дубликатов
                if (player.IsConnected("PlayerInventoryChanged", Callable.From(() => UpdatePlayerInventoryUI())))
                {
                    player.Disconnect("PlayerInventoryChanged", Callable.From(() => UpdatePlayerInventoryUI()));
                }

                // Подписываемся на изменения инвентаря
                player.Connect("PlayerInventoryChanged", Callable.From(() => UpdatePlayerInventoryUI()));
            }
            else
            {
                Logger.Error("Player.PlayerInventory is still null after initialization");
            }
        }
        else
        {
            Logger.Error("ContainerUI: Player not found");
        }
    }

    // Создание слотов для инвентаря игрока
    private void CreatePlayerSlots()
    {
        if (_playerSlotsContainer == null)
            return;

        // Очищаем существующие слоты
        foreach (var slot in _playerSlots)
        {
            if (slot != null && IsInstanceValid(slot))
            {
                slot.QueueFree();
            }
        }
        _playerSlots.Clear();

        // Получаем максимальное количество слотов
        int slotsCount = _playerInventory != null ? _playerInventory.MaxSlots : 20;

        // Создаем новые слоты
        for (int i = 0; i < slotsCount; i++)
        {
            Control slot;

            if (SlotTemplate != null)
            {
                // Используем шаблон, если он указан
                slot = SlotTemplate.Instantiate<Control>();
            }
            else
            {
                // Иначе создаем простую панель
                slot = new Panel();
                slot.CustomMinimumSize = new Vector2(50, 50);

                // Добавляем стиль
                var styleBox = new StyleBoxFlat();
                styleBox.BorderWidthBottom = styleBox.BorderWidthLeft =
                styleBox.BorderWidthRight = styleBox.BorderWidthTop = 2;
                styleBox.BorderColor = new Color(0.5f, 0.5f, 0.5f);
                styleBox.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

                ((Panel)slot).AddThemeStyleboxOverride("panel", styleBox);
            }

            // Добавляем слот в контейнер
            _playerSlotsContainer.AddChild(slot);
            _playerSlots.Add(slot);

            // Устанавливаем имя и метаданные
            slot.Name = $"PlayerSlot{i}";
            slot.SetMeta("SlotIndex", i);
            slot.SetMeta("SlotType", "Player");

            // Подписываемся на события мыши
            int index = i;
            slot.MouseEntered += () => OnPlayerSlotMouseEntered(slot, index);
            slot.MouseExited += () => OnPlayerSlotMouseExited(slot, index);
            slot.GuiInput += (InputEvent @event) => OnPlayerSlotGuiInput(@event, slot, index);
        }

        Logger.Debug($"Created {_playerSlots.Count} player inventory slots", true);
    }

    // Создание слотов для инвентаря контейнера
    private void CreateContainerSlots()
    {
        if (_containerSlotsContainer == null || _containerInventory == null)
            return;

        // Очищаем существующие слоты
        foreach (var slot in _containerSlots)
        {
            if (slot != null && IsInstanceValid(slot))
            {
                slot.QueueFree();
            }
        }
        _containerSlots.Clear();

        // Получаем максимальное количество слотов
        int slotsCount = _containerInventory.MaxSlots;

        // Создаем новые слоты
        for (int i = 0; i < slotsCount; i++)
        {
            Control slot;

            if (SlotTemplate != null)
            {
                // Используем шаблон, если он указан
                slot = SlotTemplate.Instantiate<Control>();
            }
            else
            {
                // Иначе создаем простую панель
                slot = new Panel();
                slot.CustomMinimumSize = new Vector2(50, 50);

                // Добавляем стиль
                var styleBox = new StyleBoxFlat();
                styleBox.BorderWidthBottom = styleBox.BorderWidthLeft =
                styleBox.BorderWidthRight = styleBox.BorderWidthTop = 2;
                styleBox.BorderColor = new Color(0.6f, 0.4f, 0.2f);
                styleBox.BgColor = new Color(0.3f, 0.2f, 0.1f, 0.5f);

                ((Panel)slot).AddThemeStyleboxOverride("panel", styleBox);
            }

            // Добавляем слот в контейнер
            _containerSlotsContainer.AddChild(slot);
            _containerSlots.Add(slot);

            // Устанавливаем имя и метаданные
            slot.Name = $"ContainerSlot{i}";
            slot.SetMeta("SlotIndex", i);
            slot.SetMeta("SlotType", "Container");

            // Подписываемся на события мыши
            int index = i;
            slot.MouseEntered += () => OnContainerSlotMouseEntered(slot, index);
            slot.MouseExited += () => OnContainerSlotMouseExited(slot, index);
            slot.GuiInput += (InputEvent @event) => OnContainerSlotGuiInput(@event, slot, index);
        }

        Logger.Debug($"Created {_containerSlots.Count} container inventory slots", true);
    }

    // Обновление UI инвентаря игрока
    public void UpdatePlayerInventoryUI()
    {
        if (_playerInventory == null || _playerSlots.Count == 0)
            return;

        // Получаем список предметов
        var items = _playerInventory.Items;

        // Обновляем каждый слот
        for (int i = 0; i < _playerSlots.Count; i++)
        {
            if (i < items.Count && items[i] != null)
            {
                UpdateSlot(_playerSlots[i], items[i]);
            }
            else
            {
                ClearSlot(_playerSlots[i]);
            }
        }

        Logger.Debug("Player inventory UI updated", false);
    }

    // Обновление UI инвентаря контейнера
    public void UpdateContainerInventoryUI()
    {
        if (_containerInventory == null || _containerSlots.Count == 0)
            return;

        // Получаем список предметов
        var items = _containerInventory.Items;

        // Обновляем каждый слот
        for (int i = 0; i < _containerSlots.Count; i++)
        {
            if (i < items.Count && items[i] != null)
            {
                UpdateSlot(_containerSlots[i], items[i]);
            }
            else
            {
                ClearSlot(_containerSlots[i]);
            }
        }

        Logger.Debug("Container inventory UI updated", false);
    }

    // Обновление слота (общий метод для обоих инвентарей)
    private void UpdateSlot(Control slot, Item item)
    {
        // Получаем или создаем дочерние элементы
        var iconRect = slot.GetNodeOrNull<TextureRect>("IconTexture");
        var quantityLabel = slot.GetNodeOrNull<Label>("QuantityLabel");

        // Создание TextureRect если его нет
        if (iconRect == null)
        {
            iconRect = new TextureRect();
            iconRect.Name = "IconTexture";

            // Настройки отображения текстуры
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

            // Установка размера и положения
            iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            iconRect.Size = slot.Size;
            iconRect.Position = Vector2.Zero;

            slot.AddChild(iconRect);
        }

        // ВАЖНОЕ ИЗМЕНЕНИЕ: Обновляем размер и положение с отложенным вызовом
        iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // Отложим установку размера, чтобы дать контролу возможность обновиться
        CallDeferred(nameof(UpdateIconRectSize), iconRect, slot);

        // Создание метки количества
        if (quantityLabel == null && item.Quantity > 1)
        {
            quantityLabel = new Label();
            quantityLabel.Name = "QuantityLabel";

            // Установка положения
            quantityLabel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            quantityLabel.Position = new Vector2(-20, -15);

            // Настройка стиля
            quantityLabel.HorizontalAlignment = HorizontalAlignment.Right;
            quantityLabel.VerticalAlignment = VerticalAlignment.Bottom;
            quantityLabel.AddThemeFontSizeOverride("font_size", 12);

            // Добавляем фон
            quantityLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0.5f),
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                ContentMarginLeft = 2,
                ContentMarginRight = 2,
                ContentMarginTop = 0,
                ContentMarginBottom = 0
            });

            // Стили для текста
            quantityLabel.AddThemeColorOverride("font_color", Colors.White);
            quantityLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            quantityLabel.AddThemeConstantOverride("outline_size", 1);

            slot.AddChild(quantityLabel);
        }

        // Обновление значений
        if (item.Icon != null)
        {
            iconRect.Texture = item.Icon;
        }
        else
        {
            // Используем цвет в зависимости от типа предмета, если нет иконки
            Color bgColor = Colors.Gray;
            switch (item.Type)
            {
                case ItemType.Weapon: bgColor = new Color(0.8f, 0.2f, 0.2f); break;
                case ItemType.Consumable: bgColor = new Color(0.2f, 0.8f, 0.2f); break;
                case ItemType.Resource: bgColor = new Color(0.2f, 0.2f, 0.8f); break;
                case ItemType.Key: bgColor = new Color(0.8f, 0.8f, 0.2f); break;
                case ItemType.Tool: bgColor = new Color(0.8f, 0.2f, 0.8f); break;
            }
            iconRect.Modulate = bgColor;
        }

        if (quantityLabel != null)
        {
            if (item.Quantity > 1)
            {
                quantityLabel.Text = item.Quantity.ToString();
                quantityLabel.Visible = true;
            }
            else
            {
                quantityLabel.Visible = false;
            }
        }

        // Убираем подсказку при наведении, так как используем тултип
        slot.TooltipText = "";
    }

    // метод для отложенного обновления размеров иконки
    private void UpdateIconRectSize(TextureRect iconRect, Control slot)
    {
        if (iconRect != null && IsInstanceValid(iconRect) && slot != null && IsInstanceValid(slot))
        {
            // Получим актуальные размеры слота
            float width = slot.Size.X;
            float height = slot.Size.Y;

            // Иногда размеры могут быть 0, в этом случае используем CustomMinimumSize
            if (width <= 0) width = slot.CustomMinimumSize.X;
            if (height <= 0) height = slot.CustomMinimumSize.Y;

            // Если размеры все еще 0, используем значения по умолчанию
            if (width <= 0) width = 50;
            if (height <= 0) height = 50;

            // Установим размер и позицию TextureRect
            iconRect.Size = new Vector2(width, height);
            iconRect.Position = Vector2.Zero;

            // Дополнительная информация для отладки
            Logger.Debug($"ContainerUI: UpdateIconRectSize set to {width}x{height}", false);
        }
    }

    // Очистка слота
    private void ClearSlot(Control slot)
    {
        var iconRect = slot.GetNodeOrNull<TextureRect>("IconTexture");
        var quantityLabel = slot.GetNodeOrNull<Label>("QuantityLabel");

        if (iconRect != null)
        {
            iconRect.Texture = null;
        }

        if (quantityLabel != null)
        {
            quantityLabel.Visible = false;
        }

        slot.TooltipText = "";
    }

    // Создание тултипа
    private void CreateTooltip()
    {
        try
        {
            // Загружаем сцену тултипа
            PackedScene tooltipScene = ResourceLoader.Load<PackedScene>(TooltipScenePath);
            if (tooltipScene == null)
            {
                Logger.Error($"Failed to load tooltip scene from path: {TooltipScenePath}");
                return;
            }

            // Инстанцируем тултип
            _itemTooltip = tooltipScene.Instantiate<ItemTooltip>();
            if (_itemTooltip == null)
            {
                Logger.Error("Failed to instantiate ItemTooltip");
                return;
            }

            // Добавляем в сцену
            AddChild(_itemTooltip);

            Logger.Debug("ItemTooltip created successfully for ContainerUI", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error creating tooltip: {ex.Message}");
        }
    }

    // Создание контекстного меню
    private void CreateContextMenu()
    {
        try
        {
            // Создаем новое контекстное меню
            _contextMenu = new InventoryContextMenu();

            // Добавляем в сцену
            AddChild(_contextMenu);

            // Подключаем обработчики сигналов
            _contextMenu.Connect("item_use_requested", Callable.From<int>(OnItemUseRequested));
            _contextMenu.Connect("item_drop_requested", Callable.From<int>(OnItemDropRequested));
            _contextMenu.Connect("item_info_requested", Callable.From<int>(OnItemInfoRequested));

            Logger.Debug("InventoryContextMenu created successfully for ContainerUI", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error creating context menu: {ex.Message}");
        }
    }

    // События для слотов игрока

    private void OnPlayerSlotMouseEntered(Control slot, int slotIndex)
    {
        _selectedPlayerSlot = slot;
        _selectedPlayerSlotIndex = slotIndex;

        // Показываем тултип для предмета в слоте
        if (_playerInventory != null && _itemTooltip != null)
        {
            Item item = null;
            if (slotIndex < _playerInventory.Items.Count)
            {
                item = _playerInventory.Items[slotIndex];
            }

            if (item != null)
            {
                _itemTooltip.ShowTooltip(item);
            }
        }
    }

    private void OnPlayerSlotMouseExited(Control slot, int slotIndex)
    {
        if (_selectedPlayerSlot == slot)
        {
            _selectedPlayerSlot = null;
            _selectedPlayerSlotIndex = -1;
        }

        // Скрываем тултип
        if (_itemTooltip != null)
        {
            _itemTooltip.HideTooltip();
        }
    }

    private void OnPlayerSlotGuiInput(InputEvent @event, Control slot, int slotIndex)
    {
        // Правый клик для контекстного меню
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Right &&
            mouseButton.Pressed)
        {
            // Проверяем наличие предмета в слоте
            Item slotItem = null;
            if (_playerInventory != null && slotIndex < _playerInventory.Items.Count)
            {
                slotItem = _playerInventory.Items[slotIndex];
            }

            if (slotItem != null)
            {
                // Показываем контекстное меню
                Vector2 menuPosition = GetViewport().GetMousePosition();
                _contextMenu.ShowAtPosition(menuPosition, slotIndex, slotItem);

                // Скрываем тултип
                if (_itemTooltip != null)
                {
                    _itemTooltip.HideTooltip();
                }
            }
        }

        // Левый клик для перемещения предмета
        if (@event is InputEventMouseButton leftMouseButton &&
            leftMouseButton.ButtonIndex == MouseButton.Left &&
            leftMouseButton.Pressed)
        {
            // Проверяем наличие предмета в слоте
            if (_playerInventory != null && slotIndex < _playerInventory.Items.Count)
            {
                Item playerItem = _playerInventory.Items[slotIndex];

                if (playerItem != null)
                {
                    // Перемещаем предмет в контейнер
                    MoveItemFromPlayerToContainer(playerItem, slotIndex);
                }
            }
        }
    }

    // События для слотов контейнера

    private void OnContainerSlotMouseEntered(Control slot, int slotIndex)
    {
        _selectedContainerSlot = slot;
        _selectedContainerSlotIndex = slotIndex;

        // Показываем тултип для предмета в слоте
        if (_containerInventory != null && _itemTooltip != null)
        {
            Item item = null;
            if (slotIndex < _containerInventory.Items.Count)
            {
                item = _containerInventory.Items[slotIndex];
            }

            if (item != null)
            {
                _itemTooltip.ShowTooltip(item);
            }
        }
    }

    private void OnContainerSlotMouseExited(Control slot, int slotIndex)
    {
        if (_selectedContainerSlot == slot)
        {
            _selectedContainerSlot = null;
            _selectedContainerSlotIndex = -1;
        }

        // Скрываем тултип
        if (_itemTooltip != null)
        {
            _itemTooltip.HideTooltip();
        }
    }

    private void OnContainerSlotGuiInput(InputEvent @event, Control slot, int slotIndex)
    {
        // Левый клик для перемещения предмета
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            mouseButton.Pressed)
        {
            // Проверяем наличие предмета в слоте
            if (_containerInventory != null && slotIndex < _containerInventory.Items.Count)
            {
                Item containerItem = _containerInventory.Items[slotIndex];

                if (containerItem != null)
                {
                    // Перемещаем предмет к игроку
                    MoveItemFromContainerToPlayer(containerItem, slotIndex);
                }
            }
        }
    }

    // Перемещение предмета от игрока в контейнер
    private void MoveItemFromPlayerToContainer(Item item, int playerSlotIndex)
    {
        if (_playerInventory == null || _containerInventory == null || item == null)
            return;

        // Запоминаем количество предмета
        int quantity = item.Quantity;

        // Создаем копию предмета для добавления в контейнер
        Item itemCopy = item.Clone();
        itemCopy.Quantity = quantity;

        // Добавляем в контейнер
        bool added = _containerInventory.AddItem(itemCopy);

        if (added)
        {
            // Удаляем предмет из инвентаря игрока
            _playerInventory.RemoveItem(item, quantity);

            // ИЗМЕНЕНИЕ: Обновляем UI с задержкой для правильного отображения
            CallDeferred(nameof(UpdateUIAfterItemMove));

            Logger.Debug($"Moved {item.DisplayName} x{quantity} from player to container", true);
        }
        else
        {
            Logger.Debug($"Failed to move {item.DisplayName} to container (container full?)", true);
        }
    }

    // Перемещение предмета из контейнера к игроку
    private void MoveItemFromContainerToPlayer(Item item, int containerSlotIndex)
    {
        if (_playerInventory == null || _containerInventory == null || item == null)
            return;

        // Запоминаем количество предмета
        int quantity = item.Quantity;

        // Создаем копию предмета для добавления игроку
        Item itemCopy = item.Clone();
        itemCopy.Quantity = quantity;

        // Добавляем игроку
        bool added = _playerInventory.AddItem(itemCopy);

        if (added)
        {
            // Удаляем предмет из контейнера
            _containerInventory.RemoveItem(item, quantity);

            // ИЗМЕНЕНИЕ: Обновляем UI с задержкой для правильного отображения
            CallDeferred(nameof(UpdateUIAfterItemMove));

            Logger.Debug($"Moved {item.DisplayName} x{quantity} from container to player", true);
        }
        else
        {
            Logger.Debug($"Failed to move {item.DisplayName} to player (inventory full?)", true);
        }
    }

    /// <summary>
    /// метод для отложенного обновления UI после перемещения предметов
    /// </summary>
    private void UpdateUIAfterItemMove()
    {
        // Обновляем оба инвентаря
        UpdatePlayerInventoryUI();
        UpdateContainerInventoryUI();

        // Дополнительно обновляем размеры всех иконок
        UpdateAllIconSizes();

        Logger.Debug("Inventories UI updated after item move", false);
    }

    /// <summary>
    /// Новый метод для обновления размеров всех иконок в обоих инвентарях
    /// </summary>
    public void UpdateAllIconSizes()
    {
        // Обновляем размеры иконок в инвентаре игрока
        foreach (var slot in _playerSlots)
        {
            var iconRect = slot.GetNodeOrNull<TextureRect>("IconTexture");
            if (iconRect != null)
            {
                UpdateIconRectSize(iconRect, slot);
            }
        }

        // Обновляем размеры иконок в инвентаре контейнера
        foreach (var slot in _containerSlots)
        {
            var iconRect = slot.GetNodeOrNull<TextureRect>("IconTexture");
            if (iconRect != null)
            {
                UpdateIconRectSize(iconRect, slot);
            }
        }

        Logger.Debug("All icon sizes updated in container UI", false);
    }

    // Обработчики событий для контекстного меню

    private void OnItemUseRequested(int slotIndex)
    {
        if (_playerInventory == null || slotIndex < 0 || slotIndex >= _playerInventory.Items.Count)
            return;

        Item item = _playerInventory.Items[slotIndex];
        if (item == null)
            return;

        Logger.Debug($"Using item: {item.DisplayName}", false);

        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Используем предмет через метод игрока
            bool itemUsed = player.UseItemFromInventory(item);

            if (itemUsed)
            {
                // Обновляем UI после использования
                UpdatePlayerInventoryUI();
                Logger.Debug($"Item {item.DisplayName} used successfully", false);
            }
        }
    }

    private void OnItemDropRequested(int slotIndex)
    {
        if (_playerInventory == null || slotIndex < 0 || slotIndex >= _playerInventory.Items.Count)
            return;

        Item item = _playerInventory.Items[slotIndex];
        if (item == null)
            return;

        Logger.Debug($"Dropping item: {item.DisplayName}", false);

        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Сохраняем количество
            int quantity = item.Quantity;

            // Вызываем метод для выбрасывания предмета
            // Здесь должен быть метод из InventoryUI
            // Но в данном случае мы просто удалим предмет из инвентаря
            _playerInventory.RemoveItem(item, quantity);

            // Обновляем UI
            UpdatePlayerInventoryUI();

            Logger.Debug($"Item {item.DisplayName} x{quantity} removed from inventory", false);
        }
    }

    // Обработчик нажатия кнопки закрытия
    public void _on_close_button_pressed()
    {
        Logger.Debug("Close button pressed in ContainerUI", true);

        if (_currentContainer != null)
        {
            // Сначала проверим, принадлежит ли контейнер StorageModule
            var storageModule = _currentContainer.GetParentOrNull<StorageModule>();
            if (storageModule != null)
            {
                // Для хранилища на спутнике используем специальный метод
                Logger.Debug("Container belongs to StorageModule, calling CloseStorage", true);
                storageModule.CloseStorage();
            }
            else
            {
                // Для обычных контейнеров используем стандартный метод
                Logger.Debug("Calling regular CloseContainer for world container", true);
                _currentContainer.CloseContainer();
            }
        }
        else
        {
            // Если по какой-то причине _currentContainer == null, просто закрываем UI
            Logger.Debug("Current container is null, just closing UI", true);
            CloseContainerUI();
        }
    }

    private void OnItemInfoRequested(int slotIndex)
    {
        if (_playerInventory == null || slotIndex < 0 || slotIndex >= _playerInventory.Items.Count)
            return;

        Item item = _playerInventory.Items[slotIndex];
        if (item == null)
            return;

        Logger.Debug($"Showing info for item: {item.DisplayName}", false);

        // Здесь должен быть код для отображения информации о предмете,
        // как в InventoryUI, но мы пока ограничимся логированием
        Logger.Debug($"Item Info: {item.DisplayName}, Type: {item.Type}, Weight: {item.Weight}, Value: {item.Value}", true);
    }
}