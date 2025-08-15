using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Класс для генерации и размещения контейнеров на карте
/// </summary>
public class ContainerGenerator
{

    private Dictionary<ResourceType, List<Item>> _resourceItems;
    private Dictionary<int, Dictionary<ResourceType, float>> _biomeProbabilities;

    // Константы для настройки генерации
    private const int MIN_DISTANCE_BETWEEN_CONTAINERS = 10; // Минимальное расстояние между контейнерами
    private const int MIN_DISTANCE_FROM_WALL = 2;           // Минимальное расстояние от стен
    private const int MIN_DISTANCE_FROM_RESOURCES = 3;      // Минимальное расстояние от ресурсов

    // Параметры генерации
    private int _maxContainersPerRoom;      // Максимальное количество контейнеров в комнате
    private float _containerDensity;        // Плотность контейнеров (0.0-1.0)

    // Сцена контейнера для создания
    private PackedScene _containerScene;

    // Данные о биомах и типах контейнеров
    private Dictionary<int, List<string>> _biomeContainerTypes;

    // Генератор случайных чисел
    private Random _random;

    /// <summary>
    /// Конструктор с настройками генерации
    /// </summary>
    /// <param name="containerScene">Сцена контейнера по умолчанию</param>
    /// <param name="maxContainersPerRoom">Максимальное количество контейнеров в комнате</param>
    /// <param name="containerDensity">Плотность контейнеров (0.0-1.0)</param>
    public ContainerGenerator(PackedScene containerScene, int maxContainersPerRoom = 1, float containerDensity = 0.3f)
    {
        _containerScene = containerScene;
        _maxContainersPerRoom = maxContainersPerRoom;
        _containerDensity = Mathf.Clamp(containerDensity, 0.0f, 1.0f);

        _random = new Random();

        // Инициализируем типы контейнеров для разных биомов
        InitializeBiomeContainerTypes();

        // Добавьте эти вызовы:
        InitializeBiomeProbabilities();
        InitializeResourceItems();
    }



    /// <summary>
    /// Инициализация типов контейнеров для разных биомов
    /// </summary>
    private void InitializeBiomeContainerTypes()
    {
        _biomeContainerTypes = new Dictionary<int, List<string>>();

        // Стандартный биом (Grassland)
        _biomeContainerTypes[0] = new List<string> { "Storage Box", "Abandoned Crate" };

        // Биом 1: Forest
        _biomeContainerTypes[1] = new List<string> { "Wooden Chest", "Organic Container" };

        // Биом 2: Desert
        _biomeContainerTypes[2] = new List<string> { "Ancient Vessel", "Sand-Covered Chest" };

        // Биом 3: Ice
        _biomeContainerTypes[3] = new List<string> { "Frozen Container", "Cryo Storage" };

        // Биом 4: Techno
        _biomeContainerTypes[4] = new List<string> { "Tech Storage Unit", "Data Repository" };

        // Биом 5: Anomal
        _biomeContainerTypes[5] = new List<string> { "Anomalous Container", "Mysterious Box" };

        // Биом 6: Lava Springs
        _biomeContainerTypes[6] = new List<string> { "Heat-Resistant Locker", "Volcanic Storage" };
    }

    /// <summary>
    /// Генерация контейнеров на карте
    /// </summary>
    /// <param name="rooms">Список комнат, где нужно разместить контейнеры</param>
    /// <param name="biomeType">Тип биома</param>
    /// <param name="sectionMask">Маска секции (для определения свободных мест)</param>
    /// <param name="worldOffset">Мировое смещение для правильного размещения</param>
    /// <param name="parentNode">Родительский узел, куда будут добавлены контейнеры</param>
    /// <param name="resourcePositions">Список позиций уже размещенных ресурсов</param>
    /// <returns>Количество размещенных контейнеров</returns>
    public int GenerateContainers(
        List<Rect2I> rooms,
        int biomeType,
        LevelGenerator.TileType[,] sectionMask,
        Vector2 worldOffset,
        Node parentNode,
        List<Vector2I> resourcePositions = null)
    {
        int containersPlaced = 0;

        // Проверяем, есть ли комнаты для размещения контейнеров
        if (rooms == null || rooms.Count == 0)
        {
            // Logger.Debug("No rooms available for container placement", false);
            return 0;
        }

        // Если список позиций ресурсов не указан, создаем пустой список
        if (resourcePositions == null)
            resourcePositions = new List<Vector2I>();

        // Получаем типы контейнеров для данного биома или используем стандартные
        List<string> containerTypes;
        if (!_biomeContainerTypes.TryGetValue(biomeType, out containerTypes))
        {
            containerTypes = _biomeContainerTypes[0]; // Используем стандартный биом
        }

        // Список размещенных контейнеров для проверки дистанции
        var placedPositions = new List<Vector2I>();

        // Обходим комнаты, начиная с больших, для размещения контейнеров
        var sortedRooms = new List<Rect2I>(rooms);
        sortedRooms.Sort((a, b) => (b.Size.X * b.Size.Y).CompareTo(a.Size.X * a.Size.Y)); // От большей к меньшей

        foreach (var room in sortedRooms)
        {
            // Пропускаем маленькие комнаты
            if (room.Size.X <= 6 || room.Size.Y <= 6)
                continue;

            // Определяем количество контейнеров для этой комнаты на основе площади и плотности
            int roomArea = room.Size.X * room.Size.Y;
            int maxContainersForRoom = Mathf.Min(_maxContainersPerRoom, roomArea / 36); // Не более 1 контейнера на 36 клеток
            int containersToPlace = (int)(maxContainersForRoom * _containerDensity);

            // Учитываем случайность - небольшой шанс не размещать контейнеры
            if (_random.NextDouble() > 0.7) // 30% шанс пропустить комнату
                continue;

            // Минимум 0 контейнеров в комнате (может вообще не быть)
            containersToPlace = Mathf.Max(0, containersToPlace);

            // Для больших комнат всегда хотя бы 1 контейнер с некоторой вероятностью
            if (roomArea > 100 && containersToPlace == 0 && _random.NextDouble() > 0.5)
                containersToPlace = 1;

            // Пытаемся разместить контейнеры
            for (int i = 0; i < containersToPlace; i++)
            {
                // Находим подходящую позицию для контейнера
                Vector2I? position = FindContainerPosition(
                    room,
                    sectionMask,
                    placedPositions,
                    resourcePositions);

                if (position.HasValue)
                {
                    // Размещаем контейнер
                    PlaceContainer(position.Value, biomeType, worldOffset, parentNode, containerTypes);

                    // Запоминаем позицию для проверки минимальной дистанции
                    placedPositions.Add(position.Value);

                    containersPlaced++;
                }
            }
        }

        // Logger.Debug($"Placed {containersPlaced} containers for biome {biomeType}", true);
        return containersPlaced;
    }

