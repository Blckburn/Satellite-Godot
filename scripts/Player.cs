using Godot;
using System;
using System.Text;
using System.Collections.Generic;

public partial class Player : Character
{
    // Константы
    private const float INTERACTION_RADIUS = 2.0f;
    [Export] public string StationScenePath { get; set; } = "res://scenes/station/space_station.tscn";

    private Node2D _teleportEffects;
    private AnimationPlayer _teleportAnimation;

    // Ссылки на компоненты
    private Sprite2D _sprite;
    private CollisionShape2D _collisionShape;
    private Area2D _interactionArea;
    private CollisionShape2D _interactionCollisionShape;

    // Система взаимодействия
    private IInteractable _currentInteractable;

    // Отладочные компоненты
    private Label _wallInfoLabel;

    // Счетчик для обновления отладки
    private int _debugUpdateCounter = 0;
    private const int DEBUG_UPDATE_INTERVAL = 30; // Обновление каждые 30 кадров (0.5 сек при 60 FPS)

    // Удаляем таймер для обновления Z-индекса
    // private Timer _zIndexTimer;

    public override void _Ready()
    {


        // Вызываем базовый метод для остальной инициализации
        base._Ready();

        // Инициализация инвентаря
        InitializeInventory();

        // Подписка на события инвентаря
        Connect("PlayerInventoryChanged", Callable.From(() =>
        {
            Logger.Debug("Player inventory updated!", false);
        }));

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");

        // Отключаем все принудительные установки Z-индекса
     /*   if (_sprite != null)
        {
            // Важно: не устанавливать ZIndex вручную
            // _sprite.ZIndex = FORCE_Z_INDEX;

            // Убедимся, что Y-сортировка работает
            Logger.Debug($"Player sprite initialized. Y-Sort enabled in parent: {GetParent().GetParent().YSortEnabled}", true);
        }*/

        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        _interactionArea = GetNodeOrNull<Area2D>("InteractionArea");

        // Проверяем, существует ли интерактивная область, если нет - создаем
        if (_interactionArea == null)
        {
            CreateInteractionArea();
        }

        // Отправляем сигнал о состоянии здоровья
        EmitSignal(SignalName.HealthChanged, _currentHealth, MaxHealth);

        SetupInputMap();
    }

    public override void _Process(double delta)
    {
        // Обработка ввода
        HandleInput();

        // Вызов базового метода, если он существует
        base._Process(delta);

        // Проверка нажатия клавиши T
        if (Input.IsKeyPressed(Key.T) && Input.IsActionJustPressed("teleport_to_station"))
        {
            TeleportToStation();
        }
    }
    private void TeleportToStation()
    {
        Logger.Debug("Starting teleportation to station via keyboard shortcut", true);

        // Сохраняем текущую позицию игрока
        SavePlayerPosition();

        // Показываем эффекты телепортации
        if (_teleportEffects != null)
        {
            _teleportEffects.Visible = true;
        }

        // Запускаем анимацию телепортации
        if (_teleportAnimation != null && _teleportAnimation.HasAnimation("teleport"))
        {
            _teleportAnimation.Play("teleport");
        }
        else
        {
            // Если анимации нет, просто вызываем завершение телепортации
            CompleteTeleportToStation();
        }
    }
    private void CompleteTeleportToStation()
    {
        // Устанавливаем флаг для создания игрока при загрузке станции
        ProjectSettings.SetSetting("CreatePlayerOnLoad", true);

        // ВАЖНО: Сохраняем информацию о том, что игрок должен появиться у телепортера, а не в стартовом модуле
        ProjectSettings.SetSetting("SpawnAtTeleporter", true);

        // Переходим к сцене станции
        GetTree().ChangeSceneToFile(StationScenePath);
    }

