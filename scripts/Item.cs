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
            if (_icon == null)
            {
                if (!string.IsNullOrEmpty(IconPath))
                {
                    _icon = ResourceLoader.Load<Texture2D>(IconPath);
                }

                // Если иконка не загрузилась, создаем временную
                if (_icon == null)
                {
                    GD.Print($"Creating default icon for item: {DisplayName}");
                    // Создаем динамическую текстуру
                    var imgTexture = new ImageTexture();
                    var img = Image.Create(32, 32, false, Image.Format.Rgba8);

                    // Закрашиваем разными цветами в зависимости от типа предмета
                    Color color;
                    switch (Type)
                    {
                        case ItemType.Weapon:
                            color = new Color(1, 0, 0); // Красный
                            break;
                        case ItemType.Consumable:
                            color = new Color(0, 1, 0); // Зеленый
                            break;
                        case ItemType.Resource:
                            color = new Color(0, 0, 1); // Синий
                            break;
                        case ItemType.Key:
                            color = new Color(1, 1, 0); // Желтый
                            break;
                        case ItemType.Tool:
                            color = new Color(1, 0, 1); // Фиолетовый
                            break;
                        default:
                            color = new Color(0.5f, 0.5f, 0.5f); // Серый
                            break;
                    }

                    img.Fill(color);

                    // Создаем текстуру из изображения
                    imgTexture.SetImage(img);
                    _icon = imgTexture;
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
        if (!string.IsNullOrEmpty(ResourcePath))
        {
            return ResourceLoader.Load<Item>(ResourcePath);
        }

        // Если предмет не является ресурсом, создаем новый экземпляр
        Item clone = new Item();
        clone.ID = ID;
        clone.DisplayName = DisplayName;
        clone.Description = Description;
        clone.Type = Type;
        clone.Weight = Weight;
        clone.Value = Value;
        clone.MaxStackSize = MaxStackSize;
        clone.IconPath = IconPath;
        clone.Quantity = Quantity;

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
            return 0;

        int totalQuantity = Quantity + other.Quantity;
        int newQuantity = Mathf.Min(totalQuantity, MaxStackSize);
        int remainder = totalQuantity - newQuantity;

        Quantity = newQuantity;

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