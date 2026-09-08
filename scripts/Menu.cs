using Godot;
namespace Duckov;

/// Entry screen: single-player / multiplayer (greyed, coming soon) / quit, avatar picker with a live 3D duck, a name
/// field, and the raid history from the profile. Enter starts, ←/→ change the avatar, Esc quits.
/// A voxel diorama (ground blocks, crates, a lamp) with the selected duck turning slowly fills the background.
public partial class Menu : Control
{
    Label _avatarName, _avatarTitle, _stats;
    LineEdit _name;
    VBoxContainer _history;
    Button _single, _multi, _quit;
    SubViewport _vp;
    Node3D _duckHolder; DuckVisual _duck; Camera3D _cam;
    float _t; int _avatar; bool _starting;

    static Label L(string text, int size, Color col, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
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

    Button MakeButton(string text, Color col, bool enabled = true)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(300, 58), Disabled = !enabled, FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 26);
        b.AddThemeColorOverride("font_color", enabled ? new Color(1, 1, 1) : new Color(1, 1, 1, 0.35f));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.7f));
        b.AddThemeColorOverride("font_disabled_color", new Color(1, 1, 1, 0.35f));
        b.AddThemeStyleboxOverride("normal", Box(new Color(col, 0.55f), new Color(1, 1, 1, 0.25f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(col.Lightened(0.15f), 0.75f), new Color(1, 1, 1, 0.6f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(col.Darkened(0.2f), 0.85f), new Color(1, 1, 1, 0.8f)));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.25f, 0.26f, 0.3f, 0.4f), new Color(1, 1, 1, 0.1f)));
        return b;
    }

    public override void _Ready()
    {
        Profile.Load();
        Music.Ensure(this);
        _avatar = Profile.Avatar;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // ---- 3D diorama background
        var vpc = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        vpc.SetAnchorsPreset(LayoutPreset.FullRect);
        _vp = new SubViewport { Size = new Vector2I(1280, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        vpc.AddChild(_vp); AddChild(vpc);
        BuildDiorama(_vp);

        // ---- dim gradient so text reads
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, 0.35f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(dim);

        // ---- title + buttons (left)
        var left = new VBoxContainer { Position = new Vector2(60, 70), Size = new Vector2(360, 600) };
        left.AddThemeConstantOverride("separation", 14);
        AddChild(left);
        left.AddChild(L("ESCAPE FROM", 30, new Color(0.95f, 0.9f, 0.7f)));
        left.AddChild(L("DUCKOV-LIKE", 56, new Color(1f, 0.85f, 0.2f)));
        left.AddChild(L("Voxel extraction shooter", 20, new Color(0.85f, 0.88f, 0.95f)));
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });
        _single = MakeButton("SINGLE-PLAYER", new Color(0.2f, 0.55f, 0.3f));
        _single.Pressed += StartSingle;
        left.AddChild(_single);
        _multi = MakeButton("MULTIPLAYER", new Color(0.25f, 0.4f, 0.65f), enabled: false);
        _multi.TooltipText = "Coming soon";
        left.AddChild(_multi);
        left.AddChild(L("Multiplayer coming soon", 14, new Color(0.75f, 0.78f, 0.85f, 0.8f)));
        _quit = MakeButton("QUIT", new Color(0.5f, 0.22f, 0.2f));
        _quit.Pressed += () => GetTree().Quit();
        left.AddChild(_quit);

        // name
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        left.AddChild(L("NAME", 16, new Color(0.85f, 0.88f, 0.95f)));
        _name = new LineEdit { Text = Profile.Name, MaxLength = 16, CustomMinimumSize = new Vector2(300, 42), PlaceholderText = "duck" };
        _name.AddThemeFontSizeOverride("font_size", 22);
        _name.TextSubmitted += _ => StartSingle();
        _name.TextChanged += t => { Profile.Name = string.IsNullOrWhiteSpace(t) ? "Duck" : t.Trim(); };
        left.AddChild(_name);

        // ---- avatar picker (centre)
        var pick = new HBoxContainer { Position = new Vector2(640 - 190, 560), Size = new Vector2(380, 60), Alignment = BoxContainer.AlignmentMode.Center };
        pick.AddThemeConstantOverride("separation", 18);
        AddChild(pick);
        var prev = MakeButton("◀", new Color(0.3f, 0.3f, 0.35f)); prev.CustomMinimumSize = new Vector2(58, 58); prev.Pressed += () => ChangeAvatar(-1); pick.AddChild(prev);
        var mid = new VBoxContainer { CustomMinimumSize = new Vector2(220, 58), Alignment = BoxContainer.AlignmentMode.Center };
        _avatarName = L("", 28, new Color(1f, 0.92f, 0.5f), HorizontalAlignment.Center); mid.AddChild(_avatarName);
        _avatarTitle = L("", 15, new Color(0.85f, 0.88f, 0.95f), HorizontalAlignment.Center); mid.AddChild(_avatarTitle);
        pick.AddChild(mid);
        var next = MakeButton("▶", new Color(0.3f, 0.3f, 0.35f)); next.CustomMinimumSize = new Vector2(58, 58); next.Pressed += () => ChangeAvatar(1); pick.AddChild(next);
        var pickHint = L("AVATAR   ←  →", 15, new Color(0.85f, 0.88f, 0.95f, 0.8f), HorizontalAlignment.Center);
        pickHint.Position = new Vector2(640 - 190, 626); pickHint.Size = new Vector2(380, 22); AddChild(pickHint);

        // ---- history (right)
        var panel = new PanelContainer { Position = new Vector2(1280 - 60 - 400, 70), Size = new Vector2(400, 560) };
        panel.AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.04f, 0.06f, 0.6f), new Color(1, 1, 1, 0.15f)));
        AddChild(panel);
        var right = new VBoxContainer(); right.AddThemeConstantOverride("separation", 6); panel.AddChild(right);
        right.AddChild(L("HISTORY", 24, new Color(1f, 0.9f, 0.5f)));
        _stats = L("", 15, new Color(0.85f, 0.88f, 0.95f)); _stats.AutowrapMode = TextServer.AutowrapMode.Word; right.AddChild(_stats);
        right.AddChild(new HSeparator());
        _history = new VBoxContainer(); _history.AddThemeConstantOverride("separation", 3); right.AddChild(_history);

        var foot = L("Enter start  ·  ← → change avatar  ·  Esc quit          v0.8 · Godot 4.6 · CC0 self-made + Kenney", 14, new Color(0.8f, 0.83f, 0.9f, 0.7f), HorizontalAlignment.Center);
        foot.Position = new Vector2(0, 720 - 34); foot.Size = new Vector2(1280, 24); AddChild(foot);

        ApplyAvatar();
        RefreshHistory();
    }

    void BuildDiorama(SubViewport vp)
    {
        var world = new Node3D(); vp.AddChild(world);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.45f, 0.62f, 0.88f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.66f, 0.74f, 0.86f), AmbientLightEnergy = 0.65f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.05f,
            FogEnabled = true, FogMode = Godot.Environment.FogModeEnum.Depth, FogLightColor = new Color(0.6f, 0.72f, 0.9f), FogDepthBegin = 10f, FogDepthEnd = 40f, FogSkyAffect = 0f,
        };
        world.AddChild(new WorldEnvironment { Environment = env });
        world.AddChild(new DirectionalLight3D { LightEnergy = 1.2f, LightColor = new Color(1f, 0.94f, 0.82f), ShadowEnabled = true, RotationDegrees = new Vector3(-50, 35, 0) });

        // ground: a checker of grass / dirt blocks with a stone-brick stage in the middle
        var grass = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.44f, 0.55f, 0.3f), Roughness = 0.95f });
        var dirt = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.47f, 0.34f), Roughness = 0.95f });
        var stone = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.56f, 0.55f, 0.52f), Roughness = 0.9f });
        var rng = new RandomNumberGenerator { Seed = 5 };
        for (int x = -12; x <= 12; x++)
            for (int z = -8; z <= 8; z++)
            {
                bool stage = Mathf.Abs(x) <= 2 && Mathf.Abs(z) <= 2;
                var m = stage ? stone : rng.Randf() < 0.25f ? dirt : grass;
                world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1, 1, 1) }, MaterialOverride = m, Position = new Vector3(x, stage ? -0.35f : -0.5f, z) });
            }
        // props around the stage
        void Prop(string name, float x, float z, float rot, float scale = 1f)
        {
            var ps = GD.Load<PackedScene>($"res://assets/models/props/{name}.glb");
            if (ps == null) return;
            var n = ps.Instantiate<Node3D>(); Blocky.Apply(n);
            n.Position = new Vector3(x, 0, z); n.RotationDegrees = new Vector3(0, rot, 0); n.Scale = Vector3.One * scale;
            world.AddChild(n);
        }
        Prop("crate", -4.2f, -1.5f, 10); Prop("crate", -4.2f, -1.5f, 10); Prop("barrel", 4.0f, -2.2f, 0); Prop("barrel", 4.9f, -1.6f, 30);
        Prop("sandbags", 0, -4.5f, 0); Prop("tent", -7, -4, 15); Prop("tree_round", 8, -6, 0, 0.9f); Prop("tree_pine", -10, -6, 0, 1.1f);
        Prop("loot_gold", 3.2f, 1.6f, 20); Prop("loot_medkit", -3.4f, 1.8f, -15);
        // a yard lamp, lit
        var post = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.23f, 0.26f), Roughness = 0.6f });
        world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.24f, 4.6f, 0.24f) }, MaterialOverride = post, Position = new Vector3(5.5f, 2.3f, 1.5f) });
        world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.3f, 0.12f, 0.12f) }, MaterialOverride = post, Position = new Vector3(4.95f, 4.55f, 1.5f) });
        world.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.08f, 0.36f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.92f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.55f), EmissionEnergyMultiplier = 1.4f }, Position = new Vector3(4.4f, 4.3f, 1.5f) });
        world.AddChild(new OmniLight3D { Position = new Vector3(4.4f, 4.0f, 1.5f), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 1.6f, OmniRange = 9f, ShadowEnabled = false });

        // the duck on the stage
        _duckHolder = new Node3D { Position = new Vector3(0, 0.15f, 0) };
        world.AddChild(_duckHolder);

        _cam = new Camera3D { Fov = 38f, Position = new Vector3(0.6f, 2.6f, 6.2f), Current = true };
        world.AddChild(_cam);
        _cam.LookAt(new Vector3(0.1f, 1.0f, 0), Vector3.Up);
    }

    void ApplyAvatar()
    {
        var def = AvatarDef.Get(_avatar);
        _duck?.QueueFree();
        _duck = DuckVisual.Create(def);
        _duckHolder.AddChild(_duck);
        _duck.SetWeapon(WeaponDef.Get(WeaponId.Rifle));
        _avatarName.Text = def.Name;
        _avatarTitle.Text = $"{def.Title}   ({Mathf.PosMod(_avatar, AvatarDef.All.Length) + 1} / {AvatarDef.All.Length})";
        Profile.Avatar = Mathf.PosMod(_avatar, AvatarDef.All.Length);
    }

    public void ChangeAvatar(int dir) { _avatar = Mathf.PosMod(_avatar + dir, AvatarDef.All.Length); ApplyAvatar(); }

    void RefreshHistory()
    {
        foreach (var c in _history.GetChildren()) c.QueueFree();
        if (Profile.Raids == 0)
        {
            _stats.Text = "No raids yet — finish one (extract or KIA) and it will show up here.";
            return;
        }
        _stats.Text = $"Raids {Profile.Raids}   Extracted {Profile.Extractions}   Kills {Profile.TotalKills}   Loot out {Profile.TotalLoot}   Best {Profile.BestLoot}";
        int shown = 0;
        foreach (var r in Profile.History)
        {
            if (shown++ >= 10) break;
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            var res = L(r.Extracted ? "OUT" : "KIA", 16, r.Extracted ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f)); res.CustomMinimumSize = new Vector2(44, 0); row.AddChild(res);
            var date = L(r.Date.Length >= 16 ? r.Date.Substring(5) : r.Date, 14, new Color(0.8f, 0.83f, 0.9f)); date.CustomMinimumSize = new Vector2(96, 0); row.AddChild(date);
            var detail = L($"Loot {r.Loot}  Kills {r.Kills}  {Profile.FormatTime(r.Time)}", 14, new Color(0.95f, 0.95f, 0.98f)); detail.CustomMinimumSize = new Vector2(150, 0); row.AddChild(detail);
            var extra = L($"{WeatherName(r.Weather)} {WeaponShort(r.Weapon)}", 13, new Color(0.75f, 0.8f, 0.9f, 0.85f)); row.AddChild(extra);
            _history.AddChild(row);
        }
    }

    static string WeatherName(string w) => w switch { "Rain" => "Rain", "Storm" => "Storm", "Snow" => "Snow", "Fog" => "Fog", _ => "Clear" };
    static string WeaponShort(string w) => w == "烟花枪" ? "Firework" : w;   // profiles saved before the English-only pass

    public void StartSingle()
    {
        if (_starting) return;
        _starting = true;
        Profile.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Duck" : _name.Text.Trim();
        Profile.Avatar = Mathf.PosMod(_avatar, AvatarDef.All.Length);
        Profile.Save();
        GD.Print($"[Menu] start single-player as '{Profile.Name}' avatar={Profile.Avatar}");
        var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate();
        GetParent().CallDeferred(Node.MethodName.AddChild, main);
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case Key.Enter or Key.KpEnter or Key.Space: if (!_name.HasFocus() || k.Keycode != Key.Space) StartSingle(); break;
            case Key.Left: ChangeAvatar(-1); break;
            case Key.Right: ChangeAvatar(1); break;
            case Key.Escape: GetTree().Quit(); break;
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_duckHolder != null) _duckHolder.Rotation = new Vector3(0, Mathf.Pi + 0.3f + Mathf.Sin(_t * 0.5f) * 0.6f, 0);   // face the camera, sway a little
        _duck?.Animate(0.15f + 0.1f * Mathf.Sin(_t * 1.3f), (float)delta);
    }
}
