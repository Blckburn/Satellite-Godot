using Godot;
using System;

// Расширение класса Player для добавления системы инвентаря
public partial class Player
{
    // Инвентарь игрока
    [Export] public Inventory PlayerInventory { get; private set; }

    // Сигнал изменения инвентаря
    [Signal] public delegate void PlayerInventoryChangedEventHandler();

    // Максимальный размер инвентаря по умолчанию
    private const int DEFAULT_INVENTORY_SIZE = 20;

    // Инициализация инвентаря
    public void InitializeInventory(int inventorySize = DEFAULT_INVENTORY_SIZE)
    {
        if (PlayerInventory == null)
        {
            PlayerInventory = new Inventory(inventorySize);

            // Подписываемся на изменения инвентаря
            PlayerInventory.Connect("InventoryChanged", Callable.From(() =>
            {
                EmitSignal("PlayerInventoryChanged");
                Logger.Debug($"Player inventory changed. Items: {PlayerInventory.Items.Count}", true);
            }));

            Logger.Debug($"Player inventory initialized with {inventorySize} slots", true);
        }
    }

    // Добавление предмета в инвентарь
    public bool AddItemToInventory(Item item)
    {
        if (PlayerInventory == null)
        {
            InitializeInventory();
        }

        // Отладочная информация
        GD.Print($"Adding to inventory: {item.DisplayName} (ID: {item.ID}, Type: {item.Type}, Quantity: {item.Quantity})");

        // Проверяем, что это валидный предмет
        if (string.IsNullOrEmpty(item.ID) || string.IsNullOrEmpty(item.DisplayName))
        {
            GD.Print("ERROR: Invalid item (ID or DisplayName is empty)");
            return false;
        }

        bool result = PlayerInventory.AddItem(item);

        if (result)
        {
            GD.Print($"Successfully added {item.DisplayName} x{item.Quantity} to inventory");
        }
        else
        {
            GD.Print($"Failed to add {item.DisplayName} to inventory");
        }

        return result;
    }

    // Использование предмета из инвентаря
    public bool UseItemFromInventory(Item item)
    {
        if (PlayerInventory == null)
            return false;

        return PlayerInventory.UseItem(item, this);
    }

    // Использование предмета по ID
    public bool UseItemById(string itemId)
    {
        if (PlayerInventory == null)
            return false;

        return PlayerInventory.UseItemById(itemId, this);
    }

    // Отображение информации об инвентаре
    public string GetInventoryInfo()
    {
        if (PlayerInventory == null)
            return "No inventory";

        return PlayerInventory.GetInventoryInfo();
    }
}