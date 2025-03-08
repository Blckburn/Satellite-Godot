using Godot;
using System;

public partial class PickupItem : InteractiveObject, IInteraction
{
    // Ссылка на предмет, который будет подобран
    [Export] public Item ItemResource { get; set; }

    // Путь к ресурсу предмета (используется, если ItemResource не задан напрямую)
    [Export] public string ItemResourcePath { get; set; } = "";

    // Количество предметов в стеке
    [Export] public int Quantity { get; set; } = 1;

    // Время взаимодействия для подбора предмета
    [Export] public float InteractionTime { get; set; } = 0.5f;

    // Визуальные компоненты
    private Sprite2D _sprite;
    private Label _itemLabel;

    // Переменные для эффектов
    [Export] public bool EnableRotation { get; set; } = true;
    [Export] public bool EnableBobbing { get; set; } = true;
    [Export] public float RotationSpeed { get; set; } = 1.0f;
    [Export] public float BobbingHeight { get; set; } = 0.2f;
    [Export] public float BobbingSpeed { get; set; } = 2.0f;

    private Vector2 _initialPosition;
    private float _time = 0f;

    // Переменные для процесса взаимодействия
    private bool _isInteracting = false;
    private float _interactionProgress = 0.0f;
    private float _interactionTimer = 0.0f;
    private bool _keyHeld = false;

    // Кэшированный предмет
    private Item _cachedItem;

    // Сигналы
    [Signal] public delegate void ItemPickedUpEventHandler(string itemId, int quantity);
    [Signal] public delegate void InteractionStartedEventHandler();
    [Signal] public delegate void InteractionCompletedEventHandler();
    [Signal] public delegate void InteractionCanceledEventHandler();

    public override void _Ready()
    {
        base._Ready();
        // добавляем предмет в Interactables
        AddToGroup("Interactables");
        // для дебага
        GD.Print($"PickupItem '{Name}' added to Interactables group");

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _itemLabel = GetNodeOrNull<Label>("ItemLabel");

        // Загрузка предмета, если он не был установлен напрямую
        if (ItemResource == null && !string.IsNullOrEmpty(ItemResourcePath))
        {
            ItemResource = ResourceLoader.Load<Item>(ItemResourcePath);
        }

        // Кэширование начальной позиции для эффекта покачивания
        _initialPosition = Position;

        // Обновление визуального представления
        UpdateVisuals();

        // Добавляем в группу PickupItems для удобного поиска
        AddToGroup("PickupItems");

        Logger.Debug($"PickupItem initialized: {GetItemName()}", true);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Обновляем время для эффектов
        _time += (float)delta;

        // Применяем эффекты
        ApplyVisualEffects(delta);

        // Обрабатываем процесс взаимодействия
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

    // Визуальные эффекты: вращение и покачивание
    private void ApplyVisualEffects(double delta)
    {
        if (!_isActive)
            return;

        // Эффект вращения
        if (EnableRotation && _sprite != null)
        {
            _sprite.Rotation += (float)delta * RotationSpeed;
        }

        // Эффект покачивания
        if (EnableBobbing)
        {
            float bobOffset = Mathf.Sin(_time * BobbingSpeed) * BobbingHeight;
            Position = new Vector2(_initialPosition.X, _initialPosition.Y - bobOffset);
        }
    }

    // Обновление визуального представления на основе предмета
    private void UpdateVisuals()
    {
        if (_cachedItem == null)
            _cachedItem = GetItem();

        if (_cachedItem != null)
        {
            // Обновляем подсказку
            InteractionHint = $"Press E to pick up {_cachedItem.DisplayName}";

            // Обновляем спрайт
            if (_sprite != null && _cachedItem.Icon != null)
            {
                _sprite.Texture = _cachedItem.Icon;
            }

            // Обновляем метку с названием
            if (_itemLabel != null)
            {
                _itemLabel.Text = Quantity > 1 ? $"{_cachedItem.DisplayName} x{Quantity}" : _cachedItem.DisplayName;
            }
        }
    }

    // Получение предмета, который будет подобран
    public Item GetItem()
    {
        if (_cachedItem != null)
            return _cachedItem;

        if (ItemResource != null)
        {
            _cachedItem = ItemResource.Clone();
            _cachedItem.Quantity = Quantity;
            return _cachedItem;
        }

        return null;
    }

    // Получение имени предмета для отображения
    public string GetItemName()
    {
        var item = GetItem();
        if (item != null)
            return item.DisplayName;

        return "Unknown Item";
    }

    // Реализация интерфейса IInteractable
    public override bool Interact(Node source)
    {
        if (!CanInteract(source))
            return false;

        // Начинаем процесс подбора предмета
        StartInteraction();
        return true;
    }

    // Реализация интерфейса IInteraction

    // Начало процесса взаимодействия
    private void StartInteraction()
    {
        _isInteracting = true;
        _interactionProgress = 0.0f;
        _interactionTimer = 0.0f;
        _keyHeld = true;

        EmitSignal("InteractionStarted");
        Logger.Debug($"Started picking up {GetItemName()}", false);
    }

    // Завершение процесса взаимодействия
    private void CompleteInteraction()
    {
        if (!_isInteracting)
            return;

        _isInteracting = false;
        _interactionProgress = 0.0f;
        _interactionTimer = 0.0f;
        _keyHeld = false;

        // Получаем предмет
        Item item = GetItem();
        if (item != null)
        {
            // Передаем предмет игроку, если это возможно
            bool pickedUp = false;

            // Найти игрока, который взаимодействовал с предметом
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Player player)
            {
                pickedUp = player.AddItemToInventory(item);

                if (pickedUp)
                {
                    // Отправляем сигнал о подборе предмета
                    EmitSignal("ItemPickedUp", item.ID, item.Quantity);
                    Logger.Debug($"Player picked up {item.DisplayName} x{item.Quantity}", false);

                    // Удаляем предмет из сцены
                    QueueFree();
                }
                else
                {
                    Logger.Debug($"Player's inventory couldn't hold {item.DisplayName}", false);
                }
            }
            else
            {
                Logger.Debug("No player found to give the item to", false);
            }
        }

        EmitSignal("InteractionCompleted");
    }

    // Отмена взаимодействия
    public void CancelInteraction()
    {
        if (_isInteracting)
        {
            _isInteracting = false;
            _keyHeld = false;
            _interactionProgress = 0.0f;
            _interactionTimer = 0.0f;

            EmitSignal("InteractionCanceled");
            Logger.Debug($"Cancelled picking up {GetItemName()}", false);
        }
    }

    // Для обработки отпускания клавиши взаимодействия
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

    // Реализация методов интерфейса IInteraction
    public bool IsInteracting()
    {
        return _isInteracting;
    }

    public float GetInteractionProgress()
    {
        return _interactionProgress;
    }
}