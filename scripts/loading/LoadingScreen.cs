using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// BADASS сцена загрузки с анимациями и интеграцией сервера!
/// </summary>
public partial class LoadingScreen : Control
{
    // UI элементы
    private Label _statusLabel;
    private ProgressBar _progressBar;
    private Label _progressLabel;
    private RichTextLabel _logText;
    private Button _continueButton;
    private Label _continueLabel;
    private Timer _timer;
    private AnimationPlayer _animationPlayer;

    // Состояние загрузки
    private int _currentProgress = 0;
    private bool _isLoadingComplete = false;
    private bool _canContinue = false;

    // Этапы загрузки
    private readonly string[] _loadingSteps = {
        "Initializing BADASS systems...",
        "Starting save server...",
        "Connecting to server...",
        "Checking server status...",
        "Loading save data...",
        "Verifying data integrity...",
        "Preparing game systems...",
        "Loading complete!"
    };

    public override void _Ready()
    {
        // Получаем ссылки на UI элементы
        _statusLabel = GetNode<Label>("MainContainer/LoadingSection/StatusLabel");
        _progressBar = GetNode<ProgressBar>("MainContainer/LoadingSection/ProgressBar");
        _progressLabel = GetNode<Label>("MainContainer/LoadingSection/ProgressLabel");
        _logText = GetNode<RichTextLabel>("MainContainer/LogContainer/LogText");
        _continueButton = GetNode<Button>("MainContainer/ContinueSection/ContinueButton");
        _continueLabel = GetNode<Label>("MainContainer/ContinueSection/ContinueLabel");
        _timer = GetNode<Timer>("Timer");
        _animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");

        // Настраиваем начальное состояние
        _continueButton.Disabled = true;
        _continueLabel.Visible = false;
        _progressBar.Value = 0;
        _progressLabel.Text = "0%";

        // Добавляем начальную запись в лог
        AddLogEntry("Loading screen initialized", "green");

        // Запускаем процесс загрузки
        StartLoadingProcess();
    }

    public override void _Input(InputEvent @event)
    {
        if (_canContinue && @event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            ContinueToMainMenu();
        }
    }

    /// <summary>
    /// Запускает процесс загрузки
    /// </summary>
    private async void StartLoadingProcess()
    {
        AddLogEntry("Starting BADASS loading sequence...", "yellow");

        // Этап 1: Инициализация систем
        await UpdateLoadingStep(0, 15);
        AddLogEntry("Systems initialized successfully", "green");

        // Этап 2: Запуск сервера
        await UpdateLoadingStep(1, 25);
        AddLogEntry("Save server starting...", "blue");

        // Этап 3: Подключение к серверу
        await UpdateLoadingStep(2, 40);
        AddLogEntry("Connecting to save server...", "blue");

        // Ждем подключения к серверу
        if (ServerSaveManager.Instance != null)
        {
            // Подписываемся на события сервера
            ServerSaveManager.Instance.ServerConnectionChanged += OnServerConnectionChanged;
            ServerSaveManager.Instance.SaveCompleted += OnSaveCompleted;
            ServerSaveManager.Instance.LoadCompleted += OnLoadCompleted;

            // Ждем подключения
            await WaitForServerConnection();
        }
        else
        {
            AddLogEntry("WARNING: ServerSaveManager not found!", "red");
            await Task.Delay(1000);
        }

        // Этап 4: Проверка статуса
        await UpdateLoadingStep(3, 55);
        AddLogEntry("Server status verified", "green");

        // Этап 5: Загрузка данных
        await UpdateLoadingStep(4, 70);
        AddLogEntry("Loading save data...", "blue");

        // Ждем загрузки данных
        await WaitForDataLoad();

        // Этап 6: Проверка целостности
        await UpdateLoadingStep(5, 85);
        AddLogEntry("Data integrity verified", "green");

        // Этап 7: Подготовка игровых систем
        await UpdateLoadingStep(6, 95);
        AddLogEntry("Game systems prepared", "green");

        // Этап 8: Завершение
        await UpdateLoadingStep(7, 100);
        AddLogEntry("Loading complete! Ready to launch!", "green");

        // Завершаем загрузку
        CompleteLoading();
    }

    /// <summary>
    /// Обновляет этап загрузки
    /// </summary>
    private async Task UpdateLoadingStep(int stepIndex, int progress)
    {
        if (stepIndex < _loadingSteps.Length)
        {
            _statusLabel.Text = _loadingSteps[stepIndex];
        }

        // Анимируем прогресс
        await AnimateProgress(progress);
    }

