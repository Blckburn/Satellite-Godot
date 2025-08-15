using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// UI для управления генерацией уровней в игре
/// </summary>
public partial class LevelGenerationUI : Control
{
    // UI элементы
    private Label _serverStatusLabel;
    private Label _generatorInfoLabel;
    private Label _saveServerStatusLabel;
    private Button _startServerButton;
    private Button _stopServerButton;
    private OptionButton _difficultySelect;
    private Button _generateButton;
    private Button _loadLevelButton;
    private Label _statusLabel;
    private Button _closeButton;

    // Сложности
    private readonly string[] _difficulties = { "D", "C", "B", "A", "S", "S+" };

    public override void _Ready()
    {
        // Находим UI элементы
        _serverStatusLabel = GetNode<Label>("Panel/VBoxContainer/ServerSection/ServerStatusLabel");
        _generatorInfoLabel = GetNode<Label>("Panel/VBoxContainer/ServerSection/GeneratorInfoLabel");
        _saveServerStatusLabel = GetNode<Label>("Panel/VBoxContainer/ServerSection/SaveServerStatusLabel");
        _startServerButton = GetNode<Button>("Panel/VBoxContainer/ServerSection/ServerButtons/StartServerButton");
        _stopServerButton = GetNode<Button>("Panel/VBoxContainer/ServerSection/ServerButtons/StopServerButton");
        _difficultySelect = GetNode<OptionButton>("Panel/VBoxContainer/GenerationSection/DifficultySelect");
        _generateButton = GetNode<Button>("Panel/VBoxContainer/GenerationSection/GenerateButton");
        _loadLevelButton = GetNode<Button>("Panel/VBoxContainer/GenerationSection/LoadLevelButton");
        _statusLabel = GetNode<Label>("Panel/VBoxContainer/StatusSection/StatusLabel");
        _closeButton = GetNode<Button>("Panel/VBoxContainer/CloseButton");

        // Подключаем обработчики кнопок
        _startServerButton.Pressed += OnStartServerPressed;
        _stopServerButton.Pressed += OnStopServerPressed;
        _generateButton.Pressed += OnGeneratePressed;
        _loadLevelButton.Pressed += OnLoadLevelPressed;
        _closeButton.Pressed += OnClosePressed;

        // Подписываемся на события GameLevelManager
        if (GameLevelManager.Instance != null)
        {
            GameLevelManager.Instance.LevelGenerationStarted += OnGenerationStarted;
            GameLevelManager.Instance.LevelGenerationCompleted += OnGenerationCompleted;
            GameLevelManager.Instance.LevelGenerationFailed += OnGenerationFailed;
            GameLevelManager.Instance.ServerStatusChanged += OnServerStatusChanged;
        }

        // Подписываемся на события ServerSaveManager
        if (ServerSaveManager.Instance != null)
        {
            ServerSaveManager.Instance.ServerConnectionChanged += OnSaveServerConnectionChanged;
            ServerSaveManager.Instance.SaveCompleted += OnSaveCompleted;
            ServerSaveManager.Instance.LoadCompleted += OnLoadCompleted;
            ServerSaveManager.Instance.DataIntegrityViolation += OnDataIntegrityViolation;
        }

        // Инициализируем UI
        UpdateUI();
        
        // Скрываем UI по умолчанию
        Visible = false;
    }

    public override void _Process(double delta)
    {
        UpdateStatus();
    }

    /// <summary>
    /// Показывает UI
    /// </summary>
    public void ShowUI()
    {
        Visible = true;
        UpdateUI();
    }

    /// <summary>
    /// Скрывает UI
    /// </summary>
    public void HideUI()
    {
        Visible = false;
    }

