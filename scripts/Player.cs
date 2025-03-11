using Godot;
using System;
using System.Text;
using System.Collections.Generic;

public partial class Player : Character
{
    // Константы
    private const float INTERACTION_RADIUS = 2.0f;

    // Ссылки на компоненты
    private Sprite2D _sprite;
    private CollisionShape2D _collisionShape;
    private Area2D _interactionArea;
    private CollisionShape2D _interactionCollisionShape;
    private TileMap _tileMap;

    // Система взаимодействия
    private IInteractable _currentInteractable;

    // Ссылка на систему сортировки
    private IsometricSorter _isometricSorter;

    // Отладочные компоненты
    private Label _wallInfoLabel;

    // Счетчик для обновления отладки
    private int _debugUpdateCounter = 0;
    private const int DEBUG_UPDATE_INTERVAL = 30; // Обновление каждые 30 кадров (0.5 сек при 60 FPS)

    public override void _Ready()
    {
        AddToGroup("Player");
        AddToGroup("DynamicObjects"); // Важно: добавляем в группу для сортировки

        // Инициализация инвентаря
        InitializeInventory();

        // Подписка на события инвентаря
        Connect("PlayerInventoryChanged", Callable.From(() =>
        {
            Logger.Debug("Player inventory updated!", false);
        }));

        base._Ready();

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        _interactionArea = GetNodeOrNull<Area2D>("InteractionArea");

        // Проверяем, существует ли интерактивная область, если нет - создаем
        if (_interactionArea == null)
        {
            CreateInteractionArea();
        }

        // Находим изометрическую карту
        _tileMap = FindTileMap();
        if (_tileMap != null)
        {
            Logger.Debug($"TileMap found at path: {_tileMap.GetPath()}", true);
            _tileMap.AddToGroup("TileMap");
        }
        else
        {
            Logger.Debug("WARNING: TileMap not found in scene", true);
        }

        // Находим IsometricSorter
        _isometricSorter = FindIsometricSorter();
        if (_isometricSorter != null)
        {
            Logger.Debug("IsometricSorter found", true);
        }

        // Создаем отладочные элементы
        if (ShowDebugInfo)
        {
            CreateDebugLabels();
        }

        // Отправляем сигнал о состоянии здоровья
        EmitSignal(SignalName.HealthChanged, _currentHealth, MaxHealth);

        Logger.Debug("Player initialization complete", true);
    }

    public override void _Process(double delta)
    {
        HandleInput();
        base._Process(delta);

        // Счетчик для отладки
        _debugUpdateCounter++;

        // Обновление отладочной информации с интервалом
        if (ShowDebugInfo && _debugUpdateCounter % DEBUG_UPDATE_INTERVAL == 0)
        {
            UpdateDebugInfo();
        }
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

    // Создание отладочных меток
    private void CreateDebugLabels()
    {
        // Создаем главный отладочный лейбл если он еще не существует
        if (_debugLabel == null)
        {
            _debugLabel = new Label();
            _debugLabel.Position = new Vector2(0, -70);
            _debugLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _debugLabel.ZIndex = 1000; // Поверх всего

            // Стилизация
            _debugLabel.AddThemeColorOverride("font_color", Colors.Yellow);
            _debugLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _debugLabel.AddThemeConstantOverride("outline_size", 2);

            AddChild(_debugLabel);
        }

        // Создаем метку для информации о стенах
        _wallInfoLabel = new Label();
        _wallInfoLabel.Position = new Vector2(0, -30);
        _wallInfoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _wallInfoLabel.ZIndex = 1000;

        // Стилизация
        _wallInfoLabel.AddThemeColorOverride("font_color", Colors.Cyan);
        _wallInfoLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _wallInfoLabel.AddThemeConstantOverride("outline_size", 2);

        AddChild(_wallInfoLabel);

        Logger.Debug("Debug labels created", true);
    }

    // Обновление отладочной информации
    private void UpdateDebugInfo()
    {
        // Обновляем основную информацию о персонаже
        if (_debugLabel != null)
        {
            _debugLabel.Text = $"Pos: ({Position.X:F1}, {Position.Y:F1})\nZ-Index: {ZIndex}";
        }

        // Обновляем информацию о стенах в радиусе взаимодействия
        UpdateWallsInfo();
    }

    // Обновление информации о стенах
    private void UpdateWallsInfo()
    {
        if (_wallInfoLabel == null || _isometricSorter == null)
            return;

        // Получаем стены в радиусе взаимодействия
        var wallsInRadius = _isometricSorter.GetWallsInRadius(GlobalPosition, INTERACTION_RADIUS);

        if (wallsInRadius.Count == 0)
        {
            _wallInfoLabel.Text = "No walls nearby";
            return;
        }

        // Ограничиваем количество отображаемых стен
        int maxWallsToShow = 3;
        int wallsToShow = Math.Min(wallsInRadius.Count, maxWallsToShow);

        // Формируем текст для отображения
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Nearby walls ({wallsInRadius.Count}):");

        for (int i = 0; i < wallsToShow; i++)
        {
            var wall = wallsInRadius[i];
            sb.AppendLine($"- Wall at {wall.Position}, Z: {wall.ZIndex}, D: {wall.Distance:F1}");
        }

        // Если есть еще стены, показываем количество
        if (wallsInRadius.Count > maxWallsToShow)
        {
            sb.AppendLine($"(+{wallsInRadius.Count - maxWallsToShow} more)");
        }

        _wallInfoLabel.Text = sb.ToString();

        // Также выводим в лог, но редко
        if (_debugUpdateCounter % (DEBUG_UPDATE_INTERVAL * 4) == 0) // Каждые 2 секунды
        {
            Logger.Debug($"Player at {GlobalPosition:F1} with Z-index {ZIndex}, nearby {wallsInRadius.Count} walls", false);
        }
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

    // Вспомогательные методы для поиска узлов
    private TileMap FindTileMap()
    {
        // Попробуем найти через группу
        var tileMaps = GetTree().GetNodesInGroup("TileMap");
        if (tileMaps.Count > 0 && tileMaps[0] is TileMap map)
        {
            return map;
        }

        // Поиск в корне сцены через рекурсию
        var tileMap = FindNodeRecursive<TileMap>(GetTree().Root);
        if (tileMap != null)
        {
            return tileMap;
        }

        return null;
    }

    private IsometricSorter FindIsometricSorter()
    {
        // Попробуем найти через группу
        var sorters = GetTree().GetNodesInGroup("IsometricSorter");
        if (sorters.Count > 0 && sorters[0] is IsometricSorter sorter)
        {
            return sorter;
        }

        // Поиск в корне сцены через рекурсию
        var isometricSorter = FindNodeRecursive<IsometricSorter>(GetTree().Root);
        return isometricSorter;
    }

    // Рекурсивный поиск узла заданного типа
    private T FindNodeRecursive<T>(Node root) where T : class
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T result)
            {
                return result;
            }

            var childResult = FindNodeRecursive<T>(child);
            if (childResult != null)
            {
                return childResult;
            }
        }

        return null;
    }
}