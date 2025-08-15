using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Телепортационный модуль космической станции.
/// Позволяет игроку перемещаться между станцией и планетами.
/// </summary>
public partial class TeleportationModule : BaseStationModule
{
    // Сигналы
    [Signal] public delegate void TeleportationInitiatedEventHandler(string destination);
    [Signal] public delegate void TeleportationCompletedEventHandler(string destination);

    // Список доступных направлений телепортации
    [Export] public string[] AvailableDestinations { get; set; } = new string[] { "Earth" };

    // Ссылки на компоненты модуля
    [Export] public NodePath GlobeDisplayPath { get; set; }
    [Export] public NodePath TeleportEffectsPath { get; set; }

    // Компоненты
    private Node2D _globeDisplay;
    private Node2D _teleportEffects;
    private AnimationPlayer _animationPlayer;
    private Control _teleportUI;

    // Текущее выбранное направление
    private string _selectedDestination = "";

    // Сцены для доступных планет
    private Dictionary<string, string> _destinationScenes = new Dictionary<string, string>();

    public override void _Ready()
    {
        // Устанавливаем свойства модуля
        ModuleName = "Teleportation Module";
        ModuleDescription = "Allows travel between your station and planets.";

        base._Ready();

        // Находим компоненты
        if (!string.IsNullOrEmpty(GlobeDisplayPath))
            _globeDisplay = GetNodeOrNull<Node2D>(GlobeDisplayPath);

        if (!string.IsNullOrEmpty(TeleportEffectsPath))
            _teleportEffects = GetNodeOrNull<Node2D>(TeleportEffectsPath);

        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

        // Инициализируем словарь доступных направлений
        InitializeDestinations();

        Logger.Debug("Teleportation module initialized", true);
    }

    public override void Initialize()
    {
        if (IsInitialized)
            return;

        // Создаем пользовательский интерфейс телепортации
        CreateTeleportUI();

        base.Initialize();
    }

    public override void Activate()
    {
        if (IsActive)
            return;

        // Показываем UI телепортации
        ShowTeleportUI();

        base.Activate();

        // Проигрываем анимацию активации, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("activate"))
            _animationPlayer.Play("activate");

        // Активируем отображение глобуса, если оно есть
        if (_globeDisplay != null)
            _globeDisplay.Visible = true;
    }

    public override void Deactivate()
    {
        if (!IsActive)
            return;

        // Скрываем UI телепортации
        HideTeleportUI();

        base.Deactivate();

        // Проигрываем анимацию деактивации, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("deactivate"))
            _animationPlayer.Play("deactivate");

        // Деактивируем отображение глобуса, если оно есть
        if (_globeDisplay != null)
            _globeDisplay.Visible = false;
    }

    public override string GetInteractionHint()
    {
        return "Press E to use Teleportation Module";
    }

    /// <summary>
    /// Инициализирует словарь доступных направлений телепортации
    /// </summary>
    private void InitializeDestinations()
    {
        // Заполняем словарь сцен для каждого направления
        _destinationScenes.Clear();

        // Пример соответствия направлений и сцен
        _destinationScenes.Add("Earth", "res://scenes/planets/earth.tscn");
        _destinationScenes.Add("Mars", "res://scenes/planets/mars.tscn");
        _destinationScenes.Add("Moon", "res://scenes/planets/moon.tscn");
    }

    /// <summary>
    /// Создает пользовательский интерфейс телепортации
    /// </summary>
    private void CreateTeleportUI()
    {
        // Создаем базовую структуру UI
        _teleportUI = new Control();
        _teleportUI.Name = "TeleportUI";
        _teleportUI.Visible = false;
        _teleportUI.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // Затемненный фон
        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0, 0, 0, 0.7f);
        panel.AddThemeStyleboxOverride("panel", styleBox);
        _teleportUI.AddChild(panel);

        // Контейнер для содержимого
        var container = new VBoxContainer();
        container.SetAnchorsPreset(Control.LayoutPreset.Center);
        container.Size = new Vector2(600, 400);
        container.Position = new Vector2(-300, -200);
        _teleportUI.AddChild(container);

        // Заголовок
        var titleLabel = new Label();
        titleLabel.Text = "Teleportation Module";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.AddThemeFontSizeOverride("font_size", 24);
        container.AddChild(titleLabel);

        // Разделитель
        var separator = new HSeparator();
        container.AddChild(separator);

        // Описание
        var descriptionLabel = new Label();
        descriptionLabel.Text = "Select destination to teleport:";
        descriptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        container.AddChild(descriptionLabel);

        // Список направлений
        var destinationsContainer = new VBoxContainer();
        container.AddChild(destinationsContainer);

