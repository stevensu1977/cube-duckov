using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Duck model loaded from a Blender-made GLB (tools/make_duck.py). Faces -Z, root on the ground (y=0).
/// Named parts (Body, Head, WingL/R, FootL/R, Gun, ...) are animated procedurally: waddle, foot swing, wing flap, death fall.
public partial class DuckVisual : Node3D
{
    public Node3D Muzzle { get; private set; }
    readonly List<StandardMaterial3D> _mats = new();
    Node3D _body, _head, _wingL, _wingR, _footL, _footR, _gun, _gear, _customGun;
    static readonly Vector3 CustomGunPos = new(0.36f, 0.8f, -0.15f);
    Vector3 _bodyPos, _headPos, _footLPos, _footRPos, _gearPos, _gunPos;
    float _flash, _waddleT, _fallT, _idleT;
    bool _fallen;

    static PackedScene _playerScene, _soldierScene;

    public static DuckVisual Create(Color body, Color accent, bool soldier, Color? bandana = null)
    {
        var d = new DuckVisual { Name = "Visual" };
        d.Build(body, accent, soldier, bandana);
        return d;
    }

    public static DuckVisual Create(AvatarDef a) => Create(a.Body, a.Accent, false, a.Bandana);

    void Build(Color body, Color accent, bool soldier, Color? bandana = null)
    {
        _playerScene ??= GD.Load<PackedScene>("res://assets/models/duck_player.glb");
        _soldierScene ??= GD.Load<PackedScene>("res://assets/models/duck_soldier.glb");
        var model = (soldier ? _soldierScene : _playerScene).Instantiate<Node3D>();
        model.Name = "Model";
        AddChild(model);

        // per-instance material copies so ducks can be tinted and flashed independently
        foreach (var mi in FindMeshes(model))
        {
            var mesh = mi.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetMaterial(s) is not StandardMaterial3D src) continue;
                var m = Blocky.Texturize((StandardMaterial3D)src.Duplicate());
                switch (src.ResourceName)
                {
                    case "Body": m.AlbedoColor = body; break;
                    case "BodyDark": m.AlbedoColor = body.Darkened(0.28f); break;
                    case "Accent": m.AlbedoColor = accent; break;
                    case "AccentDark": m.AlbedoColor = accent.Darkened(0.3f); break;
                    case "Red": if (bandana.HasValue) m.AlbedoColor = bandana.Value; break;
                }
                // slightly softer look than the raw glTF roughness
                m.Roughness = Mathf.Min(1f, m.Roughness + 0.05f);
                mi.SetSurfaceOverrideMaterial(s, m);
                _mats.Add(m);
            }
        }

        _body = model.FindChild("Body", true, false) as Node3D;
        _head = model.FindChild("Head", true, false) as Node3D;
        _wingL = model.FindChild("WingL", true, false) as Node3D;
        _wingR = model.FindChild("WingR", true, false) as Node3D;
        _footL = model.FindChild("FootL", true, false) as Node3D;
        _footR = model.FindChild("FootR", true, false) as Node3D;
        _gun = model.FindChild("Gun", true, false) as Node3D;
        _gear = (model.FindChild(soldier ? "Vest" : "Backpack", true, false)) as Node3D;
        _bodyPos = _body?.Position ?? Vector3.Zero;
        _headPos = _head?.Position ?? Vector3.Zero;
        _footLPos = _footL?.Position ?? Vector3.Zero;
        _footRPos = _footR?.Position ?? Vector3.Zero;
        _gearPos = _gear?.Position ?? Vector3.Zero;
        _gunPos = _gun?.Position ?? Vector3.Zero;
        // helmet / bandana ride with the head
        foreach (var n in new[] { "Helmet", "Bandana" })
            if (model.FindChild(n, true, false) is Node3D hat && _head != null) { var rel = hat.Position - _head.Position; hat.Owner = null; hat.GetParent().RemoveChild(hat); _head.AddChild(hat); hat.Position = rel; }
        // wings pivot at the shoulder: offset the mesh so rotation happens at the top edge
        Pivot(_wingL, new Vector3(0, 0.3f, 0)); Pivot(_wingR, new Vector3(0, 0.3f, 0));

        Muzzle = new Node3D { Name = "Muzzle", Position = new Vector3(0.36f, 0.82f, -0.98f) };
        AddChild(Muzzle);
    }

    static void Pivot(Node3D part, Vector3 offset)
    {
        if (part == null) return;
        var wrap = new Node3D { Name = part.Name + "Pivot", Position = part.Position + offset };
        var parent = part.GetParent();
        part.Owner = null;
        parent.RemoveChild(part);
        parent.AddChild(wrap);
        wrap.AddChild(part);
        part.Position = -offset;
    }

    static IEnumerable<MeshInstance3D> FindMeshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in FindMeshes(c)) yield return m;
    }

    public void Flash() => Flash(new Color(1f, 0.45f, 0.35f), 0.12f);
    public void Flash(Color c, float dur)
    {
        _flash = dur;
        foreach (var m in _mats) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = 0.7f; }
    }

    /// Swap the held weapon: the rifle is the GLB's own gun, the others are box-built models from WeaponMesh.
    public void SetWeapon(WeaponDef def)
    {
        if (_customGun != null) { _customGun.QueueFree(); _customGun = null; }
        if (_gun != null) _gun.Visible = def.Id == WeaponId.Rifle;
        if (def.Id != WeaponId.Rifle)
        {
            _customGun = WeaponMesh.Build(def);
            _customGun.Position = CustomGunPos;
            AddChild(_customGun);
        }
        Muzzle.Position = new Vector3(0.36f, 0.82f, -def.MuzzleZ);
    }

    public void Fall() { _fallen = true; }

    /// Call each frame. speed01: normalized movement speed for the waddle.
    public void Animate(float speed01, float dt)
    {
        if (_flash > 0)
        {
            _flash -= dt;
            if (_flash <= 0) foreach (var m in _mats) m.EmissionEnabled = false;
        }
        if (_fallen)
        {
            _fallT = Mathf.Min(1f, _fallT + dt * 3.2f);
            float e = 1f - (1f - _fallT) * (1f - _fallT);
            Rotation = new Vector3(Mathf.DegToRad(-12f) * e, Rotation.Y, Mathf.DegToRad(92f) * e);
            Position = new Vector3(0, 0.28f * e, 0);
            // feet stick out, wings splay, head lolls
            if (_footL != null) _footL.Rotation = new Vector3(Mathf.DegToRad(-70f) * e, 0, 0);
            if (_footR != null) _footR.Rotation = new Vector3(Mathf.DegToRad(-55f) * e, 0, 0);
            if (_wingL != null) _wingL.GetParent<Node3D>().Rotation = new Vector3(0, 0, Mathf.DegToRad(-50f) * e);
            if (_wingR != null) _wingR.GetParent<Node3D>().Rotation = new Vector3(0, 0, Mathf.DegToRad(50f) * e);
            if (_head != null) _head.Rotation = new Vector3(Mathf.DegToRad(25f) * e, 0, Mathf.DegToRad(-20f) * e);
            return;
        }
        _idleT += dt;
        _waddleT += dt * (5f + 11f * speed01);
        float s = Mathf.Sin(_waddleT), c = Mathf.Cos(_waddleT);
        float amp = speed01;
        // whole-body roll + bounce (the classic waddle)
        Rotation = new Vector3(Mathf.DegToRad(4f) * amp, 0, Mathf.DegToRad(7f) * s * amp);
        Position = new Vector3(0, Mathf.Abs(s) * 0.05f * amp, 0);
        // idle breathing when standing still
        float breathe = Mathf.Sin(_idleT * 2.2f) * 0.012f * (1f - amp);
        if (_body != null) _body.Position = _bodyPos + new Vector3(0, breathe, 0);
        if (_gear != null) _gear.Position = _gearPos + new Vector3(0, breathe, 0);
        // head bobs forward with each step, slight counter-yaw
        if (_head != null)
        {
            _head.Position = _headPos + new Vector3(0, Mathf.Abs(c) * 0.03f * amp + breathe, -0.03f * Mathf.Abs(s) * amp);
            _head.Rotation = new Vector3(Mathf.DegToRad(6f) * Mathf.Abs(c) * amp, Mathf.DegToRad(-5f) * s * amp, 0);
        }
        // feet: alternate swing forward/back (-Z is forward) and lift on the swing phase
        if (_footL != null) { _footL.Position = _footLPos + new Vector3(0, Mathf.Max(0, s) * 0.09f * amp, -0.16f * s * amp); _footL.Rotation = new Vector3(Mathf.DegToRad(-25f) * s * amp, 0, 0); }
        if (_footR != null) { _footR.Position = _footRPos + new Vector3(0, Mathf.Max(0, -s) * 0.09f * amp, 0.16f * s * amp); _footR.Rotation = new Vector3(Mathf.DegToRad(25f) * s * amp, 0, 0); }
        // wings flap a little when hurrying
        float flap = Mathf.DegToRad(10f) * Mathf.Abs(s) * Mathf.Max(0, amp - 0.3f);
        if (_wingL != null) _wingL.GetParent<Node3D>().Rotation = new Vector3(0, 0, -flap);
        if (_wingR != null) _wingR.GetParent<Node3D>().Rotation = new Vector3(0, 0, flap);
        // gun bob
        if (_gun != null) _gun.Position = _gunPos + new Vector3(0, Mathf.Abs(c) * 0.02f * amp, 0);
        if (_customGun != null) _customGun.Position = CustomGunPos + new Vector3(0, Mathf.Abs(c) * 0.02f * amp, 0);
    }
}
