using Godot;
using System;
using System.Collections.Generic;

public partial class Character : CharacterBody2D, IDamageable
{
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 200f;

    protected float _currentHealth;
    protected Vector2 _movementDirection = Vector2.Zero;
    protected List<string> _inventory = new List<string>();
    protected bool _isActive = true;
    protected Vector2 _currentPosition = Vector2.Zero;

    [Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
    [Signal] public delegate void CharacterDiedEventHandler();

    public override void _Ready()
    {
        base._Ready();
        _currentHealth = MaxHealth;
        _currentPosition = Position;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isActive)
        {
            ProcessMovement(delta);
        }
    }

    protected virtual void ProcessMovement(double delta)
    {
        if (_movementDirection != Vector2.Zero)
        {
            // Используем физический движок Godot для перемещения
            Velocity = _movementDirection.Normalized() * MoveSpeed;
            MoveAndSlide();
            _currentPosition = Position;
        }
        else
        {
            // Остановка персонажа
            Velocity = Vector2.Zero;
        }
    }

    public void SetMovementDirection(Vector2 direction)
    {
        _movementDirection = direction;
    }

    // Реализация IDamageable
    public virtual void TakeDamage(float amount, Node source)
    {
        _currentHealth -= amount;

        if (_currentHealth < 0)
            _currentHealth = 0;

        if (_currentHealth > MaxHealth)
            _currentHealth = MaxHealth;

        EmitSignal(SignalName.HealthChanged, _currentHealth, MaxHealth);

        if (IsDead())
        {
            OnDeath();
        }
    }

    public bool IsDead()
    {
        return _currentHealth <= 0;
    }

    public float GetHealth()
    {
        return _currentHealth;
    }

    public float GetMaxHealth()
    {
        return MaxHealth;
    }

    protected virtual void OnDeath()
    {
        _isActive = false;
        EmitSignal(SignalName.CharacterDied);
    }

    // Инвентарь
    public virtual void AddToInventory(string itemId)
    {
        _inventory.Add(itemId);
    }

    public virtual bool RemoveFromInventory(string itemId)
    {
        return _inventory.Remove(itemId);
    }

    public virtual List<string> GetInventory()
    {
        return new List<string>(_inventory);
    }

    // Методы для управления состоянием
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
}