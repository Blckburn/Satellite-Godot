using Godot;
using System;

/// <summary>
/// UI для тестирования сетевой архитектуры
/// Позволяет управлять сервером, подключаться и тестировать генерацию уровней
/// </summary>
public partial class NetworkTestUI : Control
{
    // UI элементы
    [Export] public Button StartServerButton { get; set; }
    [Export] public Button StopServerButton { get; set; }
    [Export] public Button ConnectButton { get; set; }
    [Export] public Button DisconnectButton { get; set; }
    [Export] public Button TestGenerationButton { get; set; }
    [Export] public Label StatusLabel { get; set; }
    [Export] public Label GeneratorInfoLabel { get; set; }
    [Export] public LineEdit ServerAddressInput { get; set; }
    [Export] public SpinBox ServerPortInput { get; set; }

    public override void _Ready()
    {
        // Находим UI элементы
        StartServerButton = GetNode<Button>("VBoxContainer/ServerSection/ServerButtons/StartServerButton");
        StopServerButton = GetNode<Button>("VBoxContainer/ServerSection/ServerButtons/StopServerButton");
        ConnectButton = GetNode<Button>("VBoxContainer/ClientSection/ClientButtons/ConnectButton");
        DisconnectButton = GetNode<Button>("VBoxContainer/ClientSection/ClientButtons/DisconnectButton");
        TestGenerationButton = GetNode<Button>("VBoxContainer/TestSection/TestGenerationButton");
        StatusLabel = GetNode<Label>("VBoxContainer/StatusSection/StatusLabel");
        GeneratorInfoLabel = GetNode<Label>("VBoxContainer/StatusSection/GeneratorInfoLabel");
        ServerAddressInput = GetNode<LineEdit>("VBoxContainer/ClientSection/ConnectionSettings/ServerAddressInput");
        ServerPortInput = GetNode<SpinBox>("VBoxContainer/ClientSection/ConnectionSettings/ServerPortInput");

        // Подписываемся на события NetworkManager
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.Connected += OnConnected;
            NetworkManager.Instance.Disconnected += OnDisconnected;
            NetworkManager.Instance.ServerStarted += OnServerStarted;
            NetworkManager.Instance.ServerStopped += OnServerStopped;
        }

        // Подписываемся на события LevelGenerationManager
        if (LevelGenerationManager.Instance != null)
        {
            LevelGenerationManager.Instance.GeneratorChanged += OnGeneratorChanged;
            LevelGenerationManager.Instance.GenerationStarted += OnGenerationStarted;
            LevelGenerationManager.Instance.GenerationCompleted += OnGenerationCompleted;
            LevelGenerationManager.Instance.GenerationFailed += OnGenerationFailed;
        }

        // Подключаем кнопки
        if (StartServerButton != null)
            StartServerButton.Pressed += OnStartServerPressed;
        
        if (StopServerButton != null)
            StopServerButton.Pressed += OnStopServerPressed;
        
        if (ConnectButton != null)
            ConnectButton.Pressed += OnConnectPressed;
        
        if (DisconnectButton != null)
            DisconnectButton.Pressed += OnDisconnectPressed;
        
        if (TestGenerationButton != null)
            TestGenerationButton.Pressed += OnTestGenerationPressed;

        // Инициализируем поля ввода
        if (ServerAddressInput != null)
            ServerAddressInput.Text = NetworkManager.Instance?.ServerAddress ?? "127.0.0.1";
        
        if (ServerPortInput != null)
            ServerPortInput.Value = NetworkManager.Instance?.ServerPort ?? 7777;

