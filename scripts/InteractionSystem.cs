using Godot;
using System;
using System.Linq;
using System.Xml.Linq;

public partial class InteractionSystem : Node
{
    [Export] public NodePath PlayerPath;

    private Player _player;
    private IInteractable _nearestInteractable;
    private IInteractable _lastInteractable;
    private int _lastInteractableCount = 0;

    // Отслеживаем состояние клавиши взаимодействия
    private bool _isInteractionKeyPressed = false;

    // Для отслеживания текущего ресурса, с которым взаимодействуем
    private ResourceNode _currentInteractingResource;

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
        // Сначала обновляем ближайший интерактивный объект
        UpdateNearestInteractable();

        // Явно проверяем состояние клавиши E (взаимодействие)
        bool eKeyCurrentlyPressed = Input.IsActionPressed("interact");

        // ВАЖНО: Если клавиша была нажата, а теперь отпущена - отправляем событие
        if (_isInteractionKeyPressed && !eKeyCurrentlyPressed)
        {
            Logger.Debug("InteractionSystem: Detected E key released", true);
            OnInteractionKeyReleased();
        }

        // Сохраняем текущее состояние клавиши для следующей проверки
        _isInteractionKeyPressed = eKeyCurrentlyPressed;

        // Проверяем, если игрок вышел из зоны взаимодействия во время процесса
        if (_lastInteractable != _nearestInteractable)
        {
            // Если был предыдущий объект взаимодействия и это интерактивный объект с процессом
            if (_lastInteractable != null && _lastInteractable is IInteraction interaction
                && interaction.IsInteracting())
            {
                interaction.CancelInteraction();
                Logger.Debug("InteractionSystem: Interaction canceled - player moved away", true);
            }

            _lastInteractable = _nearestInteractable;
        }

        // Обновляем процесс взаимодействия
        UpdateInteraction(delta);
    }


    // Обработка нажатия клавиши взаимодействия
    public bool HandleInteraction()
    {
        if (_player == null)
            return false;

        UpdateNearestInteractable();

        if (_nearestInteractable != null && _nearestInteractable.CanInteract(_player))
        {
            // Если это ResourceNode, сохраняем ссылку и устанавливаем флаг нажатия клавиши
            if (_nearestInteractable is ResourceNode resourceNode)
            {
                _currentInteractingResource = resourceNode;
                _isInteractionKeyPressed = true; // ВАЖНО: устанавливаем флаг нажатия клавиши
                Logger.Debug($"InteractionSystem: Started interaction with resource: {resourceNode.Name}", true);
            }

            return _nearestInteractable.Interact(_player);
        }

        return false;
    }


    // Обработка отпускания клавиши взаимодействия
    private void OnInteractionKeyReleased()
    {
        // Логируем для отладки
        Logger.Debug("InteractionSystem: Interaction key E released", true);

        // Уведомляем текущий ресурс об отпускании клавиши, если это ResourceNode
        if (_currentInteractingResource != null && _currentInteractingResource.IsInteracting())
        {
            // Явный вызов метода на ресурсе
            _currentInteractingResource.OnInteractionKeyReleased();
            Logger.Debug("InteractionSystem: Notified resource of key release", true);
        }
        // Для дверей
        else if (_nearestInteractable is Door door && door.IsInteracting())
        {
            door.OnInteractionKeyReleased();
            Logger.Debug("InteractionSystem: Notified door of key release", true);
        }
        else if (_nearestInteractable is IInteraction interaction && interaction.IsInteracting())
        {
            // Для других объектов с интерфейсом IInteraction
            interaction.CancelInteraction();
            Logger.Debug("InteractionSystem: Called CancelInteraction on interactive object", true);
        }
    }

    // Обновление активного процесса взаимодействия
    private void UpdateInteraction(double delta)
    {
        // Обновляем состояние для активного процесса взаимодействия с дверью
        if (_currentInteractingResource != null && _currentInteractingResource.IsInteracting())
        {
            // Процесс продолжается - проверяем, видно ли ресурс еще и близко ли игрок
            if (!IsInstanceValid(_currentInteractingResource) ||
                !IsInInteractionRange(_player, _currentInteractingResource))
            {
                // Если ресурс исчез или далеко, отменяем взаимодействие
                CancelCurrentInteraction();
            }
        }
        else
        {
            // Процесс завершен или прерван - сбрасываем текущий ресурс
            _currentInteractingResource = null;
        }
    }

    // Проверка, находится ли игрок в зоне взаимодействия
    private bool IsInInteractionRange(Player player, IInteractable interactable)
    {
        if (player == null || interactable == null || !(interactable is Node2D interactableNode))
            return false;

        float distance = player.GlobalPosition.DistanceTo(interactableNode.GlobalPosition);
        return distance <= interactable.GetInteractionRadius();
    }

    // Отмена текущего взаимодействия
    private void CancelCurrentInteraction()
    {
        if (_currentInteractingResource != null && _currentInteractingResource.IsInteracting())
        {
            _currentInteractingResource.CancelInteraction();
            Logger.Debug("Resource interaction canceled", false);
        }
        _currentInteractingResource = null;
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

    // Метод для уведомления о нажатии клавиши
    public void NotifyKeyPressed(Key key)
    {
        if (key == Key.E)
            _isInteractionKeyPressed = true;
    }

    // Метод для уведомления об отпускании клавиши
    public void NotifyKeyReleased(Key key)
    {
        if (key == Key.E)
        {
            _isInteractionKeyPressed = false;
            OnInteractionKeyReleased();
        }
    }
}