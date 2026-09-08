using Godot;
namespace Duckov;

/// Headless self-test for Profile: writes two records to DUCKOV_PROFILE (or user://profile.json), reloads, prints the stats.
/// Run: godot --headless --path . --script test/ProfileCheck.cs
public partial class ProfileCheck : SceneTree
{
    public override void _Initialize()
    {
        Profile.Load();
        int before = Profile.Raids;
        Profile.Name = "TestDuck"; Profile.Avatar = 3;
        Profile.AddRecord(new RaidRecord { Date = "2026-09-08 00:00", Extracted = true, Loot = 12, Kills = 4, Time = 31.5, Weather = "Rain", Weapon = "TS-128", Avatar = 3 });
        Profile.AddRecord(new RaidRecord { Date = "2026-09-08 00:01", Extracted = false, Loot = 2, Kills = 1, Time = 12.0, Weather = "Clear", Weapon = "QBZ-191", Avatar = 3 });
        // reload from disk through a fresh static state is not possible in-process; re-read the file and compare
        using var f = FileAccess.Open(Profile.Path, FileAccess.ModeFlags.Read);
        var text = f.GetAsText();
        bool ok = text.Contains("TestDuck") && text.Contains("\"Loot\": 12") && Profile.Raids == before + 2 && Profile.Extractions >= 1 && Profile.BestLoot >= 12;
        GD.Print($"[ProfileCheck] raids={Profile.Raids} extractions={Profile.Extractions} kills={Profile.TotalKills} best={Profile.BestLoot} path={ProjectSettings.GlobalizePath(Profile.Path)} ok={ok}");
        Quit(ok ? 0 : 1);
    }
}
