using Godot;
using System;

public partial class UIManager : CanvasLayer
{
    // Синглтон для доступа к UIManager из других классов
    public static UIManager Instance { get; private set; }

    // Экспортируемая переменная для прямой ссылки на InteractionSystem
    [Export] public NodePath InteractionSystemPath;

    // Ссылки на UI элементы
    private Label _interactionHintLabel;
    private ProgressBar _interactionProgressBar;
    
    // DEBUG HUD для координат углов карты
    private Label _debugCornersLabel;
    private Label _seedLabel;
    private int _currentSeed = -1;

    // Ссылка на InteractionSystem
    private InteractionSystem _interactionSystem;

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
            Instance = this;
        else
            Logger.Debug("Multiple UIManager instances found!", true);

        // Инициализируем UI компоненты
        InitializeUIComponents();

        // Находим InteractionSystem
        _interactionSystem = GetInteractionSystem();

        Logger.Debug("UIManager initialized", true);
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    private InteractionSystem GetInteractionSystem()
    {
        // Сначала пробуем использовать экспортированный путь
        if (!string.IsNullOrEmpty(InteractionSystemPath))
        {
            var system = GetNode<InteractionSystem>(InteractionSystemPath);
            if (system != null)
            {
                Logger.Debug("InteractionSystem found via exported path", true);
                return system;
            }
        }

        // Затем пробуем найти через группу
        var systems = GetTree().GetNodesInGroup("InteractionSystem");
        if (systems.Count > 0 && systems[0] is InteractionSystem groupSystem)
        {
            Logger.Debug("InteractionSystem found via group", true);
            return groupSystem;
        }

        // Наконец, ищем по имени узла в сцене
        var sceneSystem = GetTree().Root.FindChild("InteractionSystem", true, false);
        if (sceneSystem is InteractionSystem foundSystem)
        {
            Logger.Debug("InteractionSystem found by name in scene", true);
            return foundSystem;
        }

        Logger.Error("UIManager: InteractionSystem not found!");
        return null;
    }

    private void InitializeUIComponents()
    {
        // Инициализация метки подсказки взаимодействия
        _interactionHintLabel = GetNodeOrNull<Label>("%InteractionHint");

        if (_interactionHintLabel == null)
            Logger.Error("UIManager: InteractionHint label not found");
        else
        {
            // Устанавливаем начальные свойства метки
            _interactionHintLabel.Visible = false;
        }

        // Инициализация прогресс-бара
        _interactionProgressBar = GetNodeOrNull<ProgressBar>("%InteractionProgress");

        if (_interactionProgressBar == null)
            Logger.Error("UIManager: InteractionProgress bar not found");
        else
        {
            // Устанавливаем начальные свойства прогресс-бара
            _interactionProgressBar.Visible = false;
            _interactionProgressBar.MinValue = 0;
            _interactionProgressBar.MaxValue = 100;
        }
        
        // Инициализация DEBUG HUD для координат углов
        CreateDebugCornersHUD();
        CreateSeedHUD();
    }

