using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Главный класс, представляющий космическую станцию (Спутник) игрока.
/// Управляет всеми аспектами станции, включая модули и их взаимодействие.
/// </summary>
public partial class SpaceStation : Node2D
{
    // Сигналы
    [Signal] public delegate void StationModuleAddedEventHandler(BaseStationModule module);
    [Signal] public delegate void StationModuleRemovedEventHandler(BaseStationModule module);
    [Signal] public delegate void StationModuleActivatedEventHandler(BaseStationModule module);
    [Export] public PackedScene PlayerScene { get; set; }
    [Export] public NodePath PlayerSpawnPointPath { get; set; } = "PlayerSpawnPoint";
    private Node2D _playerSpawnPoint;

    // Ссылка на игрока
    [Export] public NodePath PlayerPath { get; set; }

    // Контейнер для всех модулей станции
    [Export] public NodePath ModulesContainerPath { get; set; } = "ModulesContainer";

    // Синглтон для удобного доступа из любого места
    public static SpaceStation Instance { get; private set; }

    // Список всех модулей станции
    private List<BaseStationModule> _modules = new List<BaseStationModule>();

    // Ссылки на узлы
    private Player _player;
    private Node2D _modulesContainer;

    // Текущий активный модуль
    private BaseStationModule _activeModule;

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
            Instance = this;
        else
            Logger.Debug("Multiple SpaceStation instances found!", true);

        // Добавляем в группу
        AddToGroup("SpaceStation");

        // Находим контейнер для модулей
        _modulesContainer = GetNodeOrNull<Node2D>(ModulesContainerPath);
        if (_modulesContainer == null)
        {
            Logger.Error("SpaceStation: ModulesContainer not found!");
            _modulesContainer = new Node2D
            {
                Name = "ModulesContainer"
            };
            AddChild(_modulesContainer);
        }

        // Находим игрока
        if (!string.IsNullOrEmpty(PlayerPath))
            _player = GetNode<Player>(PlayerPath);

