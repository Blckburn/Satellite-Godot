using Godot;
using System;
using System.Linq;

/// <summary>
/// Класс для представления добываемого ресурса в игровом мире
/// Наследуется от InteractiveObject и реализует IInteraction для процесса добычи
/// </summary>
public partial class ResourceNode : InteractiveObject, IInteraction
{
    // Тип ресурса (перечисление ResourceType определено в отдельном файле)
    private ResourceType _type = ResourceType.Metal;
    [Export]
    public ResourceType Type
    {
        get => _type;
        set
        {
            _type = value;
            UpdateVisuals();
            UpdateInteractionHint();
        }
    }

    // Количество ресурса, которое можно добыть
    [Export] public int ResourceAmount { get; set; } = 1;

    // Время, необходимое для добычи ресурса (в секундах)
    [Export] public float HarvestTime { get; set; } = 2.0f;

    // Путь к ресурсу-предмету, который будет добавлен в инвентарь
    [Export] public string ResourceItemPath { get; set; } = "";

    private Item _resourceItem;
    [Export]
    public Item ResourceItem
    {
        get => _resourceItem;
        set
        {
            _resourceItem = value;
            if (_resourceItem != null)
            {
                Logger.Debug($"ResourceItem set: {_resourceItem.DisplayName}", false);
                // Обновляем визуал при установке нового предмета
                UpdateVisuals();
            }
        }
    }

    // Настройки визуальных эффектов
    [Export] public bool EnablePulsating { get; set; } = true;
    [Export] public bool EnableRotation { get; set; } = false;
    [Export] public float PulsatingSpeed { get; set; } = 1.0f;
    [Export] public float PulsatingStrength { get; set; } = 0.15f;
    [Export] public float RotationSpeed { get; set; } = 30.0f;

    // Визуальные компоненты
    private Sprite2D _sprite;
    private Label _resourceLabel;

    // Переменные для процесса добычи
    private bool _isHarvesting = false;
    private float _harvestProgress = 0.0f;
    private float _harvestTimer = 0.0f;
    private bool _keyHeld = false;

    // Переменные для визуальных эффектов
    private Vector2 _initialScale = Vector2.One;
    private float _time = 0.0f;

    // Сигналы
    [Signal] public delegate void HarvestStartedEventHandler();
    [Signal] public delegate void HarvestCompletedEventHandler(int amount, int resourceType);
    [Signal] public delegate void HarvestCanceledEventHandler();

    public override void _Ready()
    {
        base._Ready();

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _resourceLabel = GetNodeOrNull<Label>("ResourceLabel");

        // Сохраняем начальный масштаб для эффектов
        if (_sprite != null)
        {
            _initialScale = _sprite.Scale;
        }

        // Загружаем предмет, если он не был установлен напрямую
        if (ResourceItem == null && !string.IsNullOrEmpty(ResourceItemPath))
        {
            ResourceItem = ResourceLoader.Load<Item>(ResourceItemPath);
        }

        // Добавляем в группу для быстрого поиска в других скриптах
        AddToGroup("ResourceNodes");
        AddToGroup("Interactables");

        // Обновляем визуальное представление ресурса
        UpdateVisuals();

        Logger.Debug($"ResourceNode '{Name}' initialized with type: {Type}", true);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Обновляем время для визуальных эффектов
        _time += (float)delta;

        // Применяем визуальные эффекты, если они включены
        ApplyVisualEffects(delta);

        // Обрабатываем процесс добычи
        if (_isHarvesting && _keyHeld)
        {
            _harvestTimer += (float)delta;
            _harvestProgress = Mathf.Min(1.0f, _harvestTimer / HarvestTime);

            // Обновляем подсказку с прогрессом
            UpdateInteractionHintDuringHarvest();

            // Проверяем завершение процесса
            if (_harvestProgress >= 1.0f)
            {
                CompleteHarvest();
            }
        }
    }

