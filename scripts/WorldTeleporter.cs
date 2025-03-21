using Godot;
using System;

/// <summary>
/// Телепортер, размещаемый в мире для перехода на космическую станцию.
/// </summary>
public partial class WorldTeleporter : InteractiveObject
{
    // Путь к сцене космической станции
    [Export] public string StationScenePath { get; set; } = "res://scenes/station/space_station.tscn";

    // Название узла для сохранения местоположения игрока
    [Export] public string PlayerPositionSaveName { get; set; } = "LastWorldPosition";

    // Эффекты телепортации
    [Export] public NodePath TeleportEffectsPath { get; set; }

    // Компоненты
    private AnimationPlayer _animationPlayer;
    private Node2D _teleportEffects;

    public override void _Ready()
    {
        AddToGroup("Interactables");
        // Настройка взаимодействия
        InteractionHint = "Press E to teleport to your station";

        base._Ready();

        // Находим компоненты
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

        if (!string.IsNullOrEmpty(TeleportEffectsPath))
            _teleportEffects = GetNodeOrNull<Node2D>(TeleportEffectsPath);

        Logger.Debug("World teleporter initialized", true);
    }

    public override bool Interact(Node source)
    {
        if (!base.Interact(source))
            return false;

        // Телепортируем игрока на станцию
        TeleportToStation(source as Player);

        return true;
    }

    /// <summary>
    /// Телепортирует игрока на космическую станцию
    /// </summary>
    private void TeleportToStation(Player player)
    {
        if (player == null)
        {
            Logger.Error("Cannot teleport: player is null");
            return;
        }

        Logger.Debug("Starting teleportation to station", false);

        // Сохраняем текущую позицию игрока
        SavePlayerPosition(player);

        // Сохраняем инвентарь игрока
        player.SaveInventory();
        Logger.Debug("Player inventory saved before teleportation to station", false);

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
        Logger.Debug("Completing teleportation to station", false);

        // Скрываем эффекты телепортации
        if (_teleportEffects != null)
            _teleportEffects.Visible = false;

        // Устанавливаем флаг для создания игрока при загрузке станции
        ProjectSettings.SetSetting("CreatePlayerOnLoad", true);

        // Переходим к сцене станции
        GetTree().ChangeSceneToFile(StationScenePath);
    }

    /// <summary>
    /// Сохраняет текущую позицию игрока
    /// </summary>
    private void SavePlayerPosition(Player player)
    {
        // Сохраняем позицию игрока в глобальной переменной или файле
        // Здесь используем синглтон для простоты

        // Проверяем, существует ли синглтон GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Сохраняем позицию через GameManager
            gameManager.SetData(PlayerPositionSaveName, player.GlobalPosition);
            Logger.Debug($"Player position saved: {player.GlobalPosition}", false);
        }
        else
        {
            // Сохраняем в автозагрузку если GameManager отсутствует
            var autoload = Engine.GetSingleton("GameState");
            if (autoload != null)
            {
                autoload.Call("SetData", PlayerPositionSaveName, player.GlobalPosition);
                Logger.Debug($"Player position saved via GameState: {player.GlobalPosition}", false);
            }
            else
            {
                // Если нет подходящего синглтона, используем Godot.ProjectSettings
                var pos = player.GlobalPosition;
                ProjectSettings.SetSetting(PlayerPositionSaveName, new Vector2(pos.X, pos.Y));
                Logger.Debug($"Player position saved via ProjectSettings: {pos}", false);
            }
        }
    }
}