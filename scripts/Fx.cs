using Godot;
namespace Duckov;

/// Small, short-lived visual effects: muzzle flash (star billboard + light), hit sparks, tumbling feathers, dust, pickup pop.
public partial class Fx : Node3D
{
    Vector3 _vel, _spin;
    float _life, _maxLife, _startScale, _drag, _gravity;
    bool _billboardFade, _grow, _keepScale;

    static ShaderMaterial _flashMat;
    static Shader _starShader;
    static readonly System.Collections.Generic.Dictionary<Color, ShaderMaterial> _flashMats = new();
    static StandardMaterial3D _sparkMat, _featherMat, _pickMat, _dustMat, _coreMat, _iceMat, _frostMat, _elecMat, _fireMat, _emberMat, _smokeMat;
    static BoxMesh _smallSphere;
    static BoxMesh _smallBox;
    static QuadMesh _quad; static BoxMesh _feather;

    static void EnsureShared()
    {
        if (_flashMat != null) return;
        var star = new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform vec3 col : source_color = vec3(1.0, 0.8, 0.35);
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float ang = atan(p.y, p.x);
    float star = 0.55 + 0.45 * pow(abs(cos(ang * 2.0)), 6.0);   // 4-point star
    float a = smoothstep(star, star * 0.2, r);
    float core = smoothstep(0.35, 0.0, r);
    ALBEDO = col * (a * 1.6 + core * 2.5);
    ALPHA = clamp(a + core, 0.0, 1.0);
}" };
        _starShader = star;
        _flashMat = FlashMat(new Color(1f, 0.8f, 0.35f));
        _iceMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.95f, 1f), EmissionEnabled = true, Emission = new Color(0.5f, 0.85f, 1f), EmissionEnergyMultiplier = 2.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _frostMat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.95f, 1f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _elecMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.92f, 1f), EmissionEnabled = true, Emission = new Color(0.55f, 0.75f, 1f), EmissionEnergyMultiplier = 5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _fireMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.55f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.08f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _emberMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.2f), EmissionEnergyMultiplier = 5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _smokeMat = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.22f, 0.24f, 0.7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _coreMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.8f, 0.4f), EmissionEnergyMultiplier = 6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _sparkMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.9f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.75f, 0.3f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _featherMat = new StandardMaterial3D { AlbedoColor = new Color(0.98f, 0.97f, 0.9f), Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _dustMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.7f, 0.6f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _pickMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 1f, 0.6f), EmissionEnabled = true, Emission = new Color(0.3f, 1f, 0.4f), EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        // voxel look: every particle is a cube (the "sphere" is a cube too), only the muzzle-flash star stays a billboard quad
        _smallSphere = new BoxMesh { Size = Vector3.One * 0.8f };
        _smallBox = new BoxMesh { Size = Vector3.One };
        _quad = new QuadMesh { Size = new Vector2(1, 1) };
        _feather = new BoxMesh { Size = new Vector3(0.5f, 0.18f, 0.5f) };
    }

    static Fx Spawn(Node parent, Mesh mesh, Material mat, Vector3 pos, float scale, Vector3 vel, float life, float gravity, Vector3? spin = null, float drag = 0f)
    {
        var fx = new Fx { Position = pos, _vel = vel, _life = life, _maxLife = life, _gravity = gravity, _startScale = scale, _spin = spin ?? Vector3.Zero, _drag = drag };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        fx.AddChild(mi);
        fx.Scale = Vector3.One * scale;
        parent.AddChild(fx);
        return fx;
    }

    static ShaderMaterial FlashMat(Color c)
    {
        if (_flashMats.TryGetValue(c, out var m)) return m;
        m = new ShaderMaterial { Shader = _starShader }; m.SetShaderParameter("col", c);
        return _flashMats[c] = m;
    }

    static void Light(Node parent, Vector3 pos, Color c, float energy, float range, float life)
    {
        var light = new OmniLight3D { Position = pos, LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false };
        var holder = new Fx { _life = life, _maxLife = life, _startScale = 1f };
        holder.AddChild(light);
        parent.AddChild(holder);
    }

    public static void MuzzleFlash(Node parent, Vector3 pos, Vector3 dir) => MuzzleFlash(parent, pos, dir, WeaponDef.Get(WeaponId.Rifle));

    /// Per-weapon muzzle flash: the rifle's yellow star; a big orange bloom with smoke for the shotgun; a small pale
    /// star with drifting frost for the ice SMG; a white-blue star with crackling arcs for the electric sniper; a
    /// red-orange bloom with a smoke puff for the firework launcher.
    public static void MuzzleFlash(Node parent, Vector3 pos, Vector3 dir, WeaponDef w)
    {
        EnsureShared();
        Sfx.Shot(parent, pos, w);
        var rng = Game.Rng;
        Color col = w.Id switch { WeaponId.Shotgun => new Color(1f, 0.6f, 0.2f), WeaponId.IceSmg => new Color(0.75f, 0.95f, 1f), WeaponId.ElecSniper => new Color(0.75f, 0.85f, 1f), WeaponId.Firework => new Color(1f, 0.45f, 0.15f), _ => new Color(1f, 0.8f, 0.35f) };
        float size = w.Id switch { WeaponId.Shotgun => 1.7f, WeaponId.IceSmg => 0.75f, WeaponId.ElecSniper => 1.5f, WeaponId.Firework => 1.6f, _ => 1.1f };
        var star = Spawn(parent, _quad, FlashMat(col), pos + dir * 0.25f, size * rng.RandfRange(0.85f, 1.15f), Vector3.Zero, w.Id == WeaponId.IceSmg ? 0.05f : 0.08f, 0f);
        star._billboardFade = true;
        var core = Spawn(parent, _smallSphere, w.Element == Element.Ice ? _iceMat : w.Element == Element.Elec ? _elecMat : w.Element == Element.Fire ? _fireMat : _coreMat, pos + dir * 0.2f, 0.22f, Vector3.Zero, 0.06f, 0f);
        core.Scale = new Vector3(0.18f, 0.18f, 0.5f) * (size / 1.1f);
        core.LookAtFromPosition(core.Position, core.Position + dir, Vector3.Up);
        switch (w.Id)
        {
            case WeaponId.Shotgun:
                for (int i = 0; i < 7; i++)
                    Spawn(parent, _smallBox, _sparkMat, pos + dir * 0.3f, 0.06f, dir * rng.RandfRange(7f, 12f) + new Vector3(rng.RandfRange(-3f, 3f), rng.RandfRange(0.5f, 3f), rng.RandfRange(-3f, 3f)), rng.RandfRange(0.1f, 0.2f), 10f);
                for (int i = 0; i < 4; i++) Smoke(parent, pos + dir * (0.4f + i * 0.25f), 0.22f, 0.6f, dir * 2f);
                Light(parent, pos, new Color(1f, 0.6f, 0.25f), 6f, 8f, 0.09f);
                break;
            case WeaponId.IceSmg:
                for (int i = 0; i < 4; i++)
                    Spawn(parent, _smallBox, _iceMat, pos + dir * 0.35f, 0.045f, dir * rng.RandfRange(1.5f, 3f) + new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(0.2f, 1f), rng.RandfRange(-0.8f, 0.8f)), rng.RandfRange(0.2f, 0.35f), 1.5f, new Vector3(4, 6, 3), 2f);
                Light(parent, pos, new Color(0.6f, 0.9f, 1f), 2.5f, 5f, 0.06f);
                break;
            case WeaponId.ElecSniper:
                for (int i = 0; i < 3; i++)
                {
                    var a = pos + dir * 0.2f + new Vector3(rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(-0.2f, 0.2f));
                    Arc(parent, a, a + dir * rng.RandfRange(0.6f, 1.4f) + new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(-0.4f, 0.5f), rng.RandfRange(-0.5f, 0.5f)), 3, 0.12f);
                }
                Light(parent, pos, new Color(0.6f, 0.75f, 1f), 7f, 9f, 0.1f);
                break;
            case WeaponId.Firework:
                for (int i = 0; i < 6; i++)
                    Spawn(parent, _smallBox, _emberMat, pos + dir * 0.4f, 0.07f, dir * rng.RandfRange(3f, 6f) + new Vector3(rng.RandfRange(-2f, 2f), rng.RandfRange(0.5f, 2.5f), rng.RandfRange(-2f, 2f)), rng.RandfRange(0.25f, 0.5f), 8f);
                for (int i = 0; i < 5; i++) Smoke(parent, pos + dir * 0.5f, 0.3f, 0.9f, dir * 1.5f + Vector3.Up * 0.5f);
                Light(parent, pos, new Color(1f, 0.5f, 0.2f), 6f, 8f, 0.1f);
                break;
            default:
                for (int i = 0; i < 3; i++)
                    Spawn(parent, _smallBox, _sparkMat, pos + dir * 0.3f, 0.05f, dir * rng.RandfRange(6f, 10f) + new Vector3(rng.RandfRange(-1.5f, 1.5f), rng.RandfRange(0.5f, 2f), rng.RandfRange(-1.5f, 1.5f)), rng.RandfRange(0.08f, 0.16f), 10f);
                Light(parent, pos, new Color(1f, 0.78f, 0.4f), 4f, 6f, 0.07f);
                break;
        }
    }

    /// A drifting, slowly growing smoke cube.
    public static void Smoke(Node parent, Vector3 pos, float scale, float life, Vector3? vel = null)
    {
        EnsureShared();
        var rng = Game.Rng;
        var v = (vel ?? Vector3.Zero) + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0.6f, 1.2f), rng.RandfRange(-0.4f, 0.4f));
        var s = Spawn(parent, _smallBox, _smokeMat, pos, scale * rng.RandfRange(0.8f, 1.2f), v, life * rng.RandfRange(0.8f, 1.2f), -0.4f, new Vector3(rng.RandfRange(-2f, 2f), rng.RandfRange(-2f, 2f), 0), 1.5f);
        s._grow = true;
    }

    /// Tiny rain splash: two pale cubes hopping up from the ground.
    public static void Splash(Node parent, Vector3 pos)
    {
        EnsureShared();
        var rng = Game.Rng;
        for (int i = 0; i < 2; i++)
            Spawn(parent, _smallBox, _frostMat, pos, 0.05f, new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(0.8f, 1.6f), rng.RandfRange(-0.8f, 0.8f)), 0.22f, 9f);
    }

    /// Frost burst where an ice round lands: cyan shards and a translucent mist.
    public static void IceBurst(Node parent, Vector3 pos)
    {
        EnsureShared();
        var rng = Game.Rng;
        for (int i = 0; i < 8; i++)
            Spawn(parent, _smallBox, _iceMat, pos, rng.RandfRange(0.06f, 0.11f), new Vector3(rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(1f, 3.5f), rng.RandfRange(-2.5f, 2.5f)), rng.RandfRange(0.35f, 0.6f), 9f, new Vector3(rng.RandfRange(-8f, 8f), rng.RandfRange(-8f, 8f), 0));
        for (int i = 0; i < 3; i++)
            Spawn(parent, _smallSphere, _frostMat, pos + Vector3.Up * 0.2f, rng.RandfRange(0.35f, 0.55f), new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(0.3f, 0.8f), rng.RandfRange(-0.5f, 0.5f)), 0.5f, 0f, null, 2f);
        Light(parent, pos, new Color(0.6f, 0.9f, 1f), 2f, 4f, 0.12f);
    }

    /// Electric hit: bright sparks plus a few short random arcs.
    public static void ElecBurst(Node parent, Vector3 pos)
    {
        EnsureShared();
        var rng = Game.Rng;
        for (int i = 0; i < 6; i++)
            Spawn(parent, _smallBox, _elecMat, pos, 0.05f, new Vector3(rng.RandfRange(-4f, 4f), rng.RandfRange(1f, 4f), rng.RandfRange(-4f, 4f)), rng.RandfRange(0.12f, 0.25f), 12f);
        for (int i = 0; i < 4; i++)
            Arc(parent, pos, pos + new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.3f, 1f), rng.RandfRange(-1f, 1f)), 3, 0.14f);
        Light(parent, pos, new Color(0.6f, 0.75f, 1f), 5f, 7f, 0.12f);
    }

    /// Jagged lightning between two points: `segs` thin boxes with a perpendicular zigzag.
    public static void Arc(Node parent, Vector3 a, Vector3 b, int segs, float life)
    {
        EnsureShared();
        var rng = Game.Rng;
        var d = b - a; float len = d.Length(); if (len < 0.05f) return;
        var dir = d / len;
        var side = dir.Cross(Vector3.Up); if (side.LengthSquared() < 0.01f) side = Vector3.Right; side = side.Normalized();
        var up = side.Cross(dir);
        var prev = a;
        for (int i = 1; i <= segs; i++)
        {
            var p = i == segs ? b : a + dir * (len * i / segs) + side * rng.RandfRange(-0.25f, 0.25f) * len * 0.35f + up * rng.RandfRange(-0.2f, 0.2f) * len * 0.35f;
            var seg = p - prev; float sl = seg.Length();
            var fx = new Fx { Position = (prev + p) * 0.5f, _life = life, _maxLife = life, _startScale = 1f, _keepScale = true };
            fx.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, sl) }, MaterialOverride = _elecMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            parent.AddChild(fx);
            fx.LookAt(fx.GlobalPosition + seg, Mathf.Abs(seg.Normalized().Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up);
            prev = p;
        }
    }

    /// Firework burst: fireball cubes, embers, smoke, an expanding ground ring and a flash of light.
    public static void Explosion(Node parent, Vector3 pos)
    {
        EnsureShared();
        Sfx.Explosion(parent, pos);
        var rng = Game.Rng;
        var star = Spawn(parent, _quad, FlashMat(new Color(1f, 0.55f, 0.2f)), pos + Vector3.Up * 0.6f, 3.2f, Vector3.Zero, 0.14f, 0f);
        star._billboardFade = true;
        for (int i = 0; i < 14; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau), sp = rng.RandfRange(3f, 8f);
            Spawn(parent, _smallBox, _fireMat, pos + Vector3.Up * 0.3f, rng.RandfRange(0.18f, 0.32f), new Vector3(Mathf.Cos(a) * sp, rng.RandfRange(2f, 6f), Mathf.Sin(a) * sp), rng.RandfRange(0.35f, 0.6f), 9f, new Vector3(rng.RandfRange(-6f, 6f), rng.RandfRange(-6f, 6f), 0), 2f);
        }
        for (int i = 0; i < 10; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau), sp = rng.RandfRange(4f, 11f);
            Spawn(parent, _smallBox, _emberMat, pos + Vector3.Up * 0.4f, rng.RandfRange(0.05f, 0.09f), new Vector3(Mathf.Cos(a) * sp, rng.RandfRange(3f, 9f), Mathf.Sin(a) * sp), rng.RandfRange(0.5f, 0.9f), 14f);
        }
        for (int i = 0; i < 8; i++) Smoke(parent, pos + new Vector3(rng.RandfRange(-0.6f, 0.6f), 0.4f, rng.RandfRange(-0.6f, 0.6f)), 0.55f, 1.4f, Vector3.Up * 1.5f);
        var ring = Spawn(parent, new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 24, RingSegments = 6 }, _fireMat, pos + Vector3.Up * 0.06f, 0.3f, Vector3.Zero, 0.35f, 0f);
        ring._billboardFade = false; ring._startScale = 3.2f; ring.Scale = Vector3.One * 0.3f;
        Light(parent, pos + Vector3.Up * 0.8f, new Color(1f, 0.55f, 0.2f), 14f, 12f, 0.2f);
    }

    public static void Sparks(Node parent, Vector3 pos, Vector3 normal, bool feathers)
    {
        EnsureShared();
        var rng = Game.Rng;
        if (feathers)
        {
            // a puff of feathers: flat quads that tumble and drift down slowly
            for (int i = 0; i < 9; i++)
            {
                var v = normal * rng.RandfRange(1f, 3f) + new Vector3(rng.RandfRange(-2.2f, 2.2f), rng.RandfRange(1.5f, 4f), rng.RandfRange(-2.2f, 2.2f));
                var spin = new Vector3(rng.RandfRange(-9f, 9f), rng.RandfRange(-9f, 9f), rng.RandfRange(-9f, 9f));
                var f = Spawn(parent, _feather, _featherMat, pos + Vector3.Up * 0.3f, rng.RandfRange(0.16f, 0.26f), v, rng.RandfRange(0.7f, 1.2f), 4f, spin, 2.2f);
                f.Rotation = new Vector3(rng.RandfRange(0, Mathf.Tau), rng.RandfRange(0, Mathf.Tau), 0);
            }
            for (int i = 0; i < 3; i++)
                Spawn(parent, _smallSphere, _dustMat, pos + Vector3.Up * 0.3f, rng.RandfRange(0.25f, 0.4f), new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(0.5f, 1.5f), rng.RandfRange(-1f, 1f)), 0.35f, 0f, null, 3f);
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                var v = normal * rng.RandfRange(2f, 5f) + new Vector3(rng.RandfRange(-2.5f, 2.5f), rng.RandfRange(1f, 4f), rng.RandfRange(-2.5f, 2.5f));
                Spawn(parent, _smallBox, _sparkMat, pos, 0.07f, v, rng.RandfRange(0.2f, 0.45f), 14f);
            }
            // dust puff off the wall
            for (int i = 0; i < 3; i++)
                Spawn(parent, _smallSphere, _dustMat, pos + normal * 0.1f, rng.RandfRange(0.2f, 0.35f), normal * rng.RandfRange(0.8f, 1.6f) + new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.3f, 1f), rng.RandfRange(-0.6f, 0.6f)), 0.3f, 0f, null, 3f);
        }
    }

    public static void PickupPop(Node parent, Vector3 pos)
    {
        EnsureShared();
        var rng = Game.Rng;
        for (int i = 0; i < 12; i++)
        {
            float a = rng.RandfRange(0, Mathf.Tau);
            var v = new Vector3(Mathf.Cos(a) * 2.5f, rng.RandfRange(3f, 6f), Mathf.Sin(a) * 2.5f);
            Spawn(parent, _smallSphere, _pickMat, pos + Vector3.Up * 0.5f, 0.14f, v, 0.5f, 14f);
        }
        // expanding ring on the ground
        var ring = Spawn(parent, new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 24, RingSegments = 6 }, _pickMat, pos + Vector3.Up * 0.05f, 0.3f, Vector3.Zero, 0.35f, 0f);
        ring._billboardFade = false; ring._startScale = 1.6f; ring.Scale = Vector3.One * 0.3f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        if (_life <= 0) { QueueFree(); return; }
        if (_drag > 0) _vel *= Mathf.Exp(-_drag * dt);
        _vel += Vector3.Down * _gravity * dt;
        Position += _vel * dt;
        if (_spin != Vector3.Zero) Rotation += _spin * dt;
        float k = _life / _maxLife;
        if (_keepScale) return;
        if (_grow) { Scale = Vector3.One * (_startScale * (0.7f + 1.1f * (1f - k))); if (GetChildCount() > 0 && GetChild(0) is MeshInstance3D smi && smi.MaterialOverride is StandardMaterial3D sm) smi.Transparency = 1f - k * 0.9f; return; }
        if (_billboardFade)
        {
            var cam = GetViewport()?.GetCamera3D();
            if (cam != null) LookAt(cam.GlobalPosition, Vector3.Up);
            Scale = Vector3.One * Mathf.Max(0.001f, _startScale * (0.6f + 0.6f * (1f - k)));
        }
        else if (_gravity == 0 && _spin == Vector3.Zero && _maxLife <= 0.36f && _startScale > 1f)
            Scale = Vector3.One * Mathf.Lerp(_startScale, 0.3f, k); // pickup ring: expands
        else
            Scale = Vector3.One * Mathf.Max(0.001f, _startScale * (0.3f + 0.7f * k));
    }
}
