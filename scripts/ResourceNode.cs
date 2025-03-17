using Godot;
using System;
using System.Linq;

/// <summary>
/// Класс для представления добываемого ресурса в игровом мире
/// Наследуется от InteractiveObject и реализует IInteraction для процесса добычи
/// </summary>
public partial class ResourceNode : InteractiveObject, IInteraction
{
    // Тип ресурса (перечисление ResourceType определено в отдельном файле)
    private ResourceType _type = ResourceType.Metal;
    [Export]
    public ResourceType Type
    {
        get => _type;
        set
        {
            _type = value;
            UpdateVisuals();
            UpdateInteractionHint();
        }
    }

    // Количество ресурса, которое можно добыть
    [Export] public int ResourceAmount { get; set; } = 1;

    // Время, необходимое для добычи ресурса (в секундах)
    [Export] public float HarvestTime { get; set; } = 2.0f;

    // Путь к ресурсу-предмету, который будет добавлен в инвентарь
    [Export] public string ResourceItemPath { get; set; } = "";

    private Item _resourceItem;
    [Export]
    public Item ResourceItem
    {
        get => _resourceItem;
        set
        {
            _resourceItem = value;
            if (_resourceItem != null)
            {
                Logger.Debug($"ResourceItem set: {_resourceItem.DisplayName}", false);
                // Обновляем визуал при установке нового предмета
                UpdateVisuals();
            }
        }
    }

    // Настройки визуальных эффектов
    [Export] public bool EnablePulsating { get; set; } = true;
    [Export] public bool EnableRotation { get; set; } = false;
    [Export] public float PulsatingSpeed { get; set; } = 1.0f;
    [Export] public float PulsatingStrength { get; set; } = 0.15f;
    [Export] public float RotationSpeed { get; set; } = 30.0f;

    // Визуальные компоненты
    private Sprite2D _sprite;
    private Godot.Label _resourceLabel;
    private Godot.Label _progressLabel;

    // Переменные для процесса добычи
    private bool _isHarvesting = false;
    private float _harvestProgress = 0.0f;
    private float _harvestTimer = 0.0f;
    private bool _keyHeld = false;
    private bool _harvestPaused = false;

    // Переменные для визуальных эффектов
    private Godot.Vector2 _initialScale = Godot.Vector2.One;
    private float _time = 0.0f;

    // Сигналы
    [Signal] public delegate void HarvestStartedEventHandler();
    [Signal] public delegate void HarvestCompletedEventHandler(int amount, int resourceType);
    [Signal] public delegate void HarvestCanceledEventHandler();

    public override void _Ready()
    {
        base._Ready();

        // Инициализация компонентов
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        _resourceLabel = GetNodeOrNull<Godot.Label>("ResourceLabel");

        // Создаем метку прогресса, если её ещё нет
        _progressLabel = GetNodeOrNull<Godot.Label>("ProgressLabel");
        if (_progressLabel == null)
        {
            CreateProgressLabel();
        }

        // Сохраняем начальный масштаб для эффектов
        if (_sprite != null)
        {
            _initialScale = _sprite.Scale;
        }

        // Загружаем предмет, если он не был установлен напрямую
        if (ResourceItem == null && !string.IsNullOrEmpty(ResourceItemPath))
        {
            ResourceItem = ResourceLoader.Load<Item>(ResourceItemPath);
        }

        // Добавляем в группу для быстрого поиска в других скриптах
        AddToGroup("ResourceNodes");
        AddToGroup("Interactables");

        // Обновляем визуальное представление ресурса
        UpdateVisuals();

        Logger.Debug($"ResourceNode '{Name}' initialized with type: {Type}", true);
    }