        UpdateUI();
    }

    public override void _Process(double delta)
    {
        UpdateStatus();
    }

    /// <summary>
    /// Обновление UI элементов
    /// </summary>
    private void UpdateUI()
    {
        if (NetworkManager.Instance == null) return;

        var isServerRunning = NetworkManager.Instance.IsServerRunning;
        var isConnected = NetworkManager.Instance.IsConnected;

        if (StartServerButton != null)
            StartServerButton.Disabled = isServerRunning;
        
        if (StopServerButton != null)
            StopServerButton.Disabled = !isServerRunning;
        
        if (ConnectButton != null)
            ConnectButton.Disabled = isConnected || isServerRunning;
        
        if (DisconnectButton != null)
            DisconnectButton.Disabled = !isConnected;
        
        if (TestGenerationButton != null)
            TestGenerationButton.Disabled = !isConnected && !(LevelGenerationManager.Instance?.IsClientAvailable ?? false);
    }

    /// <summary>
    /// Обновление статуса
    /// </summary>
    private void UpdateStatus()
    {
        if (StatusLabel == null) return;

        var networkStatus = "Network: ";
        var generatorStatus = "Generator: ";

        if (NetworkManager.Instance != null)
        {
            if (NetworkManager.Instance.IsServerRunning)
            {
                networkStatus += $"Server running (Port: {NetworkManager.Instance.ServerPort}, Peers: {NetworkManager.Instance.ConnectedPeers})";
            }
            else if (NetworkManager.Instance.IsConnected)
            {
                networkStatus += $"Connected to {NetworkManager.Instance.ServerAddress}:{NetworkManager.Instance.ServerPort}";
            }
            else
            {
                networkStatus += "Disconnected";
            }
        }
        else
        {
            networkStatus += "Not available";
        }

        if (LevelGenerationManager.Instance != null)
        {
            generatorStatus += LevelGenerationManager.Instance.GetStatusInfo();
        }
        else
        {
            generatorStatus += "Not available";
        }

        StatusLabel.Text = $"{networkStatus}\n{generatorStatus}";
    }

    // Обработчики кнопок
    private void OnStartServerPressed()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StartServer();
        }
        UpdateUI();
    }

    private void OnStopServerPressed()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.StopServer();
        }
        UpdateUI();
    }

    private void OnConnectPressed()
    {
        if (NetworkManager.Instance != null)
        {
            // Обновляем настройки из UI
            if (ServerAddressInput != null)
                NetworkManager.Instance.ServerAddress = ServerAddressInput.Text;
            
            if (ServerPortInput != null)
                NetworkManager.Instance.ServerPort = (int)ServerPortInput.Value;

            NetworkManager.Instance.ConnectToServer();
        }
        UpdateUI();
    }

    private void OnDisconnectPressed()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.DisconnectFromServer();
        }
        UpdateUI();
    }

    private async void OnTestGenerationPressed()
    {
        GD.Print("=== Test Generation Button Pressed ===");
        
        if (LevelGenerationManager.Instance == null)
        {
            GD.PrintErr("LevelGenerationManager.Instance is null!");
            return;
        }

        GD.Print($"LevelGenerationManager status: {LevelGenerationManager.Instance.GetStatusInfo()}");
        GD.Print($"Current generator: {LevelGenerationManager.Instance.CurrentGeneratorInfo}");

        var parameters = new GenerationParameters
        {
            BiomeType = 0,
            MapWidth = 50,
            MapHeight = 50,
            Seed = (int)GD.Randi(),
            MaxRooms = 10,
            MinRoomSize = 5,
            MaxRoomSize = 12
        };

        GD.Print($"Parameters: Biome={parameters.BiomeType}, Size={parameters.MapWidth}x{parameters.MapHeight}, Seed={parameters.Seed}");
        GD.Print("Starting level generation...");
        
        var levelData = await LevelGenerationManager.Instance.GenerateLevelAsync(parameters);
        
        if (levelData != null && levelData.Width > 0)
        {
            GD.Print($"✅ Test generation successful: {levelData.Width}x{levelData.Height}, Biome={levelData.BiomeType}");
        }
        else
        {
            GD.PrintErr("❌ Test generation failed - returned null or invalid data");
        }
    }

    // Обработчики событий NetworkManager
    private void OnConnected()
    {
        GD.Print("NetworkTestUI: Connected to server");
        UpdateUI();
    }

    private void OnDisconnected()
    {
        GD.Print("NetworkTestUI: Disconnected from server");
        UpdateUI();
    }

    private void OnServerStarted()
    {
        GD.Print("NetworkTestUI: Server started");
        
        // Обновляем статус генераторов после запуска сервера
        if (LevelGenerationManager.Instance != null)
        {
            LevelGenerationManager.Instance.UpdateGeneratorStatus();
        }
        
        UpdateUI();
    }

    private void OnServerStopped()
    {
        GD.Print("NetworkTestUI: Server stopped");
        UpdateUI();
    }

    // Обработчики событий LevelGenerationManager
    private void OnGeneratorChanged(string generatorInfo)
    {
        GD.Print($"NetworkTestUI: Generator changed to {generatorInfo}");
        if (GeneratorInfoLabel != null)
        {
            GeneratorInfoLabel.Text = $"Current Generator: {generatorInfo}";
        }
        UpdateUI();
    }

    private void OnGenerationStarted()
    {
        GD.Print("NetworkTestUI: Generation started");
        if (TestGenerationButton != null)
        {
            TestGenerationButton.Text = "Generating...";
            TestGenerationButton.Disabled = true;
        }
    }

    private void OnGenerationCompleted()
    {
        GD.Print("NetworkTestUI: Generation completed");
        if (TestGenerationButton != null)
        {
            TestGenerationButton.Text = "Test Generation";
            TestGenerationButton.Disabled = false;
        }
    }

    private void OnGenerationFailed(string error)
    {
        GD.PrintErr($"NetworkTestUI: Generation failed - {error}");
        if (TestGenerationButton != null)
        {
            TestGenerationButton.Text = "Test Generation";
            TestGenerationButton.Disabled = false;
        }
    }
}
