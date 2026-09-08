using Godot;
using System.Collections.Generic;
namespace Duckov;

/// One-shot positional sound effects. Streams live in assets/audio (synthesized by tools/make_sfx.py); each call spawns a
/// throwaway AudioStreamPlayer3D at the source and frees it when it finishes. Distances are measured from the
/// CameraRig's ground-level AudioListener3D, so the player's own gun is close and enemy fire falls off with range.
public static class Sfx
{
    static readonly Dictionary<string, AudioStream> _cache = new();
    static readonly RandomNumberGenerator _rng = new();   // own RNG: pitch jitter must not disturb the deterministic capture runs
    public static bool Muted;

    static AudioStream Load(string name)
    {
        if (_cache.TryGetValue(name, out var s)) return s;
        var path = $"res://assets/audio/{name}.wav";
        s = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        if (s == null) GD.PushWarning($"[Sfx] missing {path}");
        _cache[name] = s;
        return s;
    }

    public static void Play(Node parent, Vector3 pos, string name, float volumeDb = 0f, float pitchJitter = 0.06f, float unitSize = 7f, float maxDistance = 90f)
    {
        if (Muted || parent == null || !parent.IsInsideTree()) return;
        var stream = Load(name); if (stream == null) return;
        Music.Duck();
        var p = new AudioStreamPlayer3D
        {
            Stream = stream, VolumeDb = volumeDb, PitchScale = 1f + _rng.RandfRange(-pitchJitter, pitchJitter),
            UnitSize = unitSize, MaxDistance = maxDistance, MaxDb = 0f,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, PanningStrength = 0.6f,
        };
        parent.AddChild(p);
        p.GlobalPosition = pos;
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// Fire sound for a weapon, at its muzzle.
    public static void Shot(Node parent, Vector3 muzzle, WeaponDef w)
    {
        switch (w.Id)
        {
            case WeaponId.Shotgun: Play(parent, muzzle, "shot_shotgun", -2f, 0.05f); break;
            case WeaponId.IceSmg: Play(parent, muzzle, "shot_smg", -9f, 0.08f); break;
            case WeaponId.ElecSniper: Play(parent, muzzle, "shot_sniper", -2f, 0.04f); break;
            case WeaponId.Firework: Play(parent, muzzle, "shot_firework", -3f, 0.05f); break;
            default: Play(parent, muzzle, "shot_rifle", -4f, 0.06f); break;
        }
    }

    /// Firework shell burst.
    public static void Explosion(Node parent, Vector3 pos) => Play(parent, pos, "explosion", 0f, 0.05f, unitSize: 12f, maxDistance: 120f);
}
