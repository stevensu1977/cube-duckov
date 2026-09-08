using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Duckov;

/// One finished raid, as kept in the player's history.
public class RaidRecord
{
    public string Date { get; set; } = "";
    public bool Extracted { get; set; }
    public int Loot { get; set; }
    public int Kills { get; set; }
    public double Time { get; set; }
    public string Weather { get; set; } = "Clear";
    public string Weapon { get; set; } = "";
    public int Avatar { get; set; }
}

/// Player profile persisted at user://profile.json: name, chosen avatar and the raid history (newest first, capped).
/// Loaded once on first use; saved after every change. Autopilot / capture runs never write to it.
public static class Profile
{
    public const int MaxHistory = 50;
    public static string Name { get; set; } = "Duck";
    public static int Avatar { get; set; }
    public static List<RaidRecord> History { get; private set; } = new();
    public static bool Loaded { get; private set; }

    public static int Raids => History.Count;
    public static int Extractions { get { int n = 0; foreach (var r in History) if (r.Extracted) n++; return n; } }
    public static int TotalKills { get { int n = 0; foreach (var r in History) n += r.Kills; return n; } }
    public static int TotalLoot { get { int n = 0; foreach (var r in History) if (r.Extracted) n += r.Loot; return n; } }
    public static int BestLoot { get { int b = 0; foreach (var r in History) if (r.Extracted && r.Loot > b) b = r.Loot; return b; } }

    class Dto { public string Name { get; set; } public int Avatar { get; set; } public List<RaidRecord> History { get; set; } }
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    public static string Path => OS.GetEnvironment("DUCKOV_PROFILE") is { Length: > 0 } p ? p : "user://profile.json";

    public static void Load()
    {
        if (Loaded) return;
        Loaded = true;
        try
        {
            if (!FileAccess.FileExists(Path)) return;
            using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            var dto = JsonSerializer.Deserialize<Dto>(f.GetAsText(), Opts);
            if (dto == null) return;
            if (!string.IsNullOrWhiteSpace(dto.Name)) Name = dto.Name.Trim();
            Avatar = dto.Avatar;
            History = dto.History ?? new();
            GD.Print($"[Profile] loaded '{Name}' avatar={Avatar} raids={History.Count} from {Path}");
        }
        catch (System.Exception e) { GD.PrintErr($"[Profile] load failed: {e.Message}"); }
    }

    public static void Save()
    {
        try
        {
            var dir = ProjectSettings.GlobalizePath(Path).GetBaseDir();
            DirAccess.MakeDirRecursiveAbsolute(dir);
            using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            f.StoreString(JsonSerializer.Serialize(new Dto { Name = Name, Avatar = Avatar, History = History }, Opts));
        }
        catch (System.Exception e) { GD.PrintErr($"[Profile] save failed: {e.Message}"); }
    }

    public static void AddRecord(RaidRecord r)
    {
        Load();
        History.Insert(0, r);
        if (History.Count > MaxHistory) History.RemoveRange(MaxHistory, History.Count - MaxHistory);
        Save();
        GD.Print($"[Profile] recorded {(r.Extracted ? "EXTRACTED" : "KIA")} loot={r.Loot} kills={r.Kills} time={r.Time:0}s");
    }

    public static string FormatTime(double t) => $"{(int)(t / 60):00}:{(int)(t % 60):00}";
}