        // Добавляем кнопки для каждого направления
        foreach (var destination in AvailableDestinations)
        {
            var button = new Button();
            button.Text = destination;
            button.Pressed += () => OnDestinationSelected(destination);
            destinationsContainer.AddChild(button);
        }

        // Кнопка отмены
        var cancelButton = new Button();
        cancelButton.Text = "Cancel";
        cancelButton.Pressed += () => Deactivate();
        container.AddChild(cancelButton);

        // Добавляем UI в дерево сцены
        AddChild(_teleportUI);

        Logger.Debug("Teleport UI created", false);
    }

    /// <summary>
    /// Показывает пользовательский интерфейс телепортации
    /// </summary>
    private void ShowTeleportUI()
    {
        if (_teleportUI != null)
            _teleportUI.Visible = true;
    }

    /// <summary>
    /// Скрывает пользовательский интерфейс телепортации
    /// </summary>
    private void HideTeleportUI()
    {
        if (_teleportUI != null)
            _teleportUI.Visible = false;
    }

    /// <summary>
    /// Обработчик выбора направления телепортации
    /// </summary>
    private void OnDestinationSelected(string destination)
    {
        _selectedDestination = destination;

        // Проверяем, существует ли сцена для выбранного направления
        if (!_destinationScenes.ContainsKey(destination))
        {
            Logger.Error($"No scene found for destination: {destination}");
            return;
        }

        Logger.Debug($"Destination selected: {destination}", false);

        // Запускаем процесс телепортации
        StartTeleportation(destination);
    }

    /// <summary>
    /// Запускает процесс телепортации
    /// </summary>
    private void StartTeleportation(string destination)
    {
        // Блокируем автосохранение на время телепортации (через ServerSaveManager)
        if (ServerSaveManager.Instance != null)
        {
            // Временно отключаем автосохранение
            ServerSaveManager.Instance.EnableAutoSave = false;
        }

        Logger.Debug("Starting teleportation to station, autosave blocked", true);
        Logger.Debug($"Starting teleportation to {destination}", false);

        // Отправляем сигнал о начале телепортации
        EmitSignal("TeleportationInitiated", destination);

        // Скрываем UI
        HideTeleportUI();

        // Запускаем эффекты телепортации
        if (_teleportEffects != null)
            _teleportEffects.Visible = true;

        // Проигрываем анимацию телепортации, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("teleport"))
        {
            _animationPlayer.Play("teleport");

            // Ожидаем завершения анимации перед продолжением
            _animationPlayer.AnimationFinished += (animName) =>
            {
                if (animName == "teleport")
                    CompleteTeleportation(destination);
            };
        }
        else
        {
            // Если анимации нет, просто добавляем задержку
            var timer = new Timer();
            timer.OneShot = true;
            timer.WaitTime = 1.0f; // 1 секунда
            timer.Timeout += () => CompleteTeleportation(destination);
            AddChild(timer);
            timer.Start();
        }
    }

    /// <summary>
    /// Завершает процесс телепортации
    /// </summary>
    private void CompleteTeleportation(string destination)
    {
        Logger.Debug($"Completing teleportation to {destination}", false);

        // Скрываем эффекты телепортации
        if (_teleportEffects != null)
            _teleportEffects.Visible = false;

        // Получаем путь к сцене назначения
        string scenePath = _destinationScenes[destination];

        // Переходим к сцене назначения
        GetTree().ChangeSceneToFile(scenePath);

        // Отправляем сигнал о завершении телепортации
        EmitSignal("TeleportationCompleted", destination);

        // Отложенное разблокирование автосохранения
        CallDeferred("UnblockAutosaveDeferred");
    }

    /// <summary>
    /// Добавляет новое доступное направление телепортации
    /// </summary>
    public void AddDestination(string destination, string scenePath)
    {
        // Проверяем, существует ли уже такое направление
        if (_destinationScenes.ContainsKey(destination))
            return;

        // Добавляем в словарь
        _destinationScenes.Add(destination, scenePath);


        // Добавляем в список доступных направлений
        var newDestinations = new List<string>(AvailableDestinations);
        newDestinations.Add(destination);
        AvailableDestinations = newDestinations.ToArray();

        // Обновляем UI, если модуль активен
        if (IsActive && _teleportUI != null)
        {
            // Обновление интерфейса
            // В полной реализации здесь нужно будет обновить список кнопок
        }

        Logger.Debug($"New destination added: {destination}", false);
    }

    /// <summary>
    /// Отложенное разблокирование автосохранения
    /// </summary>
    private void UnblockAutosaveDeferred()
    {
        // Разблокируем автосохранение через ServerSaveManager
        if (ServerSaveManager.Instance != null)
        {
            ServerSaveManager.Instance.EnableAutoSave = true;
        }
        
        Logger.Debug("Autosave unblocked after teleportation", true);
    }
}