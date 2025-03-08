using Godot;
using System;

public partial class SimpleCamera : Camera2D
{
    [Export] public float ZoomMin { get; set; } = 0.5f; // Минимальный зум (дальний вид)
    [Export] public float ZoomMax { get; set; } = 2.0f; // Максимальный зум (близкий вид)
    [Export] public float ZoomSpeed { get; set; } = 0.1f; // Скорость изменения зума

    private Vector2 _currentZoom = Vector2.One; // Начальный зум 1.0

    public override void _Ready()
    {
        // Активируем камеру
        Enabled = true;

        // Применяем начальный зум
        Zoom = _currentZoom;

        GD.Print("Simple camera initialized");
    }

    public override void _Input(InputEvent @event)
    {
        // Обработка зума на колесико мыши
        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
            {
                // Приближение (увеличение зума)
                ZoomCamera(-ZoomSpeed);
            }
            else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
            {
                // Отдаление (уменьшение зума)
                ZoomCamera(ZoomSpeed);
            }
        }
    }

    private void ZoomCamera(float zoomChange)
    {
        // Изменяем текущий зум
        float newZoomLevel = _currentZoom.X + zoomChange;

        // Ограничиваем зум минимальным и максимальным значениями
        newZoomLevel = Mathf.Clamp(newZoomLevel, ZoomMin, ZoomMax);

        // Применяем новый зум
        _currentZoom = Vector2.One * newZoomLevel;
        Zoom = _currentZoom;

        GD.Print($"Camera zoom: {newZoomLevel}");
    }
}