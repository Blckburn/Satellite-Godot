using Godot;
using System;
using System.Collections.Generic;

public partial class InventoryUI : Control
{
    // Ссылка на инвентарь игрока
    private Inventory _inventory;

    // Контейнер для слотов инвентаря
    private GridContainer _slotsContainer;

    // Шаблон слота инвентаря (если создаем слоты программно)
    [Export] public PackedScene SlotTemplate { get; set; }

    // Количество слотов в инвентаре для отображения
    [Export] public int DisplaySlots { get; set; } = 20;

    // Количество слотов в строке
    [Export] public int SlotsPerRow { get; set; } = 5;

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
        }

        Logger.Debug($"Created {_slots.Count} inventory slots", true);
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
        slot.TooltipText = item.GetTooltip();

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
}