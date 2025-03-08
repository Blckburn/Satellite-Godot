using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class InteractionSystem : Node
{

    [Export] public NodePath PlayerPath;

    private Player _player;
    private IInteractable _currentInteractable;
    private int _lastInteractableCount = 0;
    private IInteractable _lastNearestInteractable = null;

    // Синглтон для удобного доступа
    public static InteractionSystem Instance { get; private set; }

    public override void _Ready()
    {
        AddToGroup("InteractionSystem");
        GD.Print("InteractionSystem added to group");
        // Настройка синглтона
        if (Instance == null)
            Instance = this;
        else
            GD.PushWarning("Множественные экземпляры InteractionSystem не поддерживаются!");

        // Добавляем в группу для быстрого поиска
        AddToGroup("InteractionSystem");

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
            GD.PushError("InteractionSystem: Player не найден");
    }

    public override void _ExitTree()
    {
        // Очистка синглтона при удалении
        if (Instance == this)
            Instance = null;
    }

    // Обработка нажатия клавиши взаимодействия
    public bool HandleInteraction()
    {
        if (_player == null)
            return false;

        UpdateNearestInteractable();

        if (_currentInteractable != null && _currentInteractable.CanInteract(_player))
        {
            return _currentInteractable.Interact(_player);
        }

        return false;
    }

    // Обновление ближайшего интерактивного объекта
    public void UpdateNearestInteractable()
    {
        if (_player == null)
            return;

        var nearest = FindNearestInteractable();

        // Если ближайший объект изменился
        if (_currentInteractable != nearest)
        {
            _currentInteractable = nearest;

            }
    }

    private IInteractable FindNearestInteractable()
    {
        if (_player == null)
            return null;

        IInteractable nearest = null;
        float minDistance = float.MaxValue;

        var interactables = GetTree().GetNodesInGroup("Interactables");

        // Логируем только при изменении количества объектов, не каждый кадр
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

        // Здесь нам нужно отслеживать изменение текущего объекта,
        // а не логировать каждый кадр
        if (nearest != _lastNearestInteractable)
        {
            if (nearest != null)
                Logger.Debug($"New nearest interactable: {nearest}", false);
            else if (_lastNearestInteractable != null)
                Logger.Debug("No interactable in range now", false);

            _lastNearestInteractable = nearest;
        }

        return nearest;
    }

    // Получение текущего интерактивного объекта
    public IInteractable GetCurrentInteractable()
    {
        return _currentInteractable;
    }

    // Установка текущего интерактивного объекта напрямую
    public void SetCurrentInteractable(IInteractable interactable)
    {
        if (_currentInteractable != interactable)
        {
            _currentInteractable = interactable;
        }
    }

    public override void _Process(double delta)
    {
        // Постоянно обновляем ближайший интерактивный объект
        UpdateNearestInteractable();
    }

}