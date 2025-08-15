using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Класс для управления главным меню игры
/// </summary>
public partial class MainMenu : Control
{
    // Пути к узлам кнопок
    [Export] public NodePath ContinueButtonPath { get; set; } = "%ContinueButton";
    [Export] public NodePath NewGameButtonPath { get; set; } = "%NewGameButton";
    [Export] public NodePath SettingsButtonPath { get; set; } = "%SettingsButton";
    [Export] public NodePath ServerButtonPath { get; set; } = "%ServerButton";
    [Export] public NodePath ExitButtonPath { get; set; } = "%ExitButton";

    // Путь к первой сцене игры
    [Export] public string FirstScenePath { get; set; } = "res://scenes/station/space_station.tscn";

    // Ссылки на кнопки
    private Button _continueButton;
    private Button _newGameButton;
    private Button _settingsButton;
    private Button _serverButton;
    private Button _exitButton;

    // Анимация (опционально)
    [Export] public NodePath AnimationPlayerPath { get; set; } = "AnimationPlayer";
    private AnimationPlayer _animationPlayer;

    // Панели
    [Export] public NodePath SettingsPanelPath { get; set; } = "%SettingsPanel";
    [Export] public NodePath ServerPanelPath { get; set; } = "%ServerPanel";
    private Panel _settingsPanel;
    private Panel _serverPanel;

    public override void _Ready()
    {
        // Инициализация компонентов UI
        InitializeUI();

        // Настройка кнопок и их состояний
        SetupButtons();

        // Обновить состояние кнопки "Продолжить" на основе наличия сохранения
        UpdateContinueButtonState();

        // Подписываемся на события ServerSaveManager для обновления кнопки
        if (ServerSaveManager.Instance != null)
        {
            ServerSaveManager.Instance.ServerConnectionChanged += OnServerConnectionChanged;
        }

        // Проигрываем анимацию при запуске, если она есть
        PlayStartAnimation();

        Logger.Debug("Main menu initialized", true);
        
        // ЗАПУСКАЕМ LoadingScreen КАК ОТДЕЛЬНУЮ СЦЕНУ!
        Logger.Debug("Starting loading screen...", true);
        GetTree().ChangeSceneToFile("res://scenes/loading/loading_screen.tscn");
    }

    /// <summary>
    /// Инициализирует компоненты пользовательского интерфейса
    /// </summary>
    private void InitializeUI()
    {
        // Получаем ссылки на кнопки
        _continueButton = GetNode<Button>(ContinueButtonPath);
        _newGameButton = GetNode<Button>(NewGameButtonPath);
        _settingsButton = GetNode<Button>(SettingsButtonPath);
        _serverButton = GetNode<Button>(ServerButtonPath);
        _exitButton = GetNode<Button>(ExitButtonPath);

        // Получаем ссылки на панели
        _settingsPanel = GetNodeOrNull<Panel>(SettingsPanelPath);
        if (_settingsPanel != null)
        {
            _settingsPanel.Visible = false;
        }

        _serverPanel = GetNodeOrNull<Panel>(ServerPanelPath);
        if (_serverPanel != null)
        {
            _serverPanel.Visible = false;
        }

        // Получаем ссылку на проигрыватель анимаций
        _animationPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
    }

    /// <summary>
    /// Настраивает обработчики событий для кнопок
    /// </summary>
    private void SetupButtons()
    {
        // Подключаем обработчики нажатия кнопок
        if (_continueButton != null)
        {
            _continueButton.Pressed += OnContinueButtonPressed;
        }

        if (_newGameButton != null)
        {
            _newGameButton.Pressed += OnNewGameButtonPressed;
        }

        if (_settingsButton != null)
        {
            _settingsButton.Pressed += OnSettingsButtonPressed;
        }

        if (_serverButton != null)
        {
            _serverButton.Pressed += OnServerButtonPressed;
        }

        if (_exitButton != null)
        {
            _exitButton.Pressed += OnExitButtonPressed;
        }
    }

    /// <summary>
    /// Обновляет состояние кнопки "Продолжить" в зависимости от наличия сохранения
    /// </summary>
    private void UpdateContinueButtonState()
    {
        if (_continueButton != null)
        {
            // Проверяем наличие сохранения через ServerSaveManager
            bool saveExists = false;
            
            if (ServerSaveManager.Instance != null)
            {
                // Если подключены к серверу, считаем что сохранение есть
                saveExists = ServerSaveManager.Instance.IsConnectedToServer;
            }
            else
            {
                // Fallback через GameManager
                var gameManager = GetNode<GameManager>("/root/GameManager");
                saveExists = gameManager != null && gameManager.SaveExists();
            }

            // Включаем/выключаем кнопку в зависимости от наличия сохранения
            _continueButton.Disabled = !saveExists;

            Logger.Debug($"Continue button state updated: {(saveExists ? "enabled" : "disabled")}", false);
        }
    }

