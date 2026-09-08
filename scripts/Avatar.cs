using Godot;
namespace Duckov;

/// Selectable player looks: body tint, backpack tint and bandana colour applied to the shared duck GLB (DuckVisual
/// re-tints the Body / BodyDark / Accent / AccentDark / Red materials per instance). Index = Profile.Avatar.
public class AvatarDef
{
    public string Name, Title;
    public Color Body, Accent, Bandana;

    public static readonly AvatarDef[] All =
    {
        new() { Name = "Classic", Title = "Yellow duck", Body = new(1f, 0.85f, 0.15f), Accent = new(0.35f, 0.25f, 0.15f), Bandana = new(0.62f, 0.08f, 0.08f) },
        new() { Name = "Snow", Title = "White duck", Body = new(0.95f, 0.95f, 0.92f), Accent = new(0.25f, 0.3f, 0.4f), Bandana = new(0.15f, 0.35f, 0.75f) },
        new() { Name = "Mallard", Title = "Green-head duck", Body = new(0.45f, 0.36f, 0.22f), Accent = new(0.2f, 0.42f, 0.28f), Bandana = new(0.1f, 0.5f, 0.3f) },
        new() { Name = "Bubblegum", Title = "Pink duck", Body = new(1f, 0.55f, 0.72f), Accent = new(0.5f, 0.2f, 0.4f), Bandana = new(0.95f, 0.85f, 0.2f) },
        new() { Name = "Night Ops", Title = "Dark duck", Body = new(0.22f, 0.22f, 0.26f), Accent = new(0.12f, 0.12f, 0.14f), Bandana = new(0.85f, 0.15f, 0.15f) },
        new() { Name = "Hi-Vis", Title = "Orange duck", Body = new(1f, 0.5f, 0.12f), Accent = new(0.15f, 0.15f, 0.18f), Bandana = new(0.1f, 0.6f, 0.9f) },
    };

    public static AvatarDef Get(int i) => All[Mathf.PosMod(i, All.Length)];
}
