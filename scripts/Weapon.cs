using Godot;
using System.Collections.Generic;
namespace Duckov;

public enum WeaponId { Rifle, Shotgun, IceSmg, ElecSniper, Firework }
public enum Element { None, Ice, Elec, Fire }

/// Static weapon data, Duckov-flavoured (names from the Escape from Duckov wiki: QBZ-191, Saiga-12K, Frost SR-3M, TS-128, Firework Gun).
/// Each weapon has its own tracer, muzzle flash, hit effect and a status effect for the elemental ones.
public class WeaponDef
{
    public WeaponId Id;
    public string Name, Short;
    public Element Element;
    public int Damage, MagSize, Pellets = 1, Pierce, AmmoBox;
    public float FireInterval, ReloadTime, BulletSpeed, SpreadDeg, MaxRange, Kick, MuzzleZ;
    public Color Accent;
    public bool Rocket;
    public int Slot => (int)Id + 1;

    static readonly Dictionary<WeaponId, WeaponDef> _all = new()
    {
        [WeaponId.Rifle] = new() { Id = WeaponId.Rifle, Name = "QBZ-191 Rifle", Short = "QBZ-191", Damage = 20, FireInterval = 0.16f, MagSize = 12, ReloadTime = 1.3f, BulletSpeed = 45f, MaxRange = 60f, Kick = 0.06f, AmmoBox = 24, MuzzleZ = 0.95f, Accent = new Color(1f, 0.85f, 0.35f) },
        [WeaponId.Shotgun] = new() { Id = WeaponId.Shotgun, Name = "Saiga-12K Shotgun", Short = "Saiga-12K", Damage = 9, Pellets = 6, SpreadDeg = 11f, FireInterval = 0.9f, MagSize = 5, ReloadTime = 2.0f, BulletSpeed = 40f, MaxRange = 13f, Kick = 0.22f, AmmoBox = 10, MuzzleZ = 1.0f, Accent = new Color(1f, 0.55f, 0.2f) },
        [WeaponId.IceSmg] = new() { Id = WeaponId.IceSmg, Name = "Frost SR-3M SMG", Short = "SR-3M", Element = Element.Ice, Damage = 8, SpreadDeg = 4f, FireInterval = 0.085f, MagSize = 30, ReloadTime = 1.6f, BulletSpeed = 42f, MaxRange = 40f, Kick = 0.03f, AmmoBox = 30, MuzzleZ = 0.8f, Accent = new Color(0.6f, 0.92f, 1f) },
        [WeaponId.ElecSniper] = new() { Id = WeaponId.ElecSniper, Name = "TS-128 Smart Sniper", Short = "TS-128", Element = Element.Elec, Damage = 55, Pierce = 2, FireInterval = 1.1f, MagSize = 4, ReloadTime = 2.2f, BulletSpeed = 110f, MaxRange = 80f, Kick = 0.3f, AmmoBox = 4, MuzzleZ = 1.3f, Accent = new Color(0.55f, 0.75f, 1f) },
        [WeaponId.Firework] = new() { Id = WeaponId.Firework, Name = "Firework Gun", Short = "Firework", Element = Element.Fire, Damage = 40, FireInterval = 1.3f, MagSize = 3, ReloadTime = 2.4f, BulletSpeed = 22f, MaxRange = 30f, Kick = 0.28f, AmmoBox = 3, MuzzleZ = 0.9f, Accent = new Color(1f, 0.45f, 0.15f), Rocket = true },
    };
    public static WeaponDef Get(WeaponId id) => _all[id];
    public static IEnumerable<WeaponDef> All => _all.Values;

    public Color ElementColor => Element switch { Element.Ice => new Color(0.6f, 0.92f, 1f), Element.Elec => new Color(0.6f, 0.78f, 1f), Element.Fire => new Color(1f, 0.5f, 0.15f), _ => Accent };
}

/// Per-owner ammo state of one weapon.
public class WeaponState
{
    public WeaponDef Def;
    public int Mag, Reserve;
    public float ReloadLeft, FireCd;
    public WeaponState(WeaponDef def, int reserveMags) { Def = def; Mag = def.MagSize; Reserve = def.MagSize * reserveMags; }
}