    public override void _Process(double delta)
    {
        if (_interactionSystem != null)
            UpdateInteractionUI();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.CtrlPressed && key.AltPressed && key.Keycode == Key.C)
            {
                if (_currentSeed >= 0)
                {
                    DisplayServer.ClipboardSet(_currentSeed.ToString());
                    // временно изменим текст на подтверждение
                    if (_seedLabel != null)
                    {
                        string prev = _seedLabel.Text;
                        _seedLabel.Text = $"Seed: {_currentSeed}  (copied)";
                        GetTree().CreateTimer(1.2).Timeout += () =>
                        {
                            UpdateSeedLabelText();
                        };
                    }
                }
            }
        }
    }

    private void UpdateInteractionUI()
    {
        if (_interactionHintLabel == null)
            return;

        var currentInteractable = _interactionSystem.GetCurrentInteractable();

        if (currentInteractable != null)
        {
            // Обновляем текст подсказки
            string hintText = currentInteractable.GetInteractionHint();
            _interactionHintLabel.Text = hintText;
            _interactionHintLabel.Visible = true;

            // Обновляем прогресс-бар, если поддерживается
            if (_interactionProgressBar != null &&
                currentInteractable is IInteraction interaction &&
                interaction.IsInteracting())
            {
                float progress = interaction.GetInteractionProgress();
                _interactionProgressBar.Value = progress * 100;
                _interactionProgressBar.Visible = true;

                // Выводим в лог для отладки
                Logger.Debug($"Interaction progress: {progress * 100:F1}%", false);
            }
            else if (_interactionProgressBar != null)
            {
                _interactionProgressBar.Visible = false;
            }
        }
        else
        {
            // Если нет объекта взаимодействия, скрываем элементы UI
            _interactionHintLabel.Visible = false;

            if (_interactionProgressBar != null)
                _interactionProgressBar.Visible = false;
        }
    }

    // Публичные методы для управления UI

    public void ShowInteractionHint(string text)
    {
        if (_interactionHintLabel != null)
        {
            _interactionHintLabel.Text = text;
            _interactionHintLabel.Visible = true;
        }
    }

    public void HideInteractionHint()
    {
        if (_interactionHintLabel != null)
            _interactionHintLabel.Visible = false;
    }

    public void UpdateProgressBar(float progress)
    {
        if (_interactionProgressBar != null)
        {
            _interactionProgressBar.Value = progress * 100;
        }
    }
    
    // ===== 🎯 DEBUG HUD ДЛЯ КООРДИНАТ УГЛОВ КАРТЫ =====
    
    private void CreateDebugCornersHUD()
    {
        // Создаем Label для отображения координат углов карты
        _debugCornersLabel = new Label();
        _debugCornersLabel.Name = "DebugCornersLabel";
        
        // Позиционируем в левом верхнем углу экрана
        _debugCornersLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        _debugCornersLabel.Position = new Vector2(10, 10);
        _debugCornersLabel.Size = new Vector2(400, 150);
        
        // Стилизация
        _debugCornersLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _debugCornersLabel.VerticalAlignment = VerticalAlignment.Top;
        
        // Изначально скрыто - мешает анализу
        _debugCornersLabel.Visible = false;
        
        // Добавляем к UI
        AddChild(_debugCornersLabel);
        
        Logger.Debug("DEBUG HUD for corner coordinates created", true);
    }
    
    public void UpdateDebugCorners(string cornersInfo)
    {
        if (_debugCornersLabel != null)
        {
            _debugCornersLabel.Text = cornersInfo;
            Logger.Debug($"Updated DEBUG HUD with corners info: {cornersInfo}", false);
        }
    }

    private void CreateSeedHUD()
    {
        _seedLabel = new Label();
        _seedLabel.Name = "SeedLabel";
        _seedLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _seedLabel.Position = new Vector2(-300, 10);
        _seedLabel.Size = new Vector2(290, 38);
        _seedLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _seedLabel.Visible = true;
        AddChild(_seedLabel);
    }

    public static void ShowSeed(int seed)
    {
        if (Instance?. _seedLabel != null)
        {
            Instance._currentSeed = seed;
            Instance.UpdateSeedLabelText();
            Instance._seedLabel.Visible = true;
        }
    }

    private void UpdateSeedLabelText()
    {
        if (_seedLabel == null) return;
        _seedLabel.Text = $"Seed: {_currentSeed}\nPress Ctrl+Alt+C to copy";
    }
    
    public static void SetMapCorners(Vector2I topLeft, Vector2I topRight, Vector2I bottomLeft, Vector2I bottomRight, 
                                    Vector2 topLeftWorld, Vector2 topRightWorld, Vector2 bottomLeftWorld, Vector2 bottomRightWorld)
    {
        if (Instance != null)
        {
            string cornersInfo = $"🎯 УГЛЫ КАРТЫ:\n" +
                               $"TopLeft: {topLeft} -> ({topLeftWorld.X:F0}, {topLeftWorld.Y:F0})\n" +
                               $"TopRight: {topRight} -> ({topRightWorld.X:F0}, {topRightWorld.Y:F0})\n" +
                               $"BottomLeft: {bottomLeft} -> ({bottomLeftWorld.X:F0}, {bottomLeftWorld.Y:F0})\n" +
                               $"BottomRight: {bottomRight} -> ({bottomRightWorld.X:F0}, {bottomRightWorld.Y:F0})";
            
            Instance.UpdateDebugCorners(cornersInfo);
        }
    }

    public void HideProgressBar()
    {
        if (_interactionProgressBar != null)
            _interactionProgressBar.Visible = false;
    }
}