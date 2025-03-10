using Godot;
using System;
using System.Collections.Generic;

public partial class Character : CharacterBody2D, IDamageable
{
    // Существующие поля и свойства
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 200f;

    // Новые поля для управления Z-индексом
    [Export] public bool EnableDynamicZIndex { get; set; } = true;
    [Export] public int BaseZIndex { get; set; } = 1; // Базовый Z-индекс = 1
    [Export] public int ZIndexPerGridUnit { get; set; } = 1;
    [Export] public bool ShowDebugInfo { get; set; } = false;
    [Export] public NodePath TileMapPath { get; set; }
    [Export] public NodePath SpritePath { get; set; } = "Sprite2D"; // Путь к спрайту по умолчанию

    protected float _currentHealth;
    protected Vector2 _movementDirection = Vector2.Zero;
    protected List<string> _inventory = new List<string>();
    protected bool _isActive = true;
    protected Vector2 _currentPosition = Vector2.Zero;

    // Переменные для Z-индексирования
    protected TileMap _tileMap;
    protected Vector2I _lastGridPos = new Vector2I(-999, -999);
    protected Label _debugLabel;
    protected Node2D _spriteNode; // Ссылка на узел спрайта

    [Signal] public delegate void HealthChangedEventHandler(float currentHealth, float maxHealth);
    [Signal] public delegate void CharacterDiedEventHandler();

