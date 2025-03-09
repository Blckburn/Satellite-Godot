using Godot;
using System;
using System.Collections.Generic;

public partial class InventoryUI : Control
{
    // Ссылка на инвентарь игрока
    private Inventory _inventory;

    // Контейнер для слотов инвентаря
    private GridContainer _slotsContainer;

    private InventoryContextMenu _contextMenu;

    // Шаблон слота инвентаря (если создаем слоты программно)
    [Export] public PackedScene SlotTemplate { get; set; }

    // Количество слотов в инвентаре для отображения
    [Export] public int DisplaySlots { get; set; } = 20;

    // Количество слотов в строке
    [Export] public int SlotsPerRow { get; set; } = 5;

    // Путь к сцене тултипа (добавлено)
    [Export] public string TooltipScenePath { get; set; } = "res://scenes/ui/item_tooltip.tscn";

    // Ссылка на тултип (добавлено)
    private ItemTooltip _itemTooltip;

    // Текущий слот, на который наведена мышь (добавлено)
    private Control _hoveredSlot;

    // Текущий индекс слота для тултипа (добавлено)
    private int _hoveredSlotIndex = -1;

    // Список созданных слотов
    private List<Control> _slots = new List<Control>();

    // Показывать ли инвентарь
    private bool _isVisible = false;

    // Клавиша для открытия/закрытия инвентаря
    [Export] public Key ToggleKey { get; set; } = Key.I;

    public override void _Ready()
    {
        // Находим контейнер для слотов
        _slotsContainer = GetNodeOrNull<GridContainer>("%SlotsContainer");

        if (_slotsContainer == null)
        {
            Logger.Error("InventoryUI: SlotsContainer not found");
            return;
        }

        // Устанавливаем количество колонок
        _slotsContainer.Columns = SlotsPerRow;

        // Создаем слоты
        CreateSlots();

        // Изначально скрываем инвентарь
        Visible = false;
        _isVisible = false;

        // Находим игрока и его инвентарь
        FindPlayerInventory();

        //создаем тултип для подсказок в инвентаре
        CreateTooltip();

        // Создаем контекстное меню 
        CreateContextMenu();
    }

