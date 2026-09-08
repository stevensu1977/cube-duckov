using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Fast projectile: sweeps a ray each physics step so it cannot tunnel through walls. Drawn as a glowing box tracer
/// whose size and colour come from the weapon; elemental rounds apply their effect on a duck hit (ice slows, electricity
/// arcs to a neighbour), the sniper round pierces, shotgun pellets die after MaxRange.
public partial class Bullet : Node3D
{
    public Vector3 Velocity;
    public int Damage = 20;
    public uint HitMask = Layers.World | Layers.Enemy;
    public bool FromPlayer = true;
    public GodotObject Shooter;
    public WeaponDef Weapon;
    public int Pierce;
    public float MaxRange = 60f;
    float _life = 1.6f, _travel;
    int _hits;

    static Shader _haloShader;
    static readonly Dictionary<(WeaponId, bool), (StandardMaterial3D core, ShaderMaterial halo, BoxMesh coreMesh, BoxMesh haloMesh)> _styles = new();

    static (StandardMaterial3D, ShaderMaterial, BoxMesh, BoxMesh) Style(WeaponDef w, bool fromPlayer)
    {
        if (_styles.TryGetValue((w.Id, fromPlayer), out var st)) return st;
        _haloShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;
uniform vec3 col : source_color = vec3(1.0, 0.8, 0.3);
uniform float strength = 0.55;
void fragment() {
    float f = abs(dot(normalize(NORMAL), normalize(VIEW)));
    ALBEDO = col * pow(f, 2.2) * strength;
    ALPHA = pow(f, 2.0);
}" };
        Color c = w.Element != Element.None ? w.ElementColor : fromPlayer ? new Color(1f, 0.82f, 0.35f) : new Color(1f, 0.35f, 0.18f);
        // tracer proportions per weapon: thin rifle, short fat shotgun pellet, needle SMG, long bright sniper bolt
        (float thick, float len) = w.Id switch { WeaponId.Shotgun => (0.09f, 0.45f), WeaponId.IceSmg => (0.05f, 0.7f), WeaponId.ElecSniper => (0.08f, 2.6f), _ => (0.06f, 1.0f) };
        var core = new StandardMaterial3D { AlbedoColor = c.Lightened(0.2f), EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = w.Element == Element.Elec ? 3f : 1.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var halo = new ShaderMaterial { Shader = _haloShader }; halo.SetShaderParameter("col", c); halo.SetShaderParameter("strength", w.Id == WeaponId.ElecSniper ? 0.9f : 0.55f);
        st = (core, halo, new BoxMesh { Size = new Vector3(thick, len, thick) }, new BoxMesh { Size = new Vector3(thick * 2.6f, len * 1.25f, thick * 2.6f) });
        return _styles[(w.Id, fromPlayer)] = st;
    }

    public override void _Ready()
    {
        Weapon ??= WeaponDef.Get(WeaponId.Rifle);
        var (core, halo, coreMesh, haloMesh) = Style(Weapon, FromPlayer);
        // boxes are built along +Y; rotate so the tracer lies along the flight direction (-Z after LookAt)
        var rot = new Vector3(Mathf.DegToRad(90f), 0, 0);
        AddChild(new MeshInstance3D { Mesh = coreMesh, MaterialOverride = core, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Rotation = rot });
        AddChild(new MeshInstance3D { Mesh = haloMesh, MaterialOverride = halo, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Rotation = rot });
        if (Velocity.LengthSquared() > 0.001f)
            LookAt(GlobalPosition + Velocity, Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        if (_life <= 0) { QueueFree(); return; }
        var from = GlobalPosition;
        var to = from + Velocity * dt;
        _travel += Velocity.Length() * dt;
        if (_travel > MaxRange) { QueueFree(); return; }
        var space = GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(from, to, HitMask);
        q.CollideWithAreas = false;
        var hit = space.IntersectRay(q);
        if (hit.Count > 0)
        {
            var pos = (Vector3)hit["position"];
            var normal = (Vector3)hit["normal"];
            var collider = hit["collider"].AsGodotObject();
            bool hitDuck = false;
            if (collider is IDamageable d && collider != Shooter)
            {
                d.TakeDamage(Damage, from, FromPlayer);
                hitDuck = true;
                ApplyElement(collider, pos);
                _hits++;
            }
            if (hitDuck && Weapon.Element == Element.Ice) Fx.IceBurst(GetParent(), pos);
            else if (hitDuck && Weapon.Element == Element.Elec) Fx.ElecBurst(GetParent(), pos);
            else Fx.Sparks(GetParent(), pos, normal, hitDuck);
            if (hitDuck && _hits <= Pierce) { GlobalPosition = pos + Velocity.Normalized() * 0.7f; return; }   // sniper round keeps going
            QueueFree();
            return;
        }
        GlobalPosition = to;
    }

    void ApplyElement(GodotObject target, Vector3 pos)
    {
        switch (Weapon.Element)
        {
            case Element.Ice:
                if (target is Enemy e) e.ApplySlow(2.5f);
                else if (target is Player p) p.ApplySlow(1.5f);
                break;
            case Element.Elec:
                if (!FromPlayer || Game.I == null) break;
                // the charge jumps to the nearest other duck within 3.5 m
                Enemy best = null; float bestD = 3.5f;
                foreach (var o in Game.I.Enemies)
                {
                    if (!IsInstanceValid(o) || o.Dead || o == target) continue;
                    float dd = o.GlobalPosition.DistanceTo(pos);
                    if (dd < bestD) { bestD = dd; best = o; }
                }
                if (best != null)
                {
                    var bp = best.GlobalPosition + new Vector3(0, 0.8f, 0);
                    Fx.Arc(GetParent(), pos, bp, 6, 0.18f);
                    Fx.ElecBurst(GetParent(), bp);
                    best.TakeDamage(Mathf.RoundToInt(Damage * 0.4f), pos, true);
                }
                break;
        }
    }
}