    // Применение визуальных эффектов
    private void ApplyVisualEffects(double delta)
    {
        if (_sprite == null)
            return;

        // Эффект пульсации
        if (EnablePulsating)
        {
            float pulseFactor = 1.0f + Mathf.Sin(_time * PulsatingSpeed) * PulsatingStrength;
            _sprite.Scale = _initialScale * pulseFactor;
        }

        // Эффект вращения
        if (EnableRotation)
        {
            _sprite.RotationDegrees += (float)delta * RotationSpeed;
        }
    }

    // Обновление всего визуального представления ресурса
    private void UpdateVisuals()
    {
        // Обновляем спрайт на основе ResourceItem
        if (_sprite != null)
        {
            // Сначала проверяем, есть ли у нас ResourceItem
            if (ResourceItem != null && ResourceItem.Icon != null)
            {
                // Используем текстуру непосредственно из Item
                _sprite.Texture = ResourceItem.Icon;
                Logger.Debug($"Updated sprite texture from ResourceItem.Icon for {Type} resource", false);
            }
            else
            {
                // Запасной вариант - загружаем текстуру по пути на основе типа
                string texturePath = GetTexturePathForResourceType(Type);
                var texture = ResourceLoader.Load<Texture2D>(texturePath);

                if (texture != null)
                {
                    _sprite.Texture = texture;
                    Logger.Debug($"Updated sprite texture from path for {Type} resource: {texturePath}", false);
                }
                else
                {
                    Logger.Error($"Failed to load texture from path: {texturePath}");
                }
            }
        }

        // Обновляем текстовую метку
        UpdateResourceLabel();

        // Обновляем подсказку для взаимодействия
        UpdateInteractionHint();
    }