    // Сохранение позиции игрока
    private void SavePlayerPosition()
    {
        // Сохраняем позицию игрока в глобальной переменной или файле
        // Здесь используем ProjectSettings для простоты

        // Проверяем, существует ли синглтон GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Сохраняем позицию через GameManager
            gameManager.SetData("LastWorldPosition", GlobalPosition);
            Logger.Debug($"Player position saved: {GlobalPosition}", false);
        }
        else
        {
            // Сохраняем в ProjectSettings если GameManager отсутствует
            ProjectSettings.SetSetting("LastWorldPosition", GlobalPosition);
            Logger.Debug($"Player position saved via ProjectSettings: {GlobalPosition}", false);
        }
    }
    private void SetupInputMap()
    {
        // Проверяем, существует ли действие "teleport_to_station"
        if (!InputMap.HasAction("teleport_to_station"))
        {
            // Создаем новое действие
            InputMap.AddAction("teleport_to_station");

            // Создаем событие клавиши T
            var eventT = new InputEventKey();
            eventT.Keycode = Key.T;

            // Добавляем событие к действию
            InputMap.ActionAddEvent("teleport_to_station", eventT);

            Logger.Debug("Added 'teleport_to_station' action to InputMap with key T", true);
        }
    }

    // Дополнительно обрабатываем физический процесс
    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
    }

    private void HandleInput()
    {
        // Получаем направление движения с учетом изометрии
        Vector2 inputDirection = GetIsometricInput();
        SetMovementDirection(inputDirection);

        // Взаимодействие
        if (Input.IsActionJustPressed("interact"))
        {
            TryInteract();
        }
    }

    // Метод для получения вектора направления с учетом изометрической проекции
    private Vector2 GetIsometricInput()
    {
        Vector2 input = Vector2.Zero;

        // Получаем стандартный ввод
        if (Input.IsActionPressed("move_right"))
            input.X += 1;
        if (Input.IsActionPressed("move_left"))
            input.X -= 1;
        if (Input.IsActionPressed("move_down"))
            input.Y += 1;
        if (Input.IsActionPressed("move_up"))
            input.Y -= 1;

        // Если ввод отсутствует, возвращаем нулевой вектор
        if (input == Vector2.Zero)
            return Vector2.Zero;

        // Преобразуем ввод в изометрический
        // Для стандартной изометрии (2:1)
        Vector2 isoInput = new Vector2(
            input.X - input.Y,  // X-компонент
            (input.X + input.Y) / 2  // Y-компонент
        );

        // Нормализуем вектор, чтобы диагональное движение не было быстрее
        return isoInput.Normalized();
    }

    // Создание области взаимодействия
    private void CreateInteractionArea()
    {
        _interactionArea = new Area2D();
        _interactionArea.Name = "InteractionArea";

        // Создаем коллизию для области взаимодействия
        _interactionCollisionShape = new CollisionShape2D();
        var circle = new CircleShape2D();
        circle.Radius = INTERACTION_RADIUS;
        _interactionCollisionShape.Shape = circle;

        _interactionArea.AddChild(_interactionCollisionShape);
        AddChild(_interactionArea);

        // Подключаем сигналы
        _interactionArea.BodyEntered += OnBodyEnteredInteractionArea;
        _interactionArea.BodyExited += OnBodyExitedInteractionArea;
        _interactionArea.AreaEntered += OnAreaEnteredInteractionArea;
        _interactionArea.AreaExited += OnAreaExitedInteractionArea;

        Logger.Debug("InteractionArea created with radius: " + INTERACTION_RADIUS, true);
    }

    private void TryInteract()
    {
        if (_currentInteractable != null)
        {
            bool success = _currentInteractable.Interact(this);
            if (!success)
            {
                Logger.Debug("Interaction failed", false);
            }
            else
            {
                Logger.Debug($"Interaction successful with {_currentInteractable}", false);
            }
        }
        else
        {
            // Если нет текущего объекта для взаимодействия, ищем ближайший
            var nearestInteractable = FindNearestInteractable();
            if (nearestInteractable != null)
            {
                bool success = nearestInteractable.Interact(this);
                if (!success)
                {
                    Logger.Debug("Interaction with nearest object failed", false);
                }
                else
                {
                    Logger.Debug($"Interaction successful with {nearestInteractable}", false);
                }
            }
        }
    }

    private IInteractable FindNearestInteractable()
    {
        // Получаем все узлы с интерфейсом IInteractable в сцене
        var interactables = GetTree().GetNodesInGroup("Interactables");
        IInteractable nearest = null;
        float minDistance = float.MaxValue;

        foreach (var node in interactables)
        {
            if (node is IInteractable interactable && node is Node2D interactableNode)
            {
                float distance = GlobalPosition.DistanceTo(interactableNode.GlobalPosition);
                if (distance <= interactable.GetInteractionRadius() && distance < minDistance)
                {
                    minDistance = distance;
                    nearest = interactable;
                }
            }
        }

        return nearest;
    }

    private void OnBodyEnteredInteractionArea(Node2D body)
    {
        if (body is IInteractable interactable)
        {
            _currentInteractable = interactable;
            Logger.Debug($"Can interact with: {body.Name}", false);
        }
    }

    private void OnBodyExitedInteractionArea(Node2D body)
    {
        if (body is IInteractable interactable && _currentInteractable == interactable)
        {
            _currentInteractable = null;
        }
    }

    private void OnAreaEnteredInteractionArea(Area2D area)
    {
        // Проверяем непосредственно область
        if (area is IInteractable areaInteractable)
        {
            _currentInteractable = areaInteractable;
            Logger.Debug($"Can interact with area: {area.Name}", false);
            return;
        }

        // Проверяем родителя или владельца области
        if (area.Owner is IInteractable ownerInteractable)
        {
            _currentInteractable = ownerInteractable;
            Logger.Debug($"Can interact with area owner: {area.Owner.Name}", false);
        }
    }

    private void OnAreaExitedInteractionArea(Area2D area)
    {
        // Проверяем непосредственно область
        if (area is IInteractable areaInteractable && _currentInteractable == areaInteractable)
        {
            _currentInteractable = null;
            return;
        }

        // Проверяем родителя или владельца области
        if (area.Owner is IInteractable ownerInteractable && _currentInteractable == ownerInteractable)
        {
            _currentInteractable = null;
        }
    }

    // При уничтожении уже не требуется останавливать таймер
    public override void _ExitTree()
    {
        base._ExitTree();
    }
}