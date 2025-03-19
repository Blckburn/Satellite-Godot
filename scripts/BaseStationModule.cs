using Godot;
using System;

/// <summary>
/// Базовый класс для всех модулей космической станции.
/// Определяет общую функциональность и интерфейс взаимодействия.
/// </summary>
public partial class BaseStationModule : Node2D, IInteractable
{
    // Сигналы
    [Signal] public delegate void ModuleActivatedEventHandler(BaseStationModule module);
    [Signal] public delegate void ModuleDeactivatedEventHandler(BaseStationModule module);
    [Signal] public delegate void ModuleUpgradedEventHandler(BaseStationModule module, int level);

    // Общие свойства для всех модулей
    [Export] public string ModuleName { get; set; } = "Base Module";
    [Export] public string ModuleDescription { get; set; } = "A standard station module.";
    [Export(PropertyHint.Range, "1,10,1")] public int ModuleLevel { get; set; } = 1;
    [Export] public float InteractionRadius { get; set; } = 2.0f;
    [Export] public bool CanBeUpgraded { get; set; } = true;
    [Export] public bool CanBeRemoved { get; set; } = true;
    [Export] public Texture2D ModuleIcon { get; set; }

    // Компоненты модуля
    [Export] public NodePath InteractionAreaPath { get; set; }
    [Export] public NodePath VisualNodePath { get; set; }

    // Состояние модуля
    public bool IsInitialized { get; protected set; } = false;
    public bool IsActive { get; protected set; } = false;

    // Ссылки на компоненты
    protected Area2D _interactionArea;
    protected Node2D _visualNode;

    // Реализация IInteractable
    public virtual string GetInteractionHint()
    {
        return $"Press E to use {ModuleName}";
    }

    public virtual bool CanInteract(Node source)
    {
        // Проверяем, что источник взаимодействия - игрок
        if (!(source is Player player))
            return false;

        // Проверяем расстояние
        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        return distance <= InteractionRadius;
    }

    public virtual bool Interact(Node source)
    {
        if (!CanInteract(source))
            return false;

        // Активируем модуль при взаимодействии
        Activate();

        return true;
    }

    public virtual float GetInteractionRadius()
    {
        return InteractionRadius;
    }

    public override void _Ready()
    {
        base._Ready();

        // Находим компоненты
        if (!string.IsNullOrEmpty(InteractionAreaPath))
            _interactionArea = GetNodeOrNull<Area2D>(InteractionAreaPath);

        if (!string.IsNullOrEmpty(VisualNodePath))
            _visualNode = GetNodeOrNull<Node2D>(VisualNodePath);

        // Создаем область взаимодействия, если её нет
        if (_interactionArea == null)
            CreateInteractionArea();

        // Добавляем в группу для быстрого поиска
        AddToGroup("StationModules");
        AddToGroup("Interactables");

        // Инициализируем модуль
        Initialize();
    }

    /// <summary>
    /// Создает область взаимодействия для модуля
    /// </summary>
    protected virtual void CreateInteractionArea()
    {
        _interactionArea = new Area2D();
        _interactionArea.Name = "InteractionArea";

        // Создаем коллизию для области взаимодействия
        var collisionShape = new CollisionShape2D();
        var circle = new CircleShape2D();
        circle.Radius = InteractionRadius;
        collisionShape.Shape = circle;

        _interactionArea.AddChild(collisionShape);
        AddChild(_interactionArea);

        // Подключаем сигналы
        _interactionArea.BodyEntered += OnBodyEnteredInteractionArea;
        _interactionArea.BodyExited += OnBodyExitedInteractionArea;

        Logger.Debug($"Created interaction area for module {Name} with radius {InteractionRadius}", false);
    }

    /// <summary>
    /// Инициализирует модуль. Вызывается при добавлении модуля на станцию.
    /// </summary>
    public virtual void Initialize()
    {
        if (IsInitialized)
            return;

        IsInitialized = true;

        Logger.Debug($"Module {Name} initialized", false);
    }

    /// <summary>
    /// Активирует модуль. Вызывается при взаимодействии с модулем.
    /// </summary>
    public virtual void Activate()
    {
        if (IsActive)
            return;

        IsActive = true;

        EmitSignal(SignalName.ModuleActivated, this);

        Logger.Debug($"Module {Name} activated", false);
    }

    /// <summary>
    /// Деактивирует модуль. Вызывается при переключении на другой модуль.
    /// </summary>
    public virtual void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;

        EmitSignal(SignalName.ModuleDeactivated, this);

        Logger.Debug($"Module {Name} deactivated", false);
    }

    /// <summary>
    /// Улучшает модуль на следующий уровень.
    /// </summary>
    public virtual bool Upgrade()
    {
        if (!CanBeUpgraded)
            return false;

        ModuleLevel++;

        EmitSignal(SignalName.ModuleUpgraded, this, ModuleLevel);

        Logger.Debug($"Module {Name} upgraded to level {ModuleLevel}", false);

        return true;
    }

    /// <summary>
    /// Обработчик входа объекта в зону взаимодействия
    /// </summary>
    protected virtual void OnBodyEnteredInteractionArea(Node2D body)
    {
        if (body is Player player)
        {
            // Можно добавить логику для отображения подсказки и т.д.
            Logger.Debug($"Player entered interaction area of module {Name}", false);
        }
    }

    /// <summary>
    /// Обработчик выхода объекта из зоны взаимодействия
    /// </summary>
    protected virtual void OnBodyExitedInteractionArea(Node2D body)
    {
        if (body is Player player)
        {
            // Можно добавить логику для скрытия подсказки и т.д.
            Logger.Debug($"Player exited interaction area of module {Name}", false);
        }
    }

    /// <summary>
    /// Отображает визуальное выделение модуля
    /// </summary>
    public virtual void ShowHighlight()
    {
        if (_visualNode != null)
        {
            // Меняем цвет модуля для выделения
            _visualNode.Modulate = new Color(1.2f, 1.2f, 1.2f);
        }
    }

    /// <summary>
    /// Скрывает визуальное выделение модуля
    /// </summary>
    public virtual void HideHighlight()
    {
        if (_visualNode != null)
        {
            // Возвращаем обычный цвет
            _visualNode.Modulate = Colors.White;
        }
    }

    /// <summary>
    /// Проверяет, может ли данный модуль соединяться с другим модулем
    /// </summary>
    public virtual bool CanConnectTo(BaseStationModule otherModule)
    {
        if (otherModule == null)
            return false;

        // По умолчанию модули могут соединяться друг с другом
        return true;
    }
}