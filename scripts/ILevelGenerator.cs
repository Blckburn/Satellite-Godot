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
        // Упрощенная генерация для тестирования - не используем сложную логику
        return await Task.Run(() =>
        {
            GD.Print($"ClientLevelGenerator: Generating simple level {parameters.MapWidth}x{parameters.MapHeight}");
            
            // Создаем простые данные уровня без использования TileMap
            var levelData = new LevelData
            {
                Width = parameters.MapWidth,
                Height = parameters.MapHeight,
                BiomeType = parameters.BiomeType,
                SpawnPosition = new Vector2I(parameters.MapWidth / 2, parameters.MapHeight / 2)
            };

            // Создаем простые массивы данных
            int totalTiles = parameters.MapWidth * parameters.MapHeight;
            levelData.FloorData = new byte[totalTiles];
            levelData.WallData = new byte[totalTiles];
            levelData.DecorationData = new byte[totalTiles];

            // Заполняем простым паттерном
            for (int i = 0; i < totalTiles; i++)
            {
                levelData.FloorData[i] = 1; // Пол
                levelData.WallData[i] = 0;  // Нет стен
                levelData.DecorationData[i] = 0; // Нет декораций
            }

            GD.Print($"ClientLevelGenerator: Generated level with {totalTiles} tiles");
            return levelData;
        });
    }

    public bool IsAvailable()
    {
        // Всегда доступен, даже если LevelGenerator не настроен
        return true;
    }

    public string GetGeneratorInfo()
    {
        return "Client-side level generator (simple mode)";
    }
}

/// <summary>
/// Серверный генератор уровней
/// Используется для генерации на сервере
/// </summary>
public class ServerLevelGenerator : ILevelGenerator
{
    private readonly NetworkManager _networkManager;

    public ServerLevelGenerator(NetworkManager networkManager)
    {
        _networkManager = networkManager;
    }

    public async Task<LevelData> GenerateLevelAsync(GenerationParameters parameters)
    {
        // Запрашиваем генерацию с сервера
        return await _networkManager.RequestLevelGenerationAsync(parameters);
    }

    public bool IsAvailable()
    {
        return _networkManager != null && _networkManager.IsConnected;
    }

    public string GetGeneratorInfo()
    {
        return $"Server-side level generator (connected: {_networkManager?.IsConnected})";
    }
}
