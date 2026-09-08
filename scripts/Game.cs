using Godot;
using System;
using System.Collections.Generic;
namespace Duckov;

/// Raid state: timer, loot counter, extraction countdown, KIA/EXTRACTED result, restart. Spawns loot and enemies from Arena data.
public partial class Game : Node3D
{
    public static Game I { get; private set; }
    public static RandomNumberGenerator Rng = new() { Seed = 1337 };
    /// Set before the scene loads (by test/Capture.cs) to drive the player with a scripted Autopilot.
    public static string AutopilotScenario;
    /// Forces the player's avatar index (capture scripts use 0 so the proof stills keep the classic duck); null = profile.
    public static int? AvatarOverride;
    /// Capture's menu tour writes to a throwaway profile, so it opts in to recording autopilot raids.
    public static bool RecordAutopilot;

    public enum State { Playing, Extracted, Kia }
    public const float ExtractSeconds = 3f;

    public State Current { get; private set; } = State.Playing;
    public int Loot { get; private set; }
    public int Kills { get; private set; }
    public double RaidTime { get; private set; }
    public float ExtractProgress { get; private set; }

    public Player Player { get; private set; }
    public Arena Arena { get; private set; }
    public Hud Hud { get; private set; }
    public CameraRig Cam { get; private set; }
    public Weather Weather { get; private set; }
    public readonly List<Enemy> Enemies = new();
    public readonly List<Loot> LootItems = new();

    public event Action<string> OnEvent;
    float _resultT;
    bool _restarting;

    public override void _Ready()
    {
        I = this;
        Profile.Load();
        Music.Ensure(this);
        Arena = GetNode<Arena>("Arena");
        Cam = GetNode<CameraRig>("CameraRig");
        Hud = GetNode<Hud>("HUD");
        Player = GetNode<Player>("Player");

        Arena.Build();
        Weather = new Weather { Name = "Weather" };
        AddChild(Weather);
        Weather.Setup(GetNode<WorldEnvironment>("WorldEnvironment").Environment, GetNode<DirectionalLight3D>("Sun"), Cam);
        Weather.Lamps.AddRange(Arena.Lamps);
        Player.GlobalPosition = Arena.PlayerSpawn;
        Player.AimPoint = Arena.PlayerSpawn + new Vector3(3, 0.8f, -3);
        Cam.Target = Player;
        Cam.SnapTo(Arena.PlayerSpawn);

        foreach (var (pos, kind, risky) in Arena.LootSpots) SpawnLoot(pos, kind, risky);
        foreach (var (pos, w) in Arena.WeaponSpots) SpawnWeaponLoot(pos, w);
        for (int i = 0; i < Arena.EnemyRoutes.Count; i++)
        {
            var route = Arena.EnemyRoutes[i];
            // squad loadout by route: the compound guard carries a shotgun, the warehouse guard the ice SMG, the extraction guard the electric sniper
            var gun = i switch { 0 => WeaponId.Shotgun, 3 => WeaponId.IceSmg, 5 => WeaponId.ElecSniper, _ => WeaponId.Rifle };
            var e = new Enemy { Waypoints = route, Seed = i, WeaponKind = gun };
            AddChild(e);
            e.GlobalPosition = route[0];
            Enemies.Add(e);
        }

        if (AutopilotScenario != null)
        {
            Player.External = true;
            AddChild(new Autopilot { Scenario = AutopilotScenario });
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
        }
        AddChild(new PauseMenu { Name = "PauseMenu" });
        GD.Print($"[Game] raid started: {Enemies.Count} enemies, {LootItems.Count} loot items");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (AutopilotScenario == null && Input.IsActionJustPressed("weather_next")) Weather.Next();
        if (Current == State.Playing)
        {
            RaidTime += delta;
            var flat = Player.GlobalPosition - Arena.ExtractPos; flat.Y = 0;
            bool inZone = flat.Length() < Arena.ExtractRadius && !Player.Dead;
            if (inZone)
            {
                if (ExtractProgress == 0) Notify("extract-start");
                ExtractProgress += dt;
                if (ExtractProgress >= ExtractSeconds) Extract();
            }
            else ExtractProgress = Mathf.Max(0, ExtractProgress - dt * 2f);
        }
        else
        {
            _resultT += dt;
            if (_resultT > 0.4f && Input.IsActionJustPressed("restart")) Restart();
            if (_resultT > 0.4f && AutopilotScenario == null && Input.IsActionJustPressed("ui_cancel")) ReturnToMenu();
        }
    }