    /// <summary>
    /// Поиск подходящей позиции для размещения контейнера
    /// </summary>
    private Vector2I? FindContainerPosition(
        Rect2I room,
        LevelGenerator.TileType[,] sectionMask,
        List<Vector2I> placedContainerPositions,
        List<Vector2I> resourcePositions)
    {
        // Максимальное количество попыток найти подходящую позицию
        int maxAttempts = 25;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Выбираем случайную позицию внутри комнаты (с отступом от края)
            int x = _random.Next(room.Position.X + MIN_DISTANCE_FROM_WALL,
                                room.Position.X + room.Size.X - MIN_DISTANCE_FROM_WALL);
            int y = _random.Next(room.Position.Y + MIN_DISTANCE_FROM_WALL,
                                room.Position.Y + room.Size.Y - MIN_DISTANCE_FROM_WALL);

            Vector2I position = new Vector2I(x, y);

            // Проверяем, что позиция находится в пределах маски
            if (x >= 0 && x < sectionMask.GetLength(0) &&
                y >= 0 && y < sectionMask.GetLength(1))
            {
                // Проверяем, что позиция свободна (это комната, а не стена или коридор)
                if (sectionMask[x, y] != LevelGenerator.TileType.Room)
                {
                    continue;
                }

                // Проверяем минимальное расстояние до других контейнеров
                bool tooCloseToContainer = false;
                foreach (var placedPos in placedContainerPositions)
                {
                    int distance = Mathf.Abs(placedPos.X - x) + Mathf.Abs(placedPos.Y - y);
                    if (distance < MIN_DISTANCE_BETWEEN_CONTAINERS)
                    {
                        tooCloseToContainer = true;
                        break;
                    }
                }

                if (tooCloseToContainer)
                    continue;

                // Проверяем минимальное расстояние до ресурсов
                bool tooCloseToResource = false;
                foreach (var resourcePos in resourcePositions)
                {
                    int distance = Mathf.Abs(resourcePos.X - x) + Mathf.Abs(resourcePos.Y - y);
                    if (distance < MIN_DISTANCE_FROM_RESOURCES)
                    {
                        tooCloseToResource = true;
                        break;
                    }
                }

                if (tooCloseToResource)
                    continue;

                // Все проверки пройдены, возвращаем позицию
                return position;
            }
        }

