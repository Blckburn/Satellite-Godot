using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Компонент для отладки сохранений. Позволяет принудительно сохранить игру по нажатию F5.
/// Добавьте этот компонент к корневым узлам игровых сцен.
/// </summary>
public partial class ForceSaveSystem : Node
{
    // Клавиша для принудительного сохранения
    [Export] public Key SaveKey { get; set; } = Key.F5;

    // Показывать ли уведомление при сохранении
    [Export] public bool ShowNotification { get; set; } = true;

    // Сохранять ли дополнительную отладочную информацию
    [Export] public bool VerboseLogging { get; set; } = true;

    // Ссылка на визуальное уведомление (если нужно)
    private Label _saveNotification;

    // Таймер для скрытия уведомления
    private Timer _notificationTimer;

    public override void _Ready()
    {
        // Создаем уведомление о сохранении
        if (ShowNotification)
        {
            CreateSaveNotification();
        }

        // Настраиваем действие в InputMap
        if (!InputMap.HasAction("force_save"))
        {
            InputMap.AddAction("force_save");
            var eventKey = new InputEventKey();
            eventKey.Keycode = SaveKey;
            InputMap.ActionAddEvent("force_save", eventKey);
        }

        Logger.Debug("ForceSaveSystem initialized", true);
    }

    public override void _Process(double delta)
    {
        // Проверяем нажатие клавиши сохранения
        if (Input.IsActionJustPressed("force_save"))
        {
            PerformForceSave();
        }
    }

