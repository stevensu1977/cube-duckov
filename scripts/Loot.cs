using Godot;
using System.Collections.Generic;
namespace Duckov;

/// A pickup on the ground. E within range collects it: counts as loot, and Medkit/Ammo also have an effect.
/// Model is a Blender-made GLB with a pulsing inverted-hull outline; the outline turns white when in pickup range.
public partial class Loot : Node3D
{
    public enum Kind { Cash, Medkit, Ammo, Gold, Weapon }
    public Kind Type;
    /// For Kind.Weapon: which gun lies here.
    public WeaponId Weapon;
    public bool Risky;
    public bool Collected { get; private set; }
    float _t;
    Node3D _visual;
    Label3D _label;
    StandardMaterial3D _outline;
    Color _outlineBase;
    bool _hi;

    static readonly Dictionary<Kind, PackedScene> _scenes = new();

    public string DisplayName => Type switch { Kind.Cash => "Cash", Kind.Medkit => "Medkit", Kind.Ammo => "Ammo box", Kind.Gold => "Gold bar", Kind.Weapon => WeaponDef.Get(Weapon).Name, _ => "Loot" };
    public int Value => Type switch { Kind.Gold => 3, _ => 1 };

    public override void _Ready()
    {
        _visual = new Node3D();
        AddChild(_visual);
        _t = Game.Rng.RandfRange(0, 10f);
        Node3D model;
        if (Type == Kind.Weapon)
        {
            // the box-built gun lying on its side
            model = WeaponMesh.Build(WeaponDef.Get(Weapon));
            model.Rotation = new Vector3(0, 0, Mathf.Pi / 2f);
            model.Position = new Vector3(0, 0.14f, 0);
        }
        else
        {
            string file = Type switch { Kind.Medkit => "loot_medkit", Kind.Ammo => "loot_ammo", Kind.Gold => "loot_gold", _ => "loot_cash" };
            if (!_scenes.TryGetValue(Type, out var ps)) { ps = GD.Load<PackedScene>($"res://assets/models/props/{file}.glb"); _scenes[Type] = ps; }
            model = ps.Instantiate<Node3D>();
            Blocky.Apply(model);
        }
        _visual.AddChild(model);

        // inverted-hull outline: a copy of each mesh, front faces culled, grown along normals, unshaded
        _outlineBase = Type == Kind.Weapon ? WeaponDef.Get(Weapon).ElementColor : Type == Kind.Gold ? new Color(1f, 0.85f, 0.3f) : Risky ? new Color(1f, 0.6f, 0.25f) : new Color(1f, 0.95f, 0.5f);
        _outline = new StandardMaterial3D { AlbedoColor = _outlineBase, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Front, Grow = true, GrowAmount = 0.05f, EmissionEnabled = true, Emission = _outlineBase, EmissionEnergyMultiplier = 0.8f };
        foreach (var mi in Meshes(model))
        {
            var copy = new MeshInstance3D { Mesh = mi.Mesh, Transform = mi.Transform, MaterialOverride = _outline, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            mi.GetParent().AddChild(copy);
        }

        // glowing ground ring so loot reads from the top-down camera
        var ringMat = new StandardMaterial3D { AlbedoColor = Risky ? new Color(1f, 0.55f, 0.2f, 0.6f) : new Color(1f, 0.95f, 0.4f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        // square ground frame (four thin boxes) instead of a torus
        var frame = new Node3D { Position = new Vector3(0, 0.03f, 0) };
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2; float side = i % 2 == 0 ? -1 : 1;
            frame.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = alongX ? new Vector3(1.44f, 0.03f, 0.12f) : new Vector3(0.12f, 0.03f, 1.44f) }, MaterialOverride = ringMat, Position = alongX ? new Vector3(0, 0, side * 0.66f) : new Vector3(side * 0.66f, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        AddChild(frame);

        _label = new Label3D { Text = DisplayName, FontSize = 40, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.3f, 0), Modulate = new Color(1, 1, 1, 0.9f), OutlineSize = 10, OutlineModulate = new Color(0, 0, 0, 0.8f), NoDepthTest = true };
        AddChild(_label);
        _label.Visible = false;
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        var list = new List<MeshInstance3D>();
        void Walk(Node x) { if (x is MeshInstance3D mi) list.Add(mi); foreach (var c in x.GetChildren()) Walk(c); }
        Walk(n);
        return list;
    }

    public void SetHighlighted(bool on)
    {
        _hi = on;
        if (_label != null && IsInstanceValid(_label)) _label.Visible = on;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        _visual.Position = new Vector3(0, 0.1f + Mathf.Sin(_t * 2.5f) * 0.05f, 0);
        _visual.Rotation = new Vector3(0, _t * 1.2f, 0);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 4f);
        _outline.GrowAmount = 0.035f + 0.04f * pulse;
        var c = _hi ? new Color(1f, 1f, 1f) : _outlineBase;
        _outline.AlbedoColor = c; _outline.Emission = c;
        _outline.EmissionEnergyMultiplier = _hi ? 1.6f : 0.5f + 0.7f * pulse;
    }

    public void Pickup(Player p) => ApplyPickup(p);

    /// Apply the pickup.
    public void ApplyPickup(Player p)
    {
        if (Collected) return;
        Collected = true;
        switch (Type)
        {
            case Kind.Medkit: p.Heal(40); break;
            case Kind.Ammo: p.AddReserve(24); break;
            case Kind.Weapon: p.AddWeapon(Weapon); break;
        }
        Game.I.OnLootCollected(this);
        Fx.PickupPop(GetParent(), GlobalPosition);
        QueueFree();
    }
}
