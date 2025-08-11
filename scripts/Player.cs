using Godot;
using System;
using System.Text;
using System.Collections.Generic;

public partial class Player : Character
{
    // Константы
    private const float INTERACTION_RADIUS = 2.0f;
    [Export] public string StationScenePath { get; set; } = "res://scenes/station/space_station.tscn";

    // Перечисление для хранения направления движения
    private enum MoveDirection
    {
        None,
        Up,
        UpRight,
        Right,
        DownRight,
        Down,
        DownLeft,
        Left,
        UpLeft
    }

    // Текущее направление движения
    private MoveDirection _currentDirection = MoveDirection.Down;
    // Предыдущее направление движения (для сохранения при остановке)
    private MoveDirection _lastDirection = MoveDirection.Down;
    // Флаг движения
    private bool _isMoving = false;

    private Node2D _teleportEffects;
    private AnimationPlayer _teleportAnimation;

    // Ссылки на компоненты - обновлено для AnimatedSprite2D
    private AnimatedSprite2D _playerSprite;
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

    public override void _Ready()
    {
        // Вызываем базовый метод для остальной инициализации
        base._Ready();

        // Короткий однократный лог через наш Logger
        Logger.Debug("[Player] _Ready() reached, script is active", true);

        // Мини-экранный HUD для диагностики ввода (только при включённом ShowDebugInfo)
        if (ShowDebugInfo)
        {
            if (GetNodeOrNull<Label>("DebugLabel") == null)
            {
                var label = new Label();
                label.Name = "DebugLabel";
                label.ZIndex = 1000;
                label.Position = new Vector2(10, -40);
                label.AddThemeColorOverride("font_color", Colors.Lime);
                AddChild(label);
            }
        }

        // Инициализация инвентаря
        InitializeInventory();

        // Пытаемся загрузить сохраненный инвентарь
        bool inventoryLoaded = LoadInventory();

        if (inventoryLoaded)
        {
            Logger.Debug("Successfully loaded saved inventory", true);
        }
        else
        {
            Logger.Debug("Using fresh inventory (no saved data found)", true);
        }

        // Подписка на события инвентаря
        Connect("PlayerInventoryChanged", Callable.From(() =>
        {
            Logger.Debug("Player inventory updated!", false);
        }));

        // Инициализация компонентов - обновлено для AnimatedSprite2D
        _playerSprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        _interactionArea = GetNodeOrNull<Area2D>("InteractionArea");

        // Проверяем, существует ли спрайт персонажа
        if (_playerSprite == null)
        {
            Logger.Debug("AnimatedSprite2D node not found. Make sure to rename the sprite node.", true);
        }
        else
        {
            // Инициализируем анимацию покоя
            UpdatePlayerAnimation();
            Logger.Debug("AnimatedSprite2D found and animation initialized", true);
        }

        // Проверяем, существует ли интерактивная область, если нет - создаем
        if (_interactionArea == null)
        {
            CreateInteractionArea();
        }

        // Отправляем сигнал о состоянии здоровья
        EmitSignal(SignalName.HealthChanged, _currentHealth, MaxHealth);

        // Настраиваем карту ввода
        SetupInputMap();

        // Находим компоненты для телепортации
        _teleportEffects = GetNodeOrNull<Node2D>("TeleportEffects");
        _teleportAnimation = GetNodeOrNull<AnimationPlayer>("TeleportAnimation");
    }

    public override void _Process(double delta)
    {
        // Обработка ввода
        HandleInput();

        // Обновление анимации персонажа
        UpdatePlayerAnimation();

        // Вызов базового метода, если он существует
        base._Process(delta);

        // Проверка нажатия клавиши T
        if (Input.IsKeyPressed(Key.T) && Input.IsActionJustPressed("teleport_to_station"))
        {
            TeleportToStation();
        }
    }

