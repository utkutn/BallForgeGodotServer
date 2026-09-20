#nullable enable

using System;
using System.Text.Json;

public static class AbilityFactory
{
    public static IBallAbility? CreateAbility(string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonString);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!root.TryGetProperty("type", out JsonElement typeProp))
                return null;

            string type = typeProp.GetString() ?? string.Empty;
            return type switch
            {
                "damage" => CreateDamageAbility(root),
                "defense" => CreateDefenseAbility(root),
                "physics" => CreatePhysicsAbility(root),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DamageAbility CreateDamageAbility(JsonElement root)
    {
        int value = GetInt(root, "value");
        int aoe = GetInt(root, "aoe");
        int duration = GetInt(root, "duration");
        bool pierce = GetBool(root, "pierce");
        bool onStop = GetBool(root, "on_stop");

        return new DamageAbility(value, aoe, duration, pierce, onStop);
    }

    private static DefenseAbility CreateDefenseAbility(JsonElement root)
    {
        int shield = GetInt(root, "shield");
        bool invulnerable = GetBool(root, "invulnerable");
        float reflect = GetFloat(root, "reflect");
        bool onStop = GetBool(root, "on_stop");

        return new DefenseAbility(shield, invulnerable, reflect, onStop);
    }

    private static PhysicsAbility CreatePhysicsAbility(JsonElement root)
    {
        float push = GetFloat(root, "push");
        bool pull = GetBool(root, "pull");
        float bounce = GetFloat(root, "bounce");
        float pushResist = GetFloat(root, "push_resist");
        bool softImpact = GetBool(root, "soft_impact");
        bool onStop = GetBool(root, "on_stop");

        return new PhysicsAbility(push, pull, bounce, pushResist, softImpact, onStop);
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
            return value;
        return defaultValue;
    }

    private static float GetFloat(JsonElement element, string propertyName, float defaultValue = 0f)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out float value))
            return value;
        return defaultValue;
    }

    private static bool GetBool(JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property))
        {
            if (property.ValueKind == JsonValueKind.True) return true;
            if (property.ValueKind == JsonValueKind.False) return false;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int numeric))
                return numeric != 0;
            if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out bool parsed))
                return parsed;
        }
        return defaultValue;
    }
}
