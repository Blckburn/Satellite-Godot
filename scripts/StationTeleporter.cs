using Godot;
using System.Collections.Generic;
using System;

/// <summary>
/// Телепортер на космической станции для возвращения в мир.
/// </summary>
public partial class StationTeleporter : InteractiveObject
{

    // Путь к узлу точки возрождения
    [Export] public NodePath TeleporterSpawnPointPath { get; set; } = "TeleporterSpawnPoint";
    // Ссылка на узел точки возрождения
    private Node2D _teleporterSpawnPoint;
    // Путь к сцене мира
    [Export] public string WorldScenePath { get; set; } = "res://scenes/Main.tscn";

    // Название узла для загрузки местоположения игрока
    [Export] public string PlayerPositionSaveName { get; set; } = "LastWorldPosition";

    // Точка спауна по умолчанию, если сохраненной позиции нет
    [Export] public Vector2 DefaultSpawnPosition { get; set; } = Vector2.Zero;

    // Эффекты телепортации
    [Export] public NodePath TeleportEffectsPath { get; set; }

    // Компоненты
    private AnimationPlayer _animationPlayer;
    private Node2D _teleportEffects;

    public override void _Ready()
    {
        AddToGroup("Interactables");
        // Настройка взаимодействия
        InteractionHint = "Press E to return to the world";

        base._Ready();

        // Находим компоненты
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

        if (!string.IsNullOrEmpty(TeleportEffectsPath))
            _teleportEffects = GetNodeOrNull<Node2D>(TeleportEffectsPath);

        // Инициализируем точку возрождения (с задержкой)
        CallDeferred("InitializeSpawnPoint");

        Logger.Debug("Station teleporter initialized", true);
    }
    // Метод для инициализации точки возрождения
    private void InitializeSpawnPoint()
    {
        // Ищем точку возрождения по указанному пути
        if (!string.IsNullOrEmpty(TeleporterSpawnPointPath))
        {
            _teleporterSpawnPoint = GetNodeOrNull<Node2D>(TeleporterSpawnPointPath);
            if (_teleporterSpawnPoint != null)
            {
                Logger.Debug($"Found TeleporterSpawnPoint at path: {TeleporterSpawnPointPath}", false);
                return;
            }
        }

        // Ищем точку возрождения как дочерний узел
        _teleporterSpawnPoint = FindChild("TeleporterSpawnPoint", false) as Node2D;
        if (_teleporterSpawnPoint != null)
        {
            Logger.Debug("Found TeleporterSpawnPoint as direct child", false);
            return;
        }

        // Если точка возрождения не найдена, создаем новую
        Logger.Debug("TeleporterSpawnPoint not found, creating a new one", true);
        _teleporterSpawnPoint = new Node2D();
        _teleporterSpawnPoint.Name = "TeleporterSpawnPoint";

        // Позиционируем немного впереди телепортера (чтобы игрок не появлялся прямо на телепортере)
        _teleporterSpawnPoint.Position = new Vector2(0, 50); // 50 пикселей вниз от телепортера

        // Используем CallDeferred вместо прямого AddChild
        CallDeferred("add_child", _teleporterSpawnPoint);
    }

    // Метод для получения позиции возрождения
    public Vector2 GetSpawnPosition()
    {
        // Если есть точка возрождения, используем её позицию
        if (_teleporterSpawnPoint != null)
        {
            // Используем глобальную позицию, чтобы учесть позицию самого телепортера
            return _teleporterSpawnPoint.GlobalPosition;
        }

        // Иначе используем позицию самого телепортера
        return GlobalPosition;
    }

    public override bool Interact(Node source)
    {
        if (!base.Interact(source))
            return false;

        // Телепортируем игрока обратно в мир
        TeleportToWorld(source as Player);

        return true;
    }

