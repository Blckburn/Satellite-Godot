using Godot;
using System;

public partial class ItemTooltip : PanelContainer
{
    // Ссылки на дочерние узлы
    private TextureRect _itemIcon;
    private Label _itemName;
    private Label _itemDescription;
    private Label _typeValue;
    private Label _weightValue;
    private Label _valueValue;

    // Текущий отображаемый предмет
    private Item _currentItem;

    // Таймер для задержки появления тултипа
    private Timer _showTimer;

    // Отступ от курсора
    private Vector2 _cursorOffset = new Vector2(10, 10);

    public override void _Ready()
    {
        // Получаем ссылки на дочерние узлы
        _itemIcon = GetNode<TextureRect>("MarginContainer/VBoxContainer/HBoxContainer/ItemIcon");
        _itemName = GetNode<Label>("MarginContainer/VBoxContainer/HBoxContainer/ItemName");
        _itemDescription = GetNode<Label>("MarginContainer/VBoxContainer/ItemDescription");

        // Получаем ссылки на метки значений
        var statsContainer = GetNode<GridContainer>("MarginContainer/VBoxContainer/StatsContainer");
        _typeValue = statsContainer.GetNode<Label>("TypeValue");
        _weightValue = statsContainer.GetNode<Label>("WeightValue");
        _valueValue = statsContainer.GetNode<Label>("ValueValue");

        // Создаем и настраиваем таймер задержки
        _showTimer = new Timer();
        _showTimer.OneShot = true;
        _showTimer.WaitTime = 0.5f; // Полсекунды задержки
        _showTimer.Timeout += OnShowTimerTimeout;
        AddChild(_showTimer);

        // Изначально скрываем тултип
        Visible = false;

        // Делаем тултип сверху остальных элементов
        ZIndex = 10;

        Logger.Debug("ItemTooltip initialized", true);
    }

    public override void _Process(double delta)
    {
        // Если тултип видим, следим за курсором мыши
        if (Visible)
        {
            Position = GetViewport().GetMousePosition() + _cursorOffset;

            // Проверяем, не выходит ли тултип за границы экрана
            Vector2 viewportSize = GetViewport().GetVisibleRect().Size;

            // Если выходит за пределы окна вправо, смещаем влево
            if (Position.X + Size.X > viewportSize.X)
            {
                Position = new Vector2(viewportSize.X - Size.X, Position.Y);
            }

            // Если выходит за пределы окна вниз, смещаем вверх
            if (Position.Y + Size.Y > viewportSize.Y)
            {
                Position = new Vector2(Position.X, viewportSize.Y - Size.Y);
            }
        }
    }

    // Показываем тултип для указанного предмета
    public void ShowTooltip(Item item)
    {
        if (item == null)
        {
            HideTooltip();
            return;
        }

        _currentItem = item;

        // Обновляем данные, но не показываем тултип сразу
        UpdateTooltipData();

        // Запускаем таймер задержки
        _showTimer.Start();
    }

    // Обработчик таймера - показываем тултип после задержки
    private void OnShowTimerTimeout()
    {
        Visible = true;
    }

    // Скрываем тултип
    public void HideTooltip()
    {
        Visible = false;
        _showTimer.Stop();
        _currentItem = null;
    }

    // Обновление данных в тултипе
    private void UpdateTooltipData()
    {
        if (_currentItem == null)
            return;

        // Обновляем название и иконку
        _itemName.Text = _currentItem.DisplayName;
        _itemIcon.Texture = _currentItem.Icon;

        // Обновляем описание
        _itemDescription.Text = _currentItem.Description;

        // Обновляем статы предмета
        _typeValue.Text = _currentItem.Type.ToString();
        _weightValue.Text = $"{_currentItem.Weight:0.0}";
        _valueValue.Text = _currentItem.Value.ToString();

        // Дополнительная информация в зависимости от типа предмета
        switch (_currentItem.Type)
        {
            case ItemType.Weapon:
                // Для оружия можно добавить урон и другие характеристики
                break;
            case ItemType.Consumable:
                // Для потребляемых предметов - эффекты и т.д.
                break;
            case ItemType.Resource:
                // Для ресурсов - дополнительная информация
                break;
        }
    }
}