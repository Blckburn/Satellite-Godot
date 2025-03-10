using Godot;
using System;

/// <summary>
/// Компонент для динамического управления Z-индексом объектов в изометрической проекции
/// </summary>
public partial class IsometricZSorter : Node
{
    // Ссылка на TileMap для определения координат в сетке
    [Export] public NodePath TileMapPath { get; set; }

    // Ссылка на отслеживаемый объект (обычно игрок)
    [Export] public NodePath TargetNodePath { get; set; }

    // Базовый Z-индекс игрока
    [Export] public int BaseZIndex { get; set; } = 10;

    // Размеры тайла для расчетов (обычно совпадают с размерами тайлсета)
    [Export] public Vector2 TileSize { get; set; } = new Vector2(64, 32);

    // Включить отладочный вывод
    [Export] public bool DebugMode { get; set; } = false;

    // Ссылки на объекты
    private TileMap _tileMap;
    private Node2D _targetNode;

    // Текущие координаты игрока в сетке
    private Vector2I _currentTilePos = Vector2I.Zero;

    public override void _Ready()
    {
        // Находим TileMap и целевой узел
        if (!string.IsNullOrEmpty(TileMapPath))
            _tileMap = GetNode<TileMap>(TileMapPath);
        else
            _tileMap = GetTree().Root.FindChild("TileMap", true, false) as TileMap;

        if (!string.IsNullOrEmpty(TargetNodePath))
            _targetNode = GetNode<Node2D>(TargetNodePath);
        else
            _targetNode = GetTree().GetFirstNodeInGroup("Player") as Node2D;

        if (_tileMap == null || _targetNode == null)
        {
            Logger.Error("IsometricZSorter: Unable to find TileMap or target node!");
            return;
        }

        Logger.Debug($"IsometricZSorter initialized for {_targetNode.Name}", true);
    }

    public override void _Process(double delta)
    {
        if (_tileMap == null || _targetNode == null)
            return;

        UpdateZIndex();
    }

    // Обновление Z-индекса на основе позиции в изометрической сетке
    private void UpdateZIndex()
    {
        // Получаем мировую позицию игрока
        Vector2 worldPos = _targetNode.GlobalPosition;

        // Преобразуем мировые координаты в координаты тайловой сетки
        Vector2I tilePos = WorldToIsometricTile(worldPos);

        // Если позиция изменилась, обновляем Z-индекс
        if (tilePos != _currentTilePos)
        {
            _currentTilePos = tilePos;

            // Формула для Z-индекса в изометрии: базовый Z + (X + Y)
            // Эта формула обеспечивает, что объекты "ниже и правее" в изометрическом мире
            // отрисовываются поверх объектов "выше и левее"
            int newZIndex = BaseZIndex + (tilePos.X + tilePos.Y);

            // Устанавливаем новый Z-индекс
            _targetNode.ZIndex = newZIndex;

            if (DebugMode)
            {
                Logger.Debug($"Updated Z-index for {_targetNode.Name} to {newZIndex} at tile position {tilePos}", false);
            }
        }
    }

    // Преобразование мировых координат в координаты изометрической сетки
    private Vector2I WorldToIsometricTile(Vector2 worldPos)
    {
        // Для классической изометрии 2:1
        float tileWidth = TileSize.X;
        float tileHeight = TileSize.Y;

        // Инвертируем преобразование из изометрии в декартовы координаты
        float cartX = (worldPos.X / (tileWidth / 2) + worldPos.Y / (tileHeight / 2)) / 2;
        float cartY = (worldPos.Y / (tileHeight / 2) - worldPos.X / (tileWidth / 2)) / 2;

        return new Vector2I(Mathf.FloorToInt(cartX), Mathf.FloorToInt(cartY));
    }

    // Метод для вызова из других скриптов: устанавливает базовый Z-индекс
    public void SetBaseZIndex(int baseZIndex)
    {
        BaseZIndex = baseZIndex;
        UpdateZIndex();
    }

    // Временно установить фиксированный Z-индекс (например, для анимаций)
    public void SetFixedZIndex(int zIndex, float duration = 1.0f)
    {
        if (_targetNode == null)
            return;

        // Запоминаем текущий базовый Z-индекс
        int oldBaseZIndex = BaseZIndex;

        // Устанавливаем фиксированный Z-индекс
        _targetNode.ZIndex = zIndex;

        // Запоминаем текущее состояние обработки
        bool wasEnabled = IsProcessing();

        // Приостанавливаем обработку
        SetProcess(false);

        // Создаем таймер для возврата к динамическому Z-индексу
        GetTree().CreateTimer(duration).Timeout += () => {
            BaseZIndex = oldBaseZIndex;
            SetProcess(wasEnabled);
            UpdateZIndex();
        };
    }

    // Вспомогательный метод для проверки статуса обработки
    private bool IsProcessing()
    {
        return ProcessMode != ProcessModeEnum.Disabled;
    }
}