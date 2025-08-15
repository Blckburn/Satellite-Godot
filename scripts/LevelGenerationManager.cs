using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Менеджер генерации уровней
/// Управляет выбором между клиентским и серверным генератором
/// </summary>
public partial class LevelGenerationManager : Node
{
    // Синглтон для удобного доступа
    public static LevelGenerationManager Instance { get; private set; }

    // Настройки
    [Export] public bool PreferServerGeneration { get; set; } = true;
    [Export] public bool FallbackToClient { get; set; } = true;
    [Export] public float ServerTimeout { get; set; } = 10.0f; // секунды

    // Генераторы
    private ILevelGenerator _clientGenerator;
    private ILevelGenerator _serverGenerator;
    private ILevelGenerator _currentGenerator;

    // Состояние
    public bool IsServerAvailable { get; private set; } = false;
    public bool IsClientAvailable { get; private set; } = false;
    public string CurrentGeneratorInfo { get; private set; } = "None";

    // События
    [Signal] public delegate void GeneratorChangedEventHandler(string generatorInfo);
    [Signal] public delegate void GenerationStartedEventHandler();
    [Signal] public delegate void GenerationCompletedEventHandler();
    [Signal] public delegate void GenerationFailedEventHandler(string error);

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always;
        }
        else
        {
            QueueFree();
            return;
        }

        InitializeGenerators();
        UpdateGeneratorStatus();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Инициализация генераторов
    /// </summary>
    private void InitializeGenerators()
    {
        // Инициализируем клиентский генератор
        var levelGenerator = GetNode<LevelGenerator>("/root/LevelGenerator");
        if (levelGenerator != null)
        {
            _clientGenerator = new ClientLevelGenerator(levelGenerator);
            IsClientAvailable = _clientGenerator.IsAvailable();
        }

        // Инициализируем серверный генератор
        var networkManager = GetNode<NetworkManager>("/root/NetworkManager");
        if (networkManager != null)
        {
            _serverGenerator = new ServerLevelGenerator(networkManager);
            
            // Подписываемся на события сети
            networkManager.Connected += OnServerConnected;
            networkManager.Disconnected += OnServerDisconnected;
            
            IsServerAvailable = _serverGenerator.IsAvailable();
        }

        // Выбираем начальный генератор
        SelectGenerator();
    }

    /// <summary>
    /// Выбор генератора на основе настроек и доступности
    /// </summary>
    private void SelectGenerator()
    {
        ILevelGenerator newGenerator = null;

        if (PreferServerGeneration && IsServerAvailable)
        {
            newGenerator = _serverGenerator;
        }
        else if (IsClientAvailable)
        {
            newGenerator = _clientGenerator;
        }
        else if (IsServerAvailable)
        {
            newGenerator = _serverGenerator;
        }

        if (newGenerator != _currentGenerator)
        {
            _currentGenerator = newGenerator;
            CurrentGeneratorInfo = _currentGenerator?.GetGeneratorInfo() ?? "None";
            
            GD.Print($"Selected generator: {CurrentGeneratorInfo}");
            EmitSignal(SignalName.GeneratorChanged, CurrentGeneratorInfo);
        }
    }

    /// <summary>
    /// Обновление статуса генераторов
    /// </summary>
    private void UpdateGeneratorStatus()
    {
        IsClientAvailable = _clientGenerator?.IsAvailable() ?? false;
        IsServerAvailable = _serverGenerator?.IsAvailable() ?? false;
        
        SelectGenerator();
    }

    /// <summary>
    /// Генерация уровня с автоматическим выбором генератора
    /// </summary>
    public async Task<LevelData> GenerateLevelAsync(GenerationParameters parameters)
    {
        if (_currentGenerator == null)
        {
            var error = "No level generator available";
            GD.PrintErr(error);
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }

        EmitSignal(SignalName.GenerationStarted);

        try
        {
            GD.Print($"Generating level with: {CurrentGeneratorInfo}");
            
            var levelData = await _currentGenerator.GenerateLevelAsync(parameters);
            
            if (levelData != null && levelData.Width > 0 && levelData.Height > 0)
            {
                GD.Print($"Level generated successfully: {levelData.Width}x{levelData.Height}");
                EmitSignal(SignalName.GenerationCompleted);
                return levelData;
            }
            else
            {
                throw new Exception("Generated level data is invalid");
            }
        }
        catch (Exception ex)
        {
            var error = $"Generation failed: {ex.Message}";
            GD.PrintErr(error);
            
            // Попытка fallback на клиентский генератор
            if (FallbackToClient && _currentGenerator == _serverGenerator && IsClientAvailable)
            {
                GD.Print("Attempting fallback to client generator...");
                _currentGenerator = _clientGenerator;
                CurrentGeneratorInfo = _currentGenerator.GetGeneratorInfo();
                EmitSignal(SignalName.GeneratorChanged, CurrentGeneratorInfo);
                
                try
                {
                    var fallbackData = await _currentGenerator.GenerateLevelAsync(parameters);
                    if (fallbackData != null && fallbackData.Width > 0 && fallbackData.Height > 0)
                    {
                        GD.Print("Fallback generation successful");
                        EmitSignal(SignalName.GenerationCompleted);
                        return fallbackData;
                    }
                }
                catch (Exception fallbackEx)
                {
                    error = $"Both server and client generation failed. Server: {ex.Message}, Client: {fallbackEx.Message}";
                }
            }
            
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }
    }

    /// <summary>
    /// Принудительная генерация через клиентский генератор
    /// </summary>
    public async Task<LevelData> GenerateLevelClientAsync(GenerationParameters parameters)
    {
        if (!IsClientAvailable)
        {
            var error = "Client generator not available";
            GD.PrintErr(error);
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }

        EmitSignal(SignalName.GenerationStarted);
        
        try
        {
            var levelData = await _clientGenerator.GenerateLevelAsync(parameters);
            EmitSignal(SignalName.GenerationCompleted);
            return levelData;
        }
        catch (Exception ex)
        {
            var error = $"Client generation failed: {ex.Message}";
            GD.PrintErr(error);
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }
    }

    /// <summary>
    /// Принудительная генерация через серверный генератор
    /// </summary>
    public async Task<LevelData> GenerateLevelServerAsync(GenerationParameters parameters)
    {
        if (!IsServerAvailable)
        {
            var error = "Server generator not available";
            GD.PrintErr(error);
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }

        EmitSignal(SignalName.GenerationStarted);
        
        try
        {
            var levelData = await _serverGenerator.GenerateLevelAsync(parameters);
            EmitSignal(SignalName.GenerationCompleted);
            return levelData;
        }
        catch (Exception ex)
        {
            var error = $"Server generation failed: {ex.Message}";
            GD.PrintErr(error);
            EmitSignal(SignalName.GenerationFailed, error);
            return new LevelData(); // Возвращаем пустой объект вместо null
        }
    }

    /// <summary>
    /// Получение информации о доступных генераторах
    /// </summary>
    public string GetStatusInfo()
    {
        return $"Server: {(IsServerAvailable ? "Available" : "Unavailable")}, " +
               $"Client: {(IsClientAvailable ? "Available" : "Unavailable")}, " +
               $"Current: {CurrentGeneratorInfo}";
    }

    // Обработчики сетевых событий
    private void OnServerConnected()
    {
        UpdateGeneratorStatus();
    }

    private void OnServerDisconnected()
    {
        UpdateGeneratorStatus();
    }
}