    public void Notify(string ev) => OnEvent?.Invoke(ev);

    public Loot SpawnLoot(Vector3 pos, Loot.Kind kind, bool risky)
    {
        var l = new Loot { Type = kind, Risky = risky };
        AddChild(l);
        l.GlobalPosition = pos;
        LootItems.Add(l);
        return l;
    }

    public Loot SpawnWeaponLoot(Vector3 pos, WeaponId w)
    {
        var l = new Loot { Type = Duckov.Loot.Kind.Weapon, Weapon = w, Risky = true };
        AddChild(l);
        l.GlobalPosition = pos;
        LootItems.Add(l);
        return l;
    }

    public Loot FindNearestLoot(Vector3 pos, float range)
    {
        Loot best = null; float bestD = range;
        foreach (var l in LootItems)
        {
            if (!IsInstanceValid(l)) continue;
            var d = l.GlobalPosition - pos; d.Y = 0;
            float dist = d.Length();
            if (dist < bestD) { bestD = dist; best = l; }
        }
        return best;
    }

    public void OnLootCollected(Loot l)
    {
        Loot += l.Value;
        LootItems.Remove(l);
        Notify("pickup");
    }

    public void OnEnemyKilled(Enemy e) { Kills++; Notify("kill"); }

    public void OnPlayerShot(Vector3 where)
    {
        // gunfire is loud: nearby enemies come looking
        foreach (var e in Enemies)
            if (IsInstanceValid(e) && !e.Dead && e.GlobalPosition.DistanceTo(where) < 13f) e.Alert(where);
        Notify("shot");
    }

    public void OnPlayerDied()
    {
        if (Current != State.Playing) return;
        Current = State.Kia;
        Hud.ShowResult(false, Loot, Kills, RaidTime);
        Record(false);
        Notify("kia");
    }

    void Extract()
    {
        if (Current != State.Playing) return;
        Current = State.Extracted;
        Hud.ShowResult(true, Loot, Kills, RaidTime);
        Record(true);
        Notify("extracted");
    }

    /// Append the finished raid to the player's history (never for autopilot / capture runs).
    void Record(bool extracted)
    {
        if (AutopilotScenario != null && !RecordAutopilot) return;
        Profile.AddRecord(new RaidRecord
        {
            Date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Extracted = extracted, Loot = Loot, Kills = Kills, Time = RaidTime,
            Weather = Weather?.Kind.ToString() ?? "Clear", Weapon = Player?.WeaponDef.Short ?? "", Avatar = Profile.Avatar,
        });
    }

    /// Back to the entry screen (result screen, Esc). Swaps the scene the same way Restart does.
    public void ReturnToMenu()
    {
        if (_restarting) return;
        _restarting = true;
        GetTree().Paused = false;
        Notify("menu");
        var menu = GD.Load<PackedScene>("res://scenes/Menu.tscn").Instantiate();
        var parent = GetParent();
        parent.CallDeferred(Node.MethodName.AddChild, menu);
        QueueFree();
    }

    public void Restart()
    {
        if (_restarting) return;
        _restarting = true;
        Notify("restart");
        var packed = GD.Load<PackedScene>(SceneFilePath);
        var parent = GetParent();
        var fresh = packed.Instantiate();
        parent.CallDeferred(Node.MethodName.AddChild, fresh);
        QueueFree();
    }
}
