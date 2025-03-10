using Godot;
using System;

public partial class PlayerSpawner : Node
{
    // Ссылка на сцену игрока
    [Export] public PackedScene PlayerScene { get; set; }

    // Ссылка на LevelGenerator
    [Export] public NodePath LevelGeneratorPath { get; set; }

    // Группа, в которую будет добавлен игрок
    [Export] public string PlayerGroup { get; set; } = "Player";

    // Создавать нового игрока или переместить существующего
    [Export] public bool CreateNewPlayer { get; set; } = true;

    // Ссылка на LevelGenerator
    private LevelGenerator _levelGenerator;

    // Флаг, указывающий, что игрок уже создан
    private bool _playerSpawned = false;

    // Референс на объект игрока
    private Node2D _player;

    public override void _Ready()
    {
        // Поиск LevelGenerator
        if (!string.IsNullOrEmpty(LevelGeneratorPath))
        {
            _levelGenerator = GetNode<LevelGenerator>(LevelGeneratorPath);
        }
        else
        {
            // Пытаемся найти LevelGenerator в сцене
            _levelGenerator = GetTree().Root.FindChild("LevelGenerator", true, false) as LevelGenerator;

            if (_levelGenerator == null)
            {
                var node2D = GetTree().Root.FindChild("Node2D", true, false);
                if (node2D != null)
                {
                    _levelGenerator = node2D.FindChild("LevelGenerator", true, false) as LevelGenerator;
                }
            }
        }

        if (_levelGenerator == null)
        {
            Logger.Error("PlayerSpawner: LevelGenerator not found!");
            return;
        }

        // Подписываемся на сигнал о создании уровня
        _levelGenerator.Connect(LevelGenerator.SignalName.LevelGenerated, Callable.From<Vector2>(OnLevelGenerated));

        Logger.Debug("PlayerSpawner initialized and connected to LevelGenerator", true);
    }

    // Обработчик события генерации уровня
    private void OnLevelGenerated(Vector2 spawnPosition)
    {
        Logger.Debug($"Level generated signal received. Spawn position: {spawnPosition}", true);

        if (CreateNewPlayer)
        {
            SpawnNewPlayer(spawnPosition);
        }
        else
        {
            MoveExistingPlayer(spawnPosition);
        }
    }

    // Создание нового игрока
    private void SpawnNewPlayer(Vector2 position)
    {
        if (_playerSpawned && _player != null && IsInstanceValid(_player))
        {
            Logger.Debug("Player already spawned, removing old instance", false);
            _player.QueueFree();
        }

        if (PlayerScene == null)
        {
            Logger.Error("PlayerSpawner: PlayerScene is not set!");
            return;
        }

        try
        {
            // Инстанцируем игрока
            _player = PlayerScene.Instantiate<Node2D>();

            if (_player == null)
            {
                Logger.Error("Failed to instantiate player scene");
                return;
            }

            // Устанавливаем позицию
            _player.Position = position;

            // Добавляем игрока в группу
            if (!string.IsNullOrEmpty(PlayerGroup) && !_player.IsInGroup(PlayerGroup))
            {
                _player.AddToGroup(PlayerGroup);
            }

            // Добавляем игрока в сцену
            // Решаем, куда добавить игрока - обычно на уровень выше LevelGenerator
            Node parent = GetParent();
            parent.AddChild(_player);

            _playerSpawned = true;

            // Центрируем камеру на игроке, если есть камера, которая следит за игроком
            CenterCameraOnPlayer();

            Logger.Debug($"New player spawned at position {position}", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error spawning player: {e.Message}");
        }
    }

    // Перемещение существующего игрока
    private void MoveExistingPlayer(Vector2 position)
    {
        // Ищем игрока в группе
        var playersInGroup = GetTree().GetNodesInGroup(PlayerGroup);

        if (playersInGroup.Count > 0 && playersInGroup[0] is Node2D player)
        {
            _player = player;
            _player.Position = position;
            _playerSpawned = true;

            // Центрируем камеру на игроке
            CenterCameraOnPlayer();

            Logger.Debug($"Existing player moved to position {position}", true);
        }
        else
        {
            Logger.Error($"No player found in group '{PlayerGroup}'. Creating new player instead.");
            SpawnNewPlayer(position);
        }
    }

    // Центрирование камеры на игроке
    private void CenterCameraOnPlayer()
    {
        if (_player == null)
            return;

        // Ищем камеру, которая может следить за игроком
        var cameras = GetTree().GetNodesInGroup("Camera");
        foreach (var cam in cameras)
        {
            if (cam is CameraController cameraController)
            {
                // Вызываем метод центрирования, если он есть
                cameraController.CenterOnPlayer();
                Logger.Debug("Camera centered on player", false);
                return;
            }
        }

        // Если специального контроллера камеры нет, пробуем найти любую камеру
        var camera = GetViewport().GetCamera2D();
        if (camera != null)
        {
            camera.Position = _player.Position;
            Logger.Debug("Default camera centered on player", false);
        }
    }
}