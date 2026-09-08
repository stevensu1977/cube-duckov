using Godot;
using System.Collections.Generic;
namespace Duckov;

public enum WeatherKind { Clear, Rain, Storm, Snow, Fog }

/// Weather for the raid, after the Duckov maps: rain (the warehouse district on a wet day), the violet thunderstorm of the storm zone, the snow of
/// lab area 37, and dense fog. Sky, ambient, fog and sun blend toward per-weather targets; precipitation is one MultiMesh of
/// cubes that follows the camera; storms flash lightning (sun spike, screen flash, a jagged bolt); rain wets and snow
/// whitens the world shaders through the `wet` / `snow` uniforms; bad weather shortens enemy sight (VisibilityFactor)
/// and switches the yard lamps on.
///
/// Selection: DUCKOV_WEATHER env (clear|rain|storm|snow|fog|cycle), `--weather=` on Capture, or T in game. Default Clear,
/// so the deterministic proof runs are unchanged. Its own RNG never touches Game.Rng.
public partial class Weather : Node3D
{
    public static string Initial;                 // set by Capture before the scene loads
    public WeatherKind Kind { get; private set; } = WeatherKind.Clear;
    public bool Cycle;
    public float VisibilityFactor => Kind switch { WeatherKind.Rain => 0.85f, WeatherKind.Storm => 0.7f, WeatherKind.Snow => 0.9f, WeatherKind.Fog => 0.55f, _ => 1f };
    public bool Dark => Kind is WeatherKind.Storm or WeatherKind.Rain or WeatherKind.Fog;
    public float Lightning { get; private set; }  // 0..1 screen flash for the HUD
    public string Label => Kind switch { WeatherKind.Rain => "RAIN", WeatherKind.Storm => "THUNDERSTORM", WeatherKind.Snow => "SNOW", WeatherKind.Fog => "FOG", _ => "" };

    struct Look { public Color Sky, Ambient, FogCol, Sun; public float AmbientE, FogBegin, FogEnd, SunE, Wet, Snow, Sat; }
    static Look Of(WeatherKind k) => k switch
    {
        WeatherKind.Rain => new Look { Sky = new(0.42f, 0.48f, 0.58f), Ambient = new(0.5f, 0.56f, 0.66f), AmbientE = 0.55f, FogCol = new(0.5f, 0.56f, 0.64f), FogBegin = 14f, FogEnd = 50f, Sun = new(0.85f, 0.9f, 1f), SunE = 0.7f, Wet = 1f, Sat = 0.95f },
        WeatherKind.Storm => new Look { Sky = new(0.2f, 0.16f, 0.3f), Ambient = new(0.36f, 0.3f, 0.5f), AmbientE = 0.55f, FogCol = new(0.26f, 0.22f, 0.36f), FogBegin = 10f, FogEnd = 40f, Sun = new(0.7f, 0.65f, 0.95f), SunE = 0.35f, Wet = 1f, Sat = 0.9f },
        WeatherKind.Snow => new Look { Sky = new(0.74f, 0.78f, 0.86f), Ambient = new(0.7f, 0.76f, 0.88f), AmbientE = 0.6f, FogCol = new(0.8f, 0.84f, 0.9f), FogBegin = 16f, FogEnd = 55f, Sun = new(0.92f, 0.95f, 1f), SunE = 0.75f, Snow = 0.85f, Sat = 0.9f },
        WeatherKind.Fog => new Look { Sky = new(0.6f, 0.63f, 0.66f), Ambient = new(0.6f, 0.63f, 0.68f), AmbientE = 0.7f, FogCol = new(0.64f, 0.67f, 0.7f), FogBegin = 12f, FogEnd = 40f, Sun = new(0.9f, 0.92f, 0.95f), SunE = 0.55f, Sat = 0.8f },
        _ => new Look { Sky = new(0.55f, 0.72f, 0.95f), Ambient = new(0.66f, 0.74f, 0.86f), AmbientE = 0.6f, FogCol = new(0.72f, 0.82f, 0.94f), FogBegin = 24f, FogEnd = 75f, Sun = new(1f, 0.94f, 0.82f), SunE = 1.25f, Sat = 1.08f },
    };

