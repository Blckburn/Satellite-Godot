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
        // Используем существующий LevelGenerator для локальной генерации
        return await Task.Run(() =>
        {
            // Здесь будет вызов существующего генератора
            // Пока возвращаем заглушку
            return new LevelData
            {
                Width = parameters.MapWidth,
                Height = parameters.MapHeight,
                BiomeType = parameters.BiomeType,
                SpawnPosition = new Vector2I(parameters.MapWidth / 2, parameters.MapHeight / 2)
            };
        });
    }

    public bool IsAvailable()
    {
        return _levelGenerator != null;
    }

    public string GetGeneratorInfo()
    {
        return "Client-side level generator (offline mode)";
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
