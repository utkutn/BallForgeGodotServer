using Godot;

/// <summary>
/// Centralized server-side logging for special ability activation and effects.
/// </summary>
public static class AbilityLogger
{
    public static void Log(PoolBall owner, string abilityName, string status, string details = "")
    {
        string message = $"[SPECIAL] {abilityName} | {status} | Ball: {owner.Name}";

        if (!string.IsNullOrWhiteSpace(details))
            message += $" | {details}";

        GD.Print(message);
    }
}
