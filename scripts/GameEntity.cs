using Godot;
using System;

public partial class GameEntity : Node2D
{
	[Export] public string EntityName { get; set; } = "Entity";
	[Export] public string Description { get; set; } = "";

	// Базовые свойства всех игровых объектов
	protected Vector2 _currentPosition = Vector2.Zero;
	protected bool _isActive = true;

	// Событие для уведомления об изменениях
	[Signal] public delegate void EntityStateChangedEventHandler();

	// Переопределяемые методы
	public override void _Ready()
	{
		_currentPosition = Position;
	}

	public override void _Process(double delta)
	{
		if (_isActive)
		{
			ProcessEntity(delta);
		}
	}

	// Виртуальный метод для переопределения в наследниках
	protected virtual void ProcessEntity(double delta)
	{
		// Базовая реализация пуста, переопределяется в дочерних классах
	}

	// Общие методы для всех сущностей
	public virtual void Enable()
	{
		_isActive = true;
	}

	public virtual void Disable()
	{
		_isActive = false;
	}

	public virtual void SetPosition(Vector2 newPosition)
	{
		_currentPosition = newPosition;
		Position = newPosition;
	}

	// Метод для инициализации, вызывается после создания объекта
	public virtual bool Initialize()
	{
		return true;
	}

	public bool CanInteract(Node source)
{
    GD.Print($"CanInteract check: source type = {source.GetType()}");
    
    // Убираем проверку расстояния (временно для отладки)
    // или делаем ее менее строгой
    if (source is Node2D sourceNode)
    {
        float distance = GlobalPosition.DistanceTo(sourceNode.GlobalPosition);
        GD.Print($"Distance to source: {distance}, Required: {InteractionRadius}");
        
        // Увеличим временно радиус или просто всегда возвращаем true
        // return distance <= InteractionRadius * 5; // Увеличиваем радиус в 5 раз
        return true; // Пока для отладки всегда разрешаем взаимодействие
    }
    
    GD.Print("Source is not Node2D, allowing interaction anyway");
    return true; // Временно разрешаем любое взаимодействие
}
}
