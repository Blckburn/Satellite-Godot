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
    }

    public override void _Process(double delta)
    {
        if (_interactionSystem != null)
            UpdateInteractionUI();
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
            _interactionProgressBar.Value = Mathf.Clamp(progress * 100, 0, 100);
            _interactionProgressBar.Visible = true;
        }
    }

    public void HideProgressBar()
    {
        if (_interactionProgressBar != null)
            _interactionProgressBar.Visible = false;
    }
}