    Godot.Environment _env; DirectionalLight3D _sun;
    Look _cur, _target; float _blend = 1f;
    readonly RandomNumberGenerator _rng = new() { Seed = 77 };
    MultiMeshInstance3D _drops; Vector3[] _pos; Vector3 _wind;
    int _count; float _fall, _cycleT, _boltT = 6f;
    const float HalfW = 19f, HalfD = 13f, Top = 16f;
    public readonly List<OmniLight3D> Lamps = new();
    Node3D _follow;

    public static WeatherKind Parse(string s) => (s ?? "").Trim().ToLowerInvariant() switch
    { "rain" => WeatherKind.Rain, "storm" => WeatherKind.Storm, "snow" => WeatherKind.Snow, "fog" => WeatherKind.Fog, _ => WeatherKind.Clear };

    public void Setup(Godot.Environment env, DirectionalLight3D sun, Node3D follow)
    {
        _env = env; _sun = sun; _follow = follow;
        _cur = _target = Of(WeatherKind.Clear);
        var pick = Initial ?? OS.GetEnvironment("DUCKOV_WEATHER");
        if (string.Equals(pick, "cycle", System.StringComparison.OrdinalIgnoreCase)) { Cycle = true; Set(WeatherKind.Rain, true); }
        else if (!string.IsNullOrEmpty(pick)) Set(Parse(pick), true);
    }

    public void Next() => Set((WeatherKind)(((int)Kind + 1) % 5), false);

    public void Set(WeatherKind k, bool instant)
    {
        if (k == Kind && !instant) return;
        Kind = k;
        _target = Of(k);
        _blend = instant ? 1f : 0f;
        BuildPrecipitation();
        Game.I?.Notify("weather");
        GD.Print($"[Weather] {k}");
    }

