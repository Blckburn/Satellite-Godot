using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Менеджер генерации уровней для игры
/// Интегрирует клиент-серверную архитектуру в основную игру
/// </summary>
public partial class GameLevelManager : Node
{
    // Синглтон для удобного доступа
    public static GameLevelManager Instance { get; private set; }

    // Настройки генерации
    [Export] public bool UseServerGeneration { get; set; } = true;
    [Export] public bool AutoStartServer { get; set; } = false;
    [Export] public int DefaultMapWidth { get; set; } = 100;
    [Export] public int DefaultMapHeight { get; set; } = 100;
    [Export] public int DefaultMaxRooms { get; set; } = 15;
    [Export] public int DefaultMinRoomSize { get; set; } = 8;
    [Export] public int DefaultMaxRoomSize { get; set; } = 20;

    // События
    [Signal] public delegate void LevelGenerationStartedEventHandler();
    [Signal] public delegate void LevelGenerationCompletedEventHandler();
    [Signal] public delegate void LevelGenerationFailedEventHandler(string error);
    [Signal] public delegate void ServerStatusChangedEventHandler(bool isRunning);

    // Состояние
    public bool IsGenerating { get; private set; } = false;
    public LevelData LastGeneratedLevel { get; private set; }

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

        // Автозапуск сервера если нужно
        if (AutoStartServer && NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartServer();
        }

        // Подписываемся на события LevelGenerationManager
        if (LevelGenerationManager.Instance != null)
        {
            LevelGenerationManager.Instance.GenerationStarted += OnGenerationStarted;
            LevelGenerationManager.Instance.GenerationCompleted += OnGenerationCompleted;
            LevelGenerationManager.Instance.GenerationFailed += OnGenerationFailed;
        }

        Logger.Debug("GameLevelManager initialized", true);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Генерирует уровень для планеты
    /// </summary>
    /// <param name="planetName">Название планеты</param>
    /// <param name="biomeType">Тип биома</param>
    /// <param name="difficulty">Сложность (D, C, B, A, S, S+)</param>
    /// <returns>Данные сгенерированного уровня</returns>
    public async Task<LevelData> GeneratePlanetLevelAsync(string planetName, int biomeType, string difficulty = "C")
    {
        if (IsGenerating)
        {
            Logger.Warning("Level generation already in progress");
            return LastGeneratedLevel ?? new LevelData();
        }

        IsGenerating = true;
        EmitSignal(SignalName.LevelGenerationStarted);

        try
        {
            // Определяем параметры на основе сложности
            var parameters = CreateGenerationParameters(planetName, biomeType, difficulty);

            Logger.Debug($"Generating level for planet {planetName} (Biome: {biomeType}, Difficulty: {difficulty})", true);

            // Генерируем уровень
            var levelData = await LevelGenerationManager.Instance.GenerateLevelAsync(parameters);

            if (levelData != null && levelData.Width > 0 && levelData.Height > 0)
            {
                LastGeneratedLevel = levelData;
                Logger.Debug($"Level generated successfully: {levelData.Width}x{levelData.Height}", true);
                EmitSignal(SignalName.LevelGenerationCompleted);
                return levelData;
            }
            else
            {
                throw new Exception("Generated level data is invalid");
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to generate level for planet {planetName}: {ex.Message}";
            Logger.Error(error);
            EmitSignal(SignalName.LevelGenerationFailed, error);
            return new LevelData();
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// Создает параметры генерации на основе сложности планеты
    /// </summary>
    private GenerationParameters CreateGenerationParameters(string planetName, int biomeType, string difficulty)
    {
        var parameters = new GenerationParameters
        {
            BiomeType = biomeType,
            MapWidth = DefaultMapWidth,
            MapHeight = DefaultMapHeight,
            Seed = GenerateSeed(planetName),
            MaxRooms = DefaultMaxRooms,
            MinRoomSize = DefaultMinRoomSize,
            MaxRoomSize = DefaultMaxRoomSize
        };

        // Настраиваем параметры в зависимости от сложности
        switch (difficulty.ToUpper())
        {
            case "D": // Легкая
                parameters.MapWidth = 40;
                parameters.MapHeight = 40;
                parameters.MaxRooms = 8;
                parameters.MinRoomSize = 10;
                parameters.MaxRoomSize = 25;
                break;
            case "C": // Средняя
                parameters.MapWidth = 60;
                parameters.MapHeight = 60;
                parameters.MaxRooms = 12;
                parameters.MinRoomSize = 8;
                parameters.MaxRoomSize = 20;
                break;
            case "B": // Сложная
                parameters.MapWidth = 80;
                parameters.MapHeight = 80;
                parameters.MaxRooms = 15;
                parameters.MinRoomSize = 6;
                parameters.MaxRoomSize = 18;
                break;
            case "A": // Очень сложная
                parameters.MapWidth = 100;
                parameters.MapHeight = 100;
                parameters.MaxRooms = 18;
                parameters.MinRoomSize = 5;
                parameters.MaxRoomSize = 15;
                break;
            case "S": // Экстремальная
                parameters.MapWidth = 120;
                parameters.MapHeight = 120;
                parameters.MaxRooms = 25;
                parameters.MinRoomSize = 4;
                parameters.MaxRoomSize = 12;
                break;
            case "S+": // Ультра-экстремальная
                parameters.MapWidth = 150;
                parameters.MapHeight = 150;
                parameters.MaxRooms = 40;
                parameters.MinRoomSize = 3;
                parameters.MaxRoomSize = 10;
                break;
        }

        return parameters;
    }

    /// <summary>
    /// Генерирует seed на основе названия планеты
    /// </summary>
    private int GenerateSeed(string planetName)
    {
        int seed = 0;
        foreach (char c in planetName)
        {
            seed = seed * 31 + c;
        }
        return Math.Abs(seed);
    }

    /// <summary>
    /// Запускает сервер генерации
    /// </summary>
    public void StartGenerationServer()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartServer();
            EmitSignal(SignalName.ServerStatusChanged, true);
            Logger.Debug("Generation server started", true);
        }
    }

    /// <summary>
    /// Останавливает сервер генерации
    /// </summary>
    public void StopGenerationServer()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StopServer();
            EmitSignal(SignalName.ServerStatusChanged, false);
            Logger.Debug("Generation server stopped", true);
        }
    }

    /// <summary>
    /// Получает информацию о статусе сервера
    /// </summary>
    public string GetServerStatus()
    {
        if (NetworkManager.Instance == null)
            return "NetworkManager not available";

        if (NetworkManager.Instance.IsServerRunning)
            return $"Server running (Port: {NetworkManager.Instance.ServerPort})";
        else
            return "Server not running";
    }

    /// <summary>
    /// Получает информацию о текущем генераторе
    /// </summary>
    public string GetGeneratorInfo()
    {
        if (LevelGenerationManager.Instance == null)
            return "LevelGenerationManager not available";

        return LevelGenerationManager.Instance.CurrentGeneratorInfo;
    }

    // Обработчики событий LevelGenerationManager
    private void OnGenerationStarted()
    {
        IsGenerating = true;
        EmitSignal(SignalName.LevelGenerationStarted);
    }

    private void OnGenerationCompleted()
    {
        IsGenerating = false;
        // LevelData будет получен в основном методе генерации
    }

    private void OnGenerationFailed(string error)
    {
        IsGenerating = false;
        EmitSignal(SignalName.LevelGenerationFailed, error);
    }
}