    // Новый метод для определения направления движения
    private MoveDirection GetMoveDirection(Vector2 moveVector)
    {
        // Если нет движения, возвращаем None
        if (moveVector.LengthSquared() < 0.01f)
            return MoveDirection.None;

        // Получаем угол в радианах
        float angle = Mathf.Atan2(moveVector.Y, moveVector.X);

        // Преобразуем в градусы и нормализуем (0-360)
        float degrees = Mathf.RadToDeg(angle);
        if (degrees < 0) degrees += 360f;

        // Определяем направление по углу
        // Разделим круг на 8 секторов по 45 градусов
        if (degrees >= 337.5f || degrees < 22.5f)
            return MoveDirection.Right;
        else if (degrees >= 22.5f && degrees < 67.5f)
            return MoveDirection.DownRight;
        else if (degrees >= 67.5f && degrees < 112.5f)
            return MoveDirection.Down;
        else if (degrees >= 112.5f && degrees < 157.5f)
            return MoveDirection.DownLeft;
        else if (degrees >= 157.5f && degrees < 202.5f)
            return MoveDirection.Left;
        else if (degrees >= 202.5f && degrees < 247.5f)
            return MoveDirection.UpLeft;
        else if (degrees >= 247.5f && degrees < 292.5f)
            return MoveDirection.Up;
        else // if (degrees >= 292.5f && degrees < 337.5f)
            return MoveDirection.UpRight;
    }

    // Новый метод для обновления анимации персонажа
    private void UpdatePlayerAnimation()
    {
        if (_playerSprite == null)
            return;

        // Определяем, двигается ли персонаж
        _isMoving = _movementDirection.LengthSquared() > 0.01f;

        // Если персонаж двигается, обновляем текущее направление
        if (_isMoving)
        {
            _currentDirection = GetMoveDirection(_movementDirection);
            _lastDirection = _currentDirection;
        }
        else
        {
            // Если персонаж остановился, используем последнее направление для анимации покоя
            _currentDirection = _lastDirection;
        }

        // Получаем базовое имя анимации в зависимости от состояния движения
        string animBase = _isMoving ? "walk_" : "idle_";

        // Добавляем направление к имени анимации
        string animDirection = "";
        switch (_currentDirection)
        {
            case MoveDirection.Up:
                animDirection = "up";
                break;
            case MoveDirection.UpRight:
                animDirection = "up_right";
                break;
            case MoveDirection.Right:
                animDirection = "right";
                break;
            case MoveDirection.DownRight:
                animDirection = "down_right";
                break;
            case MoveDirection.Down:
                animDirection = "down";
                break;
            case MoveDirection.DownLeft:
                animDirection = "down_left";
                break;
            case MoveDirection.Left:
                animDirection = "left";
                break;
            case MoveDirection.UpLeft:
                animDirection = "up_left";
                break;
            default:
                animDirection = "down"; // По умолчанию смотрим вниз
                break;
        }

        // Формируем полное имя анимации
        string animationName = $"{animBase}{animDirection}";

        // Проверяем, есть ли такая анимация в SpriteFrames
        if (_playerSprite.SpriteFrames != null && _playerSprite.SpriteFrames.HasAnimation(animationName))
        {
            // Играем анимацию только если она отличается от текущей или не воспроизводится
            if (_playerSprite.Animation != animationName || !_playerSprite.IsPlaying())
            {
                _playerSprite.Play(animationName);
                Logger.Debug($"Playing animation: {animationName}", false);
            }
        }
        else
        {
            // Если нет такой анимации, используем запасной вариант "idle_down"
            if (_playerSprite.SpriteFrames != null && _playerSprite.SpriteFrames.HasAnimation("idle_down"))
            {
                _playerSprite.Play("idle_down");
                Logger.Debug($"Animation {animationName} not found, playing idle_down instead", false);
            }
            else
            {
                Logger.Debug($"No animations found in SpriteFrames or SpriteFrames is null", false);
            }
        }
    }

    private void TeleportToStation()
    {
        Logger.Debug("Starting teleportation to station via keyboard shortcut", true);

        // Сохраняем текущую позицию игрока
        SavePlayerPosition();

        // Сохраняем инвентарь игрока
        SaveInventory();
        Logger.Debug("Player inventory saved before teleportation to station via keyboard", true);

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
        // Базовый процесс движения теперь дергает QueryMovementInput() у потомка
        base._PhysicsProcess(delta);
    }