    // Получение пути к текстуре в зависимости от типа ресурса
    private string GetTexturePathForResourceType(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Metal:
                return "res://resources/textures/resources/metal_ore.png";
            case ResourceType.Crystal:
                return "res://resources/textures/resources/crystal.png";
            case ResourceType.Organic:
                return "res://resources/textures/resources/organic_matter.png";
            case ResourceType.Energy:
                return "res://resources/textures/resources/energy_source.png";
            case ResourceType.Composite:
                return "res://resources/textures/resources/composite_material.png";
            default:
                return "res://icon.svg"; // Стандартная иконка Godot
        }
    }

    // Обновление текстовой метки ресурса
    private void UpdateResourceLabel()
    {
        if (_resourceLabel != null)
        {
            string resourceName = GetResourceTypeName(Type);
            _resourceLabel.Text = resourceName;
        }
    }

    // Получение названия типа ресурса для отображения
    private string GetResourceTypeName(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Metal:
                return "Metal Ore";
            case ResourceType.Crystal:
                return "Crystal";
            case ResourceType.Organic:
                return "Organic Matter";
            case ResourceType.Energy:
                return "Energy Source";
            case ResourceType.Composite:
                return "Composite Material";
            default:
                return "Resource";
        }
    }

    // Обновление подсказки для взаимодействия
    private void UpdateInteractionHint()
    {
        string resourceName = GetResourceTypeName(Type);
        InteractionHint = $"Press E to harvest {resourceName}";
    }

    // Обновление подсказки во время процесса добычи
    private void UpdateInteractionHintDuringHarvest()
    {
        int percent = (int)(_harvestProgress * 100);
        InteractionHint = $"Harvesting ({percent}%)... Hold E";
    }

    // Реализация интерфейса IInteractable для начального взаимодействия
    public override bool Interact(Node source)
    {
        if (!CanInteract(source))
        {
            return false;
        }

        // Начинаем процесс добычи
        StartHarvest();
        return true;
    }

    // Начало процесса добычи
    private void StartHarvest()
    {
        _isHarvesting = true;
        _harvestProgress = 0.0f;
        _harvestTimer = 0.0f;
        _keyHeld = true;

        EmitSignal(SignalName.HarvestStarted);
        Logger.Debug($"Started harvesting {Type} resource", false);
    }

    // Завершение процесса добычи
    private void CompleteHarvest()
    {
        _isHarvesting = false;
        _harvestProgress = 0.0f;
        _harvestTimer = 0.0f;
        _keyHeld = false;

        // Добавляем ресурс в инвентарь игрока
        if (TryAddResourceToInventory())
        {
            // Эмитим сигнал о успешной добыче
            EmitSignal(SignalName.HarvestCompleted, ResourceAmount, (int)Type);

            // Удаляем узел ресурса, так как он был исчерпан
            // В будущем можно модифицировать для поддержки многократной добычи
            QueueFree();
        }
    }

    // Добавление ресурса в инвентарь игрока
    // Исправленный метод TryAddResourceToInventory для класса ResourceNode
    private bool TryAddResourceToInventory()
    {
        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Проверяем, есть ли у нас ресурс-предмет
            if (ResourceItem != null)
            {
                Logger.Debug($"ResourceItem found: ID={ResourceItem.ID}, Name={ResourceItem.DisplayName}", true);
                GD.Print($"DEBUG: Adding resource to inventory - ID: {ResourceItem.ID}, Name: {ResourceItem.DisplayName}, Quantity: {ResourceAmount}");

                try
                {
                    // Создаем копию предмета с правильным количеством
                    Item resourceItemCopy = ResourceItem.Clone();
                    resourceItemCopy.Quantity = ResourceAmount;

                    Logger.Debug($"Cloned ResourceItem: ID={resourceItemCopy.ID}, Name={resourceItemCopy.DisplayName}, Quantity={resourceItemCopy.Quantity}", true);
                    GD.Print($"DEBUG: Resource clone created - ID: {resourceItemCopy.ID}, Name: {resourceItemCopy.DisplayName}, Quantity: {resourceItemCopy.Quantity}");

                    // Получаем текущее количество в инвентаре до добавления
                    int currentQuantity = 0;
                    if (player.PlayerInventory != null)
                    {
                        Item existingItem = player.PlayerInventory.Items.FirstOrDefault(i => i.ID == resourceItemCopy.ID);
                        if (existingItem != null)
                        {
                            currentQuantity = existingItem.Quantity;
                        }
                    }

                    // Добавляем в инвентарь
                    bool added = player.AddItemToInventory(resourceItemCopy);

                    if (added)
                    {
                        // Проверяем новое количество после добавления
                        int newQuantity = 0;
                        Item updatedItem = player.PlayerInventory.Items.FirstOrDefault(i => i.ID == resourceItemCopy.ID);
                        if (updatedItem != null)
                        {
                            newQuantity = updatedItem.Quantity;
                        }

                        int actuallyAdded = newQuantity - currentQuantity;

                        Logger.Debug($"Successfully added {actuallyAdded} {Type} resource to player inventory", true);
                        GD.Print($"SUCCESS: Added {actuallyAdded} {Type} resource to player inventory");
                        return true;
                    }
                    else
                    {
                        Logger.Debug("Failed to add resource to inventory (inventory full?)", true);
                        GD.Print($"FAILED: Could not add resource to inventory (inventory full?)");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception when adding resource to inventory: {ex.Message}");
                    GD.Print($"ERROR: Exception when adding resource to inventory: {ex.Message}");
                    return false;
                }
            }
            else
            {
                Logger.Error($"ResourceNode {Name} has no ResourceItem assigned!");
                GD.Print($"ERROR: ResourceNode {Name} has no ResourceItem assigned!");
                return false;
            }
        }

        Logger.Error("Player not found when trying to add resource to inventory");
        GD.Print("ERROR: Player not found when trying to add resource to inventory");
        return false;
    }

    // Отмена процесса добычи
    public void CancelHarvest()
    {
        if (_isHarvesting)
        {
            _isHarvesting = false;
            _keyHeld = false;
            _harvestProgress = 0.0f;
            _harvestTimer = 0.0f;

            // Возвращаем исходную подсказку
            UpdateInteractionHint();

            EmitSignal(SignalName.HarvestCanceled);
            Logger.Debug($"Harvesting of {Type} resource canceled", false);
        }
    }

    // Реализация IInteraction - проверка активности процесса
    public bool IsInteracting()
    {
        return _isHarvesting;
    }

    // Реализация IInteraction - получение прогресса
    public float GetInteractionProgress()
    {
        return _harvestProgress;
    }

    // Реализация IInteraction - отмена взаимодействия
    public void CancelInteraction()
    {
        CancelHarvest();
    }
}