using Godot;
using System;

public partial class GameManagerTest : Node
{
    public override void _Ready()
    {
        // Проверка доступа к GameManager
        var gameManager = GetNode<GameManager>("/root/GameManager");

        if (gameManager != null)
        {
            // Сохраняем тестовые данные
            gameManager.SetData("TestKey", "Testing GameManager");

            // Получаем и выводим данные
            string testValue = gameManager.GetData<string>("TestKey");
            GD.Print($"GameManager test: {testValue}");

            Logger.Debug("GameManager test successful!", true);
        }
        else
        {
            GD.Print("ERROR: GameManager not found!");
            Logger.Error("GameManager not found in autoload!");
        }
    }
}