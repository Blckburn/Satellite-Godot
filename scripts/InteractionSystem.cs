using Godot;
using System;
using System.Linq;

public partial class InteractionSystem : Node
{
    [Export] public NodePath PlayerPath;

    private Player _player;
    private IInteractable _nearestInteractable;
    private IInteractable _lastInteractable;
    private int _lastInteractableCount = 0;

    // Отслеживаем состояние клавиши взаимодействия
    private bool _isInteractionKeyPressed = false;

    // Синглтон для удобного доступа
    public static InteractionSystem Instance { get; private set; }

    public override void _Ready()
    {
        // Настройка синглтона
        if (Instance == null)
            Instance = this;
        else
            Logger.Debug("Multiple InteractionSystem instances found!", true);

        // Добавляем в группу для быстрого поиска
        AddToGroup("InteractionSystem");
        Logger.Debug("InteractionSystem added to group", true);

        // Получаем ссылку на игрока
        if (!string.IsNullOrEmpty(PlayerPath))
            _player = GetNode<Player>(PlayerPath);

        // Если путь не указан, ищем игрока в сцене
        if (_player == null)
        {
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Player player)
                _player = player;
        }

        if (_player == null)
            Logger.Error("InteractionSystem: Player not found");
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        UpdateNearestInteractable();

        // Проверяем состояние клавиши взаимодействия
        bool eKeyCurrentlyPressed = Input.IsActionPressed("interact");

        // Если клавиша была нажата, а теперь отпущена
        if (_isInteractionKeyPressed && !eKeyCurrentlyPressed)
        {
            OnInteractionKeyReleased();
        }

        _isInteractionKeyPressed = eKeyCurrentlyPressed;

        // Проверяем, если игрок вышел из зоны взаимодействия во время процесса
        if (_lastInteractable != _nearestInteractable)
        {
            // Если был предыдущий объект взаимодействия и это интерактивный объект с процессом
            if (_lastInteractable != null && _lastInteractable is IInteraction interaction
                && interaction.IsInteracting())
            {
                interaction.CancelInteraction();
                Logger.Debug("Interaction canceled - player moved away", false);
            }

            _lastInteractable = _nearestInteractable;
        }
    }

    // Обработка нажатия клавиши взаимодействия
    public bool HandleInteraction()
    {
        if (_player == null)
            return false;

        UpdateNearestInteractable();

        if (_nearestInteractable != null && _nearestInteractable.CanInteract(_player))
        {
            return _nearestInteractable.Interact(_player);
        }

        return false;
    }

    // Обработка отпускания клавиши взаимодействия
    private void OnInteractionKeyReleased()
    {
        // Уведомляем текущий объект взаимодействия об отпускании клавиши
        if (_nearestInteractable is IInteraction interaction)
        {
            // Если объект поддерживает интерфейс длительного взаимодействия
            if (interaction.IsInteracting() && interaction.GetInteractionProgress() < 1.0f)
            {
                interaction.CancelInteraction();
                Logger.Debug("Interaction canceled - key released", false);
            }
        }
    }

    // Обновление ближайшего интерактивного объекта
    public void UpdateNearestInteractable()
    {
        if (_player == null)
            return;

        _nearestInteractable = FindNearestInteractable();
    }

    // Поиск ближайшего интерактивного объекта
    private IInteractable FindNearestInteractable()
    {
        if (_player == null)
            return null;

        IInteractable nearest = null;
        float minDistance = float.MaxValue;

        // Получаем все узлы, реализующие интерфейс IInteractable
        var interactables = GetTree().GetNodesInGroup("Interactables");

        // Логируем только при изменении количества объектов
        if (interactables.Count != _lastInteractableCount)
        {
            Logger.Debug($"Found {interactables.Count} interactable objects", false);
            _lastInteractableCount = interactables.Count;
        }

        foreach (var obj in interactables)
        {
            if (obj is IInteractable interactable && obj is Node2D node)
            {
                float distance = _player.GlobalPosition.DistanceTo(node.GlobalPosition);

                if (distance <= interactable.GetInteractionRadius() && distance < minDistance)
                {
                    minDistance = distance;
                    nearest = interactable;
                }
            }
        }

        // Логируем только при изменении ближайшего объекта
        if (nearest != _nearestInteractable)
        {
            if (nearest != null)
                Logger.Debug($"New nearest interactable: {nearest}", false);
            else if (_nearestInteractable != null)
                Logger.Debug("No interactable in range now", false);
        }

        return nearest;
    }

    // Получение текущего интерактивного объекта
    public IInteractable GetCurrentInteractable()
    {
        return _nearestInteractable;
    }

    // Установка текущего интерактивного объекта напрямую
    public void SetCurrentInteractable(IInteractable interactable)
    {
        _nearestInteractable = interactable;
    }
}