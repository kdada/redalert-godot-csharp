namespace RedAlert.Simulation;

/// <summary>Explicit shared policy for map scenery combat; independent of world entities.</summary>
public static class ClassicTerrainCombatRules
{
    public const string Revision = "map-terrain-combat-2";
    public const string SourceRulesSha256 = ClassicMapObjectRules.SourceRulesSha256;
    // Original rules.ini TREE01..TREE30 omit Strength and Armor. These are
    // deliberate simulation fallbacks, NOT verified original engine defaults.
    public const int TreeStrength = 100;
    public const string TreeArmor = "wood";
    // Original rules.ini [CombatDamage], line 676. One health pool covers a whole span.
    public const int BridgeStrength = 1500;
    // Explicit simulation armor policy; BridgeStrength does not specify armor.
    public const string BridgeArmor = "concrete";
    // TREE01 explicitly has IsFlammable=no. Destruction adds no burning mechanic.
    public const bool TreesFlammable = false;

    public static bool IsTree(ClassicMapObjectRule definition) => definition.Kind == ClassicMapObjectKind.Terrain
        && (NumberedName(definition.Name, "TREE", 30) || NumberedName(definition.Name, "TIBTRE", 3));

    static bool NumberedName(string name, string prefix, int maximum)
    {
        if (name.Length != prefix.Length + 2 || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var tens = name[prefix.Length] - '0'; var ones = name[prefix.Length + 1] - '0';
        if (tens is < 0 or > 9 || ones is < 0 or > 9) return false;
        var number = tens * 10 + ones;
        return number >= 1 && number <= maximum;
    }

    public static void AddToFingerprint(ref StableHash hash)
    {
        hash.Add(Revision); hash.Add(SourceRulesSha256); hash.Add(TreeStrength); hash.Add(TreeArmor);
        hash.Add(BridgeStrength); hash.Add(BridgeArmor); hash.Add(TreesFlammable);
    }
}
