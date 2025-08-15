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

        _statusLabel.Text = $"Generating random level...";
        UpdateUI();

        try
        {
            var levelData = await GameLevelManager.Instance.GeneratePlanetLevelAsync(planetName, biomeType, difficulty);
            
            if (levelData != null && levelData.Width > 0)
            {
                _statusLabel.Text = $"Generated: {levelData.Width}x{levelData.Height} for {planetName}";
                Logger.Debug($"Level generated successfully for {planetName}: {levelData.Width}x{levelData.Height}", true);
            }
            else
            {
                _statusLabel.Text = "Generation failed";
                Logger.Error($"Failed to generate level for {planetName}");
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
            Logger.Error($"Exception during level generation: {ex.Message}");
        }

        UpdateUI();
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
            
            // Здесь можно добавить логику загрузки уровня
            // Пока просто показываем сообщение
            _statusLabel.Text = $"Loading level {levelData.Width}x{levelData.Height}...";
            
            // В будущем здесь будет переход на сцену с сгенерированным уровнем
            // GetTree().ChangeSceneToFile("res://scenes/generated_level.tscn");
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
}