    /// <summary>
    /// Телепортирует игрока обратно в мир
    /// </summary>
    private void TeleportToWorld(Player player)
    {
        if (player == null)
        {
            Logger.Error("Cannot teleport: player is null");
            return;
        }

        Logger.Debug("Starting teleportation to world", false);

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
                    CompleteTeleportation();
            };
        }
        else
        {
            // Если анимации нет, просто добавляем задержку
            var timer = new Timer();
            timer.OneShot = true;
            timer.WaitTime = 1.0f; // 1 секунда
            timer.Timeout += () => CompleteTeleportation();
            AddChild(timer);
            timer.Start();
        }
    }

    /// <summary>
    /// Завершает процесс телепортации
    /// </summary>
    private void CompleteTeleportation()
    {
        Logger.Debug("Completing teleportation to world", false);

        // Скрываем эффекты телепортации
        if (_teleportEffects != null)
            _teleportEffects.Visible = false;

        // Сохраняем позицию для телепортации
        SaveTeleportDestination();

        // Устанавливаем флаг для создания игрока при загрузке мира
        // Этот флаг будет проверяться в любой сцене, где нужно создать игрока
        ProjectSettings.SetSetting("CreatePlayerOnLoad", true);

        // Загружаем позицию для телепортации игрока в мире
        Vector2 worldPosition = LoadPlayerPosition();
        ProjectSettings.SetSetting("PlayerSpawnPosition", worldPosition);

        // Переходим к сцене мира
        GetTree().ChangeSceneToFile(WorldScenePath);
    }

    /// <summary>
    /// Сохраняет информацию о точке назначения для телепортации
    /// </summary>
    private void SaveTeleportDestination()
    {
        // Создаем ключ для сохранения информации о телепортации
        string teleportKey = "TeleportDestination";

        // Получаем сохраненную позицию игрока
        Vector2 position = LoadPlayerPosition();

        // Проверяем, существует ли синглтон GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Сохраняем данные о телепортации через GameManager
            gameManager.SetData(teleportKey, new Dictionary<string, object>
            {
                { "position", position },
                { "scene", WorldScenePath }
            });

            Logger.Debug($"Teleport destination saved: {position}", false);
        }
        else
        {
            // Сохраняем в автозагрузку если GameManager отсутствует
            var autoload = Engine.GetSingleton("GameState");
            if (autoload != null)
            {
                autoload.Call("SetTeleportDestination", position, WorldScenePath);
                Logger.Debug($"Teleport destination saved via GameState: {position}", false);
            }
            else
            {
                // Если нет подходящего синглтона, используем ProjectSettings
                ProjectSettings.SetSetting("TeleportPosition", new Vector2(position.X, position.Y));
                ProjectSettings.SetSetting("TeleportScene", WorldScenePath);
                Logger.Debug($"Teleport destination saved via ProjectSettings: {position}", false);
            }
        }
    }

    /// <summary>
    /// Загружает сохраненную позицию игрока
    /// </summary>
    private Vector2 LoadPlayerPosition()
    {
        Vector2 position = DefaultSpawnPosition;

        // Проверяем, существует ли синглтон GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Загружаем позицию через GameManager
            var savedPosition = gameManager.GetData<Vector2>(PlayerPositionSaveName);
            if (savedPosition != Vector2.Zero)
            {
                position = savedPosition;
                Logger.Debug($"Player position loaded: {position}", false);
            }
        }
        else
        {
            // Загружаем из автозагрузки если GameManager отсутствует
            var autoload = Engine.GetSingleton("GameState");
            if (autoload != null)
            {
                var savedPosition = (Vector2)autoload.Call("GetData", PlayerPositionSaveName);
                if (savedPosition != Vector2.Zero)
                {
                    position = savedPosition;
                    Logger.Debug($"Player position loaded via GameState: {position}", false);
                }
            }
            else
            {
                // Если нет подходящего синглтона, используем ProjectSettings
                if (ProjectSettings.HasSetting(PlayerPositionSaveName))
                {
                    position = (Vector2)ProjectSettings.GetSetting(PlayerPositionSaveName);
                    Logger.Debug($"Player position loaded via ProjectSettings: {position}", false);
                }
            }
        }

        return position;
    }
}