    private void HandleInput()
    {
        // Получаем направление движения с учетом изометрии
        Vector2 inputDirection = GetIsometricInput();
        SetMovementDirection(inputDirection);

        // Временная диагностика управления (каждые ~0.5 сек при 60 FPS)
        _debugUpdateCounter++;
        if (_debugUpdateCounter >= DEBUG_UPDATE_INTERVAL)
        {
            _debugUpdateCounter = 0;
            bool up = Input.IsActionPressed("move_up");
            bool down = Input.IsActionPressed("move_down");
            bool left = Input.IsActionPressed("move_left");
            bool right = Input.IsActionPressed("move_right");
            var lbl = GetNodeOrNull<Label>("DebugLabel");
            if (lbl != null)
            {
                lbl.Text = $"U:{up} D:{down} L:{left} R:{right}\nDir:{inputDirection}";
            }
        }

        // Взаимодействие
        if (Input.IsActionJustPressed("interact"))
        {
            TryInteract();
        }
    }

    // Переопределяем запрос направления движения для базового класса
    protected override Vector2 QueryMovementInput()
    {
        return _movementDirection;
    }

    // Метод для получения вектора направления с учётом желаемой схемы управления:
    // одиночные клавиши дают диагонали (A=NW, W=NE, D=SE, S=SW),
    // пары соседних клавиш дают кардинальные направления (AW=N, WD=E, SD=S, SA=W).
    private Vector2 GetIsometricInput()
    {
        bool up = Input.IsActionPressed("move_up");
        bool down = Input.IsActionPressed("move_down");
        bool left = Input.IsActionPressed("move_left");
        bool right = Input.IsActionPressed("move_right");

        // Возвращаем сразу изометрические векторы движения:
        // Кардинальные: N(0,-1), E(1,0), S(0,1), W(-1,0)
        // Диагональные (ось тайла 2:1):
        //  NE (W)  -> ( 1, -0.5)
        //  SE (D)  -> ( 1,  0.5)
        //  SW (S)  -> (-1,  0.5)
        //  NW (A)  -> (-1, -0.5)

        // Противоположные пары гасим (будем дальше учитывать 3‑клавишные варианты)
        bool horizontalOpposite = left && right;
        bool verticalOpposite = up && down;

        // 1) Точные кардинальные направления по одиночным клавишам (экранные оси):
        //    W↑, S↓, D→, A←
        if (!left && !right && !down && up)    return new Vector2(0, -1);  // W → North (вверх)
        if (!left && !right && !up && down)    return new Vector2(0, 1);   // S → South (вниз)
        if (!up && !down && !right && left)    return new Vector2(-1, 0);  // A → West (влево)
        if (!up && !down && !left && right)    return new Vector2(1, 0);   // D → East (вправо)

        // 2) Диагонали из двух соседних клавиш с соотношением 2:1 (нормализованный вектор (±2, ±1))
        //    Значения приблизительно (±0.894, ±0.447)
        if (up && right && !left && !down)   return new Vector2( 0.894f, -0.447f);  // W + D → NE
        if (up && left  && !right && !down)  return new Vector2(-0.894f, -0.447f);  // W + A → NW
        if (down && right && !left && !up)   return new Vector2( 0.894f,  0.447f);  // S + D → SE
        if (down && left  && !right && !up)  return new Vector2(-0.894f,  0.447f);  // S + A → SW

        // 3) Три клавиши: если есть противоположная пара — оставляем направление третьей
        if (horizontalOpposite && up && !down)   return new Vector2(0, -1);  // W → North
        if (horizontalOpposite && down && !up)   return new Vector2(0, 1);   // S → South
        if (verticalOpposite && right && !left)  return new Vector2(1, 0);   // D → East
        if (verticalOpposite && left && !right)  return new Vector2(-1, 0);  // A → West

        // 4) Прочее (4 клавиши или конфликт) — стоим
        return Vector2.Zero;
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

    private void UpdateInventoryUIDeferred()
    {
        // Ищем все UI инвентаря и обновляем их
        var inventoryUIs = GetTree().GetNodesInGroup("InventoryUI");
        foreach (var ui in inventoryUIs)
        {
            if (ui is InventoryUI inventoryUI)
            {
                inventoryUI.UpdateInventoryUI();
                Logger.Debug("Player: Forced inventory UI update from Player", true);
            }
        }
    }
}