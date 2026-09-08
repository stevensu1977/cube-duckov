using Godot;
namespace Duckov;

/// In-game HUD: HP bar, ammo, loot, timer, extraction countdown, pickup prompt, crosshair, hurt flash, result overlay.
public partial class Hud : CanvasLayer
{
    Label _hpText, _ammo, _reload, _loot, _timer, _extract, _prompt, _hint, _kills, _weapon;
    ColorRect _hpFill, _extractFill, _extractBg, _hurt, _resultDim;
    Control _extractBox, _result;
    Label _resultTitle, _resultSub, _resultHint;
    HudOverlay _overlay;
    Label _weather;
    ColorRect _lightning;
    Minimap _minimap;
    float _t;

    static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.08f, 0.9f));
        l.AddThemeConstantOverride("outline_size", Mathf.Max(3, size / 6));
        return l;
    }

    static Panel Panel(float x, float y, float w, float h)
    {
        var p = new Panel { Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        var sb = new StyleBoxFlat { BgColor = new Color(0.03f, 0.04f, 0.06f, 0.42f), BorderColor = new Color(1, 1, 1, 0.12f) };
        sb.SetBorderWidthAll(1); sb.SetCornerRadiusAll(8);
        p.AddThemeStyleboxOverride("panel", sb);
        return p;
    }

    public override void _Ready()
    {
        Layer = 10;
        var root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // subtle vignette (cheap fullscreen shader) under all HUD elements
        var vig = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = new Shader { Code = @"
shader_type canvas_item;
void fragment() {
    vec2 p = (UV - 0.5) * vec2(1.25, 1.0);
    float d = length(p);
    float v = smoothstep(0.42, 0.95, d);
    COLOR = vec4(0.02, 0.02, 0.04, v * 0.42);
}" } } };
        vig.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(vig);

        _overlay = new HudOverlay { MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_overlay);

        // translucent panels behind the corner blocks
        root.AddChild(Panel(12, 10, 340, 78)); root.AddChild(Panel(1280 - 12 - 330, 10, 330, 78)); root.AddChild(Panel(12, 720 - 12 - 124, 290, 124));

        // HP (top-left)
        var hpLabel = MakeLabel("HP", 22, new Color(1, 1, 1)); hpLabel.Position = new Vector2(24, 18); root.AddChild(hpLabel);
        var hpBg = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Position = new Vector2(66, 20), Size = new Vector2(264, 26), MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(hpBg);
        _hpFill = new ColorRect { Color = new Color(0.3f, 0.9f, 0.35f), Position = new Vector2(69, 23), Size = new Vector2(258, 20), MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(_hpFill);
        _hpText = MakeLabel("100 / 100", 18, new Color(1, 1, 1), HorizontalAlignment.Center); _hpText.Position = new Vector2(66, 20); _hpText.Size = new Vector2(264, 26); root.AddChild(_hpText);

        // Kills (under HP)
        _kills = MakeLabel("KILLS  0", 18, new Color(1f, 0.75f, 0.7f)); _kills.Position = new Vector2(24, 52); root.AddChild(_kills);

        // Loot + timer (top-right)
        _loot = MakeLabel("LOOT  0", 30, new Color(1f, 0.9f, 0.4f), HorizontalAlignment.Right); _loot.Position = new Vector2(1280 - 24 - 300, 14); _loot.Size = new Vector2(300, 34); root.AddChild(_loot);
        _timer = MakeLabel("00:00", 22, new Color(0.9f, 0.9f, 0.95f), HorizontalAlignment.Right); _timer.Position = new Vector2(1280 - 24 - 300, 50); _timer.Size = new Vector2(300, 26); root.AddChild(_timer);

        // Minimap (top-right, under the loot panel)
        _minimap = new Minimap { Position = new Vector2(1280 - 12 - 200, 96), Size = new Vector2(200, 200), MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        root.AddChild(_minimap);

        // Weapon + ammo (bottom-left): name, slot squares (drawn by HudOverlay), ammo, reload prompt
        _weapon = MakeLabel("QBZ-191 Rifle", 19, new Color(1f, 0.9f, 0.6f)); _weapon.Position = new Vector2(24, 720 - 12 - 124 + 6); root.AddChild(_weapon);
        _ammo = MakeLabel("12 | 48", 40, new Color(1, 1, 1)); _ammo.Position = new Vector2(24, 720 - 24 - 48); root.AddChild(_ammo);
        _reload = MakeLabel("R  RELOAD", 17, new Color(1f, 0.6f, 0.3f)); _reload.Position = new Vector2(170, 720 - 24 - 40); root.AddChild(_reload);

        // Hint (bottom-center, two short lines so it never runs into the ammo counter)
        _hint = MakeLabel("Grab loot, then reach the green EXTRACT pad in the north-east\nWASD move  ·  Mouse aim / shoot  ·  R reload  ·  E pick up", 16, new Color(0.9f, 0.92f, 1f, 0.9f), HorizontalAlignment.Center);
        _hint.Position = new Vector2(320, 720 - 66); _hint.Size = new Vector2(640, 50); root.AddChild(_hint);

        // Extraction countdown (center-top)
        _extractBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(640 - 190, 90), Size = new Vector2(380, 70) };
        _extract = MakeLabel("EXTRACTING  3.0", 30, new Color(0.5f, 1f, 0.6f), HorizontalAlignment.Center); _extract.Size = new Vector2(380, 40); _extractBox.AddChild(_extract);
        _extractBg = new ColorRect { Color = new Color(0, 0, 0, 0.55f), Position = new Vector2(0, 44), Size = new Vector2(380, 16), MouseFilter = Control.MouseFilterEnum.Ignore }; _extractBox.AddChild(_extractBg);
        _extractFill = new ColorRect { Color = new Color(0.4f, 1f, 0.55f), Position = new Vector2(3, 47), Size = new Vector2(0, 10), MouseFilter = Control.MouseFilterEnum.Ignore }; _extractBox.AddChild(_extractFill);
        root.AddChild(_extractBox);
        _extractBox.Visible = false;

        // Pickup prompt (bottom-center)
        _prompt = MakeLabel("[E]  Pick up", 26, new Color(1f, 0.95f, 0.6f), HorizontalAlignment.Center); _prompt.Position = new Vector2(0, 720 - 130); _prompt.Size = new Vector2(1280, 34); root.AddChild(_prompt);
        _prompt.Visible = false;

        // Lightning flash (weather) under the hurt flash
        _lightning = new ColorRect { Color = new Color(0.9f, 0.9f, 1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _lightning.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(_lightning);
        _weather = MakeLabel("", 17, new Color(0.85f, 0.9f, 1f), HorizontalAlignment.Center);
        _weather.Position = new Vector2(320, 36); _weather.Size = new Vector2(640, 24); root.AddChild(_weather);
        _weather.Visible = false;

        // Hurt flash
        _hurt = new ColorRect { Color = new Color(0.9f, 0.05f, 0.05f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hurt.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.AddChild(_hurt);

        // Result overlay
        _result = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _result.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _resultDim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.05f, 0.72f), MouseFilter = Control.MouseFilterEnum.Ignore }; _resultDim.SetAnchorsPreset(Control.LayoutPreset.FullRect); _result.AddChild(_resultDim);
        _resultTitle = MakeLabel("EXTRACTED", 96, new Color(0.5f, 1f, 0.6f), HorizontalAlignment.Center); _resultTitle.Position = new Vector2(0, 200); _resultTitle.Size = new Vector2(1280, 120); _result.AddChild(_resultTitle);
        _resultSub = MakeLabel("", 34, new Color(1, 1, 1), HorizontalAlignment.Center); _resultSub.Position = new Vector2(0, 330); _resultSub.Size = new Vector2(1280, 50); _result.AddChild(_resultSub);
        _resultHint = MakeLabel("Press  R  to restart      Esc  menu", 26, new Color(0.8f, 0.85f, 0.9f), HorizontalAlignment.Center); _resultHint.Position = new Vector2(0, 420); _resultHint.Size = new Vector2(1280, 40); _result.AddChild(_resultHint);
        root.AddChild(_result);
        _result.Visible = false;
    }

    public void ShowResult(bool extracted, int loot, int kills, double time)
    {
        _result.Visible = true;
        _resultTitle.Text = extracted ? "EXTRACTED" : "KIA";
        _resultTitle.AddThemeColorOverride("font_color", extracted ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.3f, 0.3f));
        _resultSub.Text = extracted
            ? $"Loot  {loot}     Kills  {kills}     Time  {FormatTime(time)}"
            : $"Lost  {loot} loot     Kills  {kills}     Survived  {FormatTime(time)}";
        _extractBox.Visible = false;
        _prompt.Visible = false;
        _hint.Visible = false;
    }

    static string FormatTime(double t) => $"{(int)(t / 60):00}:{(int)(t % 60):00}";

    public override void _Process(double delta)
    {
        _t += (float)delta;
        var g = Game.I;
        if (g == null || g.Player == null) return;
        var p = g.Player;
        _hpFill.Size = new Vector2(258f * Mathf.Clamp(p.Hp / (float)p.MaxHp, 0, 1), 20);
        _hpFill.Color = p.Hp > 50 ? new Color(0.3f, 0.9f, 0.35f) : p.Hp > 25 ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.25f, 0.2f);
        _hpText.Text = $"{p.Hp} / {p.MaxHp}";
        _kills.Text = $"KILLS  {g.Kills}";
        _ammo.Text = $"{p.Mag} | {p.Reserve}";
        _weapon.Text = p.WeaponDef.Name;
        _weapon.AddThemeColorOverride("font_color", p.WeaponDef.Element == Element.None ? new Color(1f, 0.9f, 0.6f) : p.WeaponDef.ElementColor);
        _ammo.AddThemeColorOverride("font_color", p.Mag == 0 ? new Color(1f, 0.35f, 0.3f) : new Color(1, 1, 1));
        _reload.Visible = p.Reloading || (p.Mag < p.MagSize / 2 && p.Reserve > 0 && Mathf.Sin(_t * 6f) > 0) || (p.Mag == 0 && p.Reserve == 0);
        _reload.Text = p.Reloading ? $"RELOADING  {p.ReloadLeft:0.0}" : (p.Reserve == 0 && p.Mag == 0 ? "NO AMMO — find an ammo box" : "R  RELOAD");
        _loot.Text = $"LOOT  {g.Loot}";
        _timer.Text = FormatTime(g.RaidTime);
        _hurt.Color = new Color(0.9f, 0.05f, 0.05f, 0.16f * p.HurtFlash);
        if (g.Weather != null)
        {
            float f = g.Weather.Lightning; _lightning.Color = new Color(0.9f, 0.9f, 1f, 0.55f * f * f);
            _weather.Visible = g.Weather.Kind != WeatherKind.Clear && g.Current == Game.State.Playing;
            _weather.Text = g.Weather.Label;
            _weather.Position = new Vector2(320, 12);
        }

        bool inZone = g.ExtractProgress > 0 && g.Current == Game.State.Playing;
        _extractBox.Visible = inZone;
        if (inZone)
        {
            float left = Mathf.Max(0, Game.ExtractSeconds - g.ExtractProgress);
            _extract.Text = $"EXTRACTING  {left:0.0}";
            _extractFill.Size = new Vector2(374f * Mathf.Clamp(g.ExtractProgress / Game.ExtractSeconds, 0, 1), 10);
        }
        bool showPrompt = p.NearbyLoot != null && g.Current == Game.State.Playing && !p.Dead;
        _prompt.Visible = showPrompt;
        if (showPrompt) _prompt.Text = $"[E]  Pick up {p.NearbyLoot.DisplayName}";
        _hint.Visible = g.Current == Game.State.Playing && g.RaidTime < 12;
        _minimap.Visible = g.Current == Game.State.Playing;
        _minimap.QueueRedraw();
        _overlay.QueueRedraw();
    }
}

/// Crosshair at the aim point plus a small direction indicator toward the extraction pad.
public partial class HudOverlay : Control
{
    public override void _Draw()
    {
        var g = Game.I;
        if (g == null || g.Player == null || g.Current != Game.State.Playing) return;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var p = g.Player;

        // weapon slots 1-5 under the weapon name: owned = filled, current = bright frame, ammo-empty = dimmed digit
        {
            var font = ThemeDB.FallbackFont;
            float y = 720 - 12 - 124 + 34;
            foreach (var def in WeaponDef.All)
            {
                float x = 24 + (def.Slot - 1) * 30;
                var st = p.Get(def.Id);
                var r = new Rect2(x, y, 24, 22);
                DrawRect(r, st != null ? new Color(def.ElementColor, 0.35f) : new Color(1, 1, 1, 0.06f));
                DrawRect(r, st != null && st.Def.Id == p.WeaponDef.Id ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.18f), false, st != null && st.Def.Id == p.WeaponDef.Id ? 2f : 1f);
                var col = st == null ? new Color(1, 1, 1, 0.3f) : (st.Mag + st.Reserve == 0 ? new Color(1f, 0.5f, 0.4f) : new Color(1, 1, 1));
                DrawString(font, new Vector2(x + 7, y + 16), def.Slot.ToString(), HorizontalAlignment.Left, -1, 14, col);
            }
        }

        // crosshair
        var aim = p.AimPoint;
        if (!cam.IsPositionBehind(aim))
        {
            var s = cam.UnprojectPosition(aim);
            var col = p.Reloading ? new Color(1f, 0.6f, 0.3f) : new Color(1f, 1f, 1f);
            var shadow = new Color(0, 0, 0, 0.7f);
            float r = 10f, gap = 5f, len = 9f;
            for (int pass = 0; pass < 2; pass++)
            {
                var c = pass == 0 ? shadow : col;
                float w = pass == 0 ? 4f : 2f;
                DrawArc(s, r, 0, Mathf.Tau, 32, c, w, true);
                DrawLine(s + new Vector2(0, -r - gap), s + new Vector2(0, -r - gap - len), c, w);
                DrawLine(s + new Vector2(0, r + gap), s + new Vector2(0, r + gap + len), c, w);
                DrawLine(s + new Vector2(-r - gap, 0), s + new Vector2(-r - gap - len, 0), c, w);
                DrawLine(s + new Vector2(r + gap, 0), s + new Vector2(r + gap + len, 0), c, w);
            }
            DrawCircle(s, 2f, col);
            if (p.Reloading)
                DrawArc(s, r + 16, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (1f - p.ReloadLeft / p.ReloadTime), 32, new Color(1f, 0.6f, 0.3f), 3f, true);
        }

        // extraction indicator near the player
        var ppos = p.GlobalPosition;
        var ext = g.Arena.ExtractPos;
        float dist = (ext - ppos).Length();
        var ps = cam.UnprojectPosition(ppos + new Vector3(0, 1.8f, 0));
        var es = cam.UnprojectPosition(ext);
        var dir = (es - ps);
        if (dir.LengthSquared() > 1f && dist > 5f)
        {
            dir = dir.Normalized();
            var tip = ps + dir * 78f;
            var perp = new Vector2(-dir.Y, dir.X);
            var tri = new[] { tip + dir * 12f, tip - dir * 4f + perp * 8f, tip - dir * 4f - perp * 8f };
            DrawColoredPolygon(tri, new Color(0.4f, 1f, 0.6f, 0.9f));
            var font = ThemeDB.FallbackFont;
            var label = $"{dist:0}m";
            DrawString(font, tip + perp * 0f + dir * 26f + new Vector2(-14, 6), label, HorizontalAlignment.Left, -1, 15, new Color(0.7f, 1f, 0.8f, 0.95f));
        }
    }
}

/// Classic extraction-shooter minimap: north-up, centred on the player, 4 px per metre (50 m across). Walls and paved
/// zones from Arena, loot as yellow squares (gold for the Gold bar), enemies within radar range as red dots, teammates
/// as blue dots, the extraction pad as a green square, the player as a white arrow. Off-map targets stick to the edge.
public partial class Minimap : Control
{
    const float MapScale = 4f, RadarRange = 16f;

    public override void _Draw()
    {
        var g = Game.I;
        if (g == null || g.Player == null || g.Arena == null) return;
        var size = Size; var c = size / 2f;
        var pp = g.Player.GlobalPosition;
        Vector2 W(Vector3 w) => c + new Vector2(w.X - pp.X, w.Z - pp.Z) * MapScale;
        Vector2 R(float wx, float wz) => c + new Vector2(wx - pp.X, wz - pp.Z) * MapScale;

        // frame + ground
        var sb = new StyleBoxFlat { BgColor = new Color(0.03f, 0.04f, 0.06f, 0.55f), BorderColor = new Color(1, 1, 1, 0.18f) };
        sb.SetBorderWidthAll(1); sb.SetCornerRadiusAll(8);
        DrawStyleBox(sb, new Rect2(Vector2.Zero, size));
        // arena floor and the paved zones
        DrawRect(new Rect2(R(-30, -30), new Vector2(60, 60) * MapScale), new Color(0.25f, 0.34f, 0.2f, 0.55f));
        DrawRect(new Rect2(R(g.Arena.RoadRect.Position.X, g.Arena.RoadRect.Position.Y), g.Arena.RoadRect.Size * MapScale), new Color(0.2f, 0.2f, 0.22f, 0.8f));
        foreach (var z in g.Arena.ZoneRects) DrawRect(new Rect2(R(z.Position.X, z.Position.Y), z.Size * MapScale), new Color(0.42f, 0.42f, 0.4f, 0.7f));
        // 10 m grid
        for (int i = -30; i <= 30; i += 10)
        {
            DrawLine(R(i, -30), R(i, 30), new Color(1, 1, 1, 0.06f));
            DrawLine(R(-30, i), R(30, i), new Color(1, 1, 1, 0.06f));
        }
        foreach (var r in g.Arena.PropRects) DrawRect(new Rect2(R(r.Position.X, r.Position.Y), r.Size * MapScale), new Color(0.6f, 0.55f, 0.45f, 0.75f));
        foreach (var r in g.Arena.WallRects) DrawRect(new Rect2(R(r.Position.X, r.Position.Y), r.Size * MapScale), new Color(0.82f, 0.82f, 0.85f, 0.9f));

        // extraction
        var ext = g.Arena.ExtractPos; float er = g.Arena.ExtractRadius;
        DrawRect(new Rect2(R(ext.X - er, ext.Z - er), new Vector2(er * 2, er * 2) * MapScale), new Color(0.3f, 1f, 0.55f, 0.55f));
        DrawRect(new Rect2(R(ext.X - er, ext.Z - er), new Vector2(er * 2, er * 2) * MapScale), new Color(0.5f, 1f, 0.7f), false, 1.5f);

        // loot
        foreach (var l in g.LootItems)
        {
            if (!IsInstanceValid(l) || l.Collected) continue;
            var col = l.Type == Loot.Kind.Gold ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.95f, 0.5f);
            DrawRect(new Rect2(W(l.GlobalPosition) - new Vector2(3, 3), new Vector2(6, 6)), col);
        }
        // enemies on radar
        foreach (var e in g.Enemies)
        {
            if (!IsInstanceValid(e) || e.Dead) continue;
            var d = e.GlobalPosition - pp; d.Y = 0;
            if (d.Length() > RadarRange) continue;
            DrawCircle(W(e.GlobalPosition), 4f, new Color(1f, 0.25f, 0.2f));
        }
        // player arrow
        float yaw = g.Player.Rotation.Y;
        var fwd = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        var side = new Vector2(-fwd.Y, fwd.X);
        var tri = new[] { c + fwd * 8f, c - fwd * 5f + side * 5.5f, c - fwd * 5f - side * 5.5f };
        DrawColoredPolygon(tri, new Color(0, 0, 0, 0.7f));
        DrawColoredPolygon(new[] { c + fwd * 6.5f, c - fwd * 3.8f + side * 4f, c - fwd * 3.8f - side * 4f }, new Color(1f, 1f, 1f));
        // extraction edge marker when off-map, and a north label
        var es = W(ext);
        if (!new Rect2(Vector2.Zero, size).HasPoint(es)) DrawCircle(Clamp(es, size, 7f), 4f, new Color(0.4f, 1f, 0.6f));
        DrawString(ThemeDB.FallbackFont, new Vector2(size.X / 2f - 4, 14), "N", HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, 0.8f));
    }

    static Vector2 Clamp(Vector2 p, Vector2 size, float margin) => new(Mathf.Clamp(p.X, margin, size.X - margin), Mathf.Clamp(p.Y, margin, size.Y - margin));
}
