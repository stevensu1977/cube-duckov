using Godot;
namespace Duckov;

/// Whole-map still for the README / layout checks: loads Main.tscn, parks the camera high above the arena looking
/// straight down (orthographic), renders a few frames and saves one PNG.
/// Run: xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --script test/Overview.cs ++ --out=screenshots/result/map_overview.png [--size=72]
public partial class Overview : SceneTree
{
    string _out = "screenshots/result/map_overview.png";
    float _size = 72f;
    Vector2? _at; float _dist = 16f;   // --at=x,z: perspective close-up of a spot instead of the orthographic overview
    int _frames;

    public override void _Initialize()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--out=")) _out = a.Substring(6);
            else if (a.StartsWith("--size=")) _size = float.Parse(a.Substring(7));
            else if (a.StartsWith("--at=")) { var p = a.Substring(5).Split(','); _at = new Vector2(float.Parse(p[0]), float.Parse(p[1])); }
            else if (a.StartsWith("--dist=")) _dist = float.Parse(a.Substring(7));
            else if (a.StartsWith("--weather=")) Weather.Initial = a.Substring(10);
        }
        Game.AutopilotScenario = "idle";
        Root.AddChild(GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
    }

    public override bool _Process(double delta)
    {
        if (++_frames == 2)
        {
            var cam = Root.GetCamera3D();
            cam.Current = false;
            Camera3D top;
            if (_at.HasValue)
            {
                var c = new Vector3(_at.Value.X, 0, _at.Value.Y);
                top = new Camera3D { Fov = 42f, Far = 300f, Position = c + new Vector3(0, _dist * 0.8f, _dist * 0.75f), Current = true };
                Root.AddChild(top);
                top.LookAt(c + new Vector3(0, 1f, 0), Vector3.Up);
            }
            else
            {
                top = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = _size, Far = 300f, Position = new Vector3(0, 120f, 0.01f), Current = true };
                Root.AddChild(top);
                top.LookAt(Vector3.Zero, Vector3.Forward);   // north (-Z) up on screen
            }
            var g = Game.I; if (g?.Hud != null) g.Hud.Visible = false;
            // the depth fog would wash a 120 m-high view into the sky colour
            if (!_at.HasValue && Root.FindChild("WorldEnvironment", true, false) is WorldEnvironment we) { we.Environment.FogEnabled = false; }
        }
        if (_frames < 60) return false;   // ~2 s: lets the runtime navmesh bake thread finish so the process can exit cleanly
        var img = Root.GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://" + _out.GetBaseDir()));
        img.SavePng(ProjectSettings.GlobalizePath("res://" + _out));
        GD.Print($"[Overview] saved {_out}");
        Quit(0);
        return true;
    }
}
