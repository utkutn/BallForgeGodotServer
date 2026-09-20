#nullable enable

using Godot;

public sealed class PhysicsAbility : IBallAbility
{
    public bool ActivateOnStop { get; }
    public float Push { get; }
    public bool Pull { get; }
    public float Bounce { get; }
    public float PushResist { get; }
    public bool SoftImpact { get; }

    public PhysicsAbility(float push, bool pull, float bounce, float pushResist, bool softImpact, bool activateOnStop)
    {
        Push = push;
        Pull = pull;
        Bounce = bounce;
        PushResist = pushResist;
        SoftImpact = softImpact;
        ActivateOnStop = activateOnStop;
    }

    public void Execute(PoolBall owner, PoolBall? target = null)
    {
        if (target == null)
            return;

        if (Pull)
        {
            Vector2 direction = (owner.GlobalPosition - target.GlobalPosition).Normalized();
            target.ApplyCentralImpulse(direction * 120f);
            GD.Print($"[ABILITY] {owner.Name} PhysicsAbility pulls {target.Name}.");
            return;
        }

        if (Push > 0f)
        {
            Vector2 direction = (target.GlobalPosition - owner.GlobalPosition).Normalized();
            target.ApplyCentralImpulse(direction * Push * 80f);
            GD.Print($"[ABILITY] {owner.Name} PhysicsAbility pushes {target.Name}.");
        }

        if (Bounce > 0f)
        {
            target.ApplyCentralImpulse(new Vector2(0, -Bounce * 60f));
            GD.Print($"[ABILITY] {owner.Name} PhysicsAbility gives bounce to {target.Name}.");
        }

        if (PushResist > 0f && owner.HasMeta("damage"))
        {
            owner.SetMeta("damage", (int)((int)owner.GetMeta("damage") * (1f - PushResist)));
            GD.Print($"[ABILITY] {owner.Name} PhysicsAbility reduces own damage by PushResist {PushResist}.");
        }

        if (SoftImpact)
        {
            target.SetMeta("bounce_override", 1);
            GD.Print($"[ABILITY] {owner.Name} PhysicsAbility softens impact on {target.Name}.");
        }
    }
}
