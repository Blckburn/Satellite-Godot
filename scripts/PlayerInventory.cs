using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

// Расширение класса Player для добавления системы инвентаря
public partial class Player
{
    // Инвентарь игрока
    [Export] public Inventory PlayerInventory { get; private set; }

    // Сигнал изменения инвентаря
    [Signal] public delegate void PlayerInventoryChangedEventHandler();

    // Максимальный размер инвентаря по умолчанию
    private const int DEFAULT_INVENTORY_SIZE = 20;

    // Константы для хранения данных
    private const string INVENTORY_SAVE_KEY = "PlayerInventorySaved";


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

    // Исправленный метод добавления предмета в инвентарь
    public bool AddItemToInventory(Item item)
    {
        if (PlayerInventory == null)
        {
            InitializeInventory();
        }

        // Отладочная информация
        int initialQuantity = item.Quantity;
        GD.Print($"Adding to inventory: {item.DisplayName} (ID: {item.ID}, Type: {item.Type}, Quantity: {initialQuantity})");

        // Проверяем, что это валидный предмет
        if (string.IsNullOrEmpty(item.ID) || string.IsNullOrEmpty(item.DisplayName))
        {
            GD.Print("ERROR: Invalid item (ID or DisplayName is empty)");
            return false;
        }

        // Получаем текущее количество предмета до добавления
        int currentQuantity = 0;
        Item existingItem = PlayerInventory.Items.FirstOrDefault(i => i.ID == item.ID);
        if (existingItem != null)
        {
            currentQuantity = existingItem.Quantity;
        }

        // Добавляем предмет в инвентарь
        bool result = PlayerInventory.AddItem(item);

        if (result)
        {
            // Проверяем новое количество после добавления
            int newQuantity = 0;
            Item updatedItem = PlayerInventory.Items.FirstOrDefault(i => i.ID == item.ID);
            if (updatedItem != null)
            {
                newQuantity = updatedItem.Quantity;
            }

            int actuallyAdded = newQuantity - currentQuantity;
            GD.Print($"Successfully added {item.DisplayName} x{actuallyAdded} to inventory (Previous: {currentQuantity}, New: {newQuantity})");

            // Проверяем на случай, если было добавлено меньше, чем просили
            if (actuallyAdded < initialQuantity)
            {
                GD.Print($"Note: Only {actuallyAdded} of {initialQuantity} {item.DisplayName} could be added to inventory (stack limit or other constraints)");
            }

            // НОВЫЙ КОД: Явно вызываем сигнал об изменении инвентаря, чтобы UI обновился
            EmitSignal(SignalName.PlayerInventoryChanged);

            // НОВЫЙ КОД: Чтобы быть уверенными, что UI обновится, 
            // ищем InventoryUI и вызываем его метод UpdateInventoryUI
            var inventoryUIs = GetTree().GetNodesInGroup("InventoryUI");
            foreach (var ui in inventoryUIs)
            {
                if (ui is InventoryUI inventoryUI)
                {
                    // Вызываем метод обновления с задержкой, чтобы дать время на обработку всех событий
                    CallDeferred("UpdateInventoryUIDeferred", inventoryUI);
                }
            }
        }
        else
        {
            GD.Print($"Failed to add {item.DisplayName} to inventory");
        }

        return result;
    }

    // Добавьте этот новый метод для отложенного вызова UpdateInventoryUI
    private void UpdateInventoryUIDeferred(InventoryUI inventoryUI)
    {
        if (inventoryUI != null && IsInstanceValid(inventoryUI))
        {
            inventoryUI.UpdateInventoryUI();
            GD.Print("Forced inventory UI update after adding item");
        }
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

    /// <summary>
    /// Сохраняет инвентарь игрока через GameManager
    /// </summary>
    public void SaveInventory()
    {
        if (PlayerInventory == null)
        {
            Logger.Debug("Cannot save inventory - inventory is null", true);
            return;
        }

        // Получаем GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
        {
            Logger.Error("GameManager not found for saving inventory");
            return;
        }

        // Сериализуем и сохраняем инвентарь
        var inventoryData = PlayerInventory.Serialize();
        gameManager.SetData(INVENTORY_SAVE_KEY, inventoryData);

        Logger.Debug($"Player inventory saved. Items count: {PlayerInventory.Items.Count}", true);
    }

    /// <summary>
    /// Загружает инвентарь игрока через GameManager
    /// </summary>
    public bool LoadInventory()
    {
        // Инициализируем инвентарь, если его нет
        if (PlayerInventory == null)
        {
            InitializeInventory();
        }

        // Получаем GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
        {
            Logger.Error("GameManager not found for loading inventory");
            return false;
        }

        // Проверяем наличие сохраненных данных
        if (!gameManager.HasData(INVENTORY_SAVE_KEY))
        {
            Logger.Debug("No saved inventory data found", true);
            return false;
        }

        // Десериализуем и загружаем инвентарь
        var inventoryData = gameManager.GetData<Dictionary<string, object>>(INVENTORY_SAVE_KEY);
        if (inventoryData != null)
        {
            PlayerInventory.Deserialize(inventoryData);
            Logger.Debug($"Player inventory loaded. Items count: {PlayerInventory.Items.Count}", true);
            return true;
        }

        return false;
    }

}