    /// <summary>
    /// Выполняет принудительное сохранение игры
    /// </summary>
    public void PerformForceSave()
    {
        Logger.Debug("Force save initiated by F5 key press", true);

        // Собираем данные об инвентаре игрока
        var playerInventoryData = CollectPlayerInventoryData();

        // Собираем данные о хранилищах
        var storageData = CollectStorageData();

        // Находим GameManager и сохраняем данные
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            // Сохраняем инвентарь игрока
            if (playerInventoryData != null)
            {
                gameManager.SetData("PlayerInventorySaved", playerInventoryData);
                if (VerboseLogging)
                {
                    int itemCount = 0;
                    if (playerInventoryData.ContainsKey("items") &&
                        playerInventoryData["items"] is List<Dictionary<string, object>> items)
                    {
                        itemCount = items.Count;
                    }
                    Logger.Debug($"Saved player inventory with {itemCount} items", true);
                }
            }

            // Сохраняем данные хранилищ
            foreach (var storage in storageData)
            {
                string storageId = storage.Key;
                var data = storage.Value;

                string key = $"StorageInventory_{storageId}";
                gameManager.SetData(key, data);

                if (VerboseLogging)
                {
                    int itemCount = 0;
                    if (data.ContainsKey("items") &&
                        data["items"] is List<Dictionary<string, object>> items)
                    {
                        itemCount = items.Count;
                    }
                    Logger.Debug($"Saved storage '{storageId}' with {itemCount} items", true);
                }
            }

            // Сохраняем текущую позицию и сцену
            SaveCurrentSceneInfo(gameManager);

            // Запускаем сохранение
            bool success = gameManager.SaveGame();

            if (success)
            {
                Logger.Debug("FORCE SAVE SUCCESSFUL", true);
                ShowSaveSuccessNotification();
            }
            else
            {
                Logger.Error("FORCE SAVE FAILED");
                ShowSaveErrorNotification();
            }
        }
        else
        {
            Logger.Error("GameManager not found, cannot save");
            ShowSaveErrorNotification();
        }
    }

    /// <summary>
    /// Создает визуальное уведомление о сохранении
    /// </summary>
    private void CreateSaveNotification()
    {
        // Создаем прямоугольник
        var panel = new Panel();
        panel.Name = "SaveNotificationPanel";
        panel.Position = new Vector2(20, 20);
        panel.Size = new Vector2(200, 50);
        panel.Visible = false;

        // Добавляем стиль для панели
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        styleBox.CornerRadiusTopLeft = styleBox.CornerRadiusTopRight =
        styleBox.CornerRadiusBottomLeft = styleBox.CornerRadiusBottomRight = 5;
        panel.AddThemeStyleboxOverride("panel", styleBox);

        // Создаем метку текста
        _saveNotification = new Label();
        _saveNotification.Name = "SaveNotificationLabel";
        _saveNotification.Text = "Game Saved";
        _saveNotification.HorizontalAlignment = HorizontalAlignment.Center;
        _saveNotification.VerticalAlignment = VerticalAlignment.Center;
        _saveNotification.Size = panel.Size;

        // Добавляем стиль для текста
        _saveNotification.AddThemeColorOverride("font_color", Colors.White);
        _saveNotification.AddThemeFontSizeOverride("font_size", 16);

        // Добавляем метку к панели
        panel.AddChild(_saveNotification);

        // Создаем канвас для отображения поверх всего
        var canvas = new CanvasLayer();
        canvas.Name = "SaveNotificationCanvas";
        canvas.Layer = 100; // Высокий слой, чтобы отображалось поверх всего

        // Добавляем панель к канвасу
        canvas.AddChild(panel);

        // Добавляем канвас к ноде
        AddChild(canvas);

        // Создаем таймер для скрытия уведомления
        _notificationTimer = new Timer();
        _notificationTimer.WaitTime = 2.0f; // 2 секунды
        _notificationTimer.OneShot = true;
        _notificationTimer.Timeout += () => {
            panel.Visible = false;
        };

        // Добавляем таймер
        AddChild(_notificationTimer);
    }

    /// <summary>
    /// Показывает уведомление об успешном сохранении
    /// </summary>
    private void ShowSaveSuccessNotification()
    {
        if (!ShowNotification || _saveNotification == null)
            return;

        var panel = _saveNotification.GetParent<Panel>();
        panel.Visible = true;
        _saveNotification.Text = "Game Saved Successfully";

        // Устанавливаем цвет для успешного сохранения
        var styleBox = panel.GetThemeStylebox("panel", "Panel") as StyleBoxFlat;
        if (styleBox != null)
        {
            styleBox.BgColor = new Color(0.2f, 0.5f, 0.2f, 0.8f); // Зеленый
        }

        // Перезапускаем таймер
        _notificationTimer.Start();
    }

    /// <summary>
    /// Показывает уведомление об ошибке сохранения
    /// </summary>
    private void ShowSaveErrorNotification()
    {
        if (!ShowNotification || _saveNotification == null)
            return;

        var panel = _saveNotification.GetParent<Panel>();
        panel.Visible = true;
        _saveNotification.Text = "Save Failed!";

        // Устанавливаем цвет для ошибки
        var styleBox = panel.GetThemeStylebox("panel", "Panel") as StyleBoxFlat;
        if (styleBox != null)
        {
            styleBox.BgColor = new Color(0.5f, 0.2f, 0.2f, 0.8f); // Красный
        }

        // Перезапускаем таймер
        _notificationTimer.Start();
    }

    /// <summary>
    /// Собирает данные об инвентаре игрока
    /// </summary>
    private Dictionary<string, object> CollectPlayerInventoryData()
    {
        // Ищем игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Player player && player.PlayerInventory != null)
        {
            var inventory = player.PlayerInventory;

            if (VerboseLogging)
            {
                Logger.Debug($"Player inventory has {inventory.Items.Count} items:", true);
                foreach (var item in inventory.Items)
                {
                    Logger.Debug($"- {item.DisplayName} x{item.Quantity} ({item.ID})", false);
                }
            }

            return inventory.Serialize();
        }

        Logger.Debug("No player found or inventory is null", true);
        return null;
    }

    /// <summary>
    /// Собирает данные о хранилищах
    /// </summary>
    private Dictionary<string, Dictionary<string, object>> CollectStorageData()
    {
        Dictionary<string, Dictionary<string, object>> result = new Dictionary<string, Dictionary<string, object>>();

        // Проверяем модули хранилищ
        var storageModules = GetTree().GetNodesInGroup("StorageModules");
        if (VerboseLogging)
        {
            Logger.Debug($"Found {storageModules.Count} storage modules", true);
        }

        foreach (var module in storageModules)
        {
            if (module is StorageModule storageModule)
            {
                string storageId = storageModule.StorageID;

                // Получаем контейнер хранилища
                var container = storageModule.GetNode<Container>("StorageContainer");
                if (container != null && container.ContainerInventory != null)
                {
                    var inventory = container.ContainerInventory;

                    if (VerboseLogging)
                    {
                        Logger.Debug($"Storage '{storageId}' has {inventory.Items.Count} items:", true);
                        foreach (var item in inventory.Items)
                        {
                            Logger.Debug($"- {item.DisplayName} x{item.Quantity} ({item.ID})", false);
                        }
                    }

                    result[storageId] = inventory.Serialize();
                }
                else
                {
                    Logger.Debug($"No container found for storage module: {storageId}", true);
                }
            }
        }

        // Проверяем независимые контейнеры
        var containers = GetTree().GetNodesInGroup("Containers");
        if (VerboseLogging)
        {
            Logger.Debug($"Found {containers.Count} independent containers", true);
        }

        foreach (var container in containers)
        {
            if (container is Container containerObj)
            {
                // Получаем ID контейнера
                string containerID = containerObj.Name;
                if (containerObj.HasMethod("GetStorageID"))
                {
                    containerID = (string)containerObj.Call("GetStorageID");
                }

                // Пропускаем контейнеры хранилищ модулей, так как они уже обработаны
                bool isModuleContainer = false;
                foreach (var module in storageModules)
                {
                    if (module is StorageModule storageModule &&
                        storageModule.IsAncestorOf(containerObj))
                    {
                        isModuleContainer = true;
                        break;
                    }
                }

                if (!isModuleContainer && containerObj.ContainerInventory != null)
                {
                    var inventory = containerObj.ContainerInventory;

                    if (VerboseLogging)
                    {
                        Logger.Debug($"Container '{containerID}' has {inventory.Items.Count} items:", true);
                        foreach (var item in inventory.Items)
                        {
                            Logger.Debug($"- {item.DisplayName} x{item.Quantity} ({item.ID})", false);
                        }
                    }

                    result[containerID] = inventory.Serialize();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Сохраняет информацию о текущей сцене и позиции игрока
    /// </summary>
    private void SaveCurrentSceneInfo(GameManager gameManager)
    {
        // Получаем текущую сцену
        string currentScene = GetTree().CurrentScene.SceneFilePath;
        gameManager.SetData("CurrentScene", "res://scenes/station/space_station.tscn");

        // Получаем позицию игрока
        var players = GetTree().GetNodesInGroup("Player");
        if (players.Count > 0 && players[0] is Node2D player)
        {
            gameManager.SetData("LastWorldPosition", player.GlobalPosition);

            if (VerboseLogging)
            {
                Logger.Debug($"Saved player position: {player.GlobalPosition}", true);
            }
        }
    }
}