    /// <summary>
    /// Обновляет UI элементы
    /// </summary>
    private void UpdateUI()
    {
        if (GameLevelManager.Instance == null) return;

        var isServerRunning = NetworkManager.Instance?.IsServerRunning ?? false;
        var isGenerating = GameLevelManager.Instance.IsGenerating;

        // Обновляем кнопки сервера
        _startServerButton.Disabled = isServerRunning;
        _stopServerButton.Disabled = !isServerRunning;

        // Обновляем кнопки
        _generateButton.Disabled = isGenerating;
        _loadLevelButton.Disabled = GameLevelManager.Instance.LastGeneratedLevel == null;

        // Обновляем статус сервера
        _serverStatusLabel.Text = GameLevelManager.Instance.GetServerStatus();
        _generatorInfoLabel.Text = $"Generator: {GameLevelManager.Instance.GetGeneratorInfo()}";
        
        // Обновляем статус сервера сохранений
        if (ServerSaveManager.Instance != null)
        {
            var saveStats = ServerSaveManager.Instance.GetSaveStats();
            var isConnected = (bool)saveStats["IsConnected"];
            var lastSave = (DateTime)saveStats["LastSaveTime"];
            var isProtected = (bool)saveStats["DataProtected"];
            
            _saveServerStatusLabel.Text = $"Save Server: {(isConnected ? "Connected" : "Disconnected")} | " +
                                         $"Protected: {(isProtected ? "Yes" : "No")} | " +
                                         $"Last Save: {lastSave:HH:mm:ss}";
        }
    }

    /// <summary>
    /// Обновляет статус
    /// </summary>
    private void UpdateStatus()
    {
        if (GameLevelManager.Instance == null) return;

        if (GameLevelManager.Instance.IsGenerating)
        {
            _statusLabel.Text = "Generating level...";
        }
        else if (GameLevelManager.Instance.LastGeneratedLevel != null)
        {
            var level = GameLevelManager.Instance.LastGeneratedLevel;
            _statusLabel.Text = $"Last generated: {level.Width}x{level.Height} (Biome: {level.BiomeType})";
        }
        else
        {
            _statusLabel.Text = "Ready";
        }
    }

    // Обработчики кнопок
    private void OnStartServerPressed()
    {
        if (GameLevelManager.Instance != null)
        {
            GameLevelManager.Instance.StartGenerationServer();
        }
        UpdateUI();
    }

    private void OnStopServerPressed()
    {
        if (GameLevelManager.Instance != null)
        {
            GameLevelManager.Instance.StopGenerationServer();
        }
        UpdateUI();
    }

