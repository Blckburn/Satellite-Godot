using Godot;
using System;

public partial class Door : StaticBody2D, IInteractable
{
    [Export] public bool IsOpen { get; set; } = false;
    [Export] public float InteractionRadius { get; set; } = 2.0f;
    [Export] public string InteractionHint { get; set; } = "Press E to open door";

    private Sprite2D _sprite;
    private CollisionShape2D _collisionShape;

    // Сигналы
    [Signal] public delegate void DoorOpenedEventHandler();
    [Signal] public delegate void DoorClosedEventHandler();

    public override void _Ready()
    {
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");

        // Добавляем дверь в группу интерактивных объектов
        AddToGroup("Interactables");

        // Установка начального состояния
        UpdateVisuals();

        AddToGroup("Interactables");
        GD.Print($"Door '{Name}' added to Interactables group");
    }

    public string GetInteractionHint()
    {
        return IsOpen ? "Press E to close" : "Press E to open";
    }

    public bool CanInteract(Node source)
    {
        // Проверка расстояния
        if (source is Node2D sourceNode)
        {
            float distance = GlobalPosition.DistanceTo(sourceNode.GlobalPosition);
            return distance <= InteractionRadius;
        }

        return true;
    }

    public bool Interact(Node source)
    {
        if (!CanInteract(source))
        {
            return false;
        }

        // Переключаем состояние двери
        IsOpen = !IsOpen;
        GD.Print($"Door is now {(IsOpen ? "open" : "closed")}");

        // Обновляем визуал и коллизию
        UpdateVisuals();

        // Вызываем соответствующий сигнал
        if (IsOpen)
            EmitSignal(SignalName.DoorOpened);
        else
            EmitSignal(SignalName.DoorClosed);

        return true;
    }

    public float GetInteractionRadius()
    {
        return InteractionRadius;
    }

    private void UpdateVisuals()
    {
        if (_sprite != null)
        {
            // Изменение цвета или текстуры в зависимости от состояния
            _sprite.Modulate = IsOpen ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.2f, 0.2f);
        }

        if (_collisionShape != null)
        {
            // Отключение коллизии, если дверь открыта
            _collisionShape.Disabled = IsOpen;
        }
    }

    // Для визуализации радиуса взаимодействия в редакторе
    public override void _Draw()
    {
        if (Engine.IsEditorHint())
        {
            // Рисуем круг, обозначающий радиус взаимодействия
            DrawCircle(Vector2.Zero, InteractionRadius, new Color(0, 1, 0, 0.3f));
        }
    }
}