        // Не удалось найти подходящую позицию
        return null;
    }

    /// <summary>
    /// Размещение контейнера в мире
    /// </summary>
    private void PlaceContainer(
     Vector2I position,
     int biomeType,
     Vector2 worldOffset,
     Node parentNode,
     List<string> containerTypes)
    {
        try
        {
            // Проверяем, что сцена контейнера существует
            if (_containerScene == null)
            {
                Logger.Error("Container scene is null!");
                return;
            }

            // Создаем экземпляр контейнера
            Container container = _containerScene.Instantiate<Container>();

            if (container != null)
            {
                // Вычисляем мировую позицию
                Vector2I worldPos = new Vector2I(
                    (int)worldOffset.X + position.X,
                    (int)worldOffset.Y + position.Y
                );

                // Для изометрической проекции преобразуем координаты тайлов в мировые
                Vector2 isoPos = MapTileToIsometricWorld(worldPos);
                container.Position = isoPos;

                // Настраиваем свойства контейнера
                // Выбираем случайное имя из доступных для биома
                string containerName = containerTypes[_random.Next(containerTypes.Count)];
                container.ContainerName = containerName;

                // Устанавливаем размер инвентаря (случайный в пределах 5-15)
                container.InventorySize = _random.Next(5, 16);

                // Добавляем в родительский узел
                parentNode.AddChild(container);

                // Добавляем в группу Interactables для уверенности
                container.AddToGroup("Interactables");

                // Заполняем контейнер предметами
                PopulateContainerWithItems(container, biomeType);

                // Logger.Debug($"Placed {containerName} at world position {isoPos}", false);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Error placing container: {e.Message}");
        }
    }


    /// <summary>
    /// Преобразование координат тайла в мировые координаты для изометрии
    /// </summary>
    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // Размер тайла для изометрии (стандартные значения из проекта)
        Vector2I tileSize = new Vector2I(32, 16);

        // Формула преобразования для изометрии 2:1 
        float x = (tilePos.X - tilePos.Y) * tileSize.X / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileSize.Y / 2.0f;

        return new Vector2(x, y);
    }

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

    private void InitializeResourceItems()
    {
        // Инициализируем словарь для хранения ресурсов по типам
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
            // Logger.Debug($"ContainerGenerator: Scanning for resource files in: {resourcesDirectory}", true);

            // Получаем список файлов .tres
            var dir = DirAccess.Open(resourcesDirectory);
            if (dir == null)
            {
                Logger.Error($"ContainerGenerator: Failed to open resources directory: {resourcesDirectory}");
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
                    // Logger.Debug($"ContainerGenerator: Found resource file: {fileName}", false);
                }
                fileName = dir.GetNext();
            }
            dir.ListDirEnd();

            // Logger.Debug($"ContainerGenerator: Found {resourceFiles.Count} resource files", true);

            // Загружаем каждый найденный ресурс
            foreach (string filePath in resourceFiles)
            {
                try
                {
                    var item = ResourceLoader.Load<Item>(filePath);
                    if (item != null)
                    {
                        // Получаем тип ресурса из свойства ResourceTypeEnum
                        ResourceType resourceType = item.GetResourceType();

                        // Добавляем в список соответствующего типа
                        _resourceItems[resourceType].Add(item);
                        // Logger.Debug($"ContainerGenerator: Loaded resource from {filePath}: {item.DisplayName} as {resourceType}", false);
                    }
                    else
                    {
                        Logger.Error($"ContainerGenerator: Failed to load resource from {filePath} - returned null");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"ContainerGenerator: Exception loading resource from {filePath}: {e.Message}");
                }
            }

            // Выводим информацию о загруженных ресурсах по типам
            foreach (var kvp in _resourceItems)
            {
                // Logger.Debug($"ContainerGenerator: ResourceType {kvp.Key}: loaded {kvp.Value.Count} resource variants", true);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"ContainerGenerator: Error loading resource items: {e.Message}");
        }
    }

    // Метод для выбора типа ресурса на основе вероятностей биома
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
                    return type;
                }
            }

            // Если ресурсов нет вообще, возвращаем Metal
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

    // Метод для заполнения контейнера случайными предметами
    private void PopulateContainerWithItems(Container container, int biomeType)
    {
        try
        {
            // Определим количество предметов (1-5)
            int itemCount = _random.Next(1, 6);

            // Получаем вероятности для данного биома
            Dictionary<ResourceType, float> probabilities;
            if (!_biomeProbabilities.TryGetValue(biomeType, out probabilities))
            {
                probabilities = _biomeProbabilities[0]; // Используем стандартный биом по умолчанию
            }

            // Заполняем контейнер предметами
            for (int i = 0; i < itemCount; i++)
            {
                // Выбираем тип ресурса на основе вероятностей биома
                ResourceType resourceType = SelectResourceType(probabilities);

                // Проверяем, есть ли доступные ресурсы этого типа
                if (_resourceItems.ContainsKey(resourceType) && _resourceItems[resourceType].Count > 0)
                {
                    // Выбираем случайный ресурс из доступных
                    int randomIndex = _random.Next(0, _resourceItems[resourceType].Count);
                    Item selectedItem = _resourceItems[resourceType][randomIndex];

                    // Создаем копию предмета
                    Item itemCopy = selectedItem.Clone();

                    // Устанавливаем случайное количество (1-5)
                    itemCopy.Quantity = _random.Next(1, 6);

                    // Добавляем предмет в контейнер
                    bool added = container.AddItemToContainer(itemCopy);

                    if (added)
                    {
                        // Logger.Debug($"ContainerGenerator: Added {itemCopy.DisplayName} x{itemCopy.Quantity} to container", false);
                    }
                }
            }

            // Logger.Debug($"ContainerGenerator: Container was populated with {itemCount} items for biome {biomeType}", true);
        }
        catch (Exception e)
        {
            Logger.Error($"ContainerGenerator: Error populating container: {e.Message}");
        }
    }
}