    /// <summary>
    /// Проигрывает стартовую анимацию
    /// </summary>
    private void PlayStartAnimation()
    {
        if (_animationPlayer != null && _animationPlayer.HasAnimation("menu_start"))
        {
            _animationPlayer.Play("menu_start");
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Продолжить"
    /// </summary>
    private async void OnContinueButtonPressed()
    {
        Logger.Debug("Continue button pressed", true);

        // Загружаем сохраненную игру через ServerSaveManager
        var gameManager = GetNode<GameManager>("/root/GameManager");

        if (gameManager != null && ServerSaveManager.Instance != null)
        {
            // Используем новую серверную систему сохранений
            bool loadSuccess = await gameManager.LoadGame();

            if (loadSuccess)
            {
                // Успешная загрузка игры
                Logger.Debug("Game loaded successfully from server", true);

                // Переходим к сцене после успешной загрузки
                GetTree().ChangeSceneToFile(FirstScenePath);
            }
            else
            {
                // Ошибка загрузки - переходим к первой сцене по умолчанию
                Logger.Error("Failed to load game from server, starting new game instead");
                GetTree().ChangeSceneToFile(FirstScenePath);
            }
        }
        else
        {
            Logger.Error("GameManager or ServerSaveManager not found");
            GetTree().ChangeSceneToFile(FirstScenePath);
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Новая игра"
    /// </summary>
    private async void OnNewGameButtonPressed()
    {
        Logger.Debug("New game button pressed", true);

        // Инициализируем новую игру, создавая чистое сохранение
        InitializeNewGame();

        // Переходим к первой сцене
        GetTree().ChangeSceneToFile(FirstScenePath);
    }

    /// <summary>
    /// Инициализирует новую игру, создавая базовое сохранение
    /// </summary>
    private async void InitializeNewGame()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
            return;

        // Сбрасываем данные GameManager для новой игры
        gameManager.ClearData();

        // Создаем начальный инвентарь (если нужно)
        Dictionary<string, object> initialInventory = CreateInitialInventory();
        gameManager.SetData("PlayerInventorySaved", initialInventory);

        // Начальная позиция и сцена
        gameManager.SetData("LastWorldPosition", Vector2.Zero);
        gameManager.SetData("CurrentScene", FirstScenePath);

        // Начальное здоровье
        gameManager.SetData("PlayerHealth", 100f);
        gameManager.SetData("PlayerMaxHealth", 100f);

        // Сбрасываем время игры
        gameManager.SetData("PlayTime", 0f);

        // Другие начальные данные
        gameManager.SetData("UnlockedModules", new List<string>());
        gameManager.SetData("CompletedMissions", new List<string>());
        gameManager.SetData("DiscoveredPlanets", new List<string>());
        gameManager.SetData("VisitedLocations", new List<string>());

        // Создаем чистое сохранение с начальными значениями через ServerSaveManager
        bool saved = await gameManager.SaveGame();
        Logger.Debug($"New game initialized and {(saved ? "saved successfully to server" : "save failed")}", true);
    }

    /// <summary>
    /// Создает начальный инвентарь для новой игры
    /// </summary>
    private Dictionary<string, object> CreateInitialInventory()
    {
        // Создаем базовую структуру инвентаря
        Dictionary<string, object> inventory = new Dictionary<string, object>
        {
            ["max_slots"] = 20,
            ["max_weight"] = 0f,
            ["items"] = new List<Dictionary<string, object>>()
        };

        return inventory;
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Настройки"
    /// </summary>
    private void OnSettingsButtonPressed()
    {
        Logger.Debug("Settings button pressed", true);

        // В будущем здесь будет открытие меню настроек
        if (_settingsPanel != null)
        {
            _settingsPanel.Visible = true;
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Сервер"
    /// </summary>
    private void OnServerButtonPressed()
    {
        Logger.Debug("Server button pressed", true);

        // Открываем панель управления сервером
        if (_serverPanel != null)
        {
            _serverPanel.Visible = true;
            InitializeServerPanel();
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Выход"
    /// </summary>
    private void OnExitButtonPressed()
    {
        Logger.Debug("Exit button pressed", true);

        // Сохраняем игру перед выходом
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            gameManager.SaveGame();
        }

        // Выходим из игры
        GetTree().Quit();
    }

    /// <summary>
    /// Инициализирует панель управления сервером
    /// </summary>
    private void InitializeServerPanel()
    {
        if (_serverPanel == null) return;

        // Находим элементы управления сервером
        var startServerButton = _serverPanel.GetNodeOrNull<Button>("VBoxContainer/ServerSection/ServerButtons/StartServerButton");
        var stopServerButton = _serverPanel.GetNodeOrNull<Button>("VBoxContainer/ServerSection/ServerButtons/StopServerButton");
        var testGenerationButton = _serverPanel.GetNodeOrNull<Button>("VBoxContainer/TestSection/TestGenerationButton");
        var backToMenuButton = _serverPanel.GetNodeOrNull<Button>("VBoxContainer/BackSection/BackToMenuButton");
        var statusLabel = _serverPanel.GetNodeOrNull<Label>("VBoxContainer/StatusSection/StatusLabel");
        var generatorInfoLabel = _serverPanel.GetNodeOrNull<Label>("VBoxContainer/StatusSection/GeneratorInfoLabel");

        // Подключаем обработчики кнопок
        if (startServerButton != null)
            startServerButton.Pressed += OnStartServerPressed;
        
        if (stopServerButton != null)
            stopServerButton.Pressed += OnStopServerPressed;
        
        if (testGenerationButton != null)
            testGenerationButton.Pressed += OnTestGenerationPressed;
        
        if (backToMenuButton != null)
            backToMenuButton.Pressed += OnBackToMenuPressed;

        // Подписываемся на события NetworkManager и LevelGenerationManager
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.ServerStarted += OnServerStarted;
            NetworkManager.Instance.ServerStopped += OnServerStopped;
        }

        if (LevelGenerationManager.Instance != null)
        {
            LevelGenerationManager.Instance.GeneratorChanged += OnGeneratorChanged;
        }

        // Обновляем статус
        UpdateServerStatus(statusLabel, generatorInfoLabel);
    }

    /// <summary>
    /// Обновляет статус сервера в UI
    /// </summary>
    private void UpdateServerStatus(Label statusLabel, Label generatorInfoLabel)
    {
        if (statusLabel != null)
        {
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsServerRunning)
            {
                statusLabel.Text = $"Server Status: Running (Port: {NetworkManager.Instance.ServerPort})";
            }
            else
            {
                statusLabel.Text = "Server Status: Not Running";
            }
        }

        if (generatorInfoLabel != null)
        {
            if (LevelGenerationManager.Instance != null)
            {
                generatorInfoLabel.Text = $"Generator: {LevelGenerationManager.Instance.CurrentGeneratorInfo}";
            }
            else
            {
                generatorInfoLabel.Text = "Generator: Not Available";
            }
        }
    }

    // Обработчики серверных событий
    private void OnStartServerPressed()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartServer();
        }
    }

    private void OnStopServerPressed()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StopServer();
        }
    }

    private async void OnTestGenerationPressed()
    {
        if (LevelGenerationManager.Instance == null) return;

        var parameters = new GenerationParameters
        {
            BiomeType = 0,
            MapWidth = 50,
            MapHeight = 50,
            Seed = (int)GD.Randi(),
            MaxRooms = 10,
            MinRoomSize = 5,
            MaxRoomSize = 12
        };

        var levelData = await LevelGenerationManager.Instance.GenerateLevelAsync(parameters);
        
        if (levelData != null && levelData.Width > 0)
        {
            Logger.Debug($"Test generation successful: {levelData.Width}x{levelData.Height}", true);
        }
        else
        {
            Logger.Error("Test generation failed");
        }
    }

    private void OnBackToMenuPressed()
    {
        if (_serverPanel != null)
        {
            _serverPanel.Visible = false;
        }
    }

    private void OnServerStarted()
    {
        var statusLabel = _serverPanel?.GetNodeOrNull<Label>("VBoxContainer/StatusSection/StatusLabel");
        var generatorInfoLabel = _serverPanel?.GetNodeOrNull<Label>("VBoxContainer/StatusSection/GeneratorInfoLabel");
        UpdateServerStatus(statusLabel, generatorInfoLabel);
    }

    private void OnServerStopped()
    {
        var statusLabel = _serverPanel?.GetNodeOrNull<Label>("VBoxContainer/StatusSection/StatusLabel");
        var generatorInfoLabel = _serverPanel?.GetNodeOrNull<Label>("VBoxContainer/StatusSection/GeneratorInfoLabel");
        UpdateServerStatus(statusLabel, generatorInfoLabel);
    }

    private void OnGeneratorChanged(string generatorInfo)
    {
        var generatorInfoLabel = _serverPanel?.GetNodeOrNull<Label>("VBoxContainer/StatusSection/GeneratorInfoLabel");
        if (generatorInfoLabel != null)
        {
            generatorInfoLabel.Text = $"Generator: {generatorInfo}";
        }
    }

    /// <summary>
    /// Обработчик изменения состояния подключения к серверу
    /// </summary>
    private void OnServerConnectionChanged(bool connected)
    {
        Logger.Debug($"Server connection changed: {connected}", true);
        
        // Обновляем состояние кнопки Continue
        UpdateContinueButtonState();
    }
}