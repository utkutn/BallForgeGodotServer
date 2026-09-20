#nullable enable

using Godot;

public interface IBallAbility
{
    bool ActivateOnStop { get; }
    void Execute(PoolBall owner, PoolBall? target = null);
}
