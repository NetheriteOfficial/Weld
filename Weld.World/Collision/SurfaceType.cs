namespace Weld.World.Collision;

public enum SurfaceType : byte
{
    Default = 0,
    Grass,
    Rock,
    Dirt,
    Sand,
    Mud,
    Concrete,
    Asphalt,
    Metal,
    Wood,
    Water,
    Glass,
    Gravel,
}

public readonly record struct SurfaceProperties(SurfaceType Type, float Friction, float Restitution, bool Walkable, float FootstepVolume, string Sound)
{
    public static readonly SurfaceProperties[] Table =
    {
        new(SurfaceType.Default, 0.8f, 0.0f, true, 1.0f, "step_default"),
        new(SurfaceType.Grass, 0.9f, 0.0f, true, 0.6f, "step_grass"),
        new(SurfaceType.Rock, 1.0f, 0.05f, true, 1.0f, "step_rock"),
        new(SurfaceType.Dirt, 0.85f, 0.0f, true, 0.7f, "step_dirt"),
        new(SurfaceType.Sand, 0.7f, 0.0f, true, 0.5f, "step_sand"),
        new(SurfaceType.Mud, 0.5f, 0.0f, true, 0.6f, "step_mud"),
        new(SurfaceType.Concrete, 1.0f, 0.05f, true, 1.0f, "step_concrete"),
        new(SurfaceType.Asphalt, 1.0f, 0.05f, true, 1.0f, "step_asphalt"),
        new(SurfaceType.Metal, 0.6f, 0.2f, true, 1.2f, "step_metal"),
        new(SurfaceType.Wood, 0.8f, 0.1f, true, 0.9f, "step_wood"),
        new(SurfaceType.Water, 0.2f, 0.0f, false, 0.8f, "step_water"),
        new(SurfaceType.Glass, 0.4f, 0.1f, true, 1.1f, "step_glass"),
        new(SurfaceType.Gravel, 0.9f, 0.0f, true, 0.9f, "step_gravel"),
    };

    public static SurfaceProperties Of(SurfaceType type) => Table[(int)type];

    private static readonly (string Token, SurfaceType Type)[] Tokens =
    {
        ("GRASS", SurfaceType.Grass),
        ("ROCK", SurfaceType.Rock),
        ("STONE", SurfaceType.Rock),
        ("CLIFF", SurfaceType.Rock),
        ("DIRT", SurfaceType.Dirt),
        ("SOIL", SurfaceType.Dirt),
        ("PATH", SurfaceType.Dirt),
        ("SAND", SurfaceType.Sand),
        ("BEACH", SurfaceType.Sand),
        ("MUD", SurfaceType.Mud),
        ("CONCRETE", SurfaceType.Concrete),
        ("ASPHALT", SurfaceType.Asphalt),
        ("ROAD", SurfaceType.Asphalt),
        ("TARMAC", SurfaceType.Asphalt),
        ("METAL", SurfaceType.Metal),
        ("STEEL", SurfaceType.Metal),
        ("WOOD", SurfaceType.Wood),
        ("PLANK", SurfaceType.Wood),
        ("WATER", SurfaceType.Water),
        ("SEA", SurfaceType.Water),
        ("GLASS", SurfaceType.Glass),
        ("GRAVEL", SurfaceType.Gravel),
    };

    public static SurfaceType Parse(string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return SurfaceType.Default;
        var upper = materialName.ToUpperInvariant();
        foreach (var (token, type) in Tokens)
            if (upper.Contains(token, StringComparison.Ordinal)) return type;
        return SurfaceType.Default;
    }
}
