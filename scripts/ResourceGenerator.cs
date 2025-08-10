using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Класс для генерации и размещения ресурсов на карте
/// </summary>
public class ResourceGenerator
{
    // Константы для настройки генерации
    private const int MIN_DISTANCE_BETWEEN_RESOURCES = 3; // Минимальное расстояние между ресурсами в тайлах
    private const int MIN_DISTANCE_FROM_WALL = 2;         // Минимальное расстояние от стен

    // Параметры генерации
    private int _maxResourcesPerRoom;      // Максимальное количество ресурсов в комнате
    private float _resourceDensity;        // Плотность ресурсов (0.0-1.0)

    // Пакетные сцены для создания ресурсов
    private PackedScene _resourceNodeScene; // Сцена ресурса по умолчанию

    // Биом-специфичные настройки вероятностей появления ресурсов
    private Dictionary<int, Dictionary<ResourceType, float>> _biomeProbabilities;

    // Измененная структура - теперь хранит список ресурсов для каждого типа
    private Dictionary<ResourceType, List<Item>> _resourceItems;

    // Генератор случайных чисел
    private Random _random;

    /// <summary>
    /// Конструктор с настройками генерации
    /// </summary>
    /// <param name="resourceNodeScene">Сцена ресурса по умолчанию</param>
    /// <param name="maxResourcesPerRoom">Максимальное количество ресурсов в комнате</param>
    /// <param name="resourceDensity">Плотность ресурсов (0.0-1.0)</param>
    public ResourceGenerator(PackedScene resourceNodeScene, int maxResourcesPerRoom = 3, float resourceDensity = 0.5f)
    {
        _resourceNodeScene = resourceNodeScene;
        _maxResourcesPerRoom = maxResourcesPerRoom;
        _resourceDensity = Mathf.Clamp(resourceDensity, 0.0f, 1.0f);

        _random = new Random();

        // Инициализируем словари
        InitializeBiomeProbabilities();
        InitializeResourceItems();
    }

