using Godot;
using System;

public partial class Door : StaticBody2D, IInteractable, IInteraction
{
    [Export] public bool IsOpen { get; set; } = false;
    [Export] public float InteractionRadius { get; set; } = 2.0f;
    [Export] public float InteractionTime { get; set; } = 1.5f; // Время для открытия двери в секундах

    private string _interactionHintBase = "Press E to"; // Базовая часть подсказки

    private Sprite2D _sprite;
    private CollisionShape2D _collisionShape;

    // Переменные для отслеживания прогресса взаимодействия
    private bool _isInteracting = false;
    private float _interactionProgress = 0.0f;
    private float _interactionTimer = 0.0f;
    private bool _keyHeld = false;

    // Сигналы
    [Signal] public delegate void DoorOpenedEventHandler();
    [Signal] public delegate void DoorClosedEventHandler();
    [Signal] public delegate void InteractionStartedEventHandler();
    [Signal] public delegate void InteractionCompletedEventHandler();
    [Signal] public delegate void InteractionCanceledEventHandler();

    public override void _Ready()
    {
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");

        // Добавляем дверь в группу интерактивных объектов
        AddToGroup("Interactables");

        // Установка начального состояния
        UpdateVisuals();

        Logger.Debug($"Door '{Name}' initialized. IsOpen: {IsOpen}", true);
    }

    public override void _Process(double delta)
    {
        // Обрабатываем прогресс взаимодействия только если клавиша удерживается
        if (_isInteracting && _keyHeld)
        {
            _interactionTimer += (float)delta;
            _interactionProgress = Mathf.Min(1.0f, _interactionTimer / InteractionTime);

            // Если взаимодействие завершено
            if (_interactionProgress >= 1.0f)
            {
                CompleteInteraction();
            }
        }
    }

    // Реализация IInteractable
    public string GetInteractionHint()
    {
        if (_isInteracting)
        {
            // Просто показываем действие без процента
            return $"{_interactionHintBase} {(IsOpen ? "close" : "open")}...";
        }

        return $"{_interactionHintBase} {(IsOpen ? "close" : "open")}";
    }

    public bool CanInteract(Node source)
    {
        // Если уже идет взаимодействие, то новое не начинаем
        if (_isInteracting)
            return false;

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

        // Начинаем процесс взаимодействия
        StartInteraction();
        return true;
    }

    private void StartInteraction()
    {
        _isInteracting = true;
        _interactionProgress = 0.0f;
        _interactionTimer = 0.0f;
        _keyHeld = true;

        EmitSignal(SignalName.InteractionStarted);
        Logger.Debug($"Door '{Name}' interaction started", false);
    }

    private void CompleteInteraction()
    {
        _isInteracting = false;
        _interactionProgress = 0.0f;
        _interactionTimer = 0.0f;
        _keyHeld = false;

        // Переключаем состояние двери
        IsOpen = !IsOpen;
        Logger.Debug($"Door '{Name}' is now {(IsOpen ? "open" : "closed")}", false);

        // Обновляем визуал и коллизию
        UpdateVisuals();

        // Вызываем соответствующий сигнал
        if (IsOpen)
            EmitSignal(SignalName.DoorOpened);
        else
            EmitSignal(SignalName.DoorClosed);

        EmitSignal(SignalName.InteractionCompleted);
    }

    // Метод для обработки отпускания клавиши
    public void OnInteractionKeyReleased()
    {
        if (_isInteracting && _keyHeld)
        {
            _keyHeld = false;

            // Если прогресс не завершен, отменяем взаимодействие
            if (_interactionProgress < 1.0f)
            {
                CancelInteraction();
            }
        }
    }

    public float GetInteractionRadius()
    {
        return InteractionRadius;
    }

    // Реализация IInteraction
    public bool IsInteracting()
    {
        return _isInteracting;
    }

    public float GetInteractionProgress()
    {
        return _interactionProgress;
    }

    public void CancelInteraction()
    {
        if (_isInteracting)
        {
            _isInteracting = false;
            _keyHeld = false;
            _interactionProgress = 0.0f;
            _interactionTimer = 0.0f;

            EmitSignal(SignalName.InteractionCanceled);
            Logger.Debug($"Door '{Name}' interaction canceled", false);
        }
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