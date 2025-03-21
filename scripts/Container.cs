using Godot;
using System;
using System.Collections.Generic;
using System.ComponentModel;

/// <summary>
/// Базовый класс для контейнеров (сундуков, хранилищ и т.д.)
/// </summary>
public partial class Container : InteractiveObject
{
    // Инвентарь контейнера
    [Export] public Inventory ContainerInventory { get; private set; }

    // Максимальный размер инвентаря контейнера
    [Export] public int InventorySize { get; set; } = 10;

    // Название контейнера
    [Export] public string ContainerName { get; set; } = "Storage Container";

    // Можно ли закрыть контейнер автоматически при уходе
    [Export] public bool CloseOnDistanceExceeded { get; set; } = true;

    // Максимальное расстояние для взаимодействия
    [Export] public float MaxInteractionDistance { get; set; } = 2.0f;

    // Текущее состояние контейнера
    private bool _isOpen = false;

    // Сигнал открытия контейнера
    [Signal] public delegate void ContainerOpenedEventHandler(Container container);

    // Сигнал закрытия контейнера
    [Signal] public delegate void ContainerClosedEventHandler(Container container);

    // Сигнал изменения содержимого контейнера
    [Signal] public delegate void ContainerInventoryChangedEventHandler();

    // При инициализации
    public override void _Ready()
    {
        base._Ready();

        AddToGroup("Interactables");

        // Инициализация инвентаря
        InitializeInventory();

        // Обновляем подсказку
        UpdateInteractionHint();

        // Добавляем в группу контейнеров
        AddToGroup("Containers");

        Logger.Debug($"Container '{Name}' initialized with inventory size {InventorySize}", true);
    }

    // В каждом кадре
    public override void _Process(double delta)
    {
        base._Process(delta);

        // Если контейнер открыт, проверяем расстояние до игрока
        if (_isOpen && CloseOnDistanceExceeded)
        {
            CheckPlayerDistance();
        }
    }

    /// <summary>
    /// Форсирует обновление UI контейнера
    /// </summary>
    public void ForceUpdateContainerUI()
    {
        // Вызываем сигнал изменения инвентаря
        EmitSignal(SignalName.ContainerInventoryChanged);

        // Ищем ContainerUI и обновляем его
        var containerUIs = GetTree().GetNodesInGroup("ContainerUI");
        foreach (var ui in containerUIs)
        {
            if (ui is ContainerUI containerUI)
            {
                // Вызываем методы обновления UI контейнера с помощью CallDeferred
                CallDeferred(nameof(UpdateContainerUIDeferred), containerUI);
            }
        }

        Logger.Debug($"Container '{Name}' force-updated UI", false);
    }

    /// <summary>
    /// Отложенное обновление UI контейнера
    /// </summary>
    private void UpdateContainerUIDeferred(ContainerUI containerUI)
    {
        if (containerUI != null && IsInstanceValid(containerUI))
        {
            containerUI.UpdateContainerInventoryUI();
            containerUI.UpdatePlayerInventoryUI();

            // Также можно вызвать метод обновления всех иконок, если он есть
            if (containerUI.HasMethod("UpdateAllIconSizes"))
            {
                containerUI.Call("UpdateAllIconSizes");
            }

            Logger.Debug($"Container '{Name}' deferred UI update completed", false);
        }
    }

    // Инициализация инвентаря
    private void InitializeInventory()
    {
        if (ContainerInventory == null)
        {
            ContainerInventory = new Inventory(InventorySize);

            // Подписываемся на изменения инвентаря
            ContainerInventory.Connect("InventoryChanged", Callable.From(() =>
            {
                EmitSignal("ContainerInventoryChanged");
                Logger.Debug($"Container '{Name}' inventory changed", false);
            }));
        }
    }

    // Обновление подсказки для взаимодействия
    private void UpdateInteractionHint()
    {
        InteractionHint = $"Press E to open {ContainerName}";
    }