        if (_player == null)
        {
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Player player)
                _player = player;
        }

        // Инициализируем существующие модули
        InitializeExistingModules();

        Logger.Debug("SpaceStation initialized", true);
    }


    public override void _EnterTree()
    {
        base._EnterTree();

        Logger.Debug("SpaceStation._EnterTree() called", true);

        // В любом случае, вызываем создание игрока с небольшой задержкой
        var timer = new Timer();
        timer.Name = "PlayerSpawnTimer";
        timer.OneShot = true;
        timer.WaitTime = 0.1f; 

        // Используем лямбда-функцию для callback таймера
        timer.Timeout += () => {
            Logger.Debug("PlayerSpawnTimer timeout triggered", true);

            // Проверяем, нужно ли создать игрока
            bool createPlayer = false;

            // Проверка флага
            if (ProjectSettings.HasSetting("CreatePlayerOnLoad"))
            {
                bool flagValue = (bool)ProjectSettings.GetSetting("CreatePlayerOnLoad");
                Logger.Debug($"CreatePlayerOnLoad flag is: {flagValue}", true);

                if (flagValue)
                {
                    // Сбрасываем флаг
                    ProjectSettings.SetSetting("CreatePlayerOnLoad", false);
                    createPlayer = true;
                }
            }
            else
            {
                Logger.Debug("CreatePlayerOnLoad flag not found", true);
            }

            // Проверка наличия игрока
            var players = GetTree().GetNodesInGroup("Player");
            Logger.Debug($"Players in 'Player' group: {players.Count}", true);

            if (players.Count == 0)
            {
                // Если игрока нет вообще, создаем его в любом случае
                createPlayer = true;
                Logger.Debug("No player found in scene, will create one", true);
            }

            if (createPlayer)
            {
                Logger.Debug("Calling SpawnPlayer()", true);
                SpawnPlayer();
            }
            else
            {
                Logger.Debug("Not creating player", true);
            }

            // Удаляем таймер
            timer.QueueFree();
            Logger.Debug("PlayerSpawnTimer removed", true);
        };

        AddChild(timer);
        timer.Start();

        Logger.Debug("PlayerSpawnTimer started with timeout: " + timer.WaitTime + "s", true);
    }

    // Добавьте этот метод в класс SpaceStation
    private void SpawnPlayer()
    {
        Logger.Debug("SpawnPlayer() method started", true);

        // Объявляем переменные в начале метода
        StartingModule startingModule = null;
        Vector2 spawnPosition = Vector2.Zero;
        bool foundSpawnPoint = false;

        // Проверяем, должен ли игрок появиться у телепортера
        bool spawnAtTeleporter = false;
        if (ProjectSettings.HasSetting("SpawnAtTeleporter") &&
            (bool)ProjectSettings.GetSetting("SpawnAtTeleporter"))
        {
            spawnAtTeleporter = true;
            // Сбрасываем флаг, чтобы не использовать его снова
            ProjectSettings.SetSetting("SpawnAtTeleporter", false);
            Logger.Debug("Player should spawn at teleporter", true);
        }

        // Если игрок должен появиться у телепортера
        if (spawnAtTeleporter)
        {
            // Ищем StationTeleporter в сцене
            var teleporter = FindChild("StationTeleporter", true, false) as StationTeleporter;
            if (teleporter != null)
            {
                // Используем метод GetSpawnPosition для получения точки возрождения
                spawnPosition = teleporter.GetSpawnPosition();
                foundSpawnPoint = true;
                Logger.Debug($"Using teleporter spawn position: {spawnPosition}", true);
            }
            else
            {
                Logger.Debug("StationTeleporter not found, searching for generic teleporter", true);

                // Резервный вариант - ищем любой узел с именем StationTeleporter
                var genericTeleporter = FindChild("StationTeleporter", true, false) as Node2D;
                if (genericTeleporter != null)
                {
                    spawnPosition = genericTeleporter.GlobalPosition;
                    foundSpawnPoint = true;
                    Logger.Debug($"Using generic teleporter position: {spawnPosition}", true);
                }
                else
                {
                    Logger.Debug("Generic teleporter not found, searching teleportation module", true);

                    // Запасной вариант - ищем телепортационный модуль
                    foreach (var module in _modules)
                    {
                        if (module is TeleportationModule)
                        {
                            spawnPosition = module.GlobalPosition;
                            foundSpawnPoint = true;
                            Logger.Debug($"Using teleportation module position for spawn: {spawnPosition}", true);
                            break;
                        }
                    }
                }
            }
        }

        // Если мы не нашли телепортер или игрок не должен там появляться, 
        // используем стартовый модуль
        if (!foundSpawnPoint)
        {
            // Ищем StartingModule в модулях станции
            Logger.Debug($"Modules count: {_modules.Count}", true);
            foreach (var module in _modules)
            {
                Logger.Debug($"Checking module: {module.Name} (Type: {module.GetType()})", true);
                if (module is StartingModule sm)
                {
                    startingModule = sm;
                    Logger.Debug("Found StartingModule for player spawn", true);
                    break;
                }
            }

            // Если нашли стартовый модуль, используем его для спавна
            if (startingModule != null)
            {
                // Получаем позицию для спавна от модуля
                spawnPosition = startingModule.GetRespawnPosition();
                foundSpawnPoint = true;
                Logger.Debug($"Using StartingModule respawn position: {spawnPosition}", true);
            }
            else
            {
                Logger.Debug("StartingModule was not found, trying to use PlayerSpawnPoint", true);

                // Резервный вариант - ищем точку спавна
                if (_playerSpawnPoint == null && !string.IsNullOrEmpty(PlayerSpawnPointPath))
                {
                    _playerSpawnPoint = GetNodeOrNull<Node2D>(PlayerSpawnPointPath);
                    if (_playerSpawnPoint != null)
                    {
                        Logger.Debug($"Found PlayerSpawnPoint via path: {PlayerSpawnPointPath}", true);
                        foundSpawnPoint = true;
                    }
                }

                if (_playerSpawnPoint == null)
                {
                    // Ищем точку спавна в дереве сцены
                    _playerSpawnPoint = FindChild("PlayerSpawnPoint", true, false) as Node2D;

                    if (_playerSpawnPoint != null)
                    {
                        Logger.Debug("Found PlayerSpawnPoint via FindChild", true);
                        foundSpawnPoint = true;
                    }
                    else
                    {
                        Logger.Error("No spawn point found in the station scene");
                        return;
                    }
                }

                // Используем позицию точки спавна
                if (foundSpawnPoint && _playerSpawnPoint != null)
                {
                    spawnPosition = _playerSpawnPoint.Position;
                    Logger.Debug($"Using PlayerSpawnPoint position: {spawnPosition}", true);
                }
            }
        }

        // Если мы все еще не нашли точку спавна, используем (0,0)
        if (!foundSpawnPoint)
        {
            spawnPosition = Vector2.Zero;
            Logger.Debug("No spawn point found, using (0,0)", true);
        }

        // Загружаем префаб игрока, если не задан
        if (PlayerScene == null)
        {
            Logger.Debug("PlayerScene is null, trying to load from resource", true);

            string playerScenePath = "res://scenes/player/Player.tscn";
            Logger.Debug($"Loading PlayerScene from: {playerScenePath}", true);

            try
            {
                PlayerScene = ResourceLoader.Load<PackedScene>(playerScenePath);
                if (PlayerScene == null)
                {
                    Logger.Error($"Failed to load player scene from path: {playerScenePath}");
                    return;
                }
                Logger.Debug("Successfully loaded PlayerScene resource", true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception loading player scene: {ex.Message}");
                return;
            }
        }

        // Создаем экземпляр игрока
        Logger.Debug("Instantiating player", true);
        Node2D playerNode = null;
        try
        {
            playerNode = PlayerScene.Instantiate<Node2D>();
            if (playerNode == null)
            {
                Logger.Error("Failed to instantiate player - result is null");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception instantiating player: {ex.Message}");
            return;
        }

        // Устанавливаем позицию
        playerNode.Position = spawnPosition;

        // Добавляем игрока в сцену
        Logger.Debug("Adding player to scene", true);
        try
        {
            AddChild(playerNode);

            // Добавляем игрока в группу Player, если он там еще не находится
            if (!playerNode.IsInGroup("Player"))
            {
                playerNode.AddToGroup("Player");
                Logger.Debug("Added player to 'Player' group", true);
            }

            Logger.Debug($"Player spawned at position {playerNode.Position}", true);

            // Если игрок создан успешно и реализует класс Player,
            // принудительно загружаем инвентарь
            if (playerNode is Player player)
            {
                // Этот вызов произойдет после _Ready(), поэтому мы повторно загружаем инвентарь
                try
                {
                    Logger.Debug("Ensuring player inventory is loaded", true);
                    bool loaded = player.LoadInventory();
                    Logger.Debug($"Force load inventory result: {loaded}", true);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Exception loading player inventory: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception adding player to scene: {ex.Message}");
            return;
        }

        // Если есть стартовый модуль и игрок не появляется у телепортера, вызываем эффект возрождения
        if (startingModule != null && !spawnAtTeleporter)
        {
            try
            {
                // Имитация возрождения - можно добавить анимацию или эффекты
                startingModule.RespawnPlayer();
                Logger.Debug("Called startingModule.RespawnPlayer()", true);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Exception in RespawnPlayer: {ex.Message}", true);
            }
        }
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Инициализирует существующие модули, добавленные через редактор
    /// </summary>
    private void InitializeExistingModules()
    {
        if (_modulesContainer == null)
            return;

        foreach (var child in _modulesContainer.GetChildren())
        {
            if (child is BaseStationModule module)
            {
                RegisterModule(module);
                Logger.Debug($"Found existing module: {module.Name}", false);
            }
        }
    }

    /// <summary>
    /// Добавляет новый модуль на станцию
    /// </summary>
    public void AddModule(BaseStationModule module, Vector2 position)
    {
        if (module == null || _modulesContainer == null)
            return;

        // Устанавливаем позицию модуля
        module.Position = position;

        // Добавляем модуль в контейнер
        _modulesContainer.AddChild(module);

        // Регистрируем модуль
        RegisterModule(module);

        Logger.Debug($"Added new module: {module.Name} at position {position}", false);
    }

    /// <summary>
    /// Регистрирует существующий модуль в системе
    /// </summary>
    private void RegisterModule(BaseStationModule module)
    {
        if (module == null || _modules.Contains(module))
            return;

        // Добавляем в список модулей
        _modules.Add(module);

        // Подключаем сигналы модуля
        module.Connect(BaseStationModule.SignalName.ModuleActivated,
            Callable.From<BaseStationModule>(OnModuleActivated));

        // Инициализируем модуль, если он еще не инициализирован
        if (!module.IsInitialized)
            module.Initialize();

        // Отправляем сигнал о добавлении модуля
        EmitSignal(SignalName.StationModuleAdded, module);
    }

    /// <summary>
    /// Удаляет модуль со станции
    /// </summary>
    public bool RemoveModule(BaseStationModule module)
    {
        if (module == null || !_modules.Contains(module))
            return false;

        // Удаляем из списка модулей
        _modules.Remove(module);

        // Отключаем сигналы модуля
        if (module.IsConnected(BaseStationModule.SignalName.ModuleActivated,
            Callable.From<BaseStationModule>(OnModuleActivated)))
        {
            module.Disconnect(BaseStationModule.SignalName.ModuleActivated,
                Callable.From<BaseStationModule>(OnModuleActivated));
        }

        // Отправляем сигнал об удалении модуля
        EmitSignal(SignalName.StationModuleRemoved, module);

        // Удаляем модуль из дерева сцены
        module.QueueFree();

        Logger.Debug($"Removed module: {module.Name}", false);

        return true;
    }

    /// <summary>
    /// Обработчик активации модуля
    /// </summary>
    private void OnModuleActivated(BaseStationModule module)
    {
        if (module == null)
            return;

        // Деактивируем текущий активный модуль, если он есть
        if (_activeModule != null && _activeModule != module)
            _activeModule.Deactivate();

        // Устанавливаем новый активный модуль
        _activeModule = module;

        // Отправляем сигнал об активации модуля
        EmitSignal(SignalName.StationModuleActivated, module);

        Logger.Debug($"Module activated: {module.Name}", false);
    }

    /// <summary>
    /// Получает модуль по имени
    /// </summary>
    public BaseStationModule GetModuleByName(string name)
    {
        return _modules.Find(m => m.Name == name);
    }

    /// <summary>
    /// Получает модуль по типу
    /// </summary>
    public T GetModuleByType<T>() where T : BaseStationModule
    {
        return _modules.Find(m => m is T) as T;
    }

    /// <summary>
    /// Получает все модули станции
    /// </summary>
    public List<BaseStationModule> GetAllModules()
    {
        return new List<BaseStationModule>(_modules);
    }

    /// <summary>
    /// Проверяет, есть ли модуль с указанным именем
    /// </summary>
    public bool HasModule(string name)
    {
        return _modules.Exists(m => m.Name == name);
    }

    /// <summary>
    /// Проверяет, есть ли модуль указанного типа
    /// </summary>
    public bool HasModuleOfType<T>() where T : BaseStationModule
    {
        return _modules.Exists(m => m is T);
    }
}