using Godot;

public interface IDamageable
{
    void TakeDamage(float amount, Node source);
    bool IsDead();
    float GetHealth();
    float GetMaxHealth();
}