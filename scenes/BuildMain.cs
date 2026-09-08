using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Build-time generator for scenes/Main.tscn. Run: godot --headless --script scenes/BuildMain.cs
public partial class BuildMain : SceneTree
{
    public override void _Initialize()
    {
        var temp = new Node();
        var root = new Node3D { Name = "Main" };
        temp.AddChild(root);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.55f, 0.72f, 0.95f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.66f, 0.74f, 0.86f),
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            // gentle depth haze toward the far edge of the view (cheap; no volumetrics)
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color(0.72f, 0.82f, 0.94f),
            FogLightEnergy = 1.0f,
            FogDepthBegin = 24f,
            FogDepthEnd = 75f,
            FogDepthCurve = 1.2f,
            FogSkyAffect = 0f,
            // no glow: on llvmpipe it halves the capture frame rate; tracers/flashes fake their bloom with additive halos
            GlowEnabled = false,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.08f,
            AdjustmentContrast = 1.02f,
        };
        root.AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.25f,
            LightColor = new Color(1f, 0.94f, 0.82f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 70f,
            RotationDegrees = new Vector3(-52f, 38f, 0f),
        };
        root.AddChild(sun);

        root.AddChild(new NavigationRegion3D { Name = "Arena" });
        root.AddChild(new CharacterBody3D { Name = "Player" });

        var rig = new Node3D { Name = "CameraRig" };
        // pitch so the camera looks from its offset toward the rig origin (LookAt needs a tree, so compute it)
        float pitch = -Mathf.RadToDeg(Mathf.Atan2(18.5f, 8.5f));
        var cam = new Camera3D { Name = "Camera3D", Fov = 42f, Current = true, Position = new Vector3(0, 19f, 8.5f), RotationDegrees = new Vector3(pitch, 0, 0) };
        rig.AddChild(cam);
        root.AddChild(rig);

        root.AddChild(new CanvasLayer { Name = "HUD" });

        // scripts last (SetScript disposes the C# wrapper)
        var scripts = new (string path, string script)[]
        {
            ("Arena", "res://scripts/Arena.cs"),
            ("Player", "res://scripts/Player.cs"),
            ("CameraRig", "res://scripts/CameraRig.cs"),
            ("HUD", "res://scripts/Hud.cs"),
        };
        foreach (var (path, script) in scripts)
            root.GetNode(path).SetScript(GD.Load<Script>(script));
        root.SetScript(GD.Load<Script>("res://scripts/Game.cs"));
        root = temp.GetChild<Node3D>(0);

        PackAndSave(root, "res://scenes/Main.tscn");
    }

    static void SetOwnerRecursive(Node node, Node owner)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = owner;
            if (string.IsNullOrEmpty(child.SceneFilePath)) SetOwnerRecursive(child, owner);
        }
    }

    static int CountNodes(Node n) { int c = 1; foreach (var ch in n.GetChildren()) c += CountNodes(ch); return c; }

    void PackAndSave(Node root, string path)
    {
        SetOwnerRecursive(root, root);
        int expected = CountNodes(root);
        var packed = new PackedScene();
        if (packed.Pack(root) != Error.Ok) { GD.PushError("pack failed"); Quit(1); return; }
        var test = packed.Instantiate(); int got = CountNodes(test); test.Free();
        if (got < expected) { GD.PushError($"nodes dropped: {got}/{expected}"); Quit(1); return; }
        var err = ResourceSaver.Save(packed, path);
        GD.Print($"[BuildMain] saved {path}: {got} nodes, err={err}");
        Quit(err == Error.Ok ? 0 : 1);
    }
}
