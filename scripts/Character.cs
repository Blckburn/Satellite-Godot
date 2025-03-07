using Godot;
using System;
using System.Collections.Generic;

public partial class Character : GameEntity, IDamageable
{
	[Export] public float MaxHealth { get; set; } = 100f;
	[Export] public float MoveSpeed { get; set; } = 200f;

	protected float _currentHealth;
	protected Vector2 _movementDirection = Vector2.Zero;
	protected List<string> _inventory = new List<string>();

	[Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
	[Signal] public delegate void CharacterDiedEventHandler();

	public override void _Ready()
	{
		base._Ready();
		_currentHealth = MaxHealth;
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
			Vector2 velocity = _movementDirection.Normalized() * MoveSpeed * (float)delta;
			Position += velocity;
			_currentPosition = Position;
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
}
