using Godot;
namespace Duckov;

/// Top-down camera with a slight tilt that smoothly follows the player, with a little screen shake.
public partial class CameraRig : Node3D
{
    public Vector3 Offset = new(0, 19f, 8.5f);
    public Node3D Target;
    float _shake;
    Camera3D _cam;
    RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _cam = GetNodeOrNull<Camera3D>("Camera3D");
        if (_cam == null) { _cam = new Camera3D { Name = "Camera3D" }; AddChild(_cam); }
        _cam.Fov = 42f;
        _cam.Current = true;
        _cam.Far = 200f;
        // ground-level listener: the rig sits on the player, so shots are heard by distance across the map, not from 21 m up
        var ear = new AudioListener3D { Name = "Listener" }; AddChild(ear); ear.MakeCurrent();
    }

    public void SnapTo(Vector3 p)
    {
        GlobalPosition = p;
        _cam.GlobalPosition = p + Offset;
        _cam.LookAt(p + new Vector3(0, 0.5f, 0), Vector3.Up);
    }

    public void Kick(float amount) { _shake = Mathf.Min(0.6f, _shake + amount); }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Target != null)
        {
            var goal = Target.GlobalPosition; goal.Y = 0;
            // lead slightly toward the aim point so the player sees more of where they're looking
            if (Target is Player p) { var lead = p.AimPoint - goal; lead.Y = 0; goal += lead.LimitLength(6f) * 0.25f; }
            GlobalPosition = GlobalPosition.Lerp(goal, 1f - Mathf.Exp(-6f * dt));
        }
        _shake = Mathf.Max(0, _shake - dt * 1.8f);
        var jitter = _shake > 0 ? new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * _shake * 0.5f : Vector3.Zero;
        _cam.GlobalPosition = GlobalPosition + Offset + jitter;
        _cam.LookAt(GlobalPosition + new Vector3(0, 0.5f, 0) + jitter * 0.5f, Vector3.Up);
    }
}