    /// <summary>
    /// Инициализация вероятностей появления ресурсов в разных биомах
    /// </summary>
    private void InitializeBiomeProbabilities()
    {
        _biomeProbabilities = new Dictionary<int, Dictionary<ResourceType, float>>();

        // Биом 0: Grassland (стандартный биом)
        var grasslandProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.4f },
            { ResourceType.Crystal, 0.2f },
            { ResourceType.Organic, 0.4f },
            { ResourceType.Energy, 0.0f },
            { ResourceType.Composite, 0.0f }
        };
        _biomeProbabilities[0] = grasslandProbs;

        // Биом 1: Forest
        var forestProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.2f },
            { ResourceType.Crystal, 0.2f },
            { ResourceType.Organic, 0.6f },
            { ResourceType.Energy, 0.0f },
            { ResourceType.Composite, 0.0f }
        };
        _biomeProbabilities[1] = forestProbs;

        // Биом 2: Desert
        var desertProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.5f },
            { ResourceType.Crystal, 0.3f },
            { ResourceType.Organic, 0.1f },
            { ResourceType.Energy, 0.1f },
            { ResourceType.Composite, 0.0f }
        };
        _biomeProbabilities[2] = desertProbs;

        // Биом 3: Ice
        var iceProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.3f },
            { ResourceType.Crystal, 0.5f },
            { ResourceType.Organic, 0.1f },
            { ResourceType.Energy, 0.1f },
            { ResourceType.Composite, 0.0f }
        };
        _biomeProbabilities[3] = iceProbs;

        // Биом 4: Techno
        var technoProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.4f },
            { ResourceType.Crystal, 0.2f },
            { ResourceType.Organic, 0.0f },
            { ResourceType.Energy, 0.3f },
            { ResourceType.Composite, 0.1f }
        };
        _biomeProbabilities[4] = technoProbs;

        // Биом 5: Anomal
        var anomalProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.2f },
            { ResourceType.Crystal, 0.4f },
            { ResourceType.Organic, 0.1f },
            { ResourceType.Energy, 0.2f },
            { ResourceType.Composite, 0.1f }
        };
        _biomeProbabilities[5] = anomalProbs;

        // Биом 6: Lava Springs
        var lavaProbs = new Dictionary<ResourceType, float>
        {
            { ResourceType.Metal, 0.5f },
            { ResourceType.Crystal, 0.3f },
            { ResourceType.Organic, 0.0f },
            { ResourceType.Energy, 0.2f },
            { ResourceType.Composite, 0.0f }
        };
        _biomeProbabilities[6] = lavaProbs;
    }

    /// <summary>
    /// Инициализация предметов ресурсов
    /// Загружаем ресурсы из файлов .tres
    /// </summary>
    private void InitializeResourceItems()
    {
        // Изменено: теперь хранит список ресурсов для каждого типа
        _resourceItems = new Dictionary<ResourceType, List<Item>>();

        // Инициализируем пустые списки для всех типов ресурсов
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            _resourceItems[type] = new List<Item>();
        }

        try
        {
            // Папка, содержащая ресурсы
            string resourcesDirectory = "res://scenes/resources/items/";
            Logger.Debug($"Scanning for resource files in: {resourcesDirectory}", true);

            // Получаем список файлов .tres
            var dir = DirAccess.Open(resourcesDirectory);
            if (dir == null)
            {
                Logger.Error($"Failed to open resources directory: {resourcesDirectory}");
                return;
            }

            List<string> resourceFiles = new List<string>();
            dir.ListDirBegin();
            string fileName = dir.GetNext();

            while (fileName != "")
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
                {
                    resourceFiles.Add(resourcesDirectory + fileName);
                    Logger.Debug($"Found resource file: {fileName}", true);
                }
                fileName = dir.GetNext();
            }
            dir.ListDirEnd();

            Logger.Debug($"Found {resourceFiles.Count} resource files", true);

            // Загружаем каждый найденный ресурс
            foreach (string filePath in resourceFiles)
            {
                Logger.Debug($"Attempting to load resource from: {filePath}", false);

                try
                {
                    var item = ResourceLoader.Load<Item>(filePath);
                    if (item != null)
                    {
                        // Получаем тип ресурса из свойства ResourceTypeEnum
                        ResourceType resourceType = item.GetResourceType();

                        // Добавляем в список соответствующего типа
                        _resourceItems[resourceType].Add(item);
                        Logger.Debug($"Loaded resource from {filePath}: {item.DisplayName} as {resourceType} (from {item.ResourceTypeEnum})", true);
                    }
                    else
                    {
                        Logger.Error($"Failed to load resource from {filePath} - returned null");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Exception loading resource from {filePath}: {e.Message}");
                }
            }

            // Выводим информацию о загруженных ресурсах по типам
            foreach (var kvp in _resourceItems)
            {
                Logger.Debug($"ResourceType {kvp.Key}: loaded {kvp.Value.Count} resource variants", true);
                foreach (var item in kvp.Value)
                {
                    Logger.Debug($"  - {item.DisplayName} (ID: {item.ID})", false);
                }
            }

            Logger.Debug($"Successfully loaded resources. Types with resources: {_resourceItems.Count(kvp => kvp.Value.Count > 0)}", true);
        }
        catch (Exception e)
        {
            Logger.Error($"Error loading resource items: {e.Message}");
        }
    }

    private ResourceType DetermineResourceTypeFromId(string resourceId)
    {
        // Алгоритм определения типа ресурса по его ID
        if (resourceId.Contains("metal"))
            return ResourceType.Metal;
        else if (resourceId.Contains("crystal"))
            return ResourceType.Crystal;
        else if (resourceId.Contains("organic"))
            return ResourceType.Organic;

        // Добавьте дополнительные условия для новых ресурсов
        // else if (resourceId.Contains("new_resource_keyword"))
        //    return ResourceType.YourNewType;

        // По умолчанию возвращаем Metal
        return ResourceType.Metal;
    }


    /// <summary>
    /// Генерация ресурсов на карте
    /// </summary>
    /// <param name="rooms">Список комнат, где нужно разместить ресурсы</param>
    /// <param name="biomeType">Тип биома</param>
    /// <param name="sectionMask">Маска секции (для определения свободных мест)</param>
    /// <param name="worldOffset">Мировое смещение для правильного размещения</param>
    /// <param name="parentNode">Родительский узел, куда будут добавлены ресурсы</param>
    /// <returns>Количество размещенных ресурсов</returns>
    public int GenerateResources(
        List<Rect2I> rooms,
        int biomeType,
        LevelGenerator.TileType[,] sectionMask,
        Vector2 worldOffset,
        Node parentNode)
    {
        int resourcesPlaced = 0;

        // Проверяем, есть ли комнаты для размещения ресурсов
        if (rooms == null || rooms.Count == 0)
        {
            Logger.Debug("No rooms available for resource placement", false);
            return 0;
        }

        // Получаем вероятности для данного биома или используем стандартные
        Dictionary<ResourceType, float> probabilities;
        if (!_biomeProbabilities.TryGetValue(biomeType, out probabilities))
        {
            probabilities = _biomeProbabilities[0]; // Используем стандартный биом
        }

        // Обходим все комнаты
        foreach (var room in rooms)
        {
            // Пропускаем маленькие комнаты
            if (room.Size.X <= 4 || room.Size.Y <= 4)
                continue;

            // Определяем количество ресурсов для этой комнаты на основе площади и плотности
            int roomArea = room.Size.X * room.Size.Y;
            int maxResourcesForRoom = Mathf.Min(_maxResourcesPerRoom, roomArea / 16);
            int resourcesToPlace = (int)(maxResourcesForRoom * _resourceDensity);

            // Минимум 1 ресурс в комнате, если она достаточно большая
            resourcesToPlace = Mathf.Max(1, resourcesToPlace);

            // Список размещенных ресурсов в этой комнате для проверки дистанции
            var placedPositions = new List<Vector2I>();

            // Пытаемся разместить ресурсы
            for (int i = 0; i < resourcesToPlace; i++)
            {
                // Выбираем тип ресурса на основе вероятностей для данного биома
                ResourceType resourceType = SelectResourceType(probabilities);

                // Находим подходящую позицию для ресурса
                Vector2I? position = FindResourcePosition(room, sectionMask, placedPositions);

                if (position.HasValue)
                {
                    // Размещаем ресурс
                    PlaceResource(position.Value, resourceType, worldOffset, parentNode);

                    // Запоминаем позицию для проверки минимальной дистанции
                    placedPositions.Add(position.Value);

                    resourcesPlaced++;
                }
            }
        }

        Logger.Debug($"Placed {resourcesPlaced} resources for biome {biomeType}", true);
        return resourcesPlaced;
    }

    /// <summary>
    /// Выбор типа ресурса на основе вероятностей
    /// </summary>
    /// <param name="probabilities">Словарь вероятностей для разных типов ресурсов</param>
    /// <returns>Выбранный тип ресурса</returns>
    private ResourceType SelectResourceType(Dictionary<ResourceType, float> probabilities)
    {
        // Проверяем, что словарь не пустой и содержит хотя бы один ресурс из _resourceItems
        if (probabilities == null || probabilities.Count == 0)
        {
            // Если нет подходящих вероятностей, выбираем первый доступный ресурс
            foreach (var type in _resourceItems.Keys)
            {
                if (_resourceItems[type].Count > 0)
                {
                    Logger.Debug($"No valid probabilities, defaulting to first available resource: {type}", false);
                    return type;
                }
            }

            // Если ресурсов нет вообще, возвращаем Metal
            Logger.Debug("No resources available, defaulting to Metal", false);
            return ResourceType.Metal;
        }

        // Вычисляем сумму вероятностей только для доступных ресурсов
        float totalProbability = 0f;
        foreach (var kv in probabilities)
        {
            if (_resourceItems.ContainsKey(kv.Key) && _resourceItems[kv.Key].Count > 0)
            {
                totalProbability += kv.Value;
            }
        }

        // Если сумма вероятностей равна 0, возвращаем первый доступный тип ресурса
        if (totalProbability <= 0f)
        {
            foreach (var type in _resourceItems.Keys)
            {
                if (_resourceItems[type].Count > 0)
                {
                    Logger.Debug($"Zero total probability, defaulting to first available resource: {type}", false);
                    return type;
                }
            }

            return ResourceType.Metal;
        }

        // Генерируем случайное число в диапазоне [0, totalProbability)
        float randomValue = (float)_random.NextDouble() * totalProbability;

        // Выбираем тип ресурса, но только среди доступных
        float cumulativeProbability = 0f;
        foreach (var kv in probabilities)
        {
            if (!_resourceItems.ContainsKey(kv.Key) || _resourceItems[kv.Key].Count == 0)
                continue;

            cumulativeProbability += kv.Value;
            if (randomValue < cumulativeProbability)
            {
                return kv.Key;
            }
        }

        // По умолчанию, если что-то пошло не так, возвращаем первый доступный тип
        foreach (var type in _resourceItems.Keys)
        {
            if (_resourceItems[type].Count > 0)
            {
                return type;
            }
        }

        return ResourceType.Metal;
    }

    /// <summary>
    /// Поиск подходящей позиции для размещения ресурса
    /// </summary>
    /// <param name="room">Комната для размещения</param>
    /// <param name="sectionMask">Маска секции</param>
    /// <param name="placedPositions">Уже размещенные позиции</param>
    /// <returns>Позиция для размещения или null, если подходящей позиции не найдено</returns>
    private Vector2I? FindResourcePosition(Rect2I room, LevelGenerator.TileType[,] sectionMask, List<Vector2I> placedPositions)
    {
        // Увеличиваем количество попыток для лучшего результата
        int maxAttempts = 50;

        // Сначала создадим карту безопасных позиций
        List<Vector2I> safePositions = new List<Vector2I>();

        // Проверяем все позиции в комнате
        for (int y = room.Position.Y + MIN_DISTANCE_FROM_WALL;
             y < room.Position.Y + room.Size.Y - MIN_DISTANCE_FROM_WALL; y++)
        {
            for (int x = room.Position.X + MIN_DISTANCE_FROM_WALL;
                 x < room.Position.X + room.Size.X - MIN_DISTANCE_FROM_WALL; x++)
            {
                // Проверяем, что позиция находится в пределах маски
                if (x >= 0 && x < sectionMask.GetLength(0) &&
                    y >= 0 && y < sectionMask.GetLength(1))
                {
                    // Проверяем, что вся область 5x5 вокруг точки - это комната (гарантированно вдали от стен)
                    bool isValidPosition = true;

                    for (int dy = -2; dy <= 2 && isValidPosition; dy++)
                    {
                        for (int dx = -2; dx <= 2 && isValidPosition; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < sectionMask.GetLength(0) &&
                                ny >= 0 && ny < sectionMask.GetLength(1))
                            {
                                // Позиция невалидна, если это НЕ комната
                                if (sectionMask[nx, ny] != LevelGenerator.TileType.Room)
                                {
                                    isValidPosition = false;
                                    break;
                                }
                            }
                            else
                            {
                                // Если вышли за пределы карты, позиция невалидна
                                isValidPosition = false;
                                break;
                            }
                        }
                    }

                    if (isValidPosition)
                    {
                        safePositions.Add(new Vector2I(x, y));
                    }
                }
            }
        }

        // Перемешиваем список безопасных позиций
        ShuffleList(safePositions);

        // Теперь выбираем из безопасных позиций те, которые достаточно далеко от других ресурсов
        foreach (var pos in safePositions)
        {
            // Проверяем минимальное расстояние до других ресурсов
            bool tooClose = false;
            foreach (var placedPos in placedPositions)
            {
                int distance = Mathf.Abs(placedPos.X - pos.X) + Mathf.Abs(placedPos.Y - pos.Y);
                if (distance < MIN_DISTANCE_BETWEEN_RESOURCES)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                return pos; // Нашли подходящую позицию
            }
        }

        // Если не нашли идеальную позицию, но есть безопасные точки, вернем первую из них
        if (safePositions.Count > 0)
        {
            return safePositions[0];
        }

        // Не удалось найти подходящую позицию
        return null;
    }

    // Вспомогательный метод для перемешивания списка
    private void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    /// <summary>
    /// Размещение ресурса в мире
    /// </summary>
    /// <param name="position">Позиция в тайлах</param>
    /// <param name="resourceType">Тип ресурса</param>
    /// <param name="worldOffset">Мировое смещение</param>
    /// <param name="parentNode">Родительский узел</param>
    private void PlaceResource(Vector2I position, ResourceType resourceType, Vector2 worldOffset, Node parentNode)
    {
        try
        {
            // Создаем экземпляр ресурса
            ResourceNode resourceNode = _resourceNodeScene.Instantiate<ResourceNode>();

            if (resourceNode != null)
            {
                // Вычисляем мировую позицию
                Vector2I worldPos = new Vector2I(
                    (int)worldOffset.X + position.X,
                    (int)worldOffset.Y + position.Y
                );

                // Устанавливаем позицию строго по изометрической формуле без дополнительных сдвигов
                resourceNode.Position = MapTileToIsometricWorld(worldPos);

                // Настраиваем свойства ресурса
                resourceNode.Type = resourceType;
                resourceNode.ResourceAmount = _random.Next(1, 4); // Случайное количество от 1 до 3

                // Выбираем случайный предмет ресурса из доступных для данного типа
                List<Item> availableItems = _resourceItems[resourceType];

                if (availableItems.Count > 0)
                {
                    // Случайно выбираем один из доступных ресурсов этого типа
                    int randomIndex = _random.Next(0, availableItems.Count);
                    Item selectedItem = availableItems[randomIndex];

                    Logger.Debug($"Selected {randomIndex + 1} of {availableItems.Count} available resources for type {resourceType}: {selectedItem.DisplayName}", false);

                    // Устанавливаем предмет ресурса
                    Item itemCopy = selectedItem.Clone();
                    resourceNode.ResourceItem = itemCopy;
                    Logger.Debug($"Set ResourceItem {itemCopy.DisplayName} (ID: {itemCopy.ID}) for {resourceType} resource", false);

                    // Проверяем, есть ли у предмета валидная иконка
                    if (itemCopy.Icon != null)
                    {
                        Logger.Debug($"ResourceItem has icon: {itemCopy.IconPath}", false);
                    }
                    else
                    {
                        Logger.Error($"ResourceItem has no icon. Icon path: {itemCopy.IconPath}");
                    }
                }
                else
                {
                    Logger.Error($"No ResourceItems found for {resourceType} resource type!");
                }

                // Настраиваем визуальные эффекты
                resourceNode.EnablePulsating = true;
                resourceNode.PulsatingSpeed = 1.0f + (float)_random.NextDouble() * 0.5f; // Немного рандомизируем скорость пульсации

                // Добавляем в родительский узел
                parentNode.AddChild(resourceNode);

                Logger.Debug($"Placed {resourceType} resource at world position {isoPos}", false);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error placing resource: {e.Message}");
        }
    }

    /// <summary>
    /// Преобразование координат тайла в мировые координаты для изометрии
    /// </summary>
    /// <param name="tilePos">Позиция тайла</param>
    /// <returns>Мировые координаты</returns>
    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // Размер тайла для изометрии (стандартные значения из проекта)
        Vector2I tileSize = new Vector2I(64, 32);

        // Формула преобразования для изометрии 2:1 
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;

        return new Vector2(x, y);
    }
}