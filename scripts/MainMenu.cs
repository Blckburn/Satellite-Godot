using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Класс для управления главным меню игры
/// </summary>
public partial class MainMenu : Control
{
    // Пути к узлам кнопок
    [Export] public NodePath ContinueButtonPath { get; set; } = "%ContinueButton";
    [Export] public NodePath NewGameButtonPath { get; set; } = "%NewGameButton";
    [Export] public NodePath SettingsButtonPath { get; set; } = "%SettingsButton";
    [Export] public NodePath ExitButtonPath { get; set; } = "%ExitButton";

    // Путь к первой сцене игры
    [Export] public string FirstScenePath { get; set; } = "res://scenes/station/space_station.tscn";

    // Ссылки на кнопки
    private Button _continueButton;
    private Button _newGameButton;
    private Button _settingsButton;
    private Button _exitButton;

    // Анимация (опционально)
    [Export] public NodePath AnimationPlayerPath { get; set; } = "AnimationPlayer";
    private AnimationPlayer _animationPlayer;

    // Панель настроек (будет реализована в будущем)
    [Export] public NodePath SettingsPanelPath { get; set; } = "%SettingsPanel";
    private Panel _settingsPanel;

    public override void _Ready()
    {
        // Инициализация компонентов UI
        InitializeUI();

        // Настройка кнопок и их состояний
        SetupButtons();

        // Обновить состояние кнопки "Продолжить" на основе наличия сохранения
        UpdateContinueButtonState();

        // Проигрываем анимацию при запуске, если она есть
        PlayStartAnimation();

        Logger.Debug("Main menu initialized", true);
    }

    /// <summary>
    /// Инициализирует компоненты пользовательского интерфейса
    /// </summary>
    private void InitializeUI()
    {
        // Получаем ссылки на кнопки
        _continueButton = GetNode<Button>(ContinueButtonPath);
        _newGameButton = GetNode<Button>(NewGameButtonPath);
        _settingsButton = GetNode<Button>(SettingsButtonPath);
        _exitButton = GetNode<Button>(ExitButtonPath);

        // Получаем ссылку на панель настроек
        _settingsPanel = GetNodeOrNull<Panel>(SettingsPanelPath);
        if (_settingsPanel != null)
        {
            _settingsPanel.Visible = false;
        }

        // Получаем ссылку на проигрыватель анимаций
        _animationPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
    }

    /// <summary>
    /// Настраивает обработчики событий для кнопок
    /// </summary>
    private void SetupButtons()
    {
        // Подключаем обработчики нажатия кнопок
        if (_continueButton != null)
        {
            _continueButton.Pressed += OnContinueButtonPressed;
        }

        if (_newGameButton != null)
        {
            _newGameButton.Pressed += OnNewGameButtonPressed;
        }

        if (_settingsButton != null)
        {
            _settingsButton.Pressed += OnSettingsButtonPressed;
        }

        if (_exitButton != null)
        {
            _exitButton.Pressed += OnExitButtonPressed;
        }
    }

    /// <summary>
    /// Обновляет состояние кнопки "Продолжить" в зависимости от наличия сохранения
    /// </summary>
    private void UpdateContinueButtonState()
    {
        if (_continueButton != null)
        {
            // Проверяем наличие сохранения через GameManager
            var gameManager = GetNode<GameManager>("/root/GameManager");
            bool saveExists = gameManager != null && gameManager.SaveExists();

            // Включаем/выключаем кнопку в зависимости от наличия сохранения
            _continueButton.Disabled = !saveExists;

            Logger.Debug($"Continue button state updated: {(saveExists ? "enabled" : "disabled")}", false);
        }
    }