    // Создание элементов прогресса
    private void CreateProgressLabel()
    {
        // Создаем родительский контейнер для всех элементов прогресса
        var progressContainer = new Control();
        progressContainer.Name = "ProgressContainer";
        progressContainer.Position = new Godot.Vector2(0, -40); // Располагаем над ресурсом
        progressContainer.SetAnchorsPreset(Control.LayoutPreset.Center);
        progressContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        AddChild(progressContainer);

        // Создаем фон для прогресс-бара - теперь он шире для размещения текста
        var progressBackground = new ColorRect();
        progressBackground.Name = "ProgressBackground";
        progressBackground.Size = new Godot.Vector2(100, 20); // Увеличиваем размер для текста внутри
        progressBackground.Position = new Godot.Vector2(-50, 0); // Центрируем
        progressBackground.Color = new Godot.Color(0.2f, 0.2f, 0.2f, 0.8f);
        progressContainer.AddChild(progressBackground);

        // Создаем передний план прогресс-бара (заполняющаяся часть)
        var progressFill = new ColorRect();
        progressFill.Name = "ProgressFill";
        progressFill.Size = new Godot.Vector2(0, 20); // Начальная ширина 0, та же высота
        progressFill.Position = new Godot.Vector2(-50, 0); // Та же позиция, что и у фона
        progressFill.Color = new Godot.Color(1, 1, 1, 0.9f); // Белый полупрозрачный
        progressContainer.AddChild(progressFill);

        // Создаем текстовую метку прогресса поверх прогресс-бара
        _progressLabel = new Godot.Label();
        _progressLabel.Name = "ProgressLabel";
        _progressLabel.Position = new Godot.Vector2(0, 0); // Центрируем текст в контейнере
        _progressLabel.Size = new Godot.Vector2(100, 20); // Размер как у бара
        _progressLabel.Position = new Godot.Vector2(-50, 0); // Та же позиция, что и у бара

        // Стилизация для лучшей видимости
        _progressLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _progressLabel.VerticalAlignment = VerticalAlignment.Center; // Центрируем по вертикали
        _progressLabel.AddThemeColorOverride("font_color", Colors.White);
        _progressLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _progressLabel.AddThemeConstantOverride("outline_size", 2);
        _progressLabel.AddThemeFontSizeOverride("font_size", 12);
        progressContainer.AddChild(_progressLabel);

        // Скрываем контейнер изначально
        progressContainer.Visible = false;

        Logger.Debug("Progress UI elements created for resource node", false);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Обновляем время для визуальных эффектов
        _time += (float)delta;

        // Применяем визуальные эффекты
        ApplyVisualEffects(delta);

        // Добавляем явную проверку клавиши E для дополнительной надежности
        bool eKeyPressed = Input.IsActionPressed("interact");
        if (_isHarvesting && !_harvestPaused && !eKeyPressed)
        {
            // Если ключ отпущен, но мы все еще собираем ресурс, приостанавливаем
            Logger.Debug("ResourceNode: E key not pressed, pausing harvest", true);
            PauseHarvest();
        }
        else if (_isHarvesting && !_harvestPaused && _keyHeld)
        {
            // Проверяем, находится ли игрок в пределах зоны взаимодействия
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Player player)
            {
                float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
                if (distance > InteractionRadius)
                {
                    // Игрок отошел слишком далеко, приостанавливаем сбор
                    Logger.Debug("ResourceNode: Player moved too far, pausing harvest", true);
                    PauseHarvest();
                    return; // Прекращаем обработку
                }
            }

            // Если все проверки пройдены, продолжаем сбор
            _harvestTimer += (float)delta;
            _harvestProgress = Mathf.Min(1.0f, _harvestTimer / HarvestTime);

            // Обновляем подсказку с прогрессом
            UpdateInteractionHintDuringHarvest();

            // Обновляем метку прогресса
            UpdateProgressLabel();

            // Проверяем завершение процесса
            if (_harvestProgress >= 1.0f)
            {
                CompleteHarvest();
            }
        }
    }


    // Обновление метки прогресса
    private void UpdateProgressLabel()
    {
        var progressContainer = GetNodeOrNull<Control>("ProgressContainer");
        if (progressContainer == null) return;

        if (_isHarvesting)
        {
            int percent = (int)(_harvestProgress * 100);

            // Обновляем текст
            if (_progressLabel != null)
            {
                if (_harvestPaused)
                    _progressLabel.Text = $"Paused: {percent}%";
                else
                    _progressLabel.Text = $"Harvesting: {percent}%";
            }

            // Обновляем прогресс-бар
            var progressFill = progressContainer.GetNodeOrNull<ColorRect>("ProgressFill");
            if (progressFill != null)
            {
                // Вычисляем новую ширину заполнения в зависимости от прогресса
                float maxWidth = 100; // Ширина 100 для вмещения текста
                float newWidth = maxWidth * _harvestProgress;
                progressFill.Size = new Godot.Vector2(newWidth, progressFill.Size.Y);

                // Меняем цвет в зависимости от прогресса и статуса
                if (_harvestPaused)
                {
                    // Более тусклый цвет для приостановленного сбора
                    progressFill.Color = new Godot.Color(0.5f, 0.5f, 0.5f, 0.7f);
                }
                else
                {
                    // Яркий зеленый для активного сбора
                    progressFill.Color = new Godot.Color(
                        0.3f + (_harvestProgress * 0.3f), // R увеличивается немного
                        0.8f + (_harvestProgress * 0.2f), // G увеличивается немного
                        0.3f, // B остается низким для зеленого оттенка
                        0.9f
                    );
                }
            }

            progressContainer.Visible = true;
        }
        else
        {
            progressContainer.Visible = false;
        }
    }

    // Обновленный метод для отображения статуса сбора
    private void UpdateInteractionHintDuringHarvest()
    {
        int percent = (int)(_harvestProgress * 100);
        if (_harvestPaused)
        {
            InteractionHint = $"Press E to continue harvesting ({percent}%)";
        }
        else
        {
            InteractionHint = $"Harvesting ({percent}%)... Hold E";
        }
    }

    // Применение визуальных эффектов
    private void ApplyVisualEffects(double delta)
    {
        if (_sprite == null)
            return;

        // Эффект вращения сохраняем
        if (EnableRotation)
        {
            _sprite.RotationDegrees += (float)delta * RotationSpeed;
        }

        // Во время активного процесса сбора
        if (_isHarvesting && !_harvestPaused)
        {
            // Уменьшаем размер ресурса в зависимости от прогресса
            float shrinkFactor = 1.0f - (_harvestProgress * 0.7f);
            _sprite.Scale = _initialScale * shrinkFactor;

            // Изменяем прозрачность - ресурс становится более прозрачным
            float alpha = 1.0f - (_harvestProgress * 0.5f);
            _sprite.Modulate = new Godot.Color(
                1.0f,
                1.0f - _harvestProgress * 0.3f,  // Немного краснеет
                1.0f - _harvestProgress * 0.3f,  // Немного краснеет
                alpha  // Становится прозрачнее
            );
        }
        else if (_isHarvesting && _harvestPaused)
        {
            // Для приостановленного сбора сохраняем уменьшенный размер,
            // но возвращаем нормальный цвет
            float shrinkFactor = 1.0f - (_harvestProgress * 0.7f);
            _sprite.Scale = _initialScale * shrinkFactor;
            _sprite.Modulate = Colors.White;
        }
        else
        {
            // Возвращаем нормальный вид спрайту, если не собираем
            _sprite.Modulate = Colors.White;
            _sprite.Scale = _initialScale;
        }
    }

    // Обновление всего визуального представления ресурса
    private void UpdateVisuals()
    {
        // Обновляем спрайт на основе ResourceItem
        if (_sprite != null)
        {
            // Сначала проверяем, есть ли у нас ResourceItem
            if (ResourceItem != null && ResourceItem.Icon != null)
            {
                // Используем текстуру непосредственно из Item
                _sprite.Texture = ResourceItem.Icon;
                Logger.Debug($"Updated sprite texture from ResourceItem.Icon for {Type} resource", false);
            }
            else
            {
                // Запасной вариант - загружаем текстуру по пути на основе типа
                string texturePath = GetTexturePathForResourceType(Type);
                var texture = ResourceLoader.Load<Texture2D>(texturePath);

                if (texture != null)
                {
                    _sprite.Texture = texture;
                    Logger.Debug($"Updated sprite texture from path for {Type} resource: {texturePath}", false);
                }
                else
                {
                    Logger.Error($"Failed to load texture from path: {texturePath}");
                }
            }
        }

        // Обновляем текстовую метку
        UpdateResourceLabel();

        // Обновляем подсказку для взаимодействия
        UpdateInteractionHint();
    }

    // Получение пути к текстуре в зависимости от типа ресурса
    private string GetTexturePathForResourceType(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Metal:
                return "res://resources/textures/resources/metal_ore.png";
            case ResourceType.Crystal:
                return "res://resources/textures/resources/crystal.png";
            case ResourceType.Organic:
                return "res://resources/textures/resources/organic_matter.png";
            default:
                return "res://icon.svg"; // Стандартная иконка Godot
        }
    }

    // Обновление текстовой метки ресурса
    private void UpdateResourceLabel()
    {
        if (_resourceLabel != null)
        {
            string resourceName = GetResourceTypeName(Type);
            _resourceLabel.Text = resourceName;
        }
    }

    // Получение названия типа ресурса для отображения
    private string GetResourceTypeName(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Metal:
                return "Metal Ore";
            case ResourceType.Crystal:
                return "Crystal";
            case ResourceType.Organic:
                return "Organic Matter";
            case ResourceType.Energy:
                return "Energy Source";
            case ResourceType.Composite:
                return "Composite Material";
            default:
                return "Resource";
        }
    }

    // Обновление подсказки для взаимодействия
    private void UpdateInteractionHint()
    {
        string resourceName = GetResourceTypeName(Type);
        InteractionHint = $"Press E to harvest {resourceName}";
    }

    // Реализация интерфейса IInteractable для начального взаимодействия
    public override bool Interact(Node source)
    {
        if (!CanInteract(source))
        {
            return false;
        }

        // Если сбор приостановлен, продолжаем с текущего прогресса
        if (_harvestPaused)
        {
            ResumeHarvest();
            return true;
        }

        // Если не приостановлен и не идет, начинаем новый сбор
        if (!_isHarvesting)
        {
            StartHarvest();
            return true;
        }

        return false;
    }

    // Начало процесса добычи
    private void StartHarvest()
    {
        _isHarvesting = true;
        _harvestPaused = false;
        _harvestProgress = 0.0f;
        _harvestTimer = 0.0f;
        _keyHeld = true; // Устанавливаем флаг, что клавиша удерживается

        // Явно проверяем состояние клавиши сразу после начала сбора
        // Это дополнительная защита, чтобы убедиться, что клавиша действительно нажата
        if (!Input.IsActionPressed("interact"))
        {
            Logger.Debug("ResourceNode: E key not pressed immediately after StartHarvest, forcing keyHeld=false", true);
            _keyHeld = false;
            PauseHarvest();
            return;
        }

        // Показываем метку прогресса и сбрасываем прогресс-бар
        var progressContainer = GetNodeOrNull<Control>("ProgressContainer");
        if (progressContainer != null)
        {
            var progressFill = progressContainer.GetNodeOrNull<ColorRect>("ProgressFill");
            if (progressFill != null)
            {
                progressFill.Size = new Godot.Vector2(0, progressFill.Size.Y); // Сбрасываем ширину
            }

            if (_progressLabel != null)
            {
                _progressLabel.Text = "Harvesting: 0%";
            }

            progressContainer.Visible = true;
        }

        EmitSignal(SignalName.HarvestStarted);
        Logger.Debug($"ResourceNode: Started harvesting {Type} resource", true);
    }

    // Приостановка процесса добычи (сохраняем прогресс)
    private void PauseHarvest()
    {
        if (_isHarvesting && !_harvestPaused)
        {
            _harvestPaused = true;
            _keyHeld = false;

            // Обновляем подсказку для продолжения
            UpdateInteractionHintDuringHarvest();

            // Обновляем вид прогресс-бара
            UpdateProgressLabel();

            // Возвращаем нормальный вид спрайту, но сохраняем размер для индикации прогресса
            if (_sprite != null)
            {
                _sprite.Modulate = Colors.White;
                // НЕ меняем размер, чтобы визуально показать прогресс: _sprite.Scale
            }

            Logger.Debug($"Harvesting of {Type} resource paused at {_harvestProgress * 100}%", false);
        }
    }

    // Возобновление процесса добычи
    private void ResumeHarvest()
    {
        if (_isHarvesting && _harvestPaused)
        {
            _harvestPaused = false;
            _keyHeld = true;

            // Обновляем подсказку для сбора
            UpdateInteractionHintDuringHarvest();

            // Обновляем вид прогресс-бара
            UpdateProgressLabel();

            Logger.Debug($"Resumed harvesting of {Type} resource from {_harvestProgress * 100}%", false);
        }
    }

    // Завершение процесса добычи
    private void CompleteHarvest()
    {
        _isHarvesting = false;
        _harvestPaused = false;
        _harvestProgress = 0.0f;
        _harvestTimer = 0.0f;
        _keyHeld = false;

        // Скрываем элементы прогресса
        var progressContainer = GetNodeOrNull<Control>("ProgressContainer");
        if (progressContainer != null)
        {
            progressContainer.Visible = false;
        }

        // Возвращаем нормальный вид спрайту
        if (_sprite != null)
        {
            _sprite.Modulate = Colors.White;
            _sprite.Scale = _initialScale;
        }

        // Добавляем ресурс в инвентарь игрока
        if (TryAddResourceToInventory())
        {
            // Эмитим сигнал о успешной добыче
            EmitSignal(SignalName.HarvestCompleted, ResourceAmount, (int)Type);

            // Удаляем узел ресурса, так как он был исчерпан
            QueueFree();
        }
        else
        {
            // Обновляем подсказку на случай неудачи
            UpdateInteractionHint();
        }
    }

    // Обработчик отпускания клавиши (теперь приостанавливает вместо отмены)
    public void OnInteractionKeyReleased()
    {
        if (_isHarvesting && _keyHeld)
        {
            _keyHeld = false;

            // Приостанавливаем, а не отменяем взаимодействие
            PauseHarvest();
        }
    }

    // Метод полной отмены сбора (например, если игрок отошел очень далеко)
    public void CancelHarvestCompletely()
    {
        if (_isHarvesting)
        {
            _isHarvesting = false;
            _harvestPaused = false;
            _keyHeld = false;
            _harvestProgress = 0.0f;
            _harvestTimer = 0.0f;

            // Скрываем элементы прогресса
            var progressContainer = GetNodeOrNull<Control>("ProgressContainer");
            if (progressContainer != null)
            {
                progressContainer.Visible = false;
            }

            // Возвращаем нормальный вид спрайту
            if (_sprite != null)
            {
                _sprite.Modulate = Colors.White;
                _sprite.Scale = _initialScale;
            }

            // Возвращаем исходную подсказку
            UpdateInteractionHint();

            EmitSignal(SignalName.HarvestCanceled);
            Logger.Debug($"Harvesting of {Type} resource completely canceled", false);
        }
    }

    // Добавление ресурса в инвентарь игрока
    private bool TryAddResourceToInventory()
    {
        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Проверяем, есть ли у нас ресурс-предмет
            if (ResourceItem != null)
            {
                Logger.Debug($"ResourceItem found: ID={ResourceItem.ID}, Name={ResourceItem.DisplayName}", true);
                GD.Print($"DEBUG: Adding resource to inventory - ID: {ResourceItem.ID}, Name: {ResourceItem.DisplayName}, Quantity: {ResourceAmount}");

                try
                {
                    // Создаем копию предмета с правильным количеством
                    Item resourceItemCopy = ResourceItem.Clone();
                    resourceItemCopy.Quantity = ResourceAmount;

                    Logger.Debug($"Cloned ResourceItem: ID={resourceItemCopy.ID}, Name={resourceItemCopy.DisplayName}, Quantity={resourceItemCopy.Quantity}", true);
                    GD.Print($"DEBUG: Resource clone created - ID: {resourceItemCopy.ID}, Name: {resourceItemCopy.DisplayName}, Quantity: {resourceItemCopy.Quantity}");

                    // Получаем текущее количество в инвентаре до добавления
                    int currentQuantity = 0;
                    if (player.PlayerInventory != null)
                    {
                        Item existingItem = player.PlayerInventory.Items.FirstOrDefault(i => i.ID == resourceItemCopy.ID);
                        if (existingItem != null)
                        {
                            currentQuantity = existingItem.Quantity;
                        }
                    }

                    // Добавляем в инвентарь
                    bool added = player.AddItemToInventory(resourceItemCopy);

                    if (added)
                    {
                        // Проверяем новое количество после добавления
                        int newQuantity = 0;
                        Item updatedItem = player.PlayerInventory.Items.FirstOrDefault(i => i.ID == resourceItemCopy.ID);
                        if (updatedItem != null)
                        {
                            newQuantity = updatedItem.Quantity;
                        }

                        int actuallyAdded = newQuantity - currentQuantity;

                        Logger.Debug($"Successfully added {actuallyAdded} {Type} resource to player inventory", true);
                        GD.Print($"SUCCESS: Added {actuallyAdded} {Type} resource to player inventory");
                        return true;
                    }
                    else
                    {
                        Logger.Debug("Failed to add resource to inventory (inventory full?)", true);
                        GD.Print($"FAILED: Could not add resource to inventory (inventory full?)");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception when adding resource to inventory: {ex.Message}");
                    GD.Print($"ERROR: Exception when adding resource to inventory: {ex.Message}");
                    return false;
                }
            }
            else
            {
                Logger.Error($"ResourceNode {Name} has no ResourceItem assigned!");
                GD.Print($"ERROR: ResourceNode {Name} has no ResourceItem assigned!");
                return false;
            }
        }

        Logger.Error("Player not found when trying to add resource to inventory");
        GD.Print("ERROR: Player not found when trying to add resource to inventory");
        return false;
    }

    // Реализация IInteraction - проверка активности процесса
    public bool IsInteracting()
    {
        return _isHarvesting;
    }

    // Реализация IInteraction - получение прогресса
    public float GetInteractionProgress()
    {
        return _harvestProgress;
    }

    // Реализация IInteraction - отмена взаимодействия
    public void CancelInteraction()
    {
        // Только приостанавливаем, а не отменяем полностью
        PauseHarvest();
    }
}