    public override void _Input(InputEvent @event)
    {
        // Проверяем нажатие клавиши для открытия/закрытия инвентаря
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if ((Key)keyEvent.Keycode == ToggleKey)
            {
                ToggleInventory();
            }
        }
    }

    // Открыть/закрыть инвентарь
    public void ToggleInventory()
    {
        _isVisible = !_isVisible;
        Visible = _isVisible;

        if (_isVisible)
        {
            // Обновляем UI при открытии
            UpdateInventoryUI();
        }
        else
        {
            // Скрываем тултип при закрытии инвентаря (добавлено)
            if (_itemTooltip != null)
            {
                _itemTooltip.HideTooltip();
            }
        }
    }

    // Поиск игрока и его инвентаря
    private void FindPlayerInventory()
    {
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Инициализируем инвентарь, если его нет
            if (player.PlayerInventory == null)
            {
                player.InitializeInventory();
            }

            // Получаем ссылку на инвентарь
            _inventory = player.PlayerInventory;

            // Подписываемся на изменения инвентаря
            player.Connect("PlayerInventoryChanged", Callable.From(() => UpdateInventoryUI()));

            Logger.Debug("InventoryUI connected to player inventory", true);
        }
        else
        {
            Logger.Error("InventoryUI: Player not found");
        }
    }

    // Создание слотов инвентаря
    private void CreateSlots()
    {
        // Сначала очищаем существующие слоты
        foreach (var slot in _slots)
        {
            if (slot != null && IsInstanceValid(slot))
            {
                slot.QueueFree();
            }
        }
        _slots.Clear();

        // Создаем новые слоты
        for (int i = 0; i < DisplaySlots; i++)
        {
            Control slot;

            if (SlotTemplate != null)
            {
                // Используем шаблон, если он указан
                slot = SlotTemplate.Instantiate<Control>();
            }
            else
            {
                // Иначе создаем простой Panel с текстурной рамкой
                slot = new Panel();
                slot.CustomMinimumSize = new Vector2(50, 50);

                // Можно добавить стиль для отображения рамки слота
                var styleBox = new StyleBoxFlat();
                styleBox.BorderWidthBottom = styleBox.BorderWidthLeft =
                styleBox.BorderWidthRight = styleBox.BorderWidthTop = 2;
                styleBox.BorderColor = new Color(0.5f, 0.5f, 0.5f);
                styleBox.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

                ((Panel)slot).AddThemeStyleboxOverride("panel", styleBox);
            }

            // Добавляем слот в контейнер
            _slotsContainer.AddChild(slot);
            _slots.Add(slot);

            // Устанавливаем имя слота для удобства
            slot.Name = $"Slot{i}";
            slot.SetMeta("SlotIndex", i);
            // Подписываемся на события мыши 
            int index = i; // Сохраняем индекс для использования в лямбда-выражениях
            slot.MouseEntered += () => OnSlotMouseEntered(slot, index);
            slot.MouseExited += () => OnSlotMouseExited(slot, index);
            // Добавляем обработчик контекстного меню (правый клик)
            slot.GuiInput += (InputEvent @event) => OnSlotGuiInput(@event, slot, index);

        }

        Logger.Debug($"Created {_slots.Count} inventory slots", true);
    }

    // Обработчик события GUI ввода для слота
    private void OnSlotGuiInput(InputEvent @event, Control slot, int slotIndex)
    {
        // Проверяем правый клик мыши
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Right &&
            mouseButton.Pressed)
        {
            // Проверяем, есть ли предмет в слоте
            Item slotItem = null;
            if (_inventory != null && slotIndex < _inventory.Items.Count)
            {
                slotItem = _inventory.Items[slotIndex];
            }

            if (slotItem != null)
            {
                // Вычисляем позицию для меню (близко к позиции мыши)
                Vector2 menuPosition = GetViewport().GetMousePosition();

                // Показываем контекстное меню
                _contextMenu.ShowAtPosition(menuPosition, slotIndex, slotItem);

                // Скрываем тултип, пока открыто контекстное меню
                if (_itemTooltip != null)
                {
                    _itemTooltip.HideTooltip();
                }
            }
        }
    }

    // Обновление отображения инвентаря
    public void UpdateInventoryUI()
    {
        if (_inventory == null)
        {
            GD.Print("UpdateInventoryUI: Inventory is null");
            return;
        }

        if (_slots.Count == 0)
        {
            GD.Print("UpdateInventoryUI: No slots created");
            return;
        }

        // Получаем список предметов из инвентаря
        var items = _inventory.Items;
        GD.Print($"UpdateInventoryUI: Inventory has {items.Count} items");

        // Обновляем каждый слот
        for (int i = 0; i < _slots.Count; i++)
        {
            // Если в инвентаре есть предмет для этого слота
            if (i < items.Count && items[i] != null)
            {
                GD.Print($"Updating slot {i} with item: {items[i].DisplayName}");
                UpdateSlot(_slots[i], items[i]);
            }
            else
            {
                // Иначе очищаем слот
                ClearSlot(_slots[i]);
            }
        }

        // Если после обновления у нас был наведенный слот, проверяем его снова 
        if (_hoveredSlot != null && _hoveredSlotIndex >= 0 && _itemTooltip != null)
        {
            Item hoveredItem = null;
            if (_hoveredSlotIndex < _inventory.Items.Count)
            {
                hoveredItem = _inventory.Items[_hoveredSlotIndex];
            }

            if (hoveredItem != null)
            {
                _itemTooltip.ShowTooltip(hoveredItem);
            }
            else
            {
                _itemTooltip.HideTooltip();
            }
        }
    }

    // Обновление одного слота
    private void UpdateSlot(Control slot, Item item)
    {
        // Выводим информацию для отладки
        GD.Print($"Updating slot with item: {item.DisplayName}, Icon path: {item.IconPath}");

        // Проверим, загружается ли иконка
        var iconTexture = item.Icon;
        GD.Print($"Icon loaded: {iconTexture != null}");

        // Получаем или создаем дочерние элементы
        var iconRect = slot.GetNodeOrNull<TextureRect>("IconTexture");
        var quantityLabel = slot.GetNodeOrNull<Label>("QuantityLabel");

        // Создание TextureRect если его нет
        if (iconRect == null)
        {
            GD.Print("Creating new IconTexture");
            iconRect = new TextureRect();
            iconRect.Name = "IconTexture";

            // Настройки отображения текстуры
            iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

            // Установка размера и положения для полного заполнения слота
            iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            iconRect.Size = slot.Size; // Устанавливаем размер равным размеру слота
            iconRect.Position = Vector2.Zero; // Убираем смещение

            slot.AddChild(iconRect);
        }
        else
        {
            // Обновляем размер и положение при каждом вызове, если слот изменился
            iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            iconRect.Size = slot.Size;
            iconRect.Position = Vector2.Zero;
        }

        // Создание QuantityLabel если нужно
        if (quantityLabel == null && item.Quantity > 1)
        {
            GD.Print("Creating new QuantityLabel");
            quantityLabel = new Label();
            quantityLabel.Name = "QuantityLabel";
            quantityLabel.HorizontalAlignment = HorizontalAlignment.Right;
            quantityLabel.VerticalAlignment = VerticalAlignment.Bottom;

            quantityLabel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);

            // Стили
            quantityLabel.AddThemeColorOverride("font_color", Colors.White);
            quantityLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            quantityLabel.AddThemeConstantOverride("outline_size", 1);

            slot.AddChild(quantityLabel);
        }

        // Обновление значений
        if (iconTexture != null)
        {
            iconRect.Texture = iconTexture;
        }
        else
        {
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

        // Всплывающая подсказка
        slot.TooltipText = "";

        GD.Print($"Slot updated: {slot.Name}");
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

            // Добавляем тултип в дерево сцены
            AddChild(_itemTooltip);

            Logger.Debug("ItemTooltip created successfully", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error creating tooltip: {ex.Message}");
        }
    }

    // Обработчик наведения мыши на слот 
    private void OnSlotMouseEntered(Control slot, int slotIndex)
    {
        _hoveredSlot = slot;
        _hoveredSlotIndex = slotIndex;

        // Показываем тултип, если в слоте есть предмет
        if (_inventory != null && _itemTooltip != null)
        {
            Item item = null;
            if (slotIndex < _inventory.Items.Count)
            {
                item = _inventory.Items[slotIndex];
            }

            if (item != null)
            {
                // Выводим тип предмета в лог для отладки
                Logger.Debug($"Item type: {item.Type} ({GetItemTypeDescription(item.Type)})", false);

                _itemTooltip.ShowTooltip(item);
                Logger.Debug($"Showing tooltip for item: {item.DisplayName}", false);
            }
        }
    }

    // Обработчик ухода мыши со слота 
    private void OnSlotMouseExited(Control slot, int slotIndex)
    {
        if (_hoveredSlot == slot)
        {
            _hoveredSlot = null;
            _hoveredSlotIndex = -1;
        }

        // Скрываем тултип
        if (_itemTooltip != null)
        {
            _itemTooltip.HideTooltip();
        }
    }

    private void OnItemUseRequested(int slotIndex)
    {
        if (_inventory == null || slotIndex < 0 || slotIndex >= _inventory.Items.Count)
            return;

        Item item = _inventory.Items[slotIndex];
        if (item == null)
            return;

        Logger.Debug($"Using item: {item.DisplayName}", false);

        bool itemUsed = false;
        string useMessage = "";

        // Логика использования в зависимости от типа предмета
        switch (item.Type)
        {
            case ItemType.Consumable:
                // Для расходуемых предметов (еда, зелья и т.д.)
                itemUsed = true;
                useMessage = $"Вы использовали {item.DisplayName}. +10 к здоровью!";

                // Находим игрока и восстанавливаем здоровье
                var players = GetTree().GetNodesInGroup("Player");
                if (players.Count > 0 && players[0] is Character player)
                {
                    // Восстановить здоровье игрока
                    player.TakeDamage(-10, player); // Отрицательный урон = исцеление
                    Logger.Debug("Player healed for 10 HP", false);
                }
                break;

            case ItemType.Weapon:
                // Для оружия - симуляция экипировки
                itemUsed = true;
                useMessage = $"Вы экипировали {item.DisplayName}!";

                // Здесь можно добавить логику экипировки оружия
                // Например, установка активного оружия и т.д.
                break;

            case ItemType.Tool:
                // Для инструментов - использование инструмента
                itemUsed = true;
                useMessage = $"Вы использовали {item.DisplayName}.";

                // Здесь логика использования инструмента
                break;

            case ItemType.Key:
                // Попытка использовать ключ на ближайшей двери
                var doors = GetTree().GetNodesInGroup("Doors");
                Door nearestDoor = null;
                float minDistance = float.MaxValue;

                // Находим игрока для определения расстояния
                var playerNodes = GetTree().GetNodesInGroup("Player");
                if (playerNodes.Count > 0 && playerNodes[0] is Node2D playerNode)
                {
                    // Ищем ближайшую дверь
                    foreach (var doorNode in doors)
                    {
                        if (doorNode is Door door && doorNode is Node2D doorNode2D)
                        {
                            float distance = playerNode.GlobalPosition.DistanceTo(doorNode2D.GlobalPosition);
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                nearestDoor = door;
                            }
                        }
                    }

                    // Проверяем, нашли ли дверь и достаточно ли близко
                    if (nearestDoor != null && minDistance <= 2.0f)
                    {
                        // Открываем дверь ключом
                        if (!nearestDoor.IsOpen)
                        {
                            nearestDoor.IsOpen = true;
                            itemUsed = true;
                            useMessage = $"Вы открыли дверь с помощью {item.DisplayName}!";
                        }
                        else
                        {
                            useMessage = "Дверь уже открыта.";
                            itemUsed = false;
                        }
                    }
                    else
                    {
                        useMessage = "Рядом нет дверей для использования ключа.";
                        itemUsed = false;
                    }
                }
                else
                {
                    useMessage = "Невозможно найти игрока для использования ключа.";
                    itemUsed = false;
                }
                break;

            default:
                // Для других типов предметов
                useMessage = $"Этот предмет ({item.DisplayName}) не может быть использован напрямую.";
                itemUsed = false;
                break;
        }

        // Показываем сообщение об использовании
        ShowUseMessage(useMessage);

        // Если предмет был использован и это расходуемый предмет, уменьшаем количество
        if (itemUsed && item.Type == ItemType.Consumable)
        {
            item.Quantity--;

            // Если количество стало 0, удаляем предмет из инвентаря
            if (item.Quantity <= 0)
            {
                _inventory.RemoveItem(item);
                Logger.Debug($"Item {item.DisplayName} removed from inventory after use", false);
            }
        }
    }

    // Метод для отображения сообщения об использовании предмета
    private void ShowUseMessage(string message)
    {
        // Создаем всплывающее сообщение
        var messageLabel = new Label();
        messageLabel.Text = message;
        messageLabel.HorizontalAlignment = HorizontalAlignment.Center;

        // Добавляем стиль
        var font = messageLabel.GetThemeFont("font");
        messageLabel.AddThemeFontSizeOverride("font_size", 16);
        messageLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 1)); // Белый цвет

        // Настраиваем позицию (верхняя часть экрана)
        messageLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        messageLabel.Position = new Vector2(0, 100);

        // Добавляем в сцену
        AddChild(messageLabel);

        // Создаем таймер для автоматического удаления сообщения
        var timer = new Timer();
        timer.OneShot = true;
        timer.WaitTime = 3.0f; // 3 секунды
        timer.Timeout += () => {
            // Плавное исчезновение и удаление
            var tween = CreateTween();
            tween.TweenProperty(messageLabel, "modulate:a", 0.0f, 0.5f);
            tween.TweenCallback(Callable.From(() => messageLabel.QueueFree()));
        };

        messageLabel.AddChild(timer);
        timer.Start();

        // Выводим сообщение также в лог
        Logger.Debug($"Use message: {message}", false);
    }

    private void OnItemDropRequested(int slotIndex)
    {
        Logger.Debug($"OnItemDropRequested called with slotIndex: {slotIndex}", false);

        if (_inventory == null || slotIndex < 0 || slotIndex >= _inventory.Items.Count)
            return;

        Item item = _inventory.Items[slotIndex];
        if (item == null)
            return;

        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Выбрасываем предмет
            DropItemInWorld(item, player.GlobalPosition);

            // Удаляем предмет из инвентаря
            _inventory.RemoveItem(item);

            Logger.Debug($"Item dropped: {item.DisplayName}", false);
        }
        else
        {
            Logger.Error("Could not find player for item drop");
        }
    }

    // Используйте уже существующий метод FormatItemInfo, заменив его содержимое
    // Не добавляйте новый метод!

    private void OnItemInfoRequested(int slotIndex)
    {
        if (_inventory == null || slotIndex < 0 || slotIndex >= _inventory.Items.Count)
            return;

        Item item = _inventory.Items[slotIndex];
        if (item == null)
            return;

        Logger.Debug($"Showing detailed info for item: {item.DisplayName}", false);

        // Создаем кастомный диалог
        var infoDialog = new Window();
        infoDialog.Title = item.DisplayName;
        infoDialog.Size = new Vector2I(400, 300);
        infoDialog.Exclusive = true; // Модальное окно

        // Исправляем ошибку с WindowInitialPosition
        // В Godot 4.4 это значение может называться иначе
        infoDialog.Position = new Vector2I(
            (DisplayServer.WindowGetSize().X - infoDialog.Size.X) / 2,
            (DisplayServer.WindowGetSize().Y - infoDialog.Size.Y) / 2
        );

        infoDialog.Unresizable = true;

        // Создаем главный контейнер
        var container = new VBoxContainer();
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        container.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        // Создаем RichTextLabel для отображения информации
        var richText = new RichTextLabel();
        richText.BbcodeEnabled = true;
        richText.Text = FormatItemInfo(item);
        richText.FitContent = true;
        richText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        richText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        // Создаем кнопку закрытия
        var closeButton = new Button();
        closeButton.Text = "Закрыть";

        // Исправляем ошибку с SizeExpand - используем правильное имя флага
        closeButton.SizeFlagsHorizontal = Control.SizeFlags.Expand;
        closeButton.Pressed += () => infoDialog.QueueFree();

        // Добавляем элементы в контейнер
        container.AddChild(richText);
        container.AddChild(closeButton);
        infoDialog.AddChild(container);

        // Добавляем диалог в сцену и показываем
        AddChild(infoDialog);
        infoDialog.Popup();
    }

    // Обновите существующий метод FormatItemInfo, а не добавляйте новый
    // Замените его содержимое следующим кодом:
    private string FormatItemInfo(Item item)
    {
        // Базовая информация
        string info = $"{item.Description}\n\n";

        // Общие характеристики
        info += "[b]Общие характеристики:[/b]\n";
        info += $"Тип: {GetItemTypeDescription(item.Type)}\n";
        info += $"Вес: {item.Weight:0.0}\n";
        info += $"Ценность: {item.Value}\n";

        if (item.MaxStackSize > 1)
        {
            info += $"Макс. в стеке: {item.MaxStackSize}\n";
            info += $"Текущее кол-во: {item.Quantity}\n";
        }

        // Дополнительная информация в зависимости от типа предмета
        switch (item.Type)
        {
            case ItemType.Weapon:
                info += "\n[b]Характеристики оружия:[/b]\n";
                info += "Урон: Зависит от характеристик персонажа\n";
                info += "Скорость атаки: Средняя\n";
                break;

            case ItemType.Consumable:
                info += "\n[b]Эффекты при использовании:[/b]\n";
                info += "Восстановление здоровья: +10\n";
                info += "Длительность: Мгновенно\n";
                break;

            case ItemType.Tool:
                info += "\n[b]Характеристики инструмента:[/b]\n";
                info += "Прочность: 100/100\n";
                info += "Эффективность: Высокая\n";
                break;

            case ItemType.Resource:
                info += "\n[b]Информация о ресурсе:[/b]\n";
                info += "Можно использовать для крафта\n";
                info += "Требуется для базовых рецептов\n";
                break;

            case ItemType.Key:
                info += "\n[b]Информация о ключе:[/b]\n";
                info += "Открывает специальные двери и хранилища\n";
                info += "Уникальный предмет\n";
                break;
        }

        // Дополнительная информация для редких предметов
        if (item.Value > 100)
        {
            info += "\n[b]Редкость:[/b] Высокая";
        }

        return info;
    }



    // Получение описания типа предмета
    private string GetItemTypeDescription(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon: return "Оружие";
            case ItemType.Tool: return "Инструмент";
            case ItemType.Resource: return "Ресурс";
            case ItemType.Consumable: return "Расходуемый";
            case ItemType.Quest: return "Квестовый";
            case ItemType.Key: return "Ключ";
            case ItemType.Generic:
            default: return "Обычный";
        }
    }

    // Метод для выбрасывания предмета в мир
    private void DropItemInWorld(Item item, Vector2 position)
    {
        try
        {
            Logger.Debug($"Trying to drop item: {item.DisplayName}", false);

            // Используем точный путь к вашей сцене (измените на ваш путь)
            string pickupScenePath = "res://scenes/ui/pickup_item.tscn"; // Измените на правильный путь
            Logger.Debug($"Loading pickup scene from path: {pickupScenePath}", false);

            PackedScene pickupScene = ResourceLoader.Load<PackedScene>(pickupScenePath);
            if (pickupScene == null)
            {
                Logger.Error($"Failed to load pickup scene from {pickupScenePath}");
                return;
            }

            // Инстанцируем объект PickupItem
            PickupItem pickup = pickupScene.Instantiate<PickupItem>();
            if (pickup == null)
            {
                Logger.Error("Failed to instantiate PickupItem");
                return;
            }

            Logger.Debug("PickupItem instantiated successfully", false);

            // Установка предмета в PickupItem
            pickup.ItemResource = item.Clone();
            pickup.Quantity = item.Quantity;

            // Устанавливаем позицию с небольшим случайным смещением
            float randX = (float)GD.RandRange(-50, 50) / 100f;
            float randY = (float)GD.RandRange(-50, 50) / 100f;
            Vector2 dropPosition = position + new Vector2(randX, randY);
            pickup.Position = dropPosition;

            // Важное изменение: добавляем PickupItem в текущую сцену
            // Get the current active scene
            var currentScene = GetTree().CurrentScene;
            currentScene.AddChild(pickup);

            Logger.Debug($"Item dropped in world: {item.DisplayName} at {dropPosition}", false);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error dropping item: {ex.Message}");
        }
    }

    // Создание контекстного меню (добавлено)
    private void CreateContextMenu()
    {
        try
        {
            // Создаем новый экземпляр ContextMenu
            _contextMenu = new InventoryContextMenu();

            // Добавляем в сцену
            AddChild(_contextMenu);

            //  точные имена сигналов должны соответствовать именам в EmitSignal
            _contextMenu.Connect("item_use_requested", Callable.From<int>(OnItemUseRequested));
            _contextMenu.Connect("item_drop_requested", Callable.From<int>(OnItemDropRequested));
            _contextMenu.Connect("item_info_requested", Callable.From<int>(OnItemInfoRequested));

            // Для отладки - убедимся, что обработчики подключены
            Logger.Debug("Signal handlers are connected to context menu", true);

            Logger.Debug("InventoryContextMenu created successfully", true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error creating context menu: {ex.Message}");
        }
    }
}
