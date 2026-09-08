using Godot;
namespace Duckov;

/// In-raid Esc: pauses the game and asks whether to go back to the entry screen. Esc / N resume, Enter / Y confirm; the two
/// buttons work with the mouse too. Lives on its own CanvasLayer above the HUD with ProcessMode Always so it keeps taking
/// input while the tree is paused. Music keeps playing (its node is Always as well).
public partial class PauseMenu : CanvasLayer
{
    Control _root;
    bool _open;
    Input.MouseModeEnum _mouseBefore;

    public bool IsOpen => _open;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.05f, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect); _root.AddChild(dim);

        var panel = new PanelContainer { Position = new Vector2(640 - 260, 360 - 130), Size = new Vector2(520, 260) };
        panel.AddThemeStyleboxOverride("panel", Box(new Color(0.05f, 0.06f, 0.09f, 0.92f), new Color(1f, 0.85f, 0.2f, 0.5f), 14));
        _root.AddChild(panel);
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; col.AddThemeConstantOverride("separation", 14); panel.AddChild(col);
        col.AddChild(L("PAUSED", 20, new Color(0.85f, 0.88f, 0.95f, 0.8f), HorizontalAlignment.Center));
        col.AddChild(L("Return to the start screen?", 32, new Color(1f, 0.92f, 0.5f), HorizontalAlignment.Center));
        col.AddChild(L("Your raid progress will be lost.", 16, new Color(0.85f, 0.88f, 0.95f), HorizontalAlignment.Center));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 24); col.AddChild(row);
        var yes = MakeButton("YES   Enter", new Color(0.55f, 0.22f, 0.2f)); yes.Pressed += Confirm; row.AddChild(yes);
        var no = MakeButton("NO   Esc", new Color(0.2f, 0.5f, 0.3f)); no.Pressed += Close; row.AddChild(no);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        var g = Game.I;
        if (g == null || !IsInstanceValid(g) || g.Current != Game.State.Playing) return;
        if (!_open)
        {
            if (k.Keycode == Key.Escape) { Open(); GetViewport().SetInputAsHandled(); }
            return;
        }
        switch (k.Keycode)
        {
            case Key.Escape or Key.N: Close(); GetViewport().SetInputAsHandled(); break;
            case Key.Enter or Key.KpEnter or Key.Y: Confirm(); GetViewport().SetInputAsHandled(); break;
        }
    }

    public void Open()
    {
        if (_open) return;
        _open = true;
        _root.Visible = true;
        _mouseBefore = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = true;
        Game.I?.Notify("pause");
        GD.Print("[Pause] open");
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _root.Visible = false;
        GetTree().Paused = false;
        Input.MouseMode = _mouseBefore;
        Game.I?.Notify("resume");
        GD.Print("[Pause] resume");
    }

    public void Confirm()
    {
        if (!_open) return;
        _open = false;
        _root.Visible = false;
        GetTree().Paused = false;
        GD.Print("[Pause] back to the start screen");
        Game.I?.ReturnToMenu();
    }

    // ---- the same look as the entry screen
    static Label L(string text, int size, Color col, HorizontalAlignment align)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        l.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.08f, 0.9f));
        l.AddThemeConstantOverride("outline_size", Mathf.Max(3, size / 6));
        return l;
    }

    static StyleBoxFlat Box(Color bg, Color border, int radius = 10)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(2); sb.SetCornerRadiusAll(radius); sb.SetContentMarginAll(14);
        return sb;
    }

    static Button MakeButton(string text, Color col)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(200, 54), FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.7f));
        b.AddThemeStyleboxOverride("normal", Box(new Color(col, 0.55f), new Color(1, 1, 1, 0.25f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(col.Lightened(0.15f), 0.75f), new Color(1, 1, 1, 0.6f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(col.Darkened(0.2f), 0.85f), new Color(1, 1, 1, 0.8f)));
        return b;
    }
}