/// Box-built (voxel-style) gun models so every weapon reads differently on the duck and on the ground.
/// Built in DuckVisual space: grip at the origin, barrel toward -Z.
public static class WeaponMesh
{
    static readonly Dictionary<(WeaponId, bool), StandardMaterial3D> _mats = new();
    static StandardMaterial3D Mat(WeaponId id, bool accent, Color c) => _mats.TryGetValue((id, accent), out var m) ? m
        : _mats[(id, accent)] = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = c, Roughness = accent ? 0.5f : 0.6f, Metallic = accent ? 0.2f : 0.35f, EmissionEnabled = accent, Emission = c, EmissionEnergyMultiplier = accent ? 0.5f : 0f });

    static MeshInstance3D Box(Node3D parent, Vector3 pos, Vector3 size, Material m)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos };
        parent.AddChild(mi);
        return mi;
    }

    public static Node3D Build(WeaponDef def)
    {
        var root = new Node3D { Name = "Weapon" };
        var dark = Mat(def.Id, false, new Color(0.14f, 0.15f, 0.18f));
        var wood = Mat(def.Id, false, new Color(0.42f, 0.27f, 0.14f));
        var acc = Mat(def.Id, true, def.Accent);
        switch (def.Id)
        {
            case WeaponId.Shotgun:
                Box(root, new Vector3(0, 0, -0.1f), new Vector3(0.12f, 0.14f, 0.6f), dark);       // receiver
                Box(root, new Vector3(0, 0.02f, -0.7f), new Vector3(0.09f, 0.09f, 0.7f), dark);   // barrel
                Box(root, new Vector3(0, -0.05f, -0.55f), new Vector3(0.08f, 0.06f, 0.5f), wood); // pump
                Box(root, new Vector3(0, -0.12f, -0.05f), new Vector3(0.09f, 0.22f, 0.12f), dark); // drum mag
                Box(root, new Vector3(0, -0.02f, 0.32f), new Vector3(0.1f, 0.13f, 0.34f), wood);  // stock
                Box(root, new Vector3(0, 0.08f, -0.1f), new Vector3(0.03f, 0.03f, 0.3f), acc);    // orange rail
                break;
            case WeaponId.IceSmg:
                Box(root, new Vector3(0, 0, -0.05f), new Vector3(0.12f, 0.14f, 0.45f), dark);
                Box(root, new Vector3(0, 0.02f, -0.45f), new Vector3(0.07f, 0.07f, 0.4f), dark);
                Box(root, new Vector3(0, -0.2f, -0.02f), new Vector3(0.07f, 0.3f, 0.09f), acc);   // long frosty mag
                Box(root, new Vector3(0, 0.0f, 0.25f), new Vector3(0.05f, 0.1f, 0.2f), dark);     // folding stock
                Box(root, new Vector3(0, 0.09f, -0.1f), new Vector3(0.06f, 0.04f, 0.2f), acc);    // ice sight
                break;
            case WeaponId.ElecSniper:
                Box(root, new Vector3(0, 0, -0.15f), new Vector3(0.11f, 0.13f, 0.7f), dark);
                Box(root, new Vector3(0, 0.02f, -0.95f), new Vector3(0.06f, 0.06f, 0.9f), dark);  // long barrel
                Box(root, new Vector3(0, 0.02f, -1.35f), new Vector3(0.09f, 0.09f, 0.14f), acc);  // glowing muzzle coil
                Box(root, new Vector3(0, 0.13f, -0.15f), new Vector3(0.07f, 0.07f, 0.4f), acc);   // scope
                Box(root, new Vector3(0, -0.12f, -0.1f), new Vector3(0.07f, 0.16f, 0.1f), dark);
                Box(root, new Vector3(0, -0.02f, 0.35f), new Vector3(0.09f, 0.14f, 0.36f), dark);
                Box(root, new Vector3(0.07f, 0.0f, -0.4f), new Vector3(0.02f, 0.02f, 0.5f), acc); // side cable
                break;
            case WeaponId.Firework:
                Box(root, new Vector3(0, 0.02f, -0.35f), new Vector3(0.2f, 0.2f, 1.0f), Mat(def.Id, false, new Color(0.65f, 0.12f, 0.1f))); // fat tube
                Box(root, new Vector3(0, 0.02f, -0.86f), new Vector3(0.24f, 0.24f, 0.06f), acc);  // orange muzzle band
                Box(root, new Vector3(0, 0.02f, 0.14f), new Vector3(0.22f, 0.22f, 0.06f), acc);
                Box(root, new Vector3(0, -0.14f, 0.0f), new Vector3(0.08f, 0.12f, 0.1f), wood);   // grip
                Box(root, new Vector3(0, 0.14f, -0.2f), new Vector3(0.04f, 0.06f, 0.2f), dark);   // sight
                break;
            default: // rifle stand-in when the GLB gun is not available (loot on the ground)
                Box(root, new Vector3(0, 0, -0.1f), new Vector3(0.11f, 0.14f, 0.56f), dark);
                Box(root, new Vector3(0, 0.02f, -0.6f), new Vector3(0.07f, 0.07f, 0.5f), dark);
                Box(root, new Vector3(0, -0.14f, -0.12f), new Vector3(0.08f, 0.2f, 0.14f), dark);
                Box(root, new Vector3(0, -0.02f, 0.35f), new Vector3(0.1f, 0.12f, 0.32f), wood);
                break;
        }
        return root;
    }
}