    private async void OnGeneratePressed()
    {
        if (GameLevelManager.Instance == null) return;

        // Генерируем случайное название планеты
        var planetName = GenerateRandomPlanetName();
        
        // Генерируем случайный тип биома
        var biomeType = GD.RandRange(0, 5);
        
        var difficultyIndex = _difficultySelect.Selected;
        var difficulty = _difficulties[difficultyIndex];

        _statusLabel.Text = $"Generating random level... Please wait...";
        
        // Отключаем все кнопки во время генерации
        _generateButton.Disabled = true;
        _loadLevelButton.Disabled = true;
        _closeButton.Disabled = true;
        _startServerButton.Disabled = true;
        _stopServerButton.Disabled = true;
        UpdateUI();

        try
        {
            var levelData = await GameLevelManager.Instance.GeneratePlanetLevelAsync(planetName, biomeType, difficulty);
            
            if (levelData != null && levelData.Width > 0)
            {
                _statusLabel.Text = $"Generated: {levelData.Width}x{levelData.Height} for {planetName}";
                Logger.Debug($"Level generated successfully for {planetName}: {levelData.Width}x{levelData.Height}", true);
                
                // АВТОМАТИЧЕСКИЙ ПЕРЕХОД НА КАРТУ!
                Logger.Debug($"Auto-transitioning to generated level...", true);
                LoadGeneratedLevel(levelData);
            }
            else
            {
                _statusLabel.Text = "Generation failed";
                Logger.Error($"Failed to generate level for {planetName}");
                
                // Включаем кнопки обратно при ошибке
                _generateButton.Disabled = false;
                _closeButton.Disabled = false;
                _startServerButton.Disabled = false;
                _stopServerButton.Disabled = false;
                UpdateUI();
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            Logger.Error($"Exception during level generation: {ex.Message}");
            
            // Включаем кнопки обратно при ошибке
            _generateButton.Disabled = false;
            _closeButton.Disabled = false;
            _startServerButton.Disabled = false;
            _stopServerButton.Disabled = false;
            UpdateUI();
        }
    }

    /// <summary>
    /// Генерирует случайное название планеты
    /// </summary>
    private string GenerateRandomPlanetName()
    {
        var prefixes = new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa" };
        var suffixes = new[] { "Prime", "Minor", "Major", "Nova", "Centauri", "Orion", "Vega", "Sirius", "Polaris", "Andromeda" };
        
        var prefix = prefixes[GD.RandRange(0, prefixes.Length - 1)];
        var suffix = suffixes[GD.RandRange(0, suffixes.Length - 1)];
        
        return $"{prefix} {suffix}";
    }

    private void OnLoadLevelPressed()
    {
        if (GameLevelManager.Instance?.LastGeneratedLevel != null)
        {
            var levelData = GameLevelManager.Instance.LastGeneratedLevel;
            Logger.Debug($"Loading generated level: {levelData.Width}x{levelData.Height}", true);
            
            _statusLabel.Text = $"Loading level {levelData.Width}x{levelData.Height}...";
            
            // Переходим на сцену Main.tscn с сгенерированными данными
            LoadGeneratedLevel(levelData);
        }
    }

    /// <summary>
    /// Загружает сгенерированный уровень на сцену Main.tscn
    /// </summary>
    private void LoadGeneratedLevel(LevelData levelData)
    {
        try
        {
            Logger.Debug($"Starting transition to Main scene with generated level data", true);
            
            _statusLabel.Text = $"Transitioning to generated level...";
            
            // Сохраняем параметры генерации для Main сцены
            ProjectSettings.SetSetting("GeneratedLevelWidth", Variant.From(levelData.Width));
            ProjectSettings.SetSetting("GeneratedLevelHeight", Variant.From(levelData.Height));
            ProjectSettings.SetSetting("GeneratedLevelBiome", Variant.From(levelData.BiomeType));
            ProjectSettings.SetSetting("LoadGeneratedLevel", Variant.From(true));
            
            Logger.Debug($"Level parameters saved, transitioning to Main scene", true);
            
            // Переходим на сцену Main.tscn
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load generated level: {ex.Message}");
            _statusLabel.Text = $"Error loading level: {ex.Message}";
        }
    }

    private void OnClosePressed()
    {
        HideUI();
    }

    // Обработчики событий GameLevelManager
    private void OnGenerationStarted()
    {
        _statusLabel.Text = "Generation started...";
        UpdateUI();
    }

    private void OnGenerationCompleted()
    {
        if (GameLevelManager.Instance?.LastGeneratedLevel != null)
        {
            var levelData = GameLevelManager.Instance.LastGeneratedLevel;
            _statusLabel.Text = $"Generated: {levelData.Width}x{levelData.Height}";
        }
        else
        {
            _statusLabel.Text = "Generation completed";
        }
        UpdateUI();
    }

    private void OnGenerationFailed(string error)
    {
        _statusLabel.Text = $"Failed: {error}";
        UpdateUI();
    }

    private void OnServerStatusChanged(bool isRunning)
    {
        UpdateUI();
    }

    // Обработчики событий ServerSaveManager
    private void OnSaveServerConnectionChanged(bool connected)
    {
        Logger.Debug($"Save server connection changed: {connected}", true);
        UpdateUI();
    }

    private void OnSaveCompleted(bool success, string message)
    {
        Logger.Debug($"Save completed: {success} - {message}", true);
        UpdateUI();
    }

    private void OnLoadCompleted(bool success, string message)
    {
        Logger.Debug($"Load completed: {success} - {message}", true);
        UpdateUI();
    }

    private void OnDataIntegrityViolation(string details)
    {
        Logger.Error($"Data integrity violation detected: {details}");
        _statusLabel.Text = $"SECURITY ALERT: {details}";
        UpdateUI();
    }
}
