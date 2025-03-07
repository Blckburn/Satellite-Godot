using Godot;

public interface IInteractable
{
	string GetInteractionHint();
	bool CanInteract(Node source);
	bool Interact(Node source);
	float GetInteractionRadius();
}
