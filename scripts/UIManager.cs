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

        // Тестовое сообщение, чтобы проверить видимость
        if (_interactionHintLabel != null)
        {
            _interactionHintLabel.Text = "TEST MESSAGE - PRESS E NEAR DOOR";
            _interactionHintLabel.Visible = true;
            Logger.Debug("Set test message to interaction hint", true);
        }

        Logger.Debug("UIManager initialized", true);
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
    }

    public override void _Process(double delta)
    {
        if (_interactionSystem != null)
            UpdateInteractionHint();
    }

    private void UpdateInteractionHint()
    {
        if (_interactionHintLabel == null)
            return;

        var currentInteractable = _interactionSystem.GetCurrentInteractable();

        if (currentInteractable != null)
        {
            _interactionHintLabel.Text = currentInteractable.GetInteractionHint();
            _interactionHintLabel.Visible = true;
        }
        else
        {
            _interactionHintLabel.Visible = false;
        }
    }
}