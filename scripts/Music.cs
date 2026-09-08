using Godot;
namespace Duckov;

/// Background music: one looping, non-positional player that lives under the scene-tree root, so it keeps playing across
/// the menu → raid → result → menu swaps. Sits well under the guns (BaseDb) and dips a little further on every shot.
public partial class Music : Node
{
    public const string Track = "res://assets/audio/Scrapyard Pulse.wav";
    public const float BaseDb = -16f;       // track mean is -15 dBFS, the guns fire at -4..-2 dB gain: ~15 dB headroom for the shots
    const float DuckDb = 5f;                // extra dip when a gun fires
    const float DuckRecoverPerSec = 12f;    // dB per second back toward BaseDb

    static Music _i;
    AudioStreamPlayer _player;
    float _duck;

    /// Idempotent: call from any scene's _Ready.
    public static void Ensure(Node any)
    {
        if (_i != null && GodotObject.IsInstanceValid(_i)) return;
        if (!ResourceLoader.Exists(Track)) { GD.PushWarning($"[Music] missing {Track}"); return; }
        _i = new Music { Name = "Music", ProcessMode = ProcessModeEnum.Always };
        any.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _i);
    }

    public static void Duck() { if (_i != null && GodotObject.IsInstanceValid(_i)) _i._duck = DuckDb; }

    public override void _Ready()
    {
        var stream = GD.Load<AudioStream>(Track);
        if (stream is AudioStreamWav w && w.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
        {
            w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            w.LoopBegin = 0;
            w.LoopEnd = (int)(w.GetLength() * w.MixRate);
        }
        _player = new AudioStreamPlayer { Stream = stream, VolumeDb = BaseDb, Bus = "Master" };
        AddChild(_player);
        _player.Play();
        GD.Print($"[Music] playing {Track} ({stream.GetLength():0.0}s loop) at {BaseDb} dB");
    }

    // stop before the tree tears down, else the WAV playback outlives the AudioServer and Godot reports a leaked resource at exit
    public override void _ExitTree()
    {
        if (_player == null) return;
        _player.Stop();
        _player.Stream = null;
        OS.DelayMsec(60);   // this node only leaves the tree at shutdown: give the mixer thread one buffer to drop the playback (else it races the AudioServer teardown and leaks)
    }

    public override void _Process(double delta)
    {
        if (_player == null) return;
        _duck = Mathf.Max(0f, _duck - DuckRecoverPerSec * (float)delta);
        _player.VolumeDb = BaseDb - _duck;
    }
}
