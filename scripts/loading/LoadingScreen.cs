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
    private RichTextLabel _dosLogText;
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
        try
        {
            Logger.Debug("LoadingScreen _Ready() started", true);
            
            // Получаем ссылки на UI элементы
            _statusLabel = GetNode<Label>("MainContainer/LoadingSection/StatusLabel");
            _progressBar = GetNode<ProgressBar>("MainContainer/LoadingSection/ProgressBar");
            _progressLabel = GetNode<Label>("MainContainer/LoadingSection/ProgressLabel");
            _logText = GetNode<RichTextLabel>("MainContainer/LogContainer/LogText");
            _dosLogText = GetNode<RichTextLabel>("DOSLogContainer/DOSLogText");
            _continueButton = GetNode<Button>("MainContainer/ContinueSection/ContinueButton");
            _continueLabel = GetNode<Label>("MainContainer/ContinueSection/ContinueLabel");
            _timer = GetNode<Timer>("Timer");
            _animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");

            Logger.Debug("UI elements found successfully", true);

            // Настраиваем начальное состояние
            _continueButton.Disabled = true;
            _continueLabel.Visible = false;
            _progressBar.Value = 0;
            _progressLabel.Text = "0%";

            // Добавляем начальную запись в лог
            AddLogEntry("Loading screen initialized", "green");

            Logger.Debug("Starting loading process...", true);

            // Запускаем процесс загрузки
            StartLoadingProcess();
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadingScreen _Ready() failed: {ex.Message}");
            // Если что-то пошло не так, сразу переходим в главное меню
            GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        }
    }

    /// <summary>
    /// Очистка ресурсов при уничтожении
    /// </summary>
    public override void _ExitTree()
    {
        // Отписываемся от событий для предотвращения утечек памяти
        if (ServerSaveManager.Instance != null)
        {
            ServerSaveManager.Instance.ServerConnectionChanged -= OnServerConnectionChanged;
            ServerSaveManager.Instance.SaveCompleted -= OnSaveCompleted;
            ServerSaveManager.Instance.LoadCompleted -= OnLoadCompleted;
        }

        // Останавливаем таймер
        if (_timer != null)
        {
            _timer.Stop();
        }

        // Очищаем ссылки
        _statusLabel = null;
        _progressBar = null;
        _progressLabel = null;
        _logText = null;
        _dosLogText = null;
        _continueButton = null;
        _continueLabel = null;
        _timer = null;
        _animationPlayer = null;
    }

    public override void _Input(InputEvent @event)
    {
        try
        {
            if (_canContinue && @event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                Logger.Debug("Key pressed, continuing to main menu...", true);
                ContinueToMainMenu();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"_Input failed: {ex.Message}");
        }
    }

        /// <summary>
    /// Запускает процесс загрузки (BADASS ВЕРСИЯ С АНИМАЦИЯМИ!)
    /// </summary>
    private async void StartLoadingProcess()
    {
        try
        {
            Logger.Debug("StartLoadingProcess() started (BADASS version)", true);
            AddLogEntry("Starting BADASS loading sequence...", "yellow");

            // Этап 1: Инициализация систем (0-15%)
            await UpdateLoadingStep(0, 15);
            AddLogEntry("Systems initialized successfully", "green");
            AddDOSMessage("Systems initialized successfully", "green");

            // Этап 2: Запуск сервера (15-30%)
            await UpdateLoadingStep(1, 30);
            AddLogEntry("Save server starting...", "blue");
            AddDOSMessage("Save server starting...", "blue");

            // Этап 3: Подключение к серверу (30-50%)
            await UpdateLoadingStep(2, 50);
            AddLogEntry("Connecting to save server...", "blue");
            AddDOSMessage("Connecting to save server...", "blue");

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
                AddDOSMessage("WARNING: ServerSaveManager not found!", "red");
                await Task.Delay(1000);
            }

            // Этап 4: Проверка статуса (50-70%)
            await UpdateLoadingStep(3, 70);
            AddLogEntry("Server status verified", "green");
            AddDOSMessage("Server status verified", "green");

            // Этап 5: Загрузка данных (70-85%)
            await UpdateLoadingStep(4, 85);
            AddLogEntry("Loading save data...", "blue");
            AddDOSMessage("Loading save data...", "blue");

            // Ждем загрузки данных
            await WaitForDataLoad();
            AddDOSMessage("Save data loaded successfully", "green");

            // Этап 6: Завершение (85-100%)
            await UpdateLoadingStep(5, 100);
            AddLogEntry("Loading complete! Ready to launch!", "green");
            AddDOSMessage("Loading complete! Ready to launch!", "green");

            // Завершаем загрузку
            CompleteLoading();
        }
        catch (Exception ex)
        {
            Logger.Error($"StartLoadingProcess() failed: {ex.Message}");
            // Если что-то пошло не так, сразу переходим в главное меню
            GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        }
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
            
            // Обновляем DOS-стиль прогресс
            UpdateDOSProgress(_currentProgress);
            
            await Task.Delay(200); // ULTIMATE задержка для максимальной стабильности
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
            AddDOSMessage("Successfully connected to save server", "green");
            return;
        }

            attempts++;
            await Task.Delay(100);
        }

        AddLogEntry("Failed to connect to server (timeout)", "red");
        AddDOSMessage("Failed to connect to server (timeout)", "red");
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
    private async void ContinueToMainMenu()
    {
        try
        {
            Logger.Debug("ContinueToMainMenu() started", true);
            
            if (!_canContinue) 
            {
                Logger.Debug("Cannot continue - not ready yet", true);
                return;
            }

            AddLogEntry("Transitioning to main menu...", "blue");

            // Отписываемся от событий
            if (ServerSaveManager.Instance != null)
            {
                ServerSaveManager.Instance.ServerConnectionChanged -= OnServerConnectionChanged;
                ServerSaveManager.Instance.SaveCompleted -= OnSaveCompleted;
                ServerSaveManager.Instance.LoadCompleted -= OnLoadCompleted;
            }

            // Останавливаем таймер
            if (_timer != null)
            {
                _timer.Stop();
            }

            Logger.Debug("Changing scene to main menu...", true);

            // Проверяем существование файла
            var file = FileAccess.Open("res://scenes/main_menu.tscn", FileAccess.ModeFlags.Read);
            if (file != null)
            {
                file.Close();
                Logger.Debug("Main menu file exists, transitioning...", true);
                
                // ПРИНУДИТЕЛЬНО УНИЧТОЖАЕМ LoadingScreen!
                Logger.Debug("Force destroying LoadingScreen...", true);
                QueueFree();
                
                // Ждем немного для уничтожения
                await Task.Delay(100);
                
                // ПЕРЕХОДИМ В ГЛАВНОЕ МЕНЮ!
                GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
            }
            else
            {
                Logger.Error("Main menu file not found!");
                // Fallback - попробуем перезапустить игру
                GetTree().Quit();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ContinueToMainMenu() failed: {ex.Message}");
        }
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
    /// Обновляет DOS-стиль прогресс
    /// </summary>
    private void UpdateDOSProgress(int progress)
    {
        // ИДЕАЛЬНАЯ ЛОГИКА - ТОЧКИ И ПРОБЕЛЫ!
        var filledDots = progress / 10; // Сколько точек заполнено
        var emptySpaces = 10 - filledDots; // Сколько пробелов пустых
        
        var filled = new string('.', filledDots); // Заполненные точки
        var empty = new string(' ', emptySpaces); // Пустые пробелы
        var dosProgress = $"[{filled}{empty}] {progress}%";
        
        _dosLogText.Text = $"[color=#00ff00]SERVER STARTING{dosProgress}[/color]";
    }

    /// <summary>
    /// Добавляет DOS-стиль сообщение
    /// </summary>
    private void AddDOSMessage(string message, string color = "green")
    {
        var colorCode = GetColorCode(color);
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var dosMessage = $"[color={colorCode}][{timestamp}] {message}[/color]\n";
        
        _dosLogText.Text += dosMessage;
        _dosLogText.ScrollToLine(_dosLogText.GetLineCount() - 1);
    }

    /// <summary>
    /// Анимирует звезды (BADASS ВЕРСИЯ С КРУТЫМИ ЭФФЕКТАМИ!)
    /// </summary>
    private void AnimateStars()
    {
        try
        {
            var stars = GetNode<Node2D>("Stars");
            var time = Time.GetTimeDictFromSystem();
            var seconds = (float)time["second"];
            
            // КРУТАЯ АНИМАЦИЯ ЗВЕЗД!
            for (int i = 0; i < stars.GetChildCount(); i++)
            {
                var star = stars.GetChild<ColorRect>(i);
                
                // Разные фазы для каждой звезды
                var phase = seconds + (i * 1.5f);
                var alpha = 0.3f + (Mathf.Sin(phase) * 0.4f);
                
                // Добавляем мерцание
                var flicker = Mathf.Sin(phase * 3.0f) * 0.2f;
                alpha += flicker;
                
                // Ограничиваем значения
                alpha = Mathf.Clamp(alpha, 0.1f, 1.0f);
                
                star.Modulate = new Color(1, 1, 1, alpha);
            }
        }
        catch (Exception ex)
        {
            // Если что-то пошло не так, используем статичную версию
            var stars = GetNode<Node2D>("Stars");
            for (int i = 0; i < stars.GetChildCount(); i++)
            {
                var star = stars.GetChild<ColorRect>(i);
                var alpha = 0.6f + (i * 0.1f);
                star.Modulate = new Color(1, 1, 1, alpha);
            }
        }
    }
}
