using Godot;
using System.Threading.Tasks;

/// <summary>
/// Интерфейс для генератора уровней
/// Позволяет разделить клиентскую и серверную логику генерации
/// </summary>
public interface ILevelGenerator
{
    /// <summary>
    /// Генерирует уровень асинхронно
    /// </summary>
    /// <param name="parameters">Параметры генерации</param>
    /// <returns>Данные сгенерированного уровня</returns>
    Task<LevelData> GenerateLevelAsync(GenerationParameters parameters);

    /// <summary>
    /// Проверяет, доступен ли генератор
    /// </summary>
    /// <returns>True если генератор готов к работе</returns>
    bool IsAvailable();

    /// <summary>
    /// Получает информацию о генераторе
    /// </summary>
    /// <returns>Описание генератора</returns>
    string GetGeneratorInfo();
}

/// <summary>
/// Клиентский генератор уровней
/// Используется для локальной генерации (офлайн режим)
/// </summary>
public class ClientLevelGenerator : ILevelGenerator
{
    private readonly LevelGenerator _levelGenerator;

    public ClientLevelGenerator(LevelGenerator levelGenerator)
    {
        _levelGenerator = levelGenerator;
    }

    public async Task<LevelData> GenerateLevelAsync(GenerationParameters parameters)
    {
        return await Task.Run(() =>
        {
            GD.Print($"ClientLevelGenerator: Generating real level {parameters.MapWidth}x{parameters.MapHeight}");
            
            // Используем переданный LevelGenerator
            if (_levelGenerator != null)
            {
                var levelData = _levelGenerator.GenerateLevelData(parameters);
                GD.Print($"ClientLevelGenerator: Generated real level with {levelData.Width}x{levelData.Height}");
                return levelData;
            }
            else
            {
                GD.PrintErr("ClientLevelGenerator: LevelGenerator not found, falling back to simple generation");
                // Fallback к простой генерации
                var levelData = new LevelData
                {
                    Width = parameters.MapWidth,
                    Height = parameters.MapHeight,
                    BiomeType = parameters.BiomeType,
                    SpawnPosition = new Vector2I(parameters.MapWidth / 2, parameters.MapHeight / 2)
                };
                int totalTiles = parameters.MapWidth * parameters.MapHeight;
                levelData.FloorData = new byte[totalTiles];
                levelData.WallData = new byte[totalTiles];
                levelData.DecorationData = new byte[totalTiles];
                for (int i = 0; i < totalTiles; i++)
                {
                    levelData.FloorData[i] = 1;
                    levelData.WallData[i] = 0;
                    levelData.DecorationData[i] = 0;
                }
                return levelData;
            }
        });
    }

    public bool IsAvailable()
    {
        // Всегда доступен, даже если LevelGenerator не настроен
        return true;
    }

    public string GetGeneratorInfo()
    {
        return "Client-side level generator (real mode)";
    }
}

/// <summary>
/// Серверный генератор уровней
/// Используется для генерации на сервере
/// </summary>
public class ServerLevelGenerator : ILevelGenerator
{
    private readonly NetworkManager _networkManager;
    private readonly LevelGenerator _levelGenerator;

    public ServerLevelGenerator(NetworkManager networkManager, LevelGenerator levelGenerator)
    {
        _networkManager = networkManager;
        _levelGenerator = levelGenerator;
    }

    public async Task<LevelData> GenerateLevelAsync(GenerationParameters parameters)
    {
        // Если сервер запущен, генерируем локально (для тестирования)
        if (_networkManager.IsServerRunning)
        {
            GD.Print($"ServerLevelGenerator: Generating real level locally (server is running)");
            return await Task.Run(() =>
            {
                // Используем переданный LevelGenerator
                if (_levelGenerator != null)
                {
                    var levelData = _levelGenerator.GenerateLevelData(parameters);
                    GD.Print($"ServerLevelGenerator: Generated real level with {levelData.Width}x{levelData.Height}");
                    return levelData;
                }
                else
                {
                    GD.PrintErr("ServerLevelGenerator: LevelGenerator not found, falling back to simple generation");
                    // Fallback к простой генерации
                    var levelData = new LevelData
                    {
                        Width = parameters.MapWidth,
                        Height = parameters.MapHeight,
                        BiomeType = parameters.BiomeType,
                        SpawnPosition = new Vector2I(parameters.MapWidth / 2, parameters.MapHeight / 2)
                    };
                    int totalTiles = parameters.MapWidth * parameters.MapHeight;
                    levelData.FloorData = new byte[totalTiles];
                    levelData.WallData = new byte[totalTiles];
                    levelData.DecorationData = new byte[totalTiles];
                    for (int i = 0; i < totalTiles; i++)
                    {
                        levelData.FloorData[i] = 1;
                        levelData.WallData[i] = 0;
                        levelData.DecorationData[i] = 0;
                    }
                    return levelData;
                }
            });
        }
        else
        {
            // Запрашиваем генерацию с сервера (для реального клиент-серверного режима)
            return await _networkManager.RequestLevelGenerationAsync(parameters);
        }
    }

    public bool IsAvailable()
    {
        return _networkManager != null && _networkManager.IsServerRunning;
    }

    public string GetGeneratorInfo()
    {
        return "Server-side level generator (real mode)";
    }
}