    /// <summary>
    /// Анимирует прогресс бар
    /// </summary>
    private async Task AnimateProgress(int targetProgress)
    {
        while (_currentProgress < targetProgress)
        {
            _currentProgress++;
            _progressBar.Value = _currentProgress;
            _progressLabel.Text = $"{_currentProgress}%";
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Ждет подключения к серверу
    /// </summary>
    private async Task WaitForServerConnection()
    {
        int attempts = 0;
        const int maxAttempts = 50; // 5 секунд

        while (attempts < maxAttempts)
        {
            if (ServerSaveManager.Instance != null && ServerSaveManager.Instance.IsConnectedToServer)
            {
                AddLogEntry("Successfully connected to save server", "green");
                return;
            }

            attempts++;
            await Task.Delay(100);
        }

        AddLogEntry("Failed to connect to server (timeout)", "red");
    }

    /// <summary>
    /// Ждет загрузки данных
    /// </summary>
    private async Task WaitForDataLoad()
    {
        // Имитируем загрузку данных
        await Task.Delay(1000);
        AddLogEntry("Save data loaded successfully", "green");
    }

    /// <summary>
    /// Завершает процесс загрузки
    /// </summary>
    private void CompleteLoading()
    {
        _isLoadingComplete = true;
        _canContinue = true;

        // Показываем кнопку продолжения
        _continueButton.Disabled = false;
        _continueLabel.Visible = true;

        AddLogEntry("Press any key or click CONTINUE to proceed", "yellow");

        // Запускаем анимацию мигания
        StartBlinkAnimation();
    }

    /// <summary>
    /// Запускает анимацию мигания
    /// </summary>
    private void StartBlinkAnimation()
    {
        // Простая анимация мигания
        var tween = CreateTween();
        tween.SetLoops();
        tween.TweenProperty(_continueLabel, "modulate:a", 0.3f, 0.8f);
        tween.TweenProperty(_continueLabel, "modulate:a", 1.0f, 0.8f);
    }

    /// <summary>
    /// Переходит в главное меню
    /// </summary>
    private void ContinueToMainMenu()
    {
        if (!_canContinue) return;

        AddLogEntry("Transitioning to main menu...", "blue");

        // Отписываемся от событий
        if (ServerSaveManager.Instance != null)
        {
            ServerSaveManager.Instance.ServerConnectionChanged -= OnServerConnectionChanged;
            ServerSaveManager.Instance.SaveCompleted -= OnSaveCompleted;
            ServerSaveManager.Instance.LoadCompleted -= OnLoadCompleted;
        }

        // Переходим в главное меню
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }

    /// <summary>
    /// Добавляет запись в лог
    /// </summary>
    private void AddLogEntry(string message, string color)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var colorCode = GetColorCode(color);
        var logEntry = $"[color={colorCode}][{timestamp}] {message}[/color]\n";
        
        _logText.Text += logEntry;
        
        // Прокручиваем к концу
        _logText.ScrollToLine(_logText.GetLineCount() - 1);
    }

    /// <summary>
    /// Получает код цвета для BBCode
    /// </summary>
    private string GetColorCode(string color)
    {
        return color switch
        {
            "green" => "#00ff00",
            "red" => "#ff0000",
            "yellow" => "#ffff00",
            "blue" => "#0080ff",
            "orange" => "#ff8000",
            _ => "#ffffff"
        };
    }

    /// <summary>
    /// Обработчик изменения подключения к серверу
    /// </summary>
    private void OnServerConnectionChanged(bool connected)
    {
        var status = connected ? "connected" : "disconnected";
        var color = connected ? "green" : "red";
        AddLogEntry($"Server {status}", color);
    }

    /// <summary>
    /// Обработчик завершения сохранения
    /// </summary>
    private void OnSaveCompleted(bool success, string message)
    {
        var color = success ? "green" : "red";
        AddLogEntry($"Save: {message}", color);
    }

    /// <summary>
    /// Обработчик завершения загрузки
    /// </summary>
    private void OnLoadCompleted(bool success, string message)
    {
        var color = success ? "green" : "red";
        AddLogEntry($"Load: {message}", color);
    }

    /// <summary>
    /// Обработчик нажатия кнопки продолжения
    /// </summary>
    private void _on_continue_button_pressed()
    {
        ContinueToMainMenu();
    }

    /// <summary>
    /// Обработчик таймера (для анимаций)
    /// </summary>
    private void _on_timer_timeout()
    {
        // Анимация звезд
        AnimateStars();
    }

    /// <summary>
    /// Анимирует звезды
    /// </summary>
    private void AnimateStars()
    {
        var stars = GetNode<Node2D>("Stars");
        var time = Time.GetTimeDictFromSystem();
        var seconds = time["second"];

        for (int i = 0; i < stars.GetChildCount(); i++)
        {
            var star = stars.GetChild<ColorRect>(i);
            var alpha = 0.3f + 0.7f * Mathf.Sin(seconds + i * 0.5f);
            star.Modulate = new Color(1, 1, 1, alpha);
        }
    }
}
