using Godot;
using System;

// Перечисление для типов предметов
public enum ItemType
{
    Generic,    // Базовый тип
    Weapon,     // Оружие
    Tool,       // Инструменты
    Resource,   // Ресурсы
    Consumable, // Расходные материалы
    Quest,      // Квестовые предметы
    Key         // Ключи и доступы
}

// Добавляем атрибуты для регистрации класса в редакторе Godot
[Tool]
[GlobalClass]
public partial class Item : Resource
{
    // Основные свойства предмета
    [Export] public string ID { get; set; } = "item_default";
    [Export] public string DisplayName { get; set; } = "Default Item";
    [Export] public string Description { get; set; } = "A default item description.";
    [Export] public ItemType Type { get; set; } = ItemType.Generic;
    [Export] public float Weight { get; set; } = 1.0f;
    [Export] public int Value { get; set; } = 0;
    [Export] public int MaxStackSize { get; set; } = 1;
    [Export] public string IconPath { get; set; } = "res://resources/icons/default_item.png";

    // Текущее количество в стеке (для предметов, которые можно складывать)
    private int _quantity = 1;
    [Export]
    public int Quantity
    {
        get => _quantity;
        set => _quantity = Mathf.Clamp(value, 0, MaxStackSize);
    }

    // Ссылка на текстуру иконки (загружается при необходимости)
    private Texture2D _icon;
    public Texture2D Icon
    {
        get
        {
            if (_icon == null && !string.IsNullOrEmpty(IconPath))
            {
                // Для отладки выводим полный путь
                GD.Print($"Loading icon from path: {IconPath}");
                _icon = ResourceLoader.Load<Texture2D>(IconPath);

                if (_icon == null)
                {
                    GD.Print($"WARNING: Failed to load icon from path: {IconPath}");
                }
                else
                {
                    GD.Print($"Successfully loaded icon from path: {IconPath}");
                }
            }
            return _icon;
        }
    }

    // Конструктор по умолчанию
    public Item() { }

    // Конструктор с основными параметрами
    public Item(string id, string displayName, string description, ItemType type)
    {
        ID = id;
        DisplayName = displayName;
        Description = description;
        Type = type;
    }

    // Виртуальный метод использования предмета
    // Возвращает true, если предмет был успешно использован
    public virtual bool Use(Character character)
    {
        GD.Print($"Item {DisplayName} used by {character.Name}");
        return true;
    }

    // Виртуальный метод для получения информации о предмете
    public virtual string GetTooltip()
    {
        string tooltip = $"{DisplayName}\n{Description}";

        if (Weight > 0)
            tooltip += $"\nWeight: {Weight:0.0}";

        if (Value > 0)
            tooltip += $"\nValue: {Value}";

        return tooltip;
    }

    // Создание копии предмета
    public virtual Item Clone()
    {
        // Для отладки
        GD.Print($"Cloning item: {DisplayName} (ID: {ID}, Quantity: {Quantity})");

        if (!string.IsNullOrEmpty(ResourcePath))
        {
            GD.Print($"Cloning from resource path: {ResourcePath}");
            var loadedItem = ResourceLoader.Load<Item>(ResourcePath);
            if (loadedItem != null)
            {
                // Загружаем исходный ресурс из файла
                GD.Print($"Loaded base item from resource path");
                // Клонирование ресурса, но без установки quantity
                // Используем другое имя переменной (resourceClone вместо clone)
                Item resourceClone = new Item();
                resourceClone.ID = loadedItem.ID;
                resourceClone.DisplayName = loadedItem.DisplayName;
                resourceClone.Description = loadedItem.Description;
                resourceClone.Type = loadedItem.Type;
                resourceClone.Weight = loadedItem.Weight;
                resourceClone.Value = loadedItem.Value;
                resourceClone.MaxStackSize = loadedItem.MaxStackSize;
                resourceClone.IconPath = loadedItem.IconPath;
                // Устанавливаем количество = 1 (базовое)
                resourceClone.Quantity = 1;

                GD.Print($"Clone from resource created with quantity 1: {resourceClone.DisplayName} (ID: {resourceClone.ID})");
                return resourceClone;
            }
            else
            {
                GD.Print($"WARNING: Failed to load item from resource path, falling back to manual clone");
            }
        }

        // Если предмет не является ресурсом, создаем новый экземпляр
        GD.Print($"Creating manual clone of item");
        Item clone = new Item();
        clone.ID = ID;
        clone.DisplayName = DisplayName;
        clone.Description = Description;
        clone.Type = Type;
        clone.Weight = Weight;
        clone.Value = Value;
        clone.MaxStackSize = MaxStackSize;
        clone.IconPath = IconPath; // Важно копировать путь к иконке

        // КЛЮЧЕВОЕ ИЗМЕНЕНИЕ: Устанавливаем quantity = 1, а не копируем исходное
        clone.Quantity = 1;

        GD.Print($"Clone created with quantity 1: {clone.DisplayName} (ID: {clone.ID})");
        return clone;
    }


    // Проверка, можно ли предметы объединить в стек
    public virtual bool CanStackWith(Item other)
    {
        return other != null && other.ID == ID && other.Type == Type &&
               Quantity < MaxStackSize && other.Quantity < MaxStackSize;
    }

    // Объединение предметов в стек
    public virtual int StackWith(Item other)
    {
        if (!CanStackWith(other))
        {
            GD.Print($"Cannot stack {other.DisplayName} with {DisplayName} - incompatible items");
            return other.Quantity; // Возвращаем всё количество как остаток
        }

        // Максимально возможное количество, которое можно добавить в стек
        int canAdd = MaxStackSize - Quantity;

        // Если нельзя ничего добавить, возвращаем всё количество как остаток
        if (canAdd <= 0)
        {
            GD.Print($"Cannot stack more {DisplayName} - max stack size reached ({Quantity}/{MaxStackSize})");
            return other.Quantity;
        }

        // Сколько предметов мы реально добавляем в стек
        int toAdd = Math.Min(canAdd, other.Quantity);

        // Увеличиваем текущий стек
        int oldQuantity = Quantity;
        Quantity += toAdd;

        // Сколько предметов остаётся в исходном стеке (остаток)
        int remainder = other.Quantity - toAdd;

        GD.Print($"Stacked {toAdd} items of {other.DisplayName} to existing stack " +
                 $"({oldQuantity} → {Quantity}, remainder: {remainder})");

        return remainder;
    }

    // Разделение стека
    public virtual Item SplitStack(int amount)
    {
        if (amount <= 0 || amount >= Quantity)
            return null;

        Item newItem = Clone();
        newItem.Quantity = amount;
        Quantity -= amount;

        return newItem;
    }
}