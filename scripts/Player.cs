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

    // Система взаимодействия
    private IInteractable _currentInteractable;

    public override void _Ready()
    {
        base._Ready();

        // Инициализация компонентов
        _sprite = GetNode<Sprite2D>("Sprite2D");
        _collisionShape = GetNode<CollisionShape2D>("CollisionShape2D");
        _interactionArea = GetNode<Area2D>("InteractionArea");
        if (_interactionArea != null)
        {
            _interactionCollisionShape = _interactionArea.GetNode<CollisionShape2D>("CollisionShape2D");
        }

        // Исправленная эмиссия сигнала
        HealthChanged += (currentHealth, maxHealth) => {
            GD.Print($"Player health: {currentHealth}/{maxHealth}");
        };

        // Коннект к сигналу входа объекта в область взаимодействия
        if (_interactionArea != null)
        {
            GD.Print("Setting up interaction area signals");
            _interactionArea.BodyEntered += OnBodyEnteredInteractionArea;
            _interactionArea.BodyExited += OnBodyExitedInteractionArea;
        }
        else
        {
            GD.Print("WARNING: InteractionArea not found on Player");
        }
    }

    public override void _Process(double delta)
    {
        // Обработка ввода
        HandleInput();

        base._Process(delta);
    }

    private void HandleInput()
    {
        // Движение
        Vector2 inputDirection = Vector2.Zero;

        if (Input.IsActionPressed("move_right"))
            inputDirection.X += 1;
        if (Input.IsActionPressed("move_left"))
            inputDirection.X -= 1;
        if (Input.IsActionPressed("move_down"))
            inputDirection.Y += 1;
        if (Input.IsActionPressed("move_up"))
            inputDirection.Y -= 1;

        SetMovementDirection(inputDirection);

        // Взаимодействие
        if (Input.IsActionJustPressed("interact"))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        GD.Print("Trying to interact...");
        if (_currentInteractable != null)
        {
            GD.Print($"Interacting with: {_currentInteractable}");
            bool success = _currentInteractable.Interact(this);
            GD.Print($"Interaction success: {success}");
        }
        else
        {
            // Если нет текущего объекта для взаимодействия, ищем ближайший
            var nearestInteractable = FindNearestInteractable();
            if (nearestInteractable != null)
            {
                GD.Print($"Found nearest interactable: {nearestInteractable}");
                bool success = nearestInteractable.Interact(this);
                GD.Print($"Interaction success: {success}");
            }
            else
            {
                GD.Print("No interactable object found");
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
        GD.Print($"Body entered: {body.Name}, Type: {body.GetType()}");

        if (body is IInteractable interactable)
        {
            _currentInteractable = interactable;
            GD.Print($"Can interact with: {body.Name} (IInteractable)");
        }
        else
        {
            GD.Print($"Body is not IInteractable: {body.Name}");
        }
    }

    private void OnBodyExitedInteractionArea(Node2D body)
    {
        if (body is IInteractable && _currentInteractable == body)
        {
            _currentInteractable = null;
        }
    }
}