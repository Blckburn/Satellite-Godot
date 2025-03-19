using Godot;
using System;

/// <summary>
/// Стартовый модуль космической станции, включающий капсулу перерождения.
/// Является базовым модулем, который всегда присутствует на станции.
/// </summary>
public partial class StartingModule : BaseStationModule
{
    // Путь к капсуле перерождения
    [Export] public NodePath RespawnCapsulePath { get; set; }

    // Сигналы
    [Signal] public delegate void PlayerRespawnedEventHandler();

    // Компоненты
    private Node2D _respawnCapsule;
    private AnimationPlayer _animationPlayer;

    public override void _Ready()
    {
        // Устанавливаем свойства модуля
        ModuleName = "Starting Module";
        ModuleDescription = "The core module of your station with a respawn capsule.";
        CanBeRemoved = false; // Стартовый модуль нельзя удалить

        // Выполняем базовую инициализацию
        base._Ready();

        // Находим компоненты
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

        // Инициализация капсулы возрождения - важно найти её до вызова других методов!
        InitializeRespawnCapsule();

        Logger.Debug("Starting module initialized", true);

        // Отладка - можно включить для проверки проблемы
        // DebugFindRespawnCapsule();
    }

    /// <summary>
    /// Инициализирует капсулу возрождения, проверяя разные варианты её расположения
    /// </summary>
    private void InitializeRespawnCapsule()
    {
        // Проверяем путь, указанный в инспекторе
        if (!string.IsNullOrEmpty(RespawnCapsulePath))
        {
            _respawnCapsule = GetNodeOrNull<Node2D>(RespawnCapsulePath);
            if (_respawnCapsule != null)
            {
                Logger.Debug($"Found RespawnCapsule at path: {RespawnCapsulePath}", false);
                return;
            }
        }

        // Второй вариант: ищем дочерний узел с именем RespawnCapsule
        _respawnCapsule = FindChild("RespawnCapsule", false) as Node2D;
        if (_respawnCapsule != null)
        {
            Logger.Debug("Found RespawnCapsule as direct child", false);
            return;
        }

        // Третий вариант: ищем дочерний узел с именем RespawnCapsule2 (возможно был переименован)
        _respawnCapsule = FindChild("RespawnCapsule2", false) as Node2D;
        if (_respawnCapsule != null)
        {
            Logger.Debug("Found RespawnCapsule2 - will use it", false);
            return;
        }

        // Четвертый вариант: рекурсивный поиск по всей иерархии
        _respawnCapsule = FindChild("*Respawn*", true) as Node2D;
        if (_respawnCapsule != null)
        {
            Logger.Debug($"Found respawn capsule through wildcard search: {_respawnCapsule.Name}", false);
            return;
        }

        // Если капсулу не нашли, создаем новую
        Logger.Debug("RespawnCapsule not found, creating a new one", true);
        _respawnCapsule = new Node2D();
        _respawnCapsule.Name = "RespawnCapsule";
        _respawnCapsule.Position = new Vector2(0, 0); // Размещаем в центре модуля
        AddChild(_respawnCapsule);
    }

    public override void Initialize()
    {
        if (IsInitialized)
            return;

        // Специальная инициализация для стартового модуля

        base.Initialize();
    }

    public override void Activate()
    {
        if (IsActive)
            return;

        // Показываем UI для стартового модуля
        ShowModuleUI();

        base.Activate();

        // Проигрываем анимацию активации, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("activate"))
            _animationPlayer.Play("activate");
    }

    public override void Deactivate()
    {
        if (!IsActive)
            return;

        // Скрываем UI для стартового модуля
        HideModuleUI();

        base.Deactivate();

        // Проигрываем анимацию деактивации, если она есть
        if (_animationPlayer != null && _animationPlayer.HasAnimation("deactivate"))
            _animationPlayer.Play("deactivate");
    }

    public override string GetInteractionHint()
    {
        return "Press E to use Respawn Module";
    }

    /// <summary>
    /// Показывает пользовательский интерфейс модуля
    /// </summary>
    private void ShowModuleUI()
    {
        // Здесь будет код для отображения UI стартового модуля
        // Например, меню управления капсулой перерождения, улучшений и т.д.

        Logger.Debug("Starting module UI shown", false);
    }

    /// <summary>
    /// Скрывает пользовательский интерфейс модуля
    /// </summary>
    private void HideModuleUI()
    {
        // Здесь будет код для скрытия UI стартового модуля

        Logger.Debug("Starting module UI hidden", false);
    }

    /// <summary>
    /// Метод для возрождения игрока
    /// </summary>
    public void RespawnPlayer()
    {
        // Находим игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player)
        {
            // Логика возрождения игрока
            player.GlobalPosition = GlobalPosition; // устанавливаем позицию

            // Если у игрока есть метод Respawn, вызываем его
            if (player.HasMethod("Respawn"))
                player.Call("Respawn");

            // Проигрываем анимацию возрождения, если она есть
            if (_animationPlayer != null && _animationPlayer.HasAnimation("respawn"))
                _animationPlayer.Play("respawn");

            // Отправляем сигнал о возрождении игрока
            EmitSignal(SignalName.PlayerRespawned);

            Logger.Debug("Player respawned at starting module", false);
        }
        else
        {
            Logger.Error("Starting Module: Player not found for respawn");
        }
    }

    /// <summary>
    /// Устанавливает точку возрождения игрока
    /// </summary>
    public Vector2 GetRespawnPosition()
    {
        // Если есть точка возрождения, используем её позицию
        if (_respawnCapsule != null)
        {
            // Используем глобальную позицию, чтобы учесть позицию самого модуля
            return _respawnCapsule.GlobalPosition;
        }

        // Иначе используем позицию самого модуля
        return GlobalPosition;
    }
}