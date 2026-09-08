using Godot;
namespace Duckov;

public interface IDamageable
{
    void TakeDamage(int amount, Vector3 fromPosition, bool fromPlayer);
}

/// Physics layer bits shared by every script.
public static class Layers
{
    public const uint World = 1;
    public const uint Player = 2;
    public const uint Enemy = 4;
}
