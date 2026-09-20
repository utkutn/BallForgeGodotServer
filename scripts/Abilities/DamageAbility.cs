#nullable enable

using Godot;
using System;
using System.Collections.Generic;

public sealed class DamageAbility : IBallAbility
{
    public bool ActivateOnStop { get; }
    public int Value { get; }
    public int AreaOfEffect { get; }
    public int Duration { get; }
    public bool Pierce { get; }

    public DamageAbility(int value, int aoe, int duration, bool pierce, bool activateOnStop)
    {
        Value = value;
        AreaOfEffect = aoe;
        Duration = duration;
        Pierce = pierce;
        ActivateOnStop = activateOnStop;
    }

    public void Execute(PoolBall owner, PoolBall? target = null)
    {
        if (target != null)
        {
            ApplyDamage(owner, target, Value, Pierce);
            return;
        }

        var enemies = GetEnemiesInArea(owner);
        if (enemies.Count == 0)
            return;

        GD.Print($"[ABILITY] {owner.Name} DamageAbility triggers on-stop AOE for {enemies.Count} targets.");

        if (Duration <= 1)
        {
            foreach (var enemy in enemies)
            {
                ApplyDamage(owner, enemy, Value, Pierce);
            }
            return;
        }

        for (int i = 0; i < Duration; i++)
        {
            int tickIndex = i;
            owner.GetTree().CreateTimer(1.0f * (tickIndex + 1)).Timeout += () =>
            {
                var targets = GetEnemiesInArea(owner);
                foreach (var enemy in targets)
                {
                    ApplyDamage(owner, enemy, Value, Pierce);
                }
            };
        }
    }

    private List<PoolBall> GetEnemiesInArea(PoolBall owner)
    {
        var enemies = new List<PoolBall>();
        var allBalls = owner.GetTree().GetNodesInGroup("balls");
        foreach (var node in allBalls)
        {
            if (node is PoolBall ball && ball != owner)
            {
                int teamId = ball.HasMeta("team_id") ? (int)ball.GetMeta("team_id") : -1;
                int ownerTeam = owner.HasMeta("team_id") ? (int)owner.GetMeta("team_id") : -1;
                if (teamId == ownerTeam) continue;

                if (owner.GlobalPosition.DistanceTo(ball.GlobalPosition) <= Math.Max(AreaOfEffect, 1))
                {
                    enemies.Add(ball);
                }
            }
        }
        return enemies;
    }

    private void ApplyDamage(PoolBall owner, PoolBall target, int damage, bool pierce)
    {
        if (!target.HasMeta("health"))
            return;

        if (target == owner)
            return;

        int ownerTeam = owner.HasMeta("team_id") ? (int)owner.GetMeta("team_id") : -1;
        int targetTeam = target.HasMeta("team_id") ? (int)target.GetMeta("team_id") : -1;
        if (ownerTeam == targetTeam)
            return;

        if (target.HasMeta("invulnerable") && (int)target.GetMeta("invulnerable") > 0 && !pierce)
        {
            GD.Print($"[ABILITY] {owner.Name} DamageAbility blocked by invulnerability on {target.Name}.");
            return;
        }

        int remainingDamage = damage;

        if (!pierce)
        {
            int shield = target.HasMeta("shield") ? (int)target.GetMeta("shield") : 0;
            if (shield > 0)
            {
                if (shield >= remainingDamage)
                {
                    target.SetMeta("shield", shield - remainingDamage);
                    remainingDamage = 0;
                }
                else
                {
                    remainingDamage -= shield;
                    target.SetMeta("shield", 0);
                }
            }
        }

        if (remainingDamage > 0)
        {
            int currentHealth = (int)target.GetMeta("health");
            target.SetMeta("health", currentHealth - remainingDamage);
            GD.Print($"[ABILITY] {owner.Name} DamageAbility deals {remainingDamage} damage to {target.Name}. New health: {target.GetMeta("health")}.");
        }
        else
        {
            GD.Print($"[ABILITY] {owner.Name} DamageAbility was absorbed by {target.Name}'s shield.");
        }

        SyncTargetState(target);
    }

    private void SyncTargetState(PoolBall target)
    {
        var clientNode = target.GetTree().Root.FindChild("Client", true, false) as Node;
        if (clientNode == null)
            return;

        int health = target.HasMeta("health") ? (int)target.GetMeta("health") : 0;
        int shield = target.HasMeta("shield") ? (int)target.GetMeta("shield") : 0;
        clientNode.Rpc("SyncBallStatsWithShield", target.Name, health, shield);

        if (health <= 0)
        {
            target.QueueFree();
            clientNode.Rpc("RemoveBall", target.Name);
            clientNode.Rpc("PlayBallExplosion", target.Name);
        }
    }
}
