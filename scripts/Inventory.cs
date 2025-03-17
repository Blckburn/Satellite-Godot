using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Inventory : Resource
{
    // Событие, вызываемое при изменении инвентаря
    [Signal] public delegate void InventoryChangedEventHandler();

    // Максимальная вместимость инвентаря
    [Export] public int MaxSlots { get; set; } = 20;

    // Максимальный вес, который может нести персонаж (0 = неограниченно)
    [Export] public float MaxWeight { get; set; } = 0;

    // Слоты инвентаря (без Export)
    private readonly List<Item> _items = new List<Item>();

    // Список предметов в инвентаре (без Export)
    public List<Item> Items
    {
        get => _items;
        private set  // Сделаем сеттер приватным
        {
            _items.Clear();
            if (value != null)
            {
                _items.AddRange(value);
            }
            EmitSignal("InventoryChanged");
        }
    }


    // Текущий вес инвентаря
    public float CurrentWeight => _items.Sum(item => item.Weight * item.Quantity);

    // Конструктор
    public Inventory() { }

    // Конструктор с указанием максимального количества слотов
    public Inventory(int maxSlots)
    {
        MaxSlots = maxSlots;
    }

    // Добавление предмета в инвентарь
    public bool AddItem(Item item)
    {
        if (item == null)
        {
            GD.Print("ERROR: Attempting to add null item to inventory");
            return false;
        }

        GD.Print($"Inventory.AddItem: {item.DisplayName} (ID: {item.ID}, Type: {item.Type}, Quantity: {item.Quantity})");

        // Проверка на максимальный вес
        if (MaxWeight > 0 && CurrentWeight + item.Weight * item.Quantity > MaxWeight)
        {
            GD.Print($"Inventory is too heavy to add this item. Current weight: {CurrentWeight}, Max weight: {MaxWeight}");
            return false;
        }

        // Запоминаем изначальное количество для отчета
        int initialQuantity = item.Quantity;
        int totalAddedQuantity = 0;

        // Пытаемся сначала объединить с существующими стеками
        int remainingQuantity = item.Quantity;
        foreach (var existingItem in _items.Where(i => i.CanStackWith(item)).ToList())
        {
            // Запоминаем исходное количество в существующем стеке
            int originalQuantity = existingItem.Quantity;

            // Пытаемся добавить предметы в стек
            int remainder = existingItem.StackWith(item);

            // Вычисляем, сколько предметов действительно добавлено в этот стек
            int added = existingItem.Quantity - originalQuantity;
            totalAddedQuantity += added;
            remainingQuantity -= added;

            GD.Print($"Stacked {added} items with existing stack. New quantity: {existingItem.Quantity}, Remaining: {remainingQuantity}");

            if (remainingQuantity <= 0)
            {
                GD.Print($"All items stacked successfully. Total added: {totalAddedQuantity} from initial {initialQuantity}");
                EmitSignal("InventoryChanged");
                return true;
            }

            // Обновляем оставшееся количество в исходном предмете
            item.Quantity = remainingQuantity;
        }

        // Если остались предметы, и есть свободное место, добавляем новый слот
        if (remainingQuantity > 0)
        {
            if (_items.Count >= MaxSlots)
            {
                GD.Print($"Inventory is full. Current items: {_items.Count}, Max slots: {MaxSlots}");
                // Возвращаем true только если что-то было добавлено в существующие стеки
                return totalAddedQuantity > 0;
            }

            // Создаем новый экземпляр для добавления в инвентарь
            Item newItem = item.Clone();
            newItem.Quantity = remainingQuantity;
            _items.Add(newItem);

            totalAddedQuantity += remainingQuantity;
            GD.Print($"Added new item to inventory: {newItem.DisplayName} x{newItem.Quantity}");
        }

        GD.Print($"Final result: Added {totalAddedQuantity} items to inventory");
        EmitSignal("InventoryChanged");
        return true;
    }

    // Удаление предмета из инвентаря
    public bool RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        // Ищем предмет в инвентаре
        var existingItem = _items.FirstOrDefault(i => i == item);
        if (existingItem == null)
            return false;

        // Удаляем указанное количество
        if (existingItem.Quantity <= quantity)
        {
            _items.Remove(existingItem);
        }
        else
        {
            existingItem.Quantity -= quantity;
        }

        EmitSignal("InventoryChanged");
        return true;
    }

    // Удаление предмета по ID
    public bool RemoveItemById(string itemId, int quantity = 1)
    {
        if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            return false;

        int remainingToRemove = quantity;
        foreach (var item in _items.Where(i => i.ID == itemId).OrderBy(i => i.Quantity).ToList())
        {
            if (item.Quantity <= remainingToRemove)
            {
                remainingToRemove -= item.Quantity;
                _items.Remove(item);
            }
            else
            {
                item.Quantity -= remainingToRemove;
                remainingToRemove = 0;
            }

            if (remainingToRemove <= 0)
                break;
        }

        if (quantity - remainingToRemove > 0)
        {
            EmitSignal("InventoryChanged");
            return true;
        }

        return false;
    }

    // Проверка наличия предмета в инвентаре
    public bool HasItem(string itemId, int quantity = 1)
    {
        if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            return false;

        int totalQuantity = _items.Where(i => i.ID == itemId).Sum(i => i.Quantity);
        return totalQuantity >= quantity;
    }

    // Использование предмета
    public bool UseItem(Item item, Character character)
    {
        if (item == null || character == null)
            return false;

        // Ищем предмет в инвентаре
        var existingItem = _items.FirstOrDefault(i => i == item);
        if (existingItem == null)
            return false;

        // Вызываем метод использования предмета
        bool used = existingItem.Use(character);

        // Если предмет был использован и это расходуемый предмет, уменьшаем количество
        if (used && existingItem.Type == ItemType.Consumable)
        {
            existingItem.Quantity--;

            // Если предметов не осталось, удаляем из инвентаря
            if (existingItem.Quantity <= 0)
            {
                _items.Remove(existingItem);
            }

            EmitSignal("InventoryChanged");
        }

        return used;
    }

    // Использование предмета по ID
    public bool UseItemById(string itemId, Character character)
    {
        if (string.IsNullOrEmpty(itemId) || character == null)
            return false;

        // Ищем предмет в инвентаре
        var item = _items.FirstOrDefault(i => i.ID == itemId);
        if (item == null)
            return false;

        return UseItem(item, character);
    }

    // Получение предмета по ID
    public Item GetItemById(string itemId)
    {
        return _items.FirstOrDefault(i => i.ID == itemId);
    }

    // Получение предмета по индексу слота
    public Item GetItemBySlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _items.Count)
            return null;

        return _items[slotIndex];
    }

    // Перемещение предмета между слотами
    public bool MoveItem(int fromSlot, int toSlot)
    {
        if (fromSlot < 0 || fromSlot >= _items.Count || toSlot < 0 || toSlot >= MaxSlots)
            return false;

        // Если целевой слот занят
        if (toSlot < _items.Count)
        {
            var fromItem = _items[fromSlot];
            var toItem = _items[toSlot];

            // Если предметы можно объединить
            if (fromItem.CanStackWith(toItem))
            {
                int remainder = toItem.StackWith(fromItem);

                // Если все предметы объединились
                if (remainder == 0)
                {
                    _items.RemoveAt(fromSlot);
                }
                else
                {
                    fromItem.Quantity = remainder;
                }
            }
            else
            {
                // Меняем предметы местами
                _items[fromSlot] = toItem;
                _items[toSlot] = fromItem;
            }
        }
        else
        {
            // Если целевой слот пуст, просто перемещаем
            var item = _items[fromSlot];
            _items.RemoveAt(fromSlot);

            // Заполняем пустые слоты до нужного индекса
            while (_items.Count < toSlot)
            {
                _items.Add(null);
            }

            _items.Add(item);
        }

        EmitSignal("InventoryChanged");
        return true;
    }

    // Очистка инвентаря
    public void Clear()
    {
        _items.Clear();
        EmitSignal("InventoryChanged");
    }

    // Получение информации о инвентаре
    public string GetInventoryInfo()
    {
        if (_items.Count == 0)
            return "Inventory is empty";

        string info = $"Inventory ({_items.Count}/{MaxSlots} slots";

        if (MaxWeight > 0)
        {
            info += $", {CurrentWeight:0.0}/{MaxWeight:0.0} weight";
        }

        info += "):\n";

        foreach (var item in _items)
        {
            info += $"- {item.DisplayName}";

            if (item.Quantity > 1)
            {
                info += $" x{item.Quantity}";
            }

            if (item.Weight > 0)
            {
                info += $" ({item.Weight * item.Quantity:0.0})";
            }

            info += "\n";
        }

        return info;
    }
}