    // Проверка расстояния до игрока
    private void CheckPlayerDistance()
    {
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Node2D player)
        {
            float distance = GlobalPosition.DistanceTo(player.GlobalPosition);

            // Если игрок ушел слишком далеко, закрываем контейнер
            if (distance > MaxInteractionDistance)
            {
                CloseContainer();
            }
        }
    }

    // Метод для открытия контейнера
    public virtual void OpenContainer()
    {
        if (!_isOpen)
        {
            _isOpen = true;

            // Вызываем сигнал открытия контейнера
            EmitSignal(SignalName.ContainerOpened, this);

            Logger.Debug($"Container '{Name}' opened", true);

            // Открываем UI контейнера
            OpenContainerUI();
        }
    }

    // Метод для закрытия контейнера
    public virtual void CloseContainer()
    {
        if (_isOpen)
        {
            _isOpen = false;

            // Вызываем сигнал закрытия контейнера
            EmitSignal(SignalName.ContainerClosed, this);

            Logger.Debug($"Container '{Name}' closed", true);

            // Закрываем UI контейнера
            CloseContainerUI();
        }
    }

    // Метод открытия UI контейнера
    protected virtual void OpenContainerUI()
    {
        // Получаем менеджер UI или создаем UI контейнера
        var containerUI = GetContainerUI();

        if (containerUI != null)
        {
            // Открываем UI, передавая ссылку на инвентарь контейнера
            containerUI.OpenContainerUI(this);
        }
        else
        {
            Logger.Error($"ContainerUI not found for container '{Name}'");
        }
    }

    // Метод закрытия UI контейнера
    protected virtual void CloseContainerUI()
    {
        // Получаем менеджер UI и закрываем интерфейс
        var containerUI = GetContainerUI();

        if (containerUI != null)
        {
            containerUI.CloseContainerUI();
        }
    }



    // Получение UI контейнера
    protected virtual ContainerUI GetContainerUI()
    {
        // Ищем в группах сначала
        var containerUIs = GetTree().GetNodesInGroup("ContainerUI");
        if (containerUIs.Count > 0 && containerUIs[0] is ContainerUI ui)
        {
            return ui;
        }

        // Если не нашли, возвращаем null (UI будет создан позже)
        return null;
    }

    // Реализация IInteractable
    public override bool Interact(Node source)
    {
        if (!CanInteract(source))
            return false;

        // Открываем или закрываем контейнер в зависимости от текущего состояния
        if (_isOpen)
            CloseContainer();
        else
            OpenContainer();

        return true;
    }

    // Проверка возможности взаимодействия
    public override bool CanInteract(Node source)
    {
        // Базовая проверка от родительского класса
        if (!base.CanInteract(source))
            return false;

        // Проверка дистанции до источника
        if (source is Node2D sourceNode)
        {
            float distance = GlobalPosition.DistanceTo(sourceNode.GlobalPosition);
            return distance <= MaxInteractionDistance;
        }

        return true;
    }

    // Метод для добавления предмета в контейнер
    public bool AddItemToContainer(Item item)
    {
        if (ContainerInventory == null)
            InitializeInventory();

        bool result = ContainerInventory.AddItem(item);

        // Если предмет был успешно добавлен, обновляем UI
        if (result)
        {
            // Форсируем обновление UI
            ForceUpdateContainerUI();
            Logger.Debug($"Added {item.DisplayName} x{item.Quantity} to container '{Name}' and updated UI", false);
        }

        return result;
    }

    /// <summary>
    /// Проверяет, есть ли у объекта метод с указанным именем
    /// </summary>
    private bool HasMethod(object obj, string methodName)
    {
        if (obj == null)
            return false;

        var type = obj.GetType();
        return type.GetMethod(methodName) != null;
    }

    // Метод для получения предмета из контейнера по индексу
    public Item GetItemByIndex(int index)
    {
        if (ContainerInventory == null || index < 0 || index >= ContainerInventory.Items.Count)
            return null;

        return ContainerInventory.Items[index];
    }

    // Метод для удаления предмета из контейнера
    public bool RemoveItemFromContainer(Item item, int quantity = 1)
    {
        if (ContainerInventory == null)
            return false;

        return ContainerInventory.RemoveItem(item, quantity);
    }

    // Получение информации о контейнере
    public string GetContainerInfo()
    {
        if (ContainerInventory == null)
            return $"{ContainerName} (Empty)";

        return $"{ContainerName} ({ContainerInventory.Items.Count}/{ContainerInventory.MaxSlots} items)";
    }

    // Проверка, открыт ли контейнер
    public bool IsOpen()
    {
        return _isOpen;
    }
}