    public override void _Ready()
    {
        base._Ready();
        _currentHealth = MaxHealth;
        _currentPosition = Position;

        // Инициализация Z-индексирования
        InitializeZIndexHandling();

        // Добавляем персонажа в группу "Player" для легкого поиска
        AddToGroup("Player");
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
        // Обновляем Z-индекс если он включен
        if (EnableDynamicZIndex && _isActive)
        {
            UpdateZIndex();
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

    // === Новые методы для управления Z-индексом ===

    // Инициализация Z-индексирования
    protected virtual void InitializeZIndexHandling()
    {
        // Находим TileMap для определения координат сетки
        FindTileMap();

        // Находим узел спрайта для установки Z-индекса
        FindSpriteNode();

        // Настраиваем начальный Z-индекс
        if (_spriteNode != null)
        {
            _spriteNode.ZIndex = BaseZIndex;
            Logger.Debug($"Initial Z-index set to {BaseZIndex} for sprite of {Name}", true);
        }
        else
        {
            ZIndex = BaseZIndex;
            Logger.Debug($"Sprite not found, setting Z-index for Character node: {Name}", true);
        }

        // Настраиваем отладочную информацию
        SetupDebugLabel();

        Logger.Debug($"Z-index handling initialized for {Name}", true);
    }

    // Находим узел спрайта
    protected virtual void FindSpriteNode()
    {
        if (!string.IsNullOrEmpty(SpritePath))
        {
            _spriteNode = GetNode<Node2D>(SpritePath);
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

    // Поиск TileMap в сцене
    protected virtual void FindTileMap()
    {
        if (!string.IsNullOrEmpty(TileMapPath))
        {
            // Если указан путь, пробуем найти по нему
            _tileMap = GetNode<TileMap>(TileMapPath);
        }

        if (_tileMap == null)
        {
            // Ищем в группе
            var maps = GetTree().GetNodesInGroup("TileMap");
            if (maps.Count > 0 && maps[0] is TileMap tileMap)
            {
                _tileMap = tileMap;
            }
        }

        if (_tileMap == null)
        {
            // Ищем по имени
            _tileMap = GetTree().Root.FindChild("TileMap", true, false) as TileMap;
        }

        if (_tileMap == null)
        {
            // Заменен вызов Warning на Debug с пометкой WARNING
            Logger.Debug($"WARNING: Character {Name}: Unable to find TileMap for Z-indexing", true);
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

    // Обновление Z-индекса на основе позиции
    protected virtual void UpdateZIndex()
    {
        if (_tileMap == null)
            return;

        // Получаем текущие координаты
        Vector2 worldPos = GlobalPosition;

        // Преобразуем в координаты сетки
        Vector2I gridPos = WorldToIsometricGrid(worldPos);

        // Обновляем Z-индекс только при изменении положения
        if (gridPos != _lastGridPos)
        {
            _lastGridPos = gridPos;

            // Рассчитываем Z-индекс по формуле: Базовый + (X + Y) * множитель
            int zIndex = BaseZIndex + (gridPos.X + gridPos.Y) * ZIndexPerGridUnit;

            // Устанавливаем Z-индекс для спрайта или для всего узла
            if (_spriteNode != null)
            {
                _spriteNode.ZIndex = zIndex;
            }
            else
            {
                ZIndex = zIndex;
            }

            // Обновляем отладочную информацию
            if (ShowDebugInfo && _debugLabel != null)
            {
                _debugLabel.Text = $"Grid: {gridPos}\nZ-Index: {zIndex}";
            }

            Logger.Debug($"{Name} Z-index updated to {zIndex} at grid position {gridPos}", false);
        }
    }

    // Преобразование мировых координат в координаты изометрической сетки
    protected virtual Vector2I WorldToIsometricGrid(Vector2 worldPos)
    {
        // Если TileMap реализует специальный метод
        if (_tileMap != null && _tileMap is IsometricMap isoMap)
        {
            return isoMap.WorldToMap(worldPos);
        }

        // Иначе используем общую формулу
        Vector2I tileSize = _tileMap?.TileSet?.TileSize ?? new Vector2I(64, 32);

        // Формула для изометрии 2:1
        float cartX = (worldPos.X / (tileSize.X / 2) + worldPos.Y / (tileSize.Y / 2)) / 2;
        float cartY = (worldPos.Y / (tileSize.Y / 2) - worldPos.X / (tileSize.X / 2)) / 2;

        return new Vector2I(Mathf.FloorToInt(cartX), Mathf.FloorToInt(cartY));
    }

    // Временное изменение Z-индекса
    public virtual void AdjustZIndex(int adjustment, float duration = 0)
    {
        if (_spriteNode == null && ZIndex == 0)
            return;

        int currentZIndex;
        if (_spriteNode != null)
        {
            currentZIndex = _spriteNode.ZIndex;
        }
        else
        {
            currentZIndex = ZIndex;
        }

        int newZIndex = currentZIndex + adjustment;

        // Сохраняем состояние динамического обновления
        bool wasEnabled = EnableDynamicZIndex;

        // Отключаем динамику на время ручной корректировки
        EnableDynamicZIndex = false;

        // Применяем новый Z-индекс
        if (_spriteNode != null)
        {
            _spriteNode.ZIndex = newZIndex;
        }
        else
        {
            ZIndex = newZIndex;
        }

        // Если указана длительность, возвращаем к автоматическому режиму
        if (duration > 0)
        {
            GetTree().CreateTimer(duration).Timeout += () => {
                EnableDynamicZIndex = wasEnabled;

                if (EnableDynamicZIndex)
                {
                    UpdateZIndex(); // Обновляем по текущему положению
                }
                else
                {
                    if (_spriteNode != null)
                    {
                        _spriteNode.ZIndex = currentZIndex;
                    }
                    else
                    {
                        ZIndex = currentZIndex;
                    }
                }
            };
        }
    }

    // Принудительная установка Z-индекса
    public virtual void SetFixedZIndex(int zIndex, float duration = 0)
    {
        // Запоминаем текущие значения
        int originalZIndex;
        if (_spriteNode != null)
        {
            originalZIndex = _spriteNode.ZIndex;
        }
        else
        {
            originalZIndex = ZIndex;
        }

        bool wasEnabled = EnableDynamicZIndex;

        // Отключаем динамическое обновление
        EnableDynamicZIndex = false;

        // Устанавливаем новый Z-индекс
        if (_spriteNode != null)
        {
            _spriteNode.ZIndex = zIndex;
        }
        else
        {
            ZIndex = zIndex;
        }

        // Если указана длительность, возвращаем как было
        if (duration > 0)
        {
            GetTree().CreateTimer(duration).Timeout += () => {
                EnableDynamicZIndex = wasEnabled;

                if (EnableDynamicZIndex)
                {
                    UpdateZIndex(); // Обновляем по позиции
                }
                else
                {
                    if (_spriteNode != null)
                    {
                        _spriteNode.ZIndex = originalZIndex;
                    }
                    else
                    {
                        ZIndex = originalZIndex;
                    }
                }
            };
        }
    }
} 