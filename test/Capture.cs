using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Headless proof capture: loads Main.tscn with the Autopilot enabled and saves PNG stills at events and on a timer.
/// Run: xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --fixed-fps 30 --script test/Capture.cs ++ --scenario=raid
public partial class Capture : SceneTree
{
    string _scenario = "raid";
    string _dir = "screenshots/result";
    double _t, _nextPeriodic = 1.0, _period = 4.0, _maxT = 150;
    int _idx;
    Game _bound;
    readonly List<(double at, string label)> _queued = new();
    readonly Dictionary<string, double> _lastEvent = new();
    int _quitCountdown = -1;
    Menu _menu; int _menuStep; double _returnAt = -1;
    int _pauseStep;
    static readonly (double at, Key key, string label)[] PauseKeys = { (4.0, Key.Escape, "pause-open"), (6.0, Key.Escape, "pause-resume"), (8.0, Key.Escape, "pause-open2"), (9.5, Key.Enter, "pause-confirm") };
    static readonly double[] MenuSteps = { 1.2, 2.6, 4.0, 5.4, 6.8 };   // avatar changes, then start

    public override void _Initialize()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--scenario=")) _scenario = a.Substring(11);
            else if (a.StartsWith("--period=")) _period = double.Parse(a.Substring(9));
            else if (a.StartsWith("--max=")) _maxT = double.Parse(a.Substring(6));
            else if (a.StartsWith("--dir=")) _dir = a.Substring(6);
            else if (a.StartsWith("--weather=")) Weather.Initial = a.Substring(10);
        }
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://" + _dir));
        if (_scenario == "menu")
        {
            // entry-screen tour: a throwaway profile with a few records, browse avatars, then start the scripted raid
            OS.SetEnvironment("DUCKOV_PROFILE", ProjectSettings.GlobalizePath("res://screenshots/video/menu_profile.json"));
            Profile.Load();
            if (Profile.Raids == 0)
            {
                Profile.Name = "Alpha";
                Profile.AddRecord(new RaidRecord { Date = "2026-09-07 21:14", Extracted = false, Loot = 3, Kills = 2, Time = 48, Weather = "Storm", Weapon = "Saiga-12K" });
                Profile.AddRecord(new RaidRecord { Date = "2026-09-07 22:02", Extracted = true, Loot = 11, Kills = 4, Time = 71, Weather = "Rain", Weapon = "SR-3M" });
                Profile.AddRecord(new RaidRecord { Date = "2026-09-08 01:30", Extracted = true, Loot = 15, Kills = 5, Time = 33, Weather = "Clear", Weapon = "TS-128" });
            }
            Game.AutopilotScenario = "raid";
            Game.AvatarOverride = null;
            Game.RecordAutopilot = true;
            var menu = GD.Load<PackedScene>("res://scenes/Menu.tscn").Instantiate();
            Root.AddChild(menu);
            CurrentScene = menu;
            _menu = menu as Menu;
        }
        else
        {
            Game.AutopilotScenario = _scenario == "pause" ? "raid" : _scenario;   // pause test: the scripted raid runs underneath
            Game.AvatarOverride = 0;
            var packed = GD.Load<PackedScene>("res://scenes/Main.tscn");
            Root.AddChild(packed.Instantiate());
        }
        GD.Print($"[Capture] scenario={_scenario} period={_period}s max={_maxT}s");
    }

    public override bool _Process(double delta)
    {
        _t += delta;
        if (_menu != null && IsInstanceValid(_menu) && _menuStep < MenuSteps.Length && _t >= MenuSteps[_menuStep])
        {
            if (_menuStep < MenuSteps.Length - 1) { _menu.ChangeAvatar(1); Snap("menu-avatar"); }
            else { Snap("menu-start"); _menu.StartSingle(); _menu = null; }
            _menuStep++;
        }
        if (_scenario == "pause" && _pauseStep < PauseKeys.Length && _t >= PauseKeys[_pauseStep].at)
        {
            var (at, key, label) = PauseKeys[_pauseStep++];
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            _queued.Add((_t + 0.3, label));
            if (_pauseStep == PauseKeys.Length) _queued.Add((_t + 2.0, "end"));
        }
        if (_returnAt > 0 && _t >= _returnAt) { _returnAt = -1; if (Game.I != null && IsInstanceValid(Game.I)) Game.I.ReturnToMenu(); }
        if (Game.I != null && Game.I != _bound && IsInstanceValid(Game.I))
        {
            _bound = Game.I;
            _bound.OnEvent += OnEvent;
        }
        if (_t >= _nextPeriodic) { _nextPeriodic += _period; Snap("tick"); }
        for (int i = _queued.Count - 1; i >= 0; i--)
            if (_t >= _queued[i].at) { Snap(_queued[i].label); _queued.RemoveAt(i); }

        if (_quitCountdown > 0) _quitCountdown--;
        if (_quitCountdown == 0) return true;
        if (_quitCountdown < 0 && (Autopilot.Finished || _t > _maxT))
        {
            _queued.Add((_t + 0.5, "end"));
            _quitCountdown = 40;
        }
        return false;
    }

    void OnEvent(string ev)
    {
        // things worth a still, with a short delay so the effect is visible
        double delay = ev switch { "kill" => 0.35, "pickup" => 0.25, "extract-start" => 1.6, "extracted" => 0.4, "kia" => 0.6, "alert" => 0.3, "restart" => 0.6, "player-hit" => 0.15,
                                   "net-online" => 0.3, "weather" => 1.2, "lightning" => 0.02, "peer-join" => 0.5, "remote-shot" => 0.05, "remote-pickup" => 0.3, "peer-extracted" => 0.4, "peer-killed" => 0.4, _ => -1 };
        if (delay < 0) return;
        if (_lastEvent.TryGetValue(ev, out var last) && _t - last < (ev == "pickup" ? 1.0 : ev == "remote-shot" ? 4.0 : 2.5)) return;
        _lastEvent[ev] = _t;
        _queued.Add((_t + delay, ev));
        // the restart is the last thing to prove: grab one frame of the fresh raid, then quit
        if (ev == "restart" && _quitCountdown < 0) { _queued.Add((_t + 1.5, "end")); _quitCountdown = 70; }
        // menu tour: after the result, go back to the entry screen so the new history row shows, then quit
        if (_scenario == "menu" && (ev == "extracted" || ev == "kia") && _quitCountdown < 0)
        {
            _returnAt = _t + 1.6;
            _queued.Add((_t + 3.2, "menu-return")); _queued.Add((_t + 4.2, "end"));
            _quitCountdown = 150;
        }
    }

    void Snap(string label)
    {
        var img = Root.GetTexture().GetImage();
        if (img == null) return;
        var name = $"{_dir}/{_scenario}_{_idx:D3}_{_t:000.0}s_{label}.png";
        img.SavePng(ProjectSettings.GlobalizePath("res://" + name));
        _idx++;
        GD.Print($"[Capture] {name}  ({Autopilot.Status})");
    }
}
