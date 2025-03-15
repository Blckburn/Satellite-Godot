using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Вспомогательный класс для отладки системы ресурсов
/// Можно прикрепить к любому узлу в сцене или создать новый узел для него
/// </summary>
[Tool]
public partial class ResourceDebugHelper : Node
{
    // Путь к директории, где хранятся ресурсы
    [Export] public string ResourceDirectory { get; set; } = "res://scenes/resources/items/";

    // Выводить подробную информацию в консоль
    [Export] public bool VerboseLogging { get; set; } = true;

    // Кнопка для запуска сканирования ресурсов
    [Export]
    public bool ScanResources
    {
        get => false;
        set
        {
            if (value)
                ScanResourceFiles();
        }
    }

    public override void _Ready()
    {
        // Автоматически сканируем ресурсы при запуске, если не в редакторе
        if (!Engine.IsEditorHint())
        {
            ScanResourceFiles();
        }
    }

    /// <summary>
    /// Сканирует директорию ресурсов и выводит информацию о найденных файлах .tres
    /// </summary>
    public void ScanResourceFiles()
    {
        GD.Print("\n===== RESOURCE FILES SCAN =====");
        GD.Print($"Scanning directory: {ResourceDirectory}");

        try
        {
            // Проверяем доступность директории
            if (!DirAccess.DirExistsAbsolute(ResourceDirectory))
            {
                GD.PrintErr($"Directory not found: {ResourceDirectory}");
                return;
            }

            // Открываем директорию
            var dir = DirAccess.Open(ResourceDirectory);
            if (dir == null)
            {
                GD.PrintErr($"Failed to open directory: {ResourceDirectory}");
                GD.PrintErr($"Error code: {DirAccess.GetOpenError()}");
                return;
            }

            // Сканируем файлы
            List<string> resourceFiles = new List<string>();
            dir.ListDirBegin();
            string fileName = dir.GetNext();

            while (fileName != "")
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith(".tres"))
                {
                    resourceFiles.Add(ResourceDirectory + fileName);
                    GD.Print($"Found resource file: {fileName}");
                }
                fileName = dir.GetNext();
            }
            dir.ListDirEnd();

            GD.Print($"Found {resourceFiles.Count} resource files");

            // Организуем ресурсы по типам
            var resourcesByType = new Dictionary<string, List<Item>>();

            // Загружаем и анализируем каждый ресурс
            foreach (string filePath in resourceFiles)
            {
                try
                {
                    var item = ResourceLoader.Load<Item>(filePath);
                    if (item != null)
                    {
                        // Получаем тип ресурса
                        string resourceType = item.ResourceTypeEnum;

                        // Добавляем тип в словарь, если его еще нет
                        if (!resourcesByType.ContainsKey(resourceType))
                        {
                            resourcesByType[resourceType] = new List<Item>();
                        }

                        // Добавляем ресурс в соответствующий список
                        resourcesByType[resourceType].Add(item);

                        if (VerboseLogging)
                        {
                            GD.Print($"Loaded resource: {item.DisplayName}");
                            GD.Print($"  ID: {item.ID}");
                            GD.Print($"  Type: {item.Type}");
                            GD.Print($"  ResourceType: {resourceType}");
                            GD.Print($"  IconPath: {item.IconPath}");
                            GD.Print($"  Icon loaded: {(item.Icon != null ? "Yes" : "No")}");
                        }
                    }
                    else
                    {
                        GD.PrintErr($"Failed to load resource: {filePath}");
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"Error loading resource {filePath}: {e.Message}");
                }
            }

            // Выводим итоговую статистику по типам
            GD.Print("\n=== Resource Statistics ===");
            foreach (var kvp in resourcesByType)
            {
                GD.Print($"Resource Type '{kvp.Key}': {kvp.Value.Count} items");

                if (VerboseLogging)
                {
                    foreach (var item in kvp.Value)
                    {
                        GD.Print($"  - {item.DisplayName} (ID: {item.ID})");
                    }
                }
            }

            // Проверяем иконки
            CheckResourceIcons(resourcesByType);

            GD.Print("===== SCAN COMPLETE =====\n");
        }
        catch (Exception e)
        {
            GD.PrintErr($"Error scanning resources: {e.Message}");
        }
    }

    /// <summary>
    /// Проверяет наличие и доступность иконок для ресурсов
    /// </summary>
    private void CheckResourceIcons(Dictionary<string, List<Item>> resourcesByType)
    {
        GD.Print("\n=== Icon Validation ===");

        int totalIcons = 0;
        int missingIcons = 0;

        foreach (var resourceList in resourcesByType.Values)
        {
            foreach (var item in resourceList)
            {
                totalIcons++;

                if (string.IsNullOrEmpty(item.IconPath))
                {
                    GD.PrintErr($"Missing IconPath for {item.DisplayName} (ID: {item.ID})");
                    missingIcons++;
                    continue;
                }

                if (item.Icon == null)
                {
                    GD.PrintErr($"Failed to load icon from path: {item.IconPath} for {item.DisplayName} (ID: {item.ID})");
                    missingIcons++;
                }
            }
        }

        if (missingIcons > 0)
        {
            GD.PrintErr($"WARNING: {missingIcons} out of {totalIcons} resources have missing or invalid icons");
        }
        else
        {
            GD.Print($"All {totalIcons} resource icons are valid");
        }
    }
}