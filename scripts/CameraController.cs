using Godot;
using System;

public partial class CameraController : Camera2D
{
    // Ссылка на игрока для следования
    [Export] public NodePath PlayerPath { get; set; }

    // Настройки зума
    [Export] public float ZoomMin { get; set; } = 0.2f;
    [Export] public float ZoomMax { get; set; } = 1.5f;
    [Export] public float ZoomDefault { get; set; } = 0.4f;
    [Export] public float ZoomSpeed { get; set; } = 0.1f;

    // Скорость движения камеры при использовании стрелок
    [Export] public float PanSpeed { get; set; } = 300.0f;

    // Инвертировать зум (true = колесо вверх приближает)
    [Export] public bool InvertZoom { get; set; } = true;

    // Режим камеры
    [Export] public bool FollowPlayer { get; set; } = true;

    // Ссылки на игрока
    private Node2D _player;

    // Текущий зум
    private Vector2 _currentZoom;

    // Флаг для управления с клавиатуры
    private bool _keyboardControl = false;

    public override void _Ready()
    {
        AddToGroup("Camera");
        // Активируем камеру
        Enabled = true;

        // Устанавливаем начальный зум
        _currentZoom = Vector2.One * ZoomDefault;
        Zoom = _currentZoom;

        // Получаем ссылку на игрока если путь указан
        if (!string.IsNullOrEmpty(PlayerPath))
        {
            _player = GetNode<Node2D>(PlayerPath);
        }
        else
        {
            // Ищем игрока по группе
            var players = GetTree().GetNodesInGroup("Player");
            if (players.Count > 0 && players[0] is Node2D player)
            {
                _player = player;
            }
        }

        if (_player != null && FollowPlayer)
        {
            // Перемещаем камеру на позицию игрока
            GlobalPosition = _player.GlobalPosition;
            Logger.Debug($"Camera following player at {_player.GlobalPosition}", true);
        }
        else
        {
            Logger.Debug("Player not found or not set to follow", true);
        }
    }

    public override void _Process(double delta)
    {
        // Если нужно следовать за игроком и игрок найден
        if (_player != null && FollowPlayer && !_keyboardControl)
        {
            GlobalPosition = _player.GlobalPosition;
        }

        // Обработка движения камеры стрелками (но не WASD)
        HandleKeyboardControl((float)delta);
    }

    public override void _Input(InputEvent @event)
    {
        // Обработка зума камеры с инвертированием при необходимости
        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
            {
                float zoomChange = InvertZoom ? -ZoomSpeed : ZoomSpeed;
                ZoomCamera(zoomChange);
            }
            else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
            {
                float zoomChange = InvertZoom ? ZoomSpeed : -ZoomSpeed;
                ZoomCamera(zoomChange);
            }
        }

        // Если нажаты только стрелки, переключаемся на клавиатурное управление
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            Key keycode = (Key)keyEvent.Keycode;
            if (keycode == Key.Up || keycode == Key.Down || keycode == Key.Left || keycode == Key.Right)
            {
                _keyboardControl = true;
            }

            // Переключение назад на режим следования за игроком
            if (keycode == Key.F)
            {
                _keyboardControl = false;
                FollowPlayer = true;

                if (_player != null)
                {
                    GlobalPosition = _player.GlobalPosition;
                    Logger.Debug("Camera returned to following player", false);
                }
            }

            // Сброс зума
            if (keycode == Key.R)
            {
                _currentZoom = Vector2.One * ZoomDefault;
                Zoom = _currentZoom;
                Logger.Debug($"Camera zoom reset to {ZoomDefault}", false);
            }

            // Клавиша Home центрирует карту
            if (keycode == Key.Home)
            {
                var levelGenerator = GetTree().Root.GetNodeOrNull<LevelGenerator>("Node2D/LevelGenerator");
                if (levelGenerator != null)
                {
                    // Центрируем на середине карты
                    int centerX = levelGenerator.MapWidth / 2;
                    int centerY = levelGenerator.MapHeight / 2;

                    // Получаем позицию тайла в мировых координатах
                    Vector2I centerTile = new Vector2I(centerX, centerY);
                    // Для изометрии нужно преобразовать координаты
                    Vector2 worldPos = MapTileToIsometricWorld(centerTile);

                    // Устанавливаем позицию камеры
                    GlobalPosition = worldPos;
                    _keyboardControl = true;
                    Logger.Debug($"Camera centered on map at tile ({centerX}, {centerY})", false);
                }
            }
        }
    }

    // Вспомогательный метод для преобразования координат тайла в мировые координаты для изометрии
    private Vector2 MapTileToIsometricWorld(Vector2I tilePos)
    {
        // Приблизительные значения для изометрии 2:1 - может потребоваться корректировка
        float tileWidth = 64.0f;  // Ширина тайла
        float tileHeight = 32.0f; // Высота тайла

        // Формула преобразования: 
        float x = (tilePos.X - tilePos.Y) * tileWidth / 2.0f;
        float y = (tilePos.X + tilePos.Y) * tileHeight / 2.0f;

        return new Vector2(x, y);
    }

    // Обработка управления камерой с клавиатуры (только стрелки)
    private void HandleKeyboardControl(float delta)
    {
        if (!_keyboardControl)
            return;

        Vector2 moveDirection = Vector2.Zero;

        // Получаем направление ТОЛЬКО на основе стрелок
        if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.Up))
            moveDirection.Y -= 1;
        if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.Down))
            moveDirection.Y += 1;
        if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.Left))
            moveDirection.X -= 1;
        if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.Right))
            moveDirection.X += 1;

        // Если направление не нулевое, перемещаем камеру
        if (moveDirection != Vector2.Zero)
        {
            // Нормализуем для равномерной скорости во всех направлениях
            moveDirection = moveDirection.Normalized();

            // Учитываем зум для скорости движения
            float adjustedSpeed = PanSpeed / Zoom.X;

            // Перемещаем камеру
            GlobalPosition += moveDirection * adjustedSpeed * delta;
        }
    }

    // Изменение зума камеры
    private void ZoomCamera(float zoomChange)
    {
        float newZoom = _currentZoom.X + zoomChange;
        newZoom = Mathf.Clamp(newZoom, ZoomMin, ZoomMax);

        _currentZoom = Vector2.One * newZoom;
        Zoom = _currentZoom;
    }

    // Публичный метод для установки новой позиции камеры
    public void SetPosition(Vector2 position)
    {
        GlobalPosition = position;
        _keyboardControl = true; // Переключаемся на ручное управление
    }

    // Центрирование камеры на игроке
    public void CenterOnPlayer()
    {
        if (_player != null)
        {
            GlobalPosition = _player.GlobalPosition;
            _keyboardControl = false;
            FollowPlayer = true;
        }
    }
}