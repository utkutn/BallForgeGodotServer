#nullable enable

using Godot;

public sealed class DefenseAbility : IBallAbility
{
    public bool ActivateOnStop { get; }
    public int Shield { get; }
    public bool Invulnerable { get; }
    public float Reflect { get; }

    public DefenseAbility(int shield, bool invulnerable, float reflect, bool activateOnStop)
    {
        Shield = shield;
        Invulnerable = invulnerable;
        Reflect = reflect;
        ActivateOnStop = activateOnStop;
    }

    public void Execute(PoolBall owner, PoolBall? target = null)
    {
        if (!owner.HasMeta("health"))
            return;

        if (Shield > 0)
        {
            int currentShield = owner.HasMeta("shield") ? (int)owner.GetMeta("shield") : 0;
            owner.SetMeta("shield", currentShield + Shield);
            GD.Print($"[ABILITY] {owner.Name} DefenseAbility grants {Shield} shield. Total shield: {owner.GetMeta("shield")}.");
        }

        if (Invulnerable)
        {
            owner.SetMeta("invulnerable", 1);
            GD.Print($"[ABILITY] {owner.Name} DefenseAbility sets invulnerable flag.");
        }

        if (Reflect > 0f)
        {
            owner.SetMeta("reflect", Reflect);
            GD.Print($"[ABILITY] {owner.Name} DefenseAbility enables {Reflect * 100}% reflect.");
        }
    }
}

