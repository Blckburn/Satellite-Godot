using Godot;

public interface IInteraction
{
    bool IsInteracting();  // Происходит ли взаимодействие сейчас
    float GetInteractionProgress();  // Прогресс взаимодействия от 0 до 1
    void CancelInteraction();  // Метод для отмены взаимодействия
}