    /// <summary>
    /// Проигрывает стартовую анимацию
    /// </summary>
    private void PlayStartAnimation()
    {
        if (_animationPlayer != null && _animationPlayer.HasAnimation("menu_start"))
        {
            _animationPlayer.Play("menu_start");
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Продолжить"
    /// </summary>
    private void OnContinueButtonPressed()
    {
        Logger.Debug("Continue button pressed", true);

        // Загружаем сохраненную игру
        var gameManager = GetNode<GameManager>("/root/GameManager");
        var saveManager = GetNode<SaveManager>("/root/SaveManager");

        if (gameManager != null && saveManager != null)
        {
            // Используем прямую загрузку вместо стандартной
            bool loadSuccess = saveManager.LoadGameDirectly();

            if (loadSuccess)
            {
                // Успешная загрузка игры
                Logger.Debug("Game loaded successfully", true);

                // Переходим к сцене после успешной загрузки
                GetTree().ChangeSceneToFile(FirstScenePath);
            }
            else
            {
                // Ошибка загрузки - переходим к первой сцене по умолчанию
                Logger.Error("Failed to load game, starting new game instead");
                GetTree().ChangeSceneToFile(FirstScenePath);
            }
        }
        else
        {
            Logger.Error("GameManager or SaveManager not found");
            GetTree().ChangeSceneToFile(FirstScenePath);
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Новая игра"
    /// </summary>
    private void OnNewGameButtonPressed()
    {
        Logger.Debug("New game button pressed", true);

        // Инициализируем новую игру, создавая чистое сохранение
        InitializeNewGame();

        // Переходим к первой сцене
        GetTree().ChangeSceneToFile(FirstScenePath);
    }

    /// <summary>
    /// Инициализирует новую игру, создавая базовое сохранение
    /// </summary>
    private void InitializeNewGame()
    {
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager == null)
            return;

        // Сбрасываем данные GameManager для новой игры
        gameManager.ClearData();

        // Создаем начальный инвентарь (если нужно)
        Dictionary<string, object> initialInventory = CreateInitialInventory();
        gameManager.SetData("PlayerInventorySaved", initialInventory);

        // Начальная позиция и сцена
        gameManager.SetData("LastWorldPosition", Vector2.Zero);
        gameManager.SetData("CurrentScene", FirstScenePath);

        // Начальное здоровье
        gameManager.SetData("PlayerHealth", 100f);
        gameManager.SetData("PlayerMaxHealth", 100f);

        // Сбрасываем время игры
        gameManager.SetData("PlayTime", 0f);

        // Другие начальные данные
        gameManager.SetData("UnlockedModules", new List<string>());
        gameManager.SetData("CompletedMissions", new List<string>());
        gameManager.SetData("DiscoveredPlanets", new List<string>());
        gameManager.SetData("VisitedLocations", new List<string>());

        // Создаем чистое сохранение с начальными значениями
        bool saved = gameManager.SaveGame();
        Logger.Debug($"New game initialized and {(saved ? "saved successfully" : "save failed")}", true);
    }

    /// <summary>
    /// Создает начальный инвентарь для новой игры
    /// </summary>
    private Dictionary<string, object> CreateInitialInventory()
    {
        // Создаем базовую структуру инвентаря
        Dictionary<string, object> inventory = new Dictionary<string, object>
        {
            ["max_slots"] = 20,
            ["max_weight"] = 0f,
            ["items"] = new List<Dictionary<string, object>>()
        };

        return inventory;
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Настройки"
    /// </summary>
    private void OnSettingsButtonPressed()
    {
        Logger.Debug("Settings button pressed", true);

        // В будущем здесь будет открытие меню настроек
        if (_settingsPanel != null)
        {
            _settingsPanel.Visible = true;
        }
    }

    /// <summary>
    /// Обработчик нажатия на кнопку "Выход"
    /// </summary>
    private void OnExitButtonPressed()
    {
        Logger.Debug("Exit button pressed", true);

        // Сохраняем игру перед выходом
        var gameManager = GetNode<GameManager>("/root/GameManager");
        if (gameManager != null)
        {
            gameManager.SaveGame();
        }

        // Выходим из игры
        GetTree().Quit();
    }
}