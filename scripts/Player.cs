using Godot;
using System;

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



    public override void _Ready()
    {
        AddToGroup("Player");
        GD.Print("Player added to 'Player' group");
        InitializeInventory();


        // Подписка на события инвентаря
        Connect("PlayerInventoryChanged", Callable.From(() =>
        {
            // Здесь можно добавить обновление UI или другие действия
            Logger.Debug("Player inventory updated!", false);
        }));

        base._Ready();

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        _interactionArea = GetNodeOrNull<Area2D>("InteractionArea");

        // Находим изометрическую карту в сцене
        _tileMap = GetTree().Root.GetNode<TileMap>("Node2D/TileMap");

        if (_tileMap == null)
        {
            // Если путь не точный, попробуем найти через GetTree()
            var tileMaps = GetTree().GetNodesInGroup("TileMap");
            if (tileMaps.Count > 0 && tileMaps[0] is TileMap map)
            {
                _tileMap = map;
                GD.Print("Found TileMap through GetNodesInGroup");
            }
            else
            {
                // Последняя попытка - попробовать найти через GetChildren
                foreach (var node in GetTree().Root.GetChildren())
                {
                    _tileMap = FindTileMapInChildren(node);
                    if (_tileMap != null)
                    {
                        GD.Print("Found TileMap through FindTileMapInChildren");
                        break;
                    }
                }
            }
        }

        if (_tileMap != null)
        {
            GD.Print($"TileMap found at path: {_tileMap.GetPath()}");
            // Не забудьте добавить вашу TileMap в группу для быстрого доступа
            _tileMap.AddToGroup("TileMap");
        }
        else
        {
            GD.Print("WARNING: TileMap not found in scene");
        }

        // Отправляем сигнал о состоянии здоровья
        EmitSignal(SignalName.HealthChanged, _currentHealth, MaxHealth);
    }

    public override void _Process(double delta)
    {
        HandleInput();
        base._Process(delta);

        // Изометрическая формула глубины - чем "глубже" объект (больше X+Y), тем выше Z-индекс
        // Базовый Z-индекс: 0 - средний уровень между полом и потолком

        // Расчет смещения Z-индекса на основе позиции
        float depth = (Position.X + Position.Y) / 64.0f;

        // Инвертируем значение, чтобы объекты "глубже" (с большим X+Y) имели меньший Z-индекс
        int zOffset = (int)(-depth);

        // Установка Z-индекса
        ZIndex = 5 + zOffset; // 5 - базовый Z-индекс между полом (-10) и верхними стенами (10)

        // Отладочная информация
        if (Engine.GetFramesDrawn() % 60 == 0) // Выводить раз в секунду
        {
            GD.Print($"Player Position: ({Position.X}, {Position.Y}), Z-Index: {ZIndex}");
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
                GD.Print("Interaction failed");
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
                    GD.Print("Interaction with nearest object failed");
                }
            }
            else
            {
                // Тихий вывод - не засоряем лог
                // GD.Print("No interactable object found");
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
            GD.Print($"Can interact with: {body.Name}");
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
            GD.Print($"Can interact with area: {area.Name}");
            return;
        }

        // Проверяем родителя или владельца области
        if (area.Owner is IInteractable ownerInteractable)
        {
            _currentInteractable = ownerInteractable;
            GD.Print($"Can interact with area owner: {area.Owner.Name}");
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
    private TileMap FindTileMapInChildren(Node node)
    {
        if (node is TileMap tileMap)
            return tileMap;

        foreach (var child in node.GetChildren())
        {
            var result = FindTileMapInChildren(child);
            if (result != null)
                return result;
        }

        return null;
    }
}

