using Godot;
using System;

public partial class InteractiveObject : GameEntity, IInteractable
{
    [Export] public float InteractionRadius { get; set; } = 1.5f;
    [Export] public string InteractionHint { get; set; } = "Press E to interact";

    [Signal] public delegate void InteractionStartedEventHandler();
    [Signal] public delegate void InteractionCompletedEventHandler();

    protected bool _isInteractable = true;

    public string GetInteractionHint()
    {
        return InteractionHint;
    }

    public virtual bool CanInteract(Node source)
    {
        if (!_isInteractable || !_isActive)
            return false;

        // Проверка на расстояние
        if (source is Node2D sourceNode)
        {
            float distance = Position.DistanceTo(sourceNode.Position);
            return distance <= InteractionRadius;
        }

        return true;
    }

    public virtual bool Interact(Node source)
    {
        if (!CanInteract(source))
            return false;

        EmitSignal(SignalName.InteractionStarted);

        // Выполнение взаимодействия
        OnInteractionComplete();

        return true;
    }

    protected virtual void OnInteractionComplete()
    {
        EmitSignal(SignalName.InteractionCompleted);
    }

    public float GetInteractionRadius()
    {
        return InteractionRadius;
    }

    // Визуализация радиуса взаимодействия в редакторе (опционально)
    public override void _Draw()
    {
        if (Engine.IsEditorHint())
        {
            DrawCircle(Vector2.Zero, InteractionRadius, new Color(0, 1, 0, 0.3f));
        }
    }
}