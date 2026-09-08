using Godot;
namespace Duckov;

/// Build-time generator for scenes/Menu.tscn (the entry screen). Run: godot --headless --script scenes/BuildMenu.cs
/// The menu builds its widgets and the 3D duck diorama at runtime in scripts/Menu.cs; the scene only needs the root.
public partial class BuildMenu : SceneTree
{
    public override void _Initialize()
    {
        var temp = new Node();
        var root = new Control { Name = "Menu" };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        temp.AddChild(root);
        root.SetScript(GD.Load<Script>("res://scripts/Menu.cs"));
        root = temp.GetChild<Control>(0);
        var packed = new PackedScene();
        if (packed.Pack(root) != Error.Ok) { GD.PushError("pack failed"); Quit(1); return; }
        var err = ResourceSaver.Save(packed, "res://scenes/Menu.tscn");
        GD.Print($"[BuildMenu] saved res://scenes/Menu.tscn err={err}");
        Quit(err == Error.Ok ? 0 : 1);
    }
}
