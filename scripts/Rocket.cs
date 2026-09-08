using Godot;
namespace Duckov;

/// The Firework Gun shell: an arcing projectile with a smoke trail that bursts on contact — area damage to ducks
/// within Radius (falling off with distance), a fireball of cubes, smoke, a ground ring, a light and a camera kick.
public partial class Rocket : Node3D
{
    public Vector3 Velocity;
    public int Damage = 40;
    public float Radius = 3f;
    public bool FromPlayer = true;
    public bool Cosmetic;            // remote player's shell: effects only
    float _life = 3.5f, _trail;
    static StandardMaterial3D _shellMat, _tipMat;

    public override void _Ready()
    {
        _shellMat ??= Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.15f, 0.1f), Roughness = 0.6f });
        _tipMat ??= new StandardMaterial3D { AlbedoColor = new Color(1f, 0.6f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.4f) }, MaterialOverride = _shellMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.12f) }, MaterialOverride = _tipMat, Position = new Vector3(0, 0, 0.24f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (Velocity.LengthSquared() > 0.001f) LookAt(GlobalPosition + Velocity, Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        Velocity += Vector3.Down * 9.8f * dt;
        var from = GlobalPosition;
        var to = from + Velocity * dt;
        var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.World | (Cosmetic ? 0u : (FromPlayer ? Layers.Enemy : Layers.Player)));
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) { Burst((Vector3)hit["position"]); return; }
        if (to.Y <= 0.05f || _life <= 0) { Burst(new Vector3(to.X, Mathf.Max(0.05f, to.Y), to.Z)); return; }
        GlobalPosition = to;
        if (Velocity.LengthSquared() > 0.01f) LookAt(to + Velocity, Vector3.Up);
        _trail -= dt;
        if (_trail <= 0) { _trail = 0.045f; Fx.Smoke(GetParent(), from, 0.16f, 0.5f); }
    }

    void Burst(Vector3 pos)
    {
        var game = Game.I;
        Fx.Explosion(GetParent(), pos);
        game?.Cam?.Kick(0.35f);
        if (!Cosmetic && game != null)
        {
            if (FromPlayer)
            {
                foreach (var e in game.Enemies)
                {
                    if (!IsInstanceValid(e) || e.Dead) continue;
                    float d = e.GlobalPosition.DistanceTo(pos);
                    if (d < Radius) e.TakeDamage(Mathf.RoundToInt(Damage * (1f - 0.5f * d / Radius)), pos, true);
                }
                game.OnPlayerShot(pos);     // the bang is loud
            }
            else if (game.Player != null && !game.Player.Dead && game.Player.GlobalPosition.DistanceTo(pos) < Radius)
                game.Player.TakeDamage(Mathf.RoundToInt(Damage * 0.5f), pos, false);
        }
        QueueFree();
    }
}
