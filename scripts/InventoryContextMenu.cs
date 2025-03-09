using Godot;
using System;

public partial class InventoryContextMenu : PopupMenu
{
    // Сигналы для действий над предметами - используем строковые имена
    [Signal] public delegate void item_use_requestedEventHandler(int slotIndex);
    [Signal] public delegate void item_drop_requestedEventHandler(int slotIndex);
    [Signal] public delegate void item_info_requestedEventHandler(int slotIndex);

    // ID для пунктов меню
    private const int USE_ITEM_ID = 0;
    private const int DROP_ITEM_ID = 1;
    private const int ITEM_INFO_ID = 2;

    // Индекс слота, для которого открыто меню
    private int _currentSlotIndex = -1;

    // Ссылка на предмет
    private Item _currentItem;

    public override void _Ready()
    {
        // Добавляем пункты меню
        AddItem("Использовать", USE_ITEM_ID);
        AddItem("Выбросить", DROP_ITEM_ID);
        AddItem("Информация", ITEM_INFO_ID);

        // Подписываемся на выбор пункта меню
        IdPressed += OnMenuItemPressed;

        Logger.Debug("InventoryContextMenu initialized", true);
    }

    // Открытие контекстного меню
    public void ShowAtPosition(Vector2 position, int slotIndex, Item item)
    {
        _currentSlotIndex = slotIndex;
        _currentItem = item;

        // Настройка доступности пунктов меню в зависимости от типа предмета
        SetItemDisabled(USE_ITEM_ID, !IsItemUsable(item));

        // Показываем меню в указанной позиции (исправление ошибки преобразования Vector2 в Vector2I)
        Position = new Vector2I((int)position.X, (int)position.Y);
        Size = Vector2I.Zero; // Сбрасываем размер, чтобы меню автоматически подстроилось
        Popup();

        Logger.Debug($"Context menu opened for slot {slotIndex}, item: {item?.DisplayName ?? "null"}", false);
    }

    // Обработчик выбора пункта меню
    private void OnMenuItemPressed(long id)
    {
        Logger.Debug($"Menu item pressed with ID: {id}", false);

        if (_currentSlotIndex < 0 || _currentItem == null)
        {
            Logger.Debug("Invalid slot index or item is null", false);
            return;
        }

        switch ((int)id)
        {
            case USE_ITEM_ID:
                Logger.Debug($"Emitting 'item_use_requested' signal for slot {_currentSlotIndex}", false);
                EmitSignal("item_use_requested", _currentSlotIndex);  // Заменяем SignalName на строку
                break;

            case DROP_ITEM_ID:
                Logger.Debug($"Emitting 'item_drop_requested' signal for slot {_currentSlotIndex}", false);
                EmitSignal("item_drop_requested", _currentSlotIndex);  // Заменяем SignalName на строку
                break;

            case ITEM_INFO_ID:
                Logger.Debug($"Emitting 'item_info_requested' signal for slot {_currentSlotIndex}", false);
                EmitSignal("item_info_requested", _currentSlotIndex);  // Заменяем SignalName на строку
                break;
        }
    }

    // Проверка, является ли предмет используемым
    // Обновите метод IsItemUsable в классе InventoryContextMenu
    private bool IsItemUsable(Item item)
    {
        if (item == null)
        {
            Logger.Debug("Item is null, not usable", false);
            return false;
        }

        Logger.Debug($"Checking if item {item.DisplayName} of type {item.Type} is usable", false);

        // Определяем типы предметов, которые можно использовать
        bool usable = item.Type == ItemType.Consumable ||
                      item.Type == ItemType.Tool ||
                      item.Type == ItemType.Weapon;

        Logger.Debug($"Item {item.DisplayName} is {(usable ? "usable" : "not usable")}", false);
        return usable;
    }

}