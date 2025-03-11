using Godot;
using System;

public partial class BiomeInfoUI : CanvasLayer
{
    // Ссылка на LevelGenerator
    [Export] public NodePath LevelGeneratorPath { get; set; }

    // UI элементы
    private Label _biomeLabel;
    private Panel _biomePanel;
    private Timer _fadeTimer;

    // Ссылка на генератор уровней
    private LevelGenerator _levelGenerator;

    // Время отображения панели
    [Export] public float DisplayTime { get; set; } = 3.0f;

    public override void _Ready()
    {
        // Создаем UI элементы
        CreateUI();

        // Получаем ссылку на LevelGenerator
        _levelGenerator = GetLevelGenerator();

        // Подписываемся на события
        if (_levelGenerator != null)
        {
            // Непосредственно подписаться не можем, поэтому будем обновлять информацию в _Process
        }

        // Создаем и настраиваем таймер
        _fadeTimer = new Timer();
        _fadeTimer.OneShot = true;
        _fadeTimer.WaitTime = DisplayTime;
        _fadeTimer.Timeout += OnFadeTimerTimeout;
        AddChild(_fadeTimer);

        // Скрываем панель изначально
        _biomePanel.Visible = false;

        Logger.Debug("BiomeInfoUI initialized", true);
    }

    // Создание UI элементов
    private void CreateUI()
    {
        // Создаем панель
        _biomePanel = new Panel();
        _biomePanel.Size = new Vector2(300, 80);
        _biomePanel.Position = new Vector2(50, 50);
        _biomePanel.Visible = false;

        // Стилизация панели
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.1f, 0.1f, 0.2f, 0.8f);
        stylebox.BorderWidthBottom = stylebox.BorderWidthLeft =
        stylebox.BorderWidthRight = stylebox.BorderWidthTop = 2;
        stylebox.BorderColor = new Color(0.4f, 0.4f, 0.8f);
        stylebox.CornerRadiusBottomLeft = stylebox.CornerRadiusBottomRight =
        stylebox.CornerRadiusTopLeft = stylebox.CornerRadiusTopRight = 10;

        _biomePanel.AddThemeStyleboxOverride("panel", stylebox);

        // Создаем заголовок
        var titleLabel = new Label();
        titleLabel.Text = "BIOME DISCOVERED";
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.Position = new Vector2(0, 10);
        titleLabel.Size = new Vector2(300, 20);
        titleLabel.AddThemeColorOverride("font_color", new Color(1, 1, 0.6f));
        titleLabel.AddThemeFontSizeOverride("font_size", 14);

        // Создаем метку для названия биома
        _biomeLabel = new Label();
        _biomeLabel.Text = "Grassland";
        _biomeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _biomeLabel.Position = new Vector2(0, 40);
        _biomeLabel.Size = new Vector2(300, 30);
        _biomeLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _biomeLabel.AddThemeFontSizeOverride("font_size", 18);

        // Добавляем элементы в панель
        _biomePanel.AddChild(titleLabel);
        _biomePanel.AddChild(_biomeLabel);

        // Добавляем панель в CanvasLayer
        AddChild(_biomePanel);
    }

    // Получение ссылки на LevelGenerator
    private LevelGenerator GetLevelGenerator()
    {
        if (!string.IsNullOrEmpty(LevelGeneratorPath))
        {
            return GetNode<LevelGenerator>(LevelGeneratorPath);
        }

        // Пробуем найти по имени узла
        return GetTree().Root.GetNode<LevelGenerator>("Node2D/LevelGenerator");
    }

    // Отображение информации о биоме
    public void ShowBiomeInfo(string biomeName)
    {
        // Устанавливаем текст
        _biomeLabel.Text = biomeName;

        // Показываем панель
        _biomePanel.Visible = true;
        _biomePanel.Modulate = new Color(1, 1, 1, 1);

        // Запускаем таймер для скрытия
        _fadeTimer.Start();

        Logger.Debug($"Showing biome info: {biomeName}", false);
    }

    // Обработчик таймера - скрываем панель с анимацией
    private void OnFadeTimerTimeout()
    {
        // Создаем анимацию для плавного исчезновения
        var tween = CreateTween();
        tween.TweenProperty(_biomePanel, "modulate:a", 0.0f, 0.5f);
        tween.TweenCallback(Callable.From(() => _biomePanel.Visible = false));
    }

    // Проверка, был ли сгенерирован новый уровень
    private int _lastBiomeType = -1;

    public override void _Process(double delta)
    {
        if (_levelGenerator != null)
        {
            // Проверяем, изменился ли тип биома
            if (_levelGenerator.BiomeType != _lastBiomeType)
            {
                _lastBiomeType = _levelGenerator.BiomeType;

                // Получаем название биома
                string biomeName = GetBiomeName(_levelGenerator.BiomeType);

                // Показываем информацию
                ShowBiomeInfo(biomeName);
            }
        }
    }

    // Получение названия биома по его типу
    private string GetBiomeName(int biomeType)
    {
        switch (biomeType)
        {
            case 1: return "Forest";
            case 2: return "Desert";
            case 3: return "Ice";
            case 4: return "Techno";
            case 5: return "Anomal";
            case 6: return "Lava Springs";
            default: return "Grassland";
        }
    }
}