using Godot;
using System;
using System.Collections.Generic;

public partial class Character : CharacterBody2D, IDamageable
{
    // Существующие поля и свойства
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 200f;

    // Новые поля для управления Z-индексом



    [Export] public bool ShowDebugInfo { get; set; } = false;
    [Export] public NodePath TileMapPath { get; set; }
    [Export] public Vector2 TileSize { get; set; } = new Vector2(64, 32);
    [Export] public NodePath SpritePath { get; set; } = "Sprite2D"; // Путь к спрайту по умолчанию

    protected float _currentHealth;
    protected Vector2 _movementDirection = Vector2.Zero;
    protected List<string> _inventory = new List<string>();
    protected bool _isActive = true;
    protected Vector2 _currentPosition = Vector2.Zero;
    protected Label _debugLabel;
    protected Node2D _spriteNode; // Ссылка на узел спрайта


    [Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
    [Signal] public delegate void CharacterDiedEventHandler();

    public override void _Ready()
    {
        base._Ready();
        _currentHealth = MaxHealth;
        _currentPosition = Position;

        // Добавляем персонажа в группу "Player" для легкого поиска
        AddToGroup("Player");

        // Включаем отладочный лейбл, если требуется
        SetupDebugLabel();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isActive)
        {
            ProcessMovement(delta);
        }
    }

    public override void _Process(double delta)
    {

    }

    // Новый метод для обновления Z-индекса
   

    protected virtual Vector2 QueryMovementInput()
    {
        // По умолчанию используем установленное внешне направление
        return _movementDirection;
    }

    protected virtual void ProcessMovement(double delta)
    {
        // Запрашиваем желаемое направление (игрок переопределит этот метод)
        _movementDirection = QueryMovementInput();

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

    // Находим узел спрайта
    protected virtual void FindSpriteNode()
    {
        if (!string.IsNullOrEmpty(SpritePath))
        {
            _spriteNode = GetNodeOrNull<Node2D>(SpritePath);
        }

        if (_spriteNode == null)
        {
            // Пробуем найти любой узел Sprite2D среди дочерних
            foreach (var child in GetChildren())
            {
                if (child is Sprite2D sprite)
                {
                    _spriteNode = sprite;
                    break;
                }
            }
        }

        if (_spriteNode == null)
        {
            Logger.Debug($"WARNING: Character {Name}: Unable to find sprite node for Z-indexing", true);
        }
    }

    // Настройка отладочного лейбла
    protected virtual void SetupDebugLabel()
    {
        if (ShowDebugInfo)
        {
            _debugLabel = new Label();
            _debugLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _debugLabel.VerticalAlignment = VerticalAlignment.Center;
            _debugLabel.Position = new Vector2(0, -50); // Над персонажем
            _debugLabel.ZIndex = 1000; // Поверх всего

            // Стилизация
            _debugLabel.AddThemeColorOverride("font_color", Colors.White);
            _debugLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _debugLabel.AddThemeConstantOverride("outline_size", 2);

            AddChild(_debugLabel);
        }
    }




    // Рекурсивный поиск узла заданного типа
    protected T FindNodeRecursive<T>(Node root) where T : class
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