    void BuildPrecipitation()
    {
        _drops?.QueueFree(); _drops = null; _count = 0;
        if (Kind is WeatherKind.Clear or WeatherKind.Fog) return;
        bool snow = Kind == WeatherKind.Snow;
        _count = Kind switch { WeatherKind.Rain => 420, WeatherKind.Storm => 700, _ => 320 };
        _fall = snow ? 2.2f : (Kind == WeatherKind.Storm ? 22f : 17f);
        _wind = Kind == WeatherKind.Storm ? new Vector3(6f, 0, 2f) : snow ? new Vector3(0.8f, 0, 0.3f) : new Vector3(1.5f, 0, 0.5f);
        var mesh = snow ? new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) } : new BoxMesh { Size = new Vector3(0.035f, Kind == WeatherKind.Storm ? 0.9f : 0.65f, 0.035f) };
        var mat = snow
            ? new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.98f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
            : new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.85f, 1f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = _count };
        _drops = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_drops);
        _pos = new Vector3[_count];
        var c = Centre();
        for (int i = 0; i < _count; i++) _pos[i] = c + new Vector3(_rng.RandfRange(-HalfW, HalfW), _rng.RandfRange(0.2f, Top), _rng.RandfRange(-HalfD, HalfD));
    }

    Vector3 Centre() { var c = _follow != null && IsInstanceValid(_follow) ? _follow.GlobalPosition : Vector3.Zero; c.Y = 0; return c; }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_env == null) return;
        if (Cycle) { _cycleT += dt; if (_cycleT > 8f) { _cycleT = 0; Next(); if (Kind == WeatherKind.Clear) Next(); } }

        // blend the look
        if (_blend < 1f) _blend = Mathf.Min(1f, _blend + dt / 1.6f);
        var L = Lerp(_cur, _target, _blend);
        if (_blend >= 1f) _cur = _target;
        Lightning = Mathf.Max(0, Lightning - dt * 2.5f);
        float flash = Lightning * Lightning;
        _env.BackgroundColor = L.Sky.Lerp(Colors.White, flash * 0.6f);
        _env.AmbientLightColor = L.Ambient; _env.AmbientLightEnergy = L.AmbientE + flash * 1.5f;
        _env.FogLightColor = L.FogCol.Lerp(Colors.White, flash * 0.5f); _env.FogDepthBegin = L.FogBegin; _env.FogDepthEnd = L.FogEnd;
        _env.AdjustmentSaturation = L.Sat;
        _sun.LightColor = L.Sun.Lerp(Colors.White, flash); _sun.LightEnergy = L.SunE + flash * 5f;
        foreach (var m in Game.I?.Arena?.WorldMats ?? new List<ShaderMaterial>()) { m.SetShaderParameter("wet", L.Wet); m.SetShaderParameter("snow", L.Snow); }
        float lampE = Dark ? 2.6f : Kind == WeatherKind.Snow ? 1.2f : 0f;
        foreach (var l in Lamps) if (IsInstanceValid(l)) l.LightEnergy = Mathf.Lerp(l.LightEnergy, lampE, 1f - Mathf.Exp(-3f * dt));

        // lightning
        if (Kind == WeatherKind.Storm)
        {
            _boltT -= dt;
            if (_boltT <= 0)
            {
                _boltT = _rng.RandfRange(3.5f, 8f);
                Lightning = 1f;
                var c = Centre();
                var hit = c + new Vector3(_rng.RandfRange(-16f, 16f), 0, _rng.RandfRange(-10f, 10f));
                var parent = Game.I != null ? (Node)Game.I : this;
                Fx.Arc(parent, hit + new Vector3(_rng.RandfRange(-6f, 6f), 30f, _rng.RandfRange(-6f, 6f)), hit + Vector3.Up * 0.2f, 9, 0.22f);
                Fx.ElecBurst(parent, hit + Vector3.Up * 0.3f);
                Game.I?.Cam?.Kick(0.12f);
                Game.I?.Notify("lightning");
            }
        }

        // precipitation
        if (_drops == null) return;
        var centre = Centre();
        var mm = _drops.Multimesh;
        bool snow = Kind == WeatherKind.Snow;
        var basis = Basis.Identity;
        if (!snow)
        {
            // tilt the streaks along the fall direction (wind + gravity)
            var dir = (new Vector3(_wind.X, -_fall, _wind.Z)).Normalized();
            basis = new Basis(Vector3.Up.Cross(-dir).Normalized(), Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(-dir), -1f, 1f)));
        }
        for (int i = 0; i < _count; i++)
        {
            var p = _pos[i];
            p.Y -= _fall * dt * (snow ? _rng.RandfRange(0.7f, 1.3f) : 1f);
            p.X += _wind.X * dt + (snow ? Mathf.Sin(p.Y * 1.7f + i) * 0.6f * dt : 0f);
            p.Z += _wind.Z * dt;
            if (p.Y <= 0.05f)
            {
                if (!snow && Game.I != null && (i % 9) == 0) Fx.Splash(Game.I, new Vector3(p.X, 0.03f, p.Z));
                p = new Vector3(centre.X + _rng.RandfRange(-HalfW, HalfW), Top + _rng.RandfRange(0, 2f), centre.Z + _rng.RandfRange(-HalfD, HalfD));
            }
            // keep the volume around the camera: wrap drops that drifted out of the box
            if (p.X < centre.X - HalfW) p.X += 2 * HalfW; else if (p.X > centre.X + HalfW) p.X -= 2 * HalfW;
            if (p.Z < centre.Z - HalfD) p.Z += 2 * HalfD; else if (p.Z > centre.Z + HalfD) p.Z -= 2 * HalfD;
            _pos[i] = p;
            mm.SetInstanceTransform(i, new Transform3D(basis, p));
        }
    }

    static Look Lerp(Look a, Look b, float t) => new()
    {
        Sky = a.Sky.Lerp(b.Sky, t), Ambient = a.Ambient.Lerp(b.Ambient, t), FogCol = a.FogCol.Lerp(b.FogCol, t), Sun = a.Sun.Lerp(b.Sun, t),
        AmbientE = Mathf.Lerp(a.AmbientE, b.AmbientE, t), FogBegin = Mathf.Lerp(a.FogBegin, b.FogBegin, t), FogEnd = Mathf.Lerp(a.FogEnd, b.FogEnd, t),
        SunE = Mathf.Lerp(a.SunE, b.SunE, t), Wet = Mathf.Lerp(a.Wet, b.Wet, t), Snow = Mathf.Lerp(a.Snow, b.Snow, t), Sat = Mathf.Lerp(a.Sat, b.Sat, t),
    };
}
