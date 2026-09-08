using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Close-up asset showcase for judging models: lines up ducks (and any GLBs given with --models=a.glb,b.glb) in front
/// of a camera, renders a few frames and saves screenshots/preview/showcase.png.
/// Run: xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --script test/Showcase.cs ++ --models=assets/models/props/crate.glb
public partial class Showcase : SceneTree
{
    int _frames;
    string _out = "screenshots/preview/showcase.png";
    readonly List<DuckVisual> _ducks = new();
    float _t;
    bool _fx; Node3D _fxRoot; int _fxFrame;

    public override void _Initialize()
    {
        var models = new List<string>();
        bool ducks = true, weapons = false, avatars = false; float spacing = 2.4f; float camDist = 6f;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--models=")) models.AddRange(a.Substring(9).Split(',', System.StringSplitOptions.RemoveEmptyEntries));
            else if (a == "--no-ducks") ducks = false;
            else if (a.StartsWith("--out=")) _out = a.Substring(6);
            else if (a.StartsWith("--spacing=")) spacing = float.Parse(a.Substring(10));
            else if (a.StartsWith("--dist=")) camDist = float.Parse(a.Substring(7));
            else if (a == "--fx") _fx = true;
            else if (a == "--weapons") { weapons = true; ducks = false; }
            else if (a == "--avatars") { avatars = true; ducks = false; }
        }
        var root = new Node3D();
        Root.AddChild(root);
        _fxRoot = root;
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.55f, 0.62f, 0.7f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.75f, 0.8f, 0.9f), AmbientLightEnergy = 0.7f, TonemapMode = Godot.Environment.ToneMapper.Filmic };
        root.AddChild(new WorldEnvironment { Environment = env });
        root.AddChild(new DirectionalLight3D { LightEnergy = 1.3f, ShadowEnabled = true, RotationDegrees = new Vector3(-55, 30, 0) });
        var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(60, 60) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.55f, 0.45f) } };
        root.AddChild(ground);

        var items = new List<Node3D>();
        if (ducks)
        {
            // DuckVisual animates its own Position, so give each one a holder node
            var p = DuckVisual.Create(new Color(1f, 0.85f, 0.15f), new Color(0.35f, 0.25f, 0.15f), false); _ducks.Add(p);
            var ph = new Node3D(); ph.AddChild(p); items.Add(ph);
            var e = DuckVisual.Create(new Color(0.55f, 0.6f, 0.42f), new Color(0.22f, 0.3f, 0.2f), true); _ducks.Add(e);
            var eh = new Node3D(); eh.AddChild(e); items.Add(eh);
        }
        if (avatars)
        {
            foreach (var av in AvatarDef.All)
            {
                var d = DuckVisual.Create(av); _ducks.Add(d);
                var h = new Node3D(); h.AddChild(d); items.Add(h);
                h.AddChild(new Label3D { Text = av.Name, FontSize = 48, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.2f, 0), Modulate = av.Body.Lightened(0.3f), OutlineSize = 10, OutlineModulate = new Color(0, 0, 0, 0.8f) });
            }
        }
        if (weapons)
        {
            // one player duck per weapon, in slot order
            foreach (var def in WeaponDef.All)
            {
                var d = DuckVisual.Create(new Color(1f, 0.85f, 0.15f), new Color(0.35f, 0.25f, 0.15f), false); d.SetWeapon(def); _ducks.Add(d);
                var h = new Node3D(); h.AddChild(d); items.Add(h);
                var tag = new Label3D { Text = def.Short, FontSize = 48, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.2f, 0), Modulate = def.ElementColor, OutlineSize = 10, OutlineModulate = new Color(0, 0, 0, 0.8f) };
                h.AddChild(tag);
            }
        }
        foreach (var m in models)
        {
            var packed = GD.Load<PackedScene>("res://" + m);
            if (packed == null) { GD.PushError($"cannot load {m}"); continue; }
            items.Add(packed.Instantiate<Node3D>());
        }
        float x0 = -(items.Count - 1) * spacing / 2f;
        for (int i = 0; i < items.Count; i++)
        {
            items[i].Position = new Vector3(x0 + i * spacing, 0, 0);
            items[i].RotationDegrees = new Vector3(0, weapons ? -90 : 180 - 35, 0); // models face -Z; turn them toward the camera (side view for the weapon line-up)
            root.AddChild(items[i]);
        }
        var cam = new Camera3D { Fov = 40, Position = new Vector3(0, camDist * 0.75f, camDist), Current = true };
        root.AddChild(cam);
        cam.LookAtFromPosition(cam.Position, new Vector3(0, 0.8f, 0), Vector3.Up);
    }

    public override bool _Process(double delta)
    {
        _t += (float)delta;
        foreach (var d in _ducks) d.Animate(0.8f, (float)delta);
        if (_fx)
        {
            // effects demo: a tracer, a muzzle flash and a feather burst in front of the ducks, staggered so all are visible at capture time
            _fxFrame++;
            if (_fxFrame == 4) { var b = new Bullet { Velocity = new Vector3(-6f, 0, 0), FromPlayer = true, HitMask = 0 }; _fxRoot.AddChild(b); b.GlobalPosition = new Vector3(2.0f, 0.8f, 1.2f); }
            if (_fxFrame == 6) { var b = new Bullet { Velocity = new Vector3(5f, 0, 0), FromPlayer = false, HitMask = 0 }; _fxRoot.AddChild(b); b.GlobalPosition = new Vector3(-2.2f, 0.9f, 1.6f); }
            if (_fxFrame == 11) Fx.MuzzleFlash(_fxRoot, new Vector3(-0.9f, 0.85f, 0.8f), new Vector3(0.7f, 0, 0.7f).Normalized());
            if (_fxFrame == 7) Fx.Sparks(_fxRoot, new Vector3(1.2f, 0.8f, 0.4f), new Vector3(0, 0.3f, 1f).Normalized(), true);
            if (_fxFrame == 9) Fx.Sparks(_fxRoot, new Vector3(-0.2f, 0.4f, 1.4f), new Vector3(0, 0, 1f), false);
        }
        if (++_frames < 12) return false;
        var img = Root.GetTexture().GetImage();
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://" + _out.GetBaseDir()));
        img.SavePng(ProjectSettings.GlobalizePath("res://" + _out));
        GD.Print($"[Showcase] saved {_out}");
        return true;
    }
}
