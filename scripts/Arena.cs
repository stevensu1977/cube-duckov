using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Builds the 60x60 m arena procedurally (walls, buildings, props, extraction pad, loot spots, patrol routes)
/// and bakes a navigation mesh over it for enemy pathing. Props are Blender-made GLBs (tools/make_props.py) with
/// primitive collision shapes derived from their AABB.
public partial class Arena : NavigationRegion3D
{
    public Vector3 PlayerSpawn = new(-25, 0, 25);
    public Vector3 ExtractPos = new(24, 0, -24);
    public float ExtractRadius = 3f;
    public readonly List<(Vector3 pos, Loot.Kind kind, bool risky)> LootSpots = new();
    /// Guns lying on the map.
    public readonly List<(Vector3 pos, WeaponId w)> WeaponSpots = new();
    /// Footprints for the HUD minimap (world XZ rectangles): walls, colliding props, and the paved building zones.
    public readonly List<Rect2> WallRects = new(), PropRects = new();
    public readonly Rect2 RoadRect = new(-30f, -30f, 60f, 4f);   // asphalt strip along the north wall
    public readonly Rect2[] ZoneRects = { new(-8.3f, -8.3f, 16.6f, 16.6f), new(-26.3f, -20.3f, 12.6f, 10.6f), new(11.7f, 3.7f, 14.6f, 12.6f) };
    public readonly List<Vector3[]> EnemyRoutes = new();

    Node3D _extractRing, _extractBeam, _beaconLamp;
    StandardMaterial3D _beamMat, _lampMat;
    OmniLight3D _beaconLight;
    float _t;
    readonly RandomNumberGenerator _deco = new() { Seed = 4242 }; // decoration only: never touches gameplay RNG

    // wall styles
    ShaderMaterial _concrete, _brick, _steel, _rust, _concreteLow;
    /// Every world ShaderMaterial (walls, roofs, ground) — Weather drives their `wet` / `snow` uniforms.
    public readonly List<ShaderMaterial> WorldMats = new();
    /// Yard lamps (OmniLights) that Weather switches on in rain, storm, fog and snow.
    public readonly List<OmniLight3D> Lamps = new();
    StandardMaterial3D _darkMetal, _lampMatOn, _paint, _plinth;
    StandardMaterial3D _frameMat, _glass, _trim;
    ShaderMaterial _tileRoof, _metalRoof, _blueRoof;

    static readonly Dictionary<string, PackedScene> _props = new();
    static PackedScene PropScene(string name)
    {
        if (!_props.TryGetValue(name, out var ps)) { ps = GD.Load<PackedScene>(name.Contains('/') ? $"res://assets/models/{name}.glb" : $"res://assets/models/props/{name}.glb"); _props[name] = ps; }
        return ps;
    }

    public void Build()
    {
        var wallShader = new Shader { Code = WallShaderCode };
        ShaderMaterial WallMat(int style, Color a, Color b, Color mortar, Color cap)
        {
            var m = new ShaderMaterial { Shader = wallShader };
            WorldMats.Add(m);
            m.SetShaderParameter("style", style);
            m.SetShaderParameter("base_col", a); m.SetShaderParameter("alt_col", b);
            m.SetShaderParameter("mortar_col", mortar); m.SetShaderParameter("cap_col", cap);
            return m;
        }
        _concrete = WallMat(0, new Color(0.70f, 0.68f, 0.63f), new Color(0.6f, 0.58f, 0.54f), new Color(0.5f, 0.48f, 0.45f), new Color(0.45f, 0.44f, 0.42f));
        _concreteLow = WallMat(0, new Color(0.66f, 0.64f, 0.6f), new Color(0.56f, 0.54f, 0.5f), new Color(0.46f, 0.45f, 0.42f), new Color(0.4f, 0.39f, 0.37f));
        _brick = WallMat(1, new Color(0.66f, 0.3f, 0.22f), new Color(0.55f, 0.25f, 0.2f), new Color(0.72f, 0.66f, 0.58f), new Color(0.38f, 0.2f, 0.16f));
        _steel = WallMat(2, new Color(0.36f, 0.48f, 0.6f), new Color(0.28f, 0.38f, 0.5f), new Color(0.2f, 0.26f, 0.34f), new Color(0.22f, 0.28f, 0.36f));
        _rust = WallMat(3, new Color(0.68f, 0.4f, 0.2f), new Color(0.45f, 0.26f, 0.14f), new Color(0.3f, 0.18f, 0.1f), new Color(0.32f, 0.2f, 0.12f));
        _frameMat = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.22f, 0.15f), Roughness = 0.9f });
        var roofShader = new Shader { Code = RoofShaderCode };
        ShaderMaterial RoofMat(int style, Color a, Color b, Color edge)
        {
            var m = new ShaderMaterial { Shader = roofShader };
            WorldMats.Add(m);
            m.SetShaderParameter("style", style);
            m.SetShaderParameter("col_a", a); m.SetShaderParameter("col_b", b); m.SetShaderParameter("edge_col", edge);
            return m;
        }
        _tileRoof = RoofMat(0, new Color(0.62f, 0.3f, 0.2f), new Color(0.5f, 0.24f, 0.17f), new Color(0.32f, 0.15f, 0.1f));
        _metalRoof = RoofMat(1, new Color(0.42f, 0.43f, 0.45f), new Color(0.3f, 0.31f, 0.34f), new Color(0.2f, 0.2f, 0.22f));
        _blueRoof = RoofMat(1, new Color(0.28f, 0.36f, 0.46f), new Color(0.2f, 0.26f, 0.35f), new Color(0.14f, 0.18f, 0.24f));
        _glass = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.62f, 0.72f), Roughness = 0.15f, Metallic = 0.4f };
        _trim = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.68f, 0.63f), Roughness = 0.85f });

        BuildFloor();

        // perimeter
        Wall(0, -30.5f, 62, 1, 3.2f, _concrete);
        Wall(0, 30.5f, 62, 1, 3.2f, _concrete);
        Wall(-30.5f, 0, 1, 62, 3.2f, _concrete);
        Wall(30.5f, 0, 1, 62, 3.2f, _concrete);
        // perimeter pillars every ~10 m
        for (int i = -3; i <= 3; i++)
        {
            Pillar(i * 10f, -30.5f); Pillar(i * 10f, 30.5f); Pillar(-30.5f, i * 10f); Pillar(30.5f, i * 10f);
        }

        // central compound (+-8) with a door on each side
        float h = 2.6f;
        Wall(-4.75f, -8, 6.5f, 0.6f, h, _brick); Wall(4.75f, -8, 6.5f, 0.6f, h, _brick);
        Wall(-4.75f, 8, 6.5f, 0.6f, h, _brick); Wall(4.75f, 8, 6.5f, 0.6f, h, _brick);
        Wall(-8, -4.75f, 0.6f, 6.5f, h, _brick); Wall(-8, 4.75f, 0.6f, 6.5f, h, _brick);
        Wall(8, -4.75f, 0.6f, 6.5f, h, _brick); Wall(8, 4.75f, 0.6f, 6.5f, h, _brick);
        DoorFrame(0, -8, 3f, 0.6f, h, false); DoorFrame(0, 8, 3f, 0.6f, h, false);
        DoorFrame(-8, 0, 3f, 0.6f, h, true); DoorFrame(8, 0, 3f, 0.6f, h, true);
        Eaves(-4.75f, -8, 6.5f, 0.6f, h, _tileRoof); Eaves(4.75f, -8, 6.5f, 0.6f, h, _tileRoof);
        Eaves(-4.75f, 8, 6.5f, 0.6f, h, _tileRoof); Eaves(4.75f, 8, 6.5f, 0.6f, h, _tileRoof);
        Eaves(-8, -4.75f, 0.6f, 6.5f, h, _tileRoof); Eaves(-8, 4.75f, 0.6f, 6.5f, h, _tileRoof);
        Eaves(8, -4.75f, 0.6f, 6.5f, h, _tileRoof); Eaves(8, 4.75f, 0.6f, 6.5f, h, _tileRoof);
        Crate(-5.5f, -5.5f); Crate(5.5f, 5.5f); Crate(5.5f, 4.2f, true);
        Barrel(-6.2f, 5.8f, 0); Barrel(-5.3f, 6.3f, 1);
        Pallet(-6.3f, -2.5f, 20); Sandbags(-4.2f, 6.6f, 0, false);
        Pedestal(0, 0);

        // west building: x -26..-14, z -20..-10, door on the east side
        Wall(-20, -20, 12.6f, 0.6f, 3f, _steel);
        Wall(-20, -10, 12.6f, 0.6f, 3f, _steel);
        Wall(-26, -15, 0.6f, 10f, 3f, _steel);
        Wall(-14, -18.25f, 0.6f, 3.5f, 3f, _steel);
        Wall(-14, -11.75f, 0.6f, 3.5f, 3f, _steel);
        DoorFrame(-14, -15, 3f, 0.6f, 3f, true);
        Eaves(-20, -20, 12.6f, 0.6f, 3f, _blueRoof); Eaves(-20, -10, 12.6f, 0.6f, 3f, _blueRoof);
        Eaves(-26, -15, 0.6f, 10f, 3f, _blueRoof); Eaves(-14, -18.25f, 0.6f, 3.5f, 3f, _blueRoof); Eaves(-14, -11.75f, 0.6f, 3.5f, 3f, _blueRoof);
        Window(-23, -20, false, -1); Window(-17, -20, false, -1); Window(-23, -10, false, 1); Window(-17, -10, false, 1); Window(-26, -15, true, -1);
        Crate(-24, -12); Crate(-24, -13.3f); Crate(-24, -12.6f, true);
        Pallet(-16.5f, -19.2f, 0); Barrel(-25.2f, -19.2f, 2);

        // east warehouse: x 12..26, z 4..16, doors north and west
        Wall(14.75f, 4, 5.5f, 0.6f, 3f, _rust); Wall(23.25f, 4, 5.5f, 0.6f, 3f, _rust);
        Wall(19, 16, 14.6f, 0.6f, 3f, _rust);
        Wall(12, 6.25f, 0.6f, 4.5f, 3f, _rust); Wall(12, 13.75f, 0.6f, 4.5f, 3f, _rust);
        Wall(26, 10, 0.6f, 12.6f, 3f, _rust);
        DoorFrame(19, 4, 3f, 0.6f, 3f, false); DoorFrame(12, 10, 3f, 0.6f, 3f, true);
        Eaves(14.75f, 4, 5.5f, 0.6f, 3f, _metalRoof); Eaves(23.25f, 4, 5.5f, 0.6f, 3f, _metalRoof); Eaves(19, 16, 14.6f, 0.6f, 3f, _metalRoof);
        Eaves(12, 6.25f, 0.6f, 4.5f, 3f, _metalRoof); Eaves(12, 13.75f, 0.6f, 4.5f, 3f, _metalRoof); Eaves(26, 10, 0.6f, 12.6f, 3f, _metalRoof);
        Window(15, 16, false, 1); Window(19, 16, false, 1); Window(23, 16, false, 1); Window(26, 7, true, 1); Window(26, 13, true, 1);
        Crate(19, 9); Crate(20.3f, 9); Crate(19.6f, 10.3f); Crate(19.6f, 9.6f, true);
        Barrel(24.5f, 5.5f, 1); Barrel(13.5f, 14.5f, 0);
        Pallet(24.5f, 14.8f, 0); Pallet(22.8f, 14.9f, 90);

        // scattered cover
        Wall(-18, 4, 6, 0.6f, 2.2f, _concreteLow); Wall(-15.3f, 6.7f, 0.6f, 6, 2.2f, _concreteLow);
        Wall(5, -20, 8, 0.6f, 2.2f, _concreteLow);
        Wall(0, 18, 0.6f, 6, 2.2f, _concreteLow);
        Wall(-8, -22, 0.6f, 5, 2.2f, _concreteLow);
        Wall(18, -24, 0.6f, 8, 2.6f, _concreteLow); // funnels the approach to extraction
        Crate(-10, 14); Crate(-8.7f, 14.3f); Crate(-9.3f, 14.1f, true);
        Crate(14, -14); Crate(15.3f, -14.1f); Crate(14.6f, -12.8f);
        Crate(18, 22); Crate(19.3f, 22.1f);
        Crate(-6, -14); Crate(-4.8f, -14.2f);
        Crate(26, -6); Crate(26, -7.3f); Crate(26, -6.6f, true);
        Crate(-24, 10); Crate(8, 26); Crate(9.3f, 26.2f);
        Crate(24, -18); Crate(-18, 24);
        Barrel(-12, -2, 0); Barrel(-11.1f, -1.3f, 2); Barrel(10, 12, 1); Barrel(-2, -27, 0); Barrel(22, -10, 2); Barrel(-27, -4, 1);

        // sandbag nests and fences (kept clear of patrol loops and the scripted raid path)
        Sandbags(5, -21.3f, 0, true); Sandbags(-18, 5.3f, 0, true);
        Sandbags(20.5f, -26.5f, 90, true); Sandbags(-24.5f, 21, 0, true);
        Sandbags(-2.5f, 21.5f, 35, true);
        for (int i = 0; i < 5; i++) Fence(-27.5f + i * 2.4f, 28.4f, 0);
        for (int i = 0; i < 4; i++) Fence(28.4f, 5.5f + i * 2.4f, 90);
        for (int i = 0; i < 3; i++) Fence(-6 + i * 2.4f, -28.6f, 0);
        Fence(28.4f, -14, 90); Fence(28.4f, -11.6f, 90);
        // camp tents
        Tent(-4, 27.3f, 0); Tent(16, 28.2f, 8); Tent(-27.2f, -26.8f, 88);
        Pallet(-1.5f, 25.3f, 70); Pallet(12.5f, 25.5f, 0);
        Barrel(-6.4f, 26.6f, 1); Barrel(18.6f, 26.2f, 0);
        Rock(-13, 27.5f, 1.2f); Rock(27.5f, -20.5f, 0.9f); Rock(-28, 12, 1.0f); Rock(12, -8, 0.8f);
        Bush(-28.6f, 17.5f, 0.9f); Bush(28.5f, 20, 1.0f); Bush(-28.5f, -12, 0.8f); Bush(22, -29, 0.9f); Bush(-15, -28.8f, 0.8f); Bush(29, 26, 0.9f);

        BuildExtraction();
        BuildOutskirts();

        // loot: safe pickups near spawn, richer loot deeper in / near enemies
        L(-22, 14, Loot.Kind.Cash); L(-14, 24, Loot.Kind.Ammo); L(-24, 2, Loot.Kind.Medkit);
        L(-8, 22, Loot.Kind.Cash); L(2, 26, Loot.Kind.Ammo);
        L(-22, -17, Loot.Kind.Cash, true); L(-17, -12, Loot.Kind.Medkit, true);
        L(0, 0, Loot.Kind.Gold, true); L(-5, 4, Loot.Kind.Cash, true); L(5, -4, Loot.Kind.Ammo, true);
        L(24, 14.5f, Loot.Kind.Cash, true); L(14, 6, Loot.Kind.Medkit, true);
        L(-4, -26, Loot.Kind.Ammo, true); L(14, -27, Loot.Kind.Cash, true);
        // weapons: SMG by the camp, shotgun in the west building, firework launcher behind the north cover wall, sniper in the compound
        W(-23.5f, 19.5f, WeaponId.IceSmg); W(-19.5f, -14.5f, WeaponId.Shotgun); W(-9.3f, -22.5f, WeaponId.Firework); W(5, 4, WeaponId.ElecSniper);

        // enemy patrol routes (7 enemies)
        R((-5, -5), (5, -5), (5, 5), (-5, 5));
        R((-11, -11), (11, -11), (11, 11), (-11, 11));
        R((-20, -6), (-12, -6), (-12, 1), (-22, 2));
        R((15, 7), (23, 7), (23, 13), (15, 13));
        R((-20, -25), (0, -25), (8, -18));
        R((16, -20), (26, -16), (12, -12));
        R((10, 22), (24, 25), (20, 18));

        BuildYard();
        BuildDetails();
        BakeNav();
    }

    // ---------------------------------------------------------------- building details (Brief 7): after the Duckov warehouse / bunker maps
    // Watchtowers, a bunker, yard lamps and warehouse dressing. Colliders only on the towers, the bunker and the lamp posts,
    // all placed clear of patrol routes and the autopilot's waypoints.

    MeshInstance3D Box(Vector3 pos, Vector3 size, Material m, Node parent = null, bool shadow = true)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off };
        (parent ?? this).AddChild(mi);
        return mi;
    }

    StaticBody3D Solid(float x, float z, Vector3 size, float rotDeg = 0)
    {
        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = new Vector3(x, 0, z), RotationDegrees = new Vector3(0, rotDeg, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2f, 0) });
        AddChild(body);
        PropRects.Add(new Rect2(x - Mathf.Max(size.X, size.Z) / 2f, z - Mathf.Max(size.X, size.Z) / 2f, Mathf.Max(size.X, size.Z), Mathf.Max(size.X, size.Z)));
        return body;
    }

    void BuildDetails()
    {
        _darkMetal = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.23f, 0.26f), Roughness = 0.6f, Metallic = 0.3f });
        _paint = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.75f, 0.2f), Roughness = 0.7f });
        _lampMatOn = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.92f, 0.7f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.55f), EmissionEnergyMultiplier = 1.4f };

        Watchtower(-28.6f, 28.6f); Watchtower(28.6f, -28.6f);
        Bunker(-19.5f, 9.5f);
        foreach (var (lx, lz) in new[] { (-8.5f, -29.3f), (19.5f, -22f), (-23.5f, -7.5f), (3f, 16f), (24f, 19f), (-27f, 0f) }) Lamp(lx, lz);

        // east warehouse: roll-up doors, awnings over the doorways, rooftop units, vents, antenna, pipe run, sign
        RollupDoor(23.2f, 4.36f, false); RollupDoor(15.5f, 16.36f, false); RollupDoor(26.36f, 12.5f, true);
        Awning(19, 4, false, 1); Awning(12, 10, true, -1);
        AcUnit(14.5f, 16, 3.8f); AcUnit(22.8f, 4, 3.8f); AcUnit(26, 8.5f, 3.8f);
        Vent(17.5f, 16, 3.8f); Vent(21, 16, 3.8f); Vent(26, 14.2f, 3.8f);
        Antenna(25.7f, 15.7f, 3.8f);
        for (int i = 0; i < 5; i++) Box(new Vector3(26.5f, 0.55f, 5.6f + i * 2f), new Vector3(0.2f, 0.2f, 1.8f), _darkMetal);
        Box(new Vector3(26.5f, 1.2f, 5.6f), new Vector3(0.2f, 1.4f, 0.2f), _darkMetal);
        Sign(19, 16.4f, "WAREHOUSE 3", false, 1);

        // west building (office block): antenna, units, awning at the east door, exterior stair to the roof on the north side
        Antenna(-25.6f, -19.6f, 3.8f); AcUnit(-17.5f, -10, 3.8f); Vent(-22, -10, 3.8f);
        Awning(-14, -15, true, 1);
        Stair(-24.6f, -20.8f, 3.0f);
        Sign(-13.64f, -12.2f, "OFFICE", true, 1);

        // brick compound: corner pillars, a flag on the north-east one, loudspeaker horns
        foreach (var (cx, cz) in new[] { (-8f, -8f), (8f, -8f), (-8f, 8f), (8f, 8f) })
            Box(new Vector3(cx, 1.6f, cz), new Vector3(1.0f, 3.3f, 1.0f), _brick);
        Box(new Vector3(8f, 3.25f, -8f), new Vector3(1.1f, 0.12f, 1.1f), _trim);
        Flag(8f, -8f, 3.3f);
        Horn(-8f, 8f, 3.3f); Horn(8f, 8f, 3.3f);
    }

    void Watchtower(float x, float z)
    {
        var root = new Node3D { Position = new Vector3(x, 0, z) }; AddChild(root);
        var wood = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.16f), Roughness = 0.9f });
        foreach (var sx in new[] { -0.75f, 0.75f }) foreach (var sz in new[] { -0.75f, 0.75f })
            Box(new Vector3(sx, 2.1f, sz), new Vector3(0.22f, 4.2f, 0.22f), wood, root);
        Box(new Vector3(0, 2.0f, 0), new Vector3(1.6f, 0.12f, 0.12f), wood, root); Box(new Vector3(0, 2.0f, 0), new Vector3(0.12f, 0.12f, 1.6f), wood, root);   // braces
        Box(new Vector3(0, 4.25f, 0), new Vector3(2.3f, 0.22f, 2.3f), wood, root);                     // platform
        for (int i = 0; i < 4; i++)                                                                 // railing
        {
            bool alongX = i < 2; float side = i % 2 == 0 ? -1 : 1;
            Box(alongX ? new Vector3(0, 4.85f, side * 1.1f) : new Vector3(side * 1.1f, 4.85f, 0), alongX ? new Vector3(2.3f, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, 2.3f), wood, root);
            Box(alongX ? new Vector3(0, 4.55f, side * 1.1f) : new Vector3(side * 1.1f, 4.55f, 0), alongX ? new Vector3(2.3f, 0.5f, 0.06f) : new Vector3(0.06f, 0.5f, 2.3f), Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.66f, 0.5f), Roughness = 0.95f }), root);
        }
        foreach (var (px, pz) in new[] { (-1.05f, -1.05f), (1.05f, -1.05f), (-1.05f, 1.05f), (1.05f, 1.05f) })
            Box(new Vector3(px, 5.3f, pz), new Vector3(0.14f, 2.0f, 0.14f), wood, root);                  // roof posts
        Box(new Vector3(0, 6.35f, 0), new Vector3(2.8f, 0.22f, 2.8f), _metalRoof, root);               // roof slabs (stairs)
        Box(new Vector3(0, 6.6f, 0), new Vector3(1.8f, 0.24f, 1.8f), _metalRoof, root);
        Box(new Vector3(0, 6.85f, 0), new Vector3(0.8f, 0.24f, 0.8f), _metalRoof, root);
        for (int i = 0; i < 7; i++) Box(new Vector3(-1.0f, 0.5f + i * 0.55f, 0), new Vector3(0.5f, 0.06f, 0.08f), _darkMetal, root);   // ladder rungs
        var lamp = new OmniLight3D { Position = new Vector3(0, 6.0f, 0), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 0f, OmniRange = 12f, ShadowEnabled = false };
        root.AddChild(lamp); Lamps.Add(lamp);
        Box(new Vector3(0, 5.9f, 0), new Vector3(0.3f, 0.2f, 0.3f), _lampMatOn, root, false);
        Solid(x, z, new Vector3(1.9f, 4.3f, 1.9f));
    }

    void Bunker(float x, float z)
    {
        var root = new Node3D { Position = new Vector3(x, 0, z) }; AddChild(root);
        Box(new Vector3(0, 1.1f, 0), new Vector3(4.2f, 2.2f, 3.0f), _concreteLow, root);
        Box(new Vector3(0, 0.95f, 1.52f), new Vector3(1.3f, 1.9f, 0.08f), new StandardMaterial3D { AlbedoColor = new Color(0.06f, 0.06f, 0.07f), Roughness = 1f }, root, false);   // dark doorway
        Box(new Vector3(0, 2.02f, 1.52f), new Vector3(1.7f, 0.3f, 0.12f), _paint, root);              // yellow lintel stripe
        Box(new Vector3(-1.3f, 2.5f, -0.6f), new Vector3(0.36f, 0.7f, 0.36f), _darkMetal, root);       // vents
        Box(new Vector3(1.2f, 2.45f, -0.8f), new Vector3(0.36f, 0.6f, 0.36f), _darkMetal, root);
        Box(new Vector3(1.4f, 2.26f, 0.6f), new Vector3(0.9f, 0.12f, 0.9f), _darkMetal, root);         // hatch
        Box(new Vector3(0, 0.02f, 2.4f), new Vector3(2.0f, 0.04f, 1.6f), _trim, root, false);          // threshold slab
        Solid(x, z, new Vector3(4.2f, 2.2f, 3.0f));
        // blast wall in front of the entrance
        Wall(x, z + 4.3f, 3.2f, 0.6f, 1.8f, _concreteLow);
        Sandbags(x - 3.2f, z + 3.6f, 90, true);
    }

    void Lamp(float x, float z)
    {
        var root = new Node3D { Position = new Vector3(x, 0, z) }; AddChild(root);
        Box(new Vector3(0, 2.3f, 0), new Vector3(0.24f, 4.6f, 0.24f), _darkMetal, root);
        Box(new Vector3(0.55f, 4.55f, 0), new Vector3(1.3f, 0.12f, 0.12f), _darkMetal, root);
        Box(new Vector3(1.1f, 4.4f, 0), new Vector3(0.6f, 0.18f, 0.45f), _darkMetal, root);
        Box(new Vector3(1.1f, 4.28f, 0), new Vector3(0.5f, 0.06f, 0.36f), _lampMatOn, root, false);
        var light = new OmniLight3D { Position = new Vector3(1.1f, 4.0f, 0), LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 0f, OmniRange = 11f, ShadowEnabled = false };
        root.AddChild(light); Lamps.Add(light);
        Solid(x, z, new Vector3(0.3f, 4.6f, 0.3f));
    }

    /// Closed roll-up door slab on a wall face (vertical = the wall runs along z), with horizontal rib texels.
    void RollupDoor(float x, float z, bool vertical)
    {
        var door = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.52f, 0.55f), Roughness = 0.6f, Metallic = 0.3f });
        var size = vertical ? new Vector3(0.1f, 2.5f, 3.2f) : new Vector3(3.2f, 2.5f, 0.1f);
        Box(new Vector3(x, 1.25f, z), size, door);
        for (int i = 0; i < 5; i++)
            Box(new Vector3(x + (vertical ? 0.03f : 0), 0.35f + i * 0.5f, z + (vertical ? 0 : 0.03f)), vertical ? new Vector3(0.06f, 0.08f, 3.0f) : new Vector3(3.0f, 0.08f, 0.06f), _darkMetal);
        Box(new Vector3(x, 2.65f, z), vertical ? new Vector3(0.36f, 0.36f, 3.4f) : new Vector3(3.4f, 0.36f, 0.36f), _darkMetal);   // roller housing
    }

    /// Corrugated canopy over a doorway; side = which face of the wall it hangs from.
    void Awning(float x, float z, bool vertical, int side)
    {
        float off = 0.3f + 0.9f;
        var pos = vertical ? new Vector3(x + side * off, 3.05f, z) : new Vector3(x, 3.05f, z + side * off);
        Box(pos, vertical ? new Vector3(1.8f, 0.14f, 4.2f) : new Vector3(4.2f, 0.14f, 1.8f), _metalRoof);
        for (int s = -1; s <= 1; s += 2)
        {
            var p = vertical ? new Vector3(x + side * 2.0f, 1.5f, z + s * 1.9f) : new Vector3(x + s * 1.9f, 1.5f, z + side * 2.0f);
            Box(p, new Vector3(0.12f, 3.0f, 0.12f), _darkMetal);
        }
    }

    void AcUnit(float x, float z, float y)
    {
        Box(new Vector3(x, y + 0.35f, z), new Vector3(0.9f, 0.7f, 0.9f), Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.73f, 0.74f), Roughness = 0.7f }));
        Box(new Vector3(x, y + 0.72f, z), new Vector3(0.6f, 0.04f, 0.6f), _darkMetal);
    }

    void Vent(float x, float z, float y)
    {
        Box(new Vector3(x, y + 0.5f, z), new Vector3(0.3f, 1.0f, 0.3f), _darkMetal);
        Box(new Vector3(x, y + 1.05f, z), new Vector3(0.5f, 0.12f, 0.5f), _darkMetal);
    }

    void Antenna(float x, float z, float y)
    {
        Box(new Vector3(x, y + 1.6f, z), new Vector3(0.1f, 3.2f, 0.1f), _darkMetal);
        Box(new Vector3(x, y + 2.4f, z), new Vector3(0.7f, 0.06f, 0.06f), _darkMetal);
        Box(new Vector3(x, y + 3.25f, z), new Vector3(0.16f, 0.16f, 0.16f), new StandardMaterial3D { AlbedoColor = new Color(1f, 0.2f, 0.15f), EmissionEnabled = true, Emission = new Color(1f, 0.15f, 0.1f), EmissionEnergyMultiplier = 2f }, null, false);
    }

    /// Exterior stair up a wall face, running along +x from (x, z) to roof height h.
    void Stair(float x, float z, float h)
    {
        int steps = 8; float run = 0.45f, rise = h / steps;
        for (int i = 0; i < steps; i++)
            Box(new Vector3(x + i * run, rise * (i + 0.5f), z), new Vector3(run, rise, 0.9f), _darkMetal);
        Box(new Vector3(x + steps * run - 0.2f, h + 0.05f, z), new Vector3(1.2f, 0.1f, 0.9f), _darkMetal);
        Box(new Vector3(x + steps * run / 2f, h * 0.5f + 0.6f, z - 0.5f), new Vector3(steps * run, 0.06f, 0.06f), _darkMetal, null, false);
    }

    void Sign(float x, float z, string text, bool vertical, int side)
    {
        var plate = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.18f, 0.22f), Roughness = 0.8f });
        var pos = vertical ? new Vector3(x + side * 0.06f, 2.3f, z) : new Vector3(x, 2.3f, z + side * 0.06f);
        Box(pos, vertical ? new Vector3(0.08f, 0.7f, 2.6f) : new Vector3(2.6f, 0.7f, 0.08f), plate);
        var fontSize = System.Math.Min(60, 360 / System.Math.Max(1, text.Length));   // Latin glyphs ~0.55 em: fit the 2.6 m plate
        var label = new Label3D { Text = text, FontSize = fontSize, PixelSize = 0.012f, Position = pos + (vertical ? new Vector3(side * 0.06f, 0, 0) : new Vector3(0, 0, side * 0.06f)), Modulate = new Color(1f, 0.85f, 0.3f), OutlineSize = 6, OutlineModulate = new Color(0, 0, 0, 0.6f) };
        label.RotationDegrees = new Vector3(0, vertical ? (side > 0 ? 90 : -90) : (side > 0 ? 0 : 180), 0);
        AddChild(label);
    }

    void Flag(float x, float z, float y)
    {
        Box(new Vector3(x, y + 1.8f, z), new Vector3(0.1f, 3.6f, 0.1f), _darkMetal);
        var cloth = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.75f, 0.15f), Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        Box(new Vector3(x + 0.75f, y + 3.1f, z), new Vector3(1.4f, 0.8f, 0.06f), cloth);
        Box(new Vector3(x + 0.75f, y + 3.1f, z), new Vector3(0.5f, 0.3f, 0.08f), new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.22f), Roughness = 0.9f });   // duck silhouette block
    }

    void Horn(float x, float z, float y)
    {
        Box(new Vector3(x, y + 0.5f, z), new Vector3(0.1f, 1.0f, 0.1f), _darkMetal);
        Box(new Vector3(x, y + 1.0f, z), new Vector3(0.5f, 0.35f, 0.35f), _darkMetal);
        Box(new Vector3(x, y + 1.0f, z), new Vector3(0.35f, 0.5f, 0.5f), _darkMetal);
    }

    // ---------------------------------------------------------------- Kenney dressing (CC0): vehicles, containers, industry, camp
    // Placed clear of every patrol route and of the autopilot's waypoints (see the route list above) so the navmesh corridors
    // the enemies and the scripted raid use stay open. Kenney kits are miniature scale: each call carries its own multiplier.
    Node3D Kenney(string rel, float x, float z, float rotDeg, float scale, bool collide, float y = 0f)
    {
        var p = Prop("kenney/" + rel, x, z, rotDeg, scale, collide);
        if (y != 0f) p.Position = new Vector3(x, y, z);
        return p;
    }

    void BuildYard()
    {
        // north service road: containers and a truck on the asphalt, a tank at the east end
        Kenney("industrial/shipping-container-a", -1f, -28.3f, 90, 7f, true);
        Kenney("industrial/shipping-container-b", 5.2f, -28.3f, 90, 7f, true);
        Kenney("industrial/shipping-container-c", -1f, -28.3f, 90, 7f, false, 2.45f);   // stacked, decorative
        Kenney("cars/truck", -14f, -28.2f, 90, 1.6f, true);
        Kenney("cars/cone", -10.5f, -26.6f, 0, 1.6f, false); Kenney("cars/cone", -17.5f, -26.7f, 20, 1.6f, false);
        Kenney("industrial/detail-tank-large", 10.5f, -27.9f, 0, 2.2f, true);
        // west yard: a container against the wall, barrels by the west building, a police wreck on the west lane
        Kenney("industrial/shipping-container-a", -28.5f, -17f, 0, 7f, true);
        Kenney("survival/barrel-open", -27.2f, -9.2f, 0, 3f, true); Kenney("survival/barrel-open", -26.4f, -8.4f, 30, 3f, true);
        Kenney("cars/police", -27.6f, 14f, 0, 1.6f, true);
        Kenney("cars/suv", -16f, 20f, 8, 1.6f, true);
        // warehouse: ambulance at the south door, crate stacks, a chain-link run, a chimney, a container by the east wall
        Kenney("cars/ambulance", 19f, -1.4f, 90, 1.6f, true);
        Kenney("survival/box-large", 21f, 2.2f, 0, 3f, true); Kenney("survival/box-large", 22.2f, 2.2f, 90, 3f, true);
        Kenney("survival/box-large", 21.6f, 2.2f, 0, 3f, false, 1.5f);
        for (int i = 0; i < 3; i++) Kenney("survival/fence-fortified", 13.5f + i * 2f, 1.5f, 0, 4f, true);
        Kenney("industrial/chimney-medium", 25.3f, 15.3f, 0, 3f, true);
        Kenney("industrial/shipping-container-b", 28.3f, 22f, 0, 7f, true);
        // camp by the spawn: van, campfire, bedrolls, workbench, signpost; a sedan wreck by the south fence
        Kenney("cars/van", -12f, 27f, 90, 1.6f, true);
        Kenney("survival/campfire-pit", 0f, 26.6f, 0, 3.5f, false);
        Kenney("survival/bedroll", -2.2f, 25.6f, 20, 3f, false); Kenney("survival/bedroll", 2.2f, 25.9f, -15, 3f, false);
        Kenney("survival/workbench", 13.5f, 26.5f, 0, 3.5f, false);
        Kenney("survival/signpost", -22.5f, 22.5f, -40, 3.5f, false);
        Kenney("cars/sedan", 4.5f, 23.5f, 0, 1.6f, true);
        // skyline outside the walls (no colliders): water tower, factory buildings, chimneys
        Kenney("industrial/water-tower", 37f, -33f, 0, 4f, false);
        Kenney("industrial/building-a", -12f, -41f, 0, 6f, false);
        Kenney("industrial/building-a", 41f, 8f, 90, 6f, false);
        Kenney("industrial/chimney-medium", -3f, -40f, 0, 3.5f, false);
        Kenney("industrial/detail-tank-large", 36f, 26f, 0, 2.5f, false); Kenney("industrial/detail-tank-large", 40f, 24f, 0, 2.2f, false);
    }

    void L(float x, float z, Loot.Kind k, bool risky = false) => LootSpots.Add((new Vector3(x, 0, z), k, risky));
    void W(float x, float z, WeaponId w) => WeaponSpots.Add((new Vector3(x, 0, z), w));
    void R(params (float x, float z)[] pts)
    {
        var arr = new Vector3[pts.Length];
        for (int i = 0; i < pts.Length; i++) arr[i] = new Vector3(pts[i].x, 0, pts[i].z);
        EnemyRoutes.Add(arr);
    }

    // ---------------------------------------------------------------- props (GLB)

    /// Instantiate a GLB prop with a primitive collider sized from its mesh AABB (never trimesh).
    Node3D Prop(string name, float x, float z, float rotDeg = 0, float scale = 1f, bool collide = true, bool cylinder = false)
    {
        var model = PropScene(name).Instantiate<Node3D>();
        Blocky.Apply(model);
        var aabb = new Aabb(); bool first = true;
        foreach (var mi in Meshes(model))
        {
            var a = mi.Transform * mi.GetAabb();
            aabb = first ? a : aabb.Merge(a); first = false;
        }
        Node3D root;
        if (collide)
        {
            var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
            var size = aabb.Size * scale; var center = aabb.GetCenter() * scale;
            Shape3D shape = cylinder
                ? new CylinderShape3D { Radius = Mathf.Max(size.X, size.Z) * 0.5f, Height = size.Y }
                : new BoxShape3D { Size = size };
            body.AddChild(new CollisionShape3D { Shape = shape, Position = center });
            root = body;
            float fp = Mathf.Max(size.X, size.Z);   // rotation-safe square footprint for the minimap
            PropRects.Add(new Rect2(x - fp / 2f, z - fp / 2f, fp, fp));
        }
        else root = new Node3D();
        root.Name = name;
        root.Position = new Vector3(x, 0, z);
        root.RotationDegrees = new Vector3(0, rotDeg, 0);
        model.Scale = Vector3.One * scale;
        root.AddChild(model);
        AddChild(root);
        return root;
    }

    static IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is MeshInstance3D mi) yield return mi;
        foreach (var c in n.GetChildren()) foreach (var m in Meshes(c)) yield return m;
    }

    static void Tint(Node3D prop, string matName, Color c)
    {
        foreach (var mi in Meshes(prop))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D m && m.ResourceName == matName)
                { var d = Blocky.Texturize((StandardMaterial3D)m.Duplicate()); d.AlbedoColor = c; mi.SetSurfaceOverrideMaterial(s, d); }
    }

    void Crate(float x, float z, bool stacked = false)
    {
        float rot = stacked ? 15f : _deco.RandfRange(-6f, 6f);
        var p = Prop("crate", x, z, rot);
        if (stacked) p.Position = new Vector3(x, 1.3f, z);
    }

    static readonly Color[] BarrelColors = { new(0.62f, 0.22f, 0.16f), new(0.2f, 0.42f, 0.66f), new(0.36f, 0.42f, 0.26f) };
    void Barrel(float x, float z, int color)
    {
        var p = Prop("barrel", x, z, _deco.RandfRange(0, 360), 1f, true, true);
        Tint(p, "BarrelBody", BarrelColors[color % BarrelColors.Length]);
    }
    void Sandbags(float x, float z, float rot, bool collide) => Prop("sandbags", x, z, rot, 1f, collide);
    void Fence(float x, float z, float rot) => Prop("fence", x, z, rot, 1f, true);
    void Pallet(float x, float z, float rot) => Prop("pallet", x, z, rot, 1f, false);
    void Tent(float x, float z, float rot) => Prop("tent", x, z, rot, 1f, true);
    void Rock(float x, float z, float s) => Prop("rock", x, z, _deco.RandfRange(0, 360), s, true);
    void Bush(float x, float z, float s) => Prop("bush", x, z, _deco.RandfRange(0, 360), s, false);

    // ---------------------------------------------------------------- ground

    void BuildFloor()
    {
        var body = new StaticBody3D { Name = "Floor", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(64, 1, 64) }, Position = new Vector3(0, -0.5f, 0) });
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.02f, Seed = 7 };
        var img = noise.GetSeamlessImage(256, 256);
        var tex = ImageTexture.CreateFromImage(img);
        var mat = new ShaderMaterial { Shader = new Shader { Code = GroundShaderCode } };
        WorldMats.Add(mat);
        // block seams read better with the slabs on a 1 m grid; colours unchanged
        mat.SetShaderParameter("noise_tex", tex);
        mat.SetShaderParameter("grass_a", new Color(0.44f, 0.55f, 0.3f));
        mat.SetShaderParameter("grass_b", new Color(0.36f, 0.47f, 0.26f));
        mat.SetShaderParameter("dirt_a", new Color(0.55f, 0.47f, 0.34f));
        mat.SetShaderParameter("dirt_b", new Color(0.47f, 0.4f, 0.29f));
        mat.SetShaderParameter("concrete_a", new Color(0.56f, 0.55f, 0.52f));
        mat.SetShaderParameter("concrete_b", new Color(0.49f, 0.48f, 0.46f));
        mat.SetShaderParameter("grout", new Color(0.42f, 0.41f, 0.39f));
        // one big plane: grass continues beyond the walls so the camera never sees the void
        var mesh = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(300, 300) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        body.AddChild(mesh);
        AddChild(body);
    }

    // ---------------------------------------------------------------- walls & building dressing

    void Wall(float x, float z, float w, float d, float h, Material mat)
    {
        var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = new Vector3(x, h / 2f, z) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(w, h, d) } });
        WallRects.Add(new Rect2(x - w / 2f, z - d / 2f, w, d));
        // darker plinth row so walls meet the ground with a visible base (visual only)
        _plinth ??= Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.38f, 0.37f, 0.36f), Roughness = 0.95f });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w + 0.16f, 0.32f, d + 0.16f) }, MaterialOverride = _plinth, Position = new Vector3(x, 0.16f, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, h, d) }, MaterialOverride = mat });
        AddChild(body);
    }

    void Pillar(float x, float z)
    {
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, 3.6f, 1.4f) }, MaterialOverride = _concrete, Position = new Vector3(x, 1.8f, z) });
    }

    /// Posts + lintel around a door gap of width `gap` centred at (x,z) in a wall of thickness d (vertical = wall runs along z).
    void DoorFrame(float x, float z, float gap, float d, float h, bool vertical)
    {
        float pt = 0.28f, lt = 0.3f;
        for (int s = -1; s <= 1; s += 2)
        {
            var pos = vertical ? new Vector3(x, h / 2f + 0.1f, z + s * (gap / 2f + pt / 2f)) : new Vector3(x + s * (gap / 2f + pt / 2f), h / 2f + 0.1f, z);
            var size = vertical ? new Vector3(d + 0.2f, h + 0.2f, pt) : new Vector3(pt, h + 0.2f, d + 0.2f);
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = _frameMat, Position = pos });
        }
        var lsize = vertical ? new Vector3(d + 0.2f, lt, gap + pt * 2) : new Vector3(gap + pt * 2, lt, d + 0.2f);
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = lsize }, MaterialOverride = _frameMat, Position = new Vector3(x, h - lt / 2f + 0.2f, z) });
        // a warning stripe on the ground through the doorway reads from above
        var stripe = new MeshInstance3D { Mesh = new BoxMesh { Size = vertical ? new Vector3(d + 0.6f, 0.02f, gap) : new Vector3(gap, 0.02f, d + 0.6f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.47f, 0.42f), Roughness = 0.95f }, Position = new Vector3(x, 0.011f, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(stripe);
    }

    /// A stair-stepped roof strip along the top of a wall segment (Minecraft stairs): three quarter-block slabs per side
    /// descending from the ridge to the eave. Overhangs both sides but leaves the interior open for the top-down camera.
    void Eaves(float x, float z, float w, float d, float h, Material mat)
    {
        bool alongX = w > d;
        float len = (alongX ? w : d) + 0.3f;
        float half = (alongX ? d : w) / 2f + 0.95f;      // horizontal run from ridge to eave edge
        const int steps = 3; const float thick = 0.25f;
        float depth = half / steps, rise = 0.2f;
        float ridgeY = h + 0.12f + rise * (steps - 1) + thick / 2f;
        for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < steps; k++)
            {
                float off = (k + 0.5f) * depth, y = ridgeY - k * rise;
                var slab = new MeshInstance3D { Mesh = new BoxMesh { Size = alongX ? new Vector3(len, thick, depth + 0.02f) : new Vector3(depth + 0.02f, thick, len) }, MaterialOverride = mat };
                slab.Position = alongX ? new Vector3(x, y, z + side * off) : new Vector3(x + side * off, y, z);
                AddChild(slab);
            }
        // ridge beam
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = alongX ? new Vector3(len + 0.1f, 0.2f, 0.25f) : new Vector3(0.25f, 0.2f, len + 0.1f) }, MaterialOverride = _frameMat, Position = new Vector3(x, ridgeY + thick / 2f + 0.05f, z) });
    }

    /// Window on a wall face: `vertical` walls run along z; `side` picks which face (+1/-1) the glass protrudes from.
    void Window(float x, float z, bool vertical, int side)
    {
        float off = 0.3f + 0.03f;
        var pos = vertical ? new Vector3(x + side * off, 1.7f, z) : new Vector3(x, 1.7f, z + side * off);
        var frame = vertical ? new Vector3(0.06f, 1.0f, 1.3f) : new Vector3(1.3f, 1.0f, 0.06f);
        var glass = vertical ? new Vector3(0.08f, 0.84f, 1.14f) : new Vector3(1.14f, 0.84f, 0.08f);
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = frame }, MaterialOverride = _trim, Position = pos });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = glass }, MaterialOverride = _glass, Position = pos });
        var bar = vertical ? new Vector3(0.1f, 0.84f, 0.06f) : new Vector3(0.06f, 0.84f, 0.1f);
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = bar }, MaterialOverride = _trim, Position = pos });
    }

    void Pedestal(float x, float z)
    {
        var mat = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.44f), Roughness = 0.7f });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.2f, 0.14f, 2.2f) }, MaterialOverride = mat, Position = new Vector3(x, 0.07f, z) });
        var rim = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.3f), Roughness = 0.5f, Metallic = 0.4f });
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2; float side = i % 2 == 0 ? -1 : 1;
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = alongX ? new Vector3(2.3f, 0.1f, 0.15f) : new Vector3(0.15f, 0.1f, 2.3f) }, MaterialOverride = rim, Position = new Vector3(x + (alongX ? 0 : side * 1.075f), 0.16f, z + (alongX ? side * 1.075f : 0)) });
        }
    }

    // ---------------------------------------------------------------- extraction & outskirts

    void BuildExtraction()
    {
        var root = new Node3D { Name = "Extraction", Position = ExtractPos };
        // stone apron and glowing pad as pixel circles of 1 m blocks (block centres inside the radius)
        var apron = Blocky.Texturize(new StandardMaterial3D { AlbedoColor = new Color(0.58f, 0.57f, 0.54f), Roughness = 0.95f });
        var padMat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.9f, 0.45f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color(0.1f, 0.8f, 0.3f), EmissionEnergyMultiplier = 0.8f };
        var apronMesh = new BoxMesh { Size = new Vector3(1f, 0.06f, 1f) };
        var padMesh = new BoxMesh { Size = new Vector3(0.94f, 0.05f, 0.94f) };
        for (int bx = -5; bx < 5; bx++)
            for (int bz = -5; bz < 5; bz++)
            {
                var c = new Vector2(bx + 0.5f, bz + 0.5f);
                if (c.Length() <= ExtractRadius + 1.3f)
                    root.AddChild(new MeshInstance3D { Mesh = apronMesh, MaterialOverride = apron, Position = new Vector3(c.X, 0.03f, c.Y), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                if (c.Length() <= ExtractRadius)
                    root.AddChild(new MeshInstance3D { Mesh = padMesh, MaterialOverride = padMat, Position = new Vector3(c.X, 0.08f, c.Y), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
        var ringMat = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 1f, 0.6f), EmissionEnabled = true, Emission = new Color(0.3f, 1f, 0.5f), EmissionEnergyMultiplier = 2.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _extractRing = new Node3D { Position = new Vector3(0, 0.12f, 0) };
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2; float side = i % 2 == 0 ? -1 : 1;
            _extractRing.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = alongX ? new Vector3(ExtractRadius * 2 + 0.2f, 0.08f, 0.2f) : new Vector3(0.2f, 0.08f, ExtractRadius * 2 + 0.2f) }, MaterialOverride = ringMat, Position = alongX ? new Vector3(0, 0, side * ExtractRadius) : new Vector3(side * ExtractRadius, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        root.AddChild(_extractRing);
        // painted "H" on the pad
        var paint = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.95f, 0.85f), Roughness = 0.9f };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.02f, 2.4f) }, MaterialOverride = paint, Position = new Vector3(-0.8f, 0.11f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.02f, 2.4f) }, MaterialOverride = paint, Position = new Vector3(0.8f, 0.11f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.6f, 0.02f, 0.3f) }, MaterialOverride = paint, Position = new Vector3(0, 0.11f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _beamMat = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 1f, 0.6f, 0.16f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _extractBeam = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(ExtractRadius * 2, 5f, ExtractRadius * 2) }, MaterialOverride = _beamMat, Position = new Vector3(0, 2.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(_extractBeam);
        var label = new Label3D { Text = "EXTRACT", FontSize = 64, PixelSize = 0.01f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.2f, 0), Modulate = new Color(0.6f, 1f, 0.7f), OutlineSize = 12, OutlineModulate = new Color(0, 0.15f, 0.05f), NoDepthTest = true };
        root.AddChild(label);
        AddChild(root);
        // radio beacon beside the pad (collides; outside the pad radius), with a blinking lamp
        var beacon = Prop("beacon", ExtractPos.X + ExtractRadius + 1.3f, ExtractPos.Z + 0.8f, -30, 1f, true, true);
        foreach (var mi in Meshes(beacon))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D m && m.ResourceName == "BeaconLight")
                { _lampMat = Blocky.Texturize((StandardMaterial3D)m.Duplicate()); _lampMat.EmissionEnabled = true; mi.SetSurfaceOverrideMaterial(s, _lampMat); }
        _beaconLight = new OmniLight3D { LightColor = new Color(0.4f, 1f, 0.55f), LightEnergy = 1.5f, OmniRange = 7f, ShadowEnabled = false, Position = new Vector3(0, 2.9f, 0) };
        beacon.AddChild(_beaconLight);
        Sandbags(ExtractPos.X - 1.5f, ExtractPos.Z + ExtractRadius + 2.2f, 0, true);
    }

    /// Trees, bushes and rocks outside the perimeter wall: pure decoration, no collision.
    void BuildOutskirts()
    {
        var rng = _deco;
        for (int i = 0; i < 64; i++)
        {
            // pick a random point in the ring 33..44 m from centre (square ring)
            float side = rng.Randf();
            float a = rng.RandfRange(-44f, 44f), b = rng.RandfRange(33f, 44f);
            if (rng.Randf() < 0.5f) b = -b;
            var pos = side < 0.5f ? new Vector2(a, b) : new Vector2(b, a);
            float r = rng.Randf();
            if (r < 0.45f) Prop("tree_round", pos.X, pos.Y, rng.RandfRange(0, 360), rng.RandfRange(0.85f, 1.25f), false);
            else if (r < 0.8f) Prop("tree_pine", pos.X, pos.Y, rng.RandfRange(0, 360), rng.RandfRange(0.9f, 1.3f), false);
            else if (r < 0.93f) Prop("bush", pos.X, pos.Y, rng.RandfRange(0, 360), rng.RandfRange(1.0f, 1.6f), false);
            else Prop("rock", pos.X, pos.Y, rng.RandfRange(0, 360), rng.RandfRange(0.8f, 1.6f), false);
        }
        // a second, sparser ring further out so the horizon never looks empty
        for (int i = 0; i < 40; i++)
        {
            float a = rng.RandfRange(-60f, 60f), b = rng.RandfRange(45f, 60f);
            if (rng.Randf() < 0.5f) b = -b;
            var pos = rng.Randf() < 0.5f ? new Vector2(a, b) : new Vector2(b, a);
            Prop(rng.Randf() < 0.5f ? "tree_round" : "tree_pine", pos.X, pos.Y, rng.RandfRange(0, 360), rng.RandfRange(1.0f, 1.5f), false);
        }
    }

    // ---------------------------------------------------------------- navigation

    void BakeNav()
    {
        var nm = new NavigationMesh
        {
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.RootNodeChildren,
            GeometryCollisionMask = Layers.World,
            AgentRadius = 0.75f,
            AgentHeight = 2.0f,
            AgentMaxClimb = 0.25f,
            CellSize = 0.25f,
            CellHeight = 0.25f,
            RegionMinSize = 4f,
            EdgeMaxError = 1.0f,
        };
        NavigationMesh = nm;
        BakeNavigationMesh(false);
        GD.Print($"[Arena] navmesh baked: {NavigationMesh.GetPolygonCount()} polygons");
    }

    public Vector3[] GetPath(Vector3 from, Vector3 to)
    {
        var map = GetWorld3D().NavigationMap;
        var path = NavigationServer3D.MapGetPath(map, from, to, true);
        if (path == null || path.Length == 0) return new[] { from, to };
        return path;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_extractRing != null)
        {
            float s = 1f + 0.06f * Mathf.Sin(_t * 3f);
            _extractRing.Scale = new Vector3(s, 1, s);
            _extractRing.Rotation = new Vector3(0, Mathf.Round(_t * 0.6f / (Mathf.Pi / 2)) * (Mathf.Pi / 2), 0);   // blocks turn in quarter steps
            var c = _beamMat.AlbedoColor; c.A = 0.08f + 0.05f * Mathf.Sin(_t * 2f);
            _beamMat.AlbedoColor = c;
        }
        if (_lampMat != null)
        {
            float blink = Mathf.Pow(Mathf.Max(0, Mathf.Sin(_t * 4f)), 6f);
            _lampMat.EmissionEnergyMultiplier = 0.6f + 3.5f * blink;
            _beaconLight.LightEnergy = 0.4f + 2.2f * blink;
        }
    }

    // ---------------------------------------------------------------- shaders

    const string WallShaderCode = @"
shader_type spatial;
// Minecraft-style texel walls: everything is quantised to a 16-texel-per-metre grid, blocks are 1 m with a seam texel.
uniform int style = 0; // 0 stone, 1 bricks, 2 iron panels, 3 rusted panels
uniform vec3 base_col : source_color = vec3(0.7);
uniform vec3 alt_col : source_color = vec3(0.6);
uniform vec3 mortar_col : source_color = vec3(0.5);
uniform vec3 cap_col : source_color = vec3(0.4);
uniform float wet = 0.0;    // rain: darker, glossier
uniform float snow = 0.0;   // snow: white caps
varying vec3 wpos;
varying vec3 wnrm;
const float T = 16.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float tone(float h) { return h < 0.45 ? 1.0 : (h < 0.75 ? 0.93 : (h < 0.92 ? 0.86 : 0.78)); }
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    vec2 uv = abs(wnrm.x) > abs(wnrm.z) ? vec2(wpos.z, wpos.y) : vec2(wpos.x, wpos.y);
    if (wnrm.y > 0.7) uv = wpos.xz;
    vec2 tx = floor(uv * T + 0.001);
    vec2 blk = floor(tx / T);
    vec2 tin = tx - blk * T;                        // texel inside its block, 0..15
    float t = tone(hash(tx));
    float edge = min(step(tin.x, 0.5) + step(tin.y, 0.5), 1.0);
    vec3 c;
    float rough = 0.9;
    if (wnrm.y > 0.7) {
        c = mix(cap_col, cap_col * 0.8, step(0.7, hash(floor(tx / 3.0) + 3.0))) * t;
        c = mix(c, cap_col * 0.6, edge * 0.6);
    } else if (style == 1) {
        // bricks 8x4 texels, alternate rows offset half a brick, 1-texel mortar
        float row = floor(tx.y / 4.0);
        float bx = tx.x + mod(row, 2.0) * 4.0;
        vec2 bid = vec2(floor(bx / 8.0), row);
        float mortar = min(step(mod(bx, 8.0), 0.5) + step(mod(tx.y, 4.0), 0.5), 1.0);
        vec3 brick = mix(base_col, alt_col, step(0.5, hash(bid))) * t;
        c = mix(brick, mortar_col, mortar);
    } else if (style == 2) {
        c = mix(base_col, alt_col, step(0.6, hash(floor(tx / 2.0)))) * t;
        float rivet = step(length(tin - vec2(2.0)), 1.1) + step(length(tin - vec2(13.0)), 1.1) + step(length(tin - vec2(2.0, 13.0)), 1.1) + step(length(tin - vec2(13.0, 2.0)), 1.1);
        c = mix(c, mortar_col, edge);
        c = mix(c, alt_col * 0.55, min(rivet, 1.0));
        rough = 0.6;
    } else if (style == 3) {
        vec3 panel = mix(base_col, alt_col, step(0.5, hash(blk))) * t;
        float blotch = step(0.62, hash(floor(tx / 3.0)) * 0.6 + hash(floor(tx / 7.0)) * 0.4);
        c = mix(panel, mortar_col, blotch * 0.8);
        c = mix(c, mortar_col, edge);
        rough = 0.75;
    } else {
        // stone: texel tones, darker pebble clumps, block seams, dark grime texels near the ground
        float clump = step(0.72, hash(floor(tx / 3.0) + 11.0));
        c = mix(base_col, alt_col, clump) * t;
        c = mix(c, mortar_col, edge * 0.7);
        c = mix(c, mortar_col, step(wpos.y, 0.5) * step(0.45, hash(tx + 5.0)) * 0.5);
    }
    if (wnrm.y > 0.7) c = mix(c, vec3(0.93, 0.95, 0.98) * t, snow);
    c *= mix(1.0, 0.7, wet); c = mix(c, c * vec3(0.9, 0.95, 1.05), wet);
    ALBEDO = c;
    ROUGHNESS = mix(rough, 0.35, wet);
}";

    const string RoofShaderCode = @"
shader_type spatial;
uniform int style = 0; // 0 clay tiles, 1 corrugated metal
uniform vec3 col_a : source_color = vec3(0.6, 0.3, 0.2);
uniform vec3 col_b : source_color = vec3(0.5, 0.24, 0.17);
uniform vec3 edge_col : source_color = vec3(0.3, 0.15, 0.1);
uniform float wet = 0.0;
uniform float snow = 0.0;
varying vec3 wpos;
varying vec3 wnrm;
const float T = 16.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float tone(float h) { return h < 0.45 ? 1.0 : (h < 0.75 ? 0.93 : (h < 0.92 ? 0.86 : 0.78)); }
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    vec2 uv = wnrm.y > 0.5 ? wpos.xz : (abs(wnrm.x) > abs(wnrm.z) ? vec2(wpos.z, wpos.y) : vec2(wpos.x, wpos.y));
    vec2 tx = floor(uv * T + 0.001);
    float t = tone(hash(tx));
    vec3 c;
    float rough = 0.85;
    if (style == 0) {
        // clay tiles: 8x4-texel tiles, rows offset by half a tile, dark edge texels
        float row = floor(tx.y / 4.0);
        float bx = tx.x + mod(row, 2.0) * 4.0;
        c = mix(col_a, col_b, step(0.5, hash(vec2(floor(bx / 8.0), row)))) * t;
        c = mix(c, edge_col, step(mod(tx.y, 4.0), 0.5) * 0.85);
        c = mix(c, edge_col, step(mod(bx, 8.0), 0.5) * 0.5);
    } else {
        // corrugated metal: 2-texel stripes, a seam every block
        c = mix(col_a, col_b, mod(floor(tx.x / 2.0), 2.0)) * t;
        c = mix(c, edge_col, step(mod(tx.y, T), 0.5));
        rough = 0.55;
    }
    if (wnrm.y > 0.5) c = mix(c, vec3(0.93, 0.95, 0.98) * t, snow);
    c *= mix(1.0, 0.7, wet); c = mix(c, c * vec3(0.9, 0.95, 1.05), wet);
    ALBEDO = c;
    ROUGHNESS = mix(rough, 0.3, wet);
}";

    const string GroundShaderCode = @"
shader_type spatial;
// Minecraft-style ground: 1 m blocks decide the zone (grass / dirt / stone slab), 16 texels per metre paint it.
uniform sampler2D noise_tex : repeat_enable, filter_nearest;
uniform vec3 grass_a : source_color; uniform vec3 grass_b : source_color;
uniform vec3 dirt_a : source_color; uniform vec3 dirt_b : source_color;
uniform vec3 concrete_a : source_color; uniform vec3 concrete_b : source_color;
uniform vec3 grout : source_color;
uniform float wet = 0.0;
uniform float snow = 0.0;
varying vec3 wpos;
const float T = 16.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float tone(float h) { return h < 0.45 ? 1.0 : (h < 0.75 ? 0.93 : (h < 0.92 ? 0.86 : 0.78)); }
// signed distance to an axis-aligned rectangle (min, max)
float rect(vec2 p, vec2 lo, vec2 hi) {
    vec2 c = (lo + hi) * 0.5, h = (hi - lo) * 0.5;
    vec2 d = abs(p - c) - h;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 p = wpos.xz;
    vec2 tx = floor(p * T + 0.001);
    vec2 blk = floor(tx / T);
    vec2 bc = blk + 0.5;                                 // block centre decides the zone
    vec2 tin = tx - blk * T;
    float t = tone(hash(tx));
    float n1 = texture(noise_tex, bc * 0.012).r;         // large patches
    float n2 = hash(blk);                                // per-block tone
    float dConc = min(min(rect(bc, vec2(-8.3, -8.3), vec2(8.3, 8.3)), rect(bc, vec2(-26.3, -20.3), vec2(-13.7, -9.7))),
                      min(rect(bc, vec2(11.7, 3.7), vec2(26.3, 16.3)), length(bc - vec2(24.0, -24.0)) - 4.4));
    float conc = step(dConc, 0.0);
    // worn dirt blocks: dense next to the slabs and thinning out over 3.5 m, along the perimeter wall, and in noisy patches
    float dirt = step(n2, 1.0 - dConc / 3.5);
    dirt = max(dirt, step(0.64, n1));
    float wallD = 30.0 - max(abs(bc.x), abs(bc.y));
    dirt = max(dirt, step(wallD, 2.0) * step(0.0, wallD));
    // grass texels: two greens, a per-block tone, a few bright blade texels
    vec3 g = mix(grass_a, grass_b, step(0.5, hash(tx + 1.0))) * t * (0.94 + 0.12 * n2);
    g = mix(g, grass_a * 1.12, step(0.94, hash(tx + 2.0)));
    float outside = step(30.5, max(abs(bc.x), abs(bc.y)));
    g = mix(g, g * vec3(0.75, 0.82, 0.8), outside);
    vec3 d = mix(dirt_a, dirt_b, step(0.5, hash(tx + 3.0))) * t * (0.94 + 0.12 * n2);
    d = mix(d, dirt_b * 0.8, step(0.9, hash(tx + 4.0)));   // pebbles
    vec3 c = mix(g, d, dirt);
    // asphalt service road along the north wall (z -30..-26): dark texels, yellow dashes on the centre row, white south edge
    float road = step(-29.9, bc.y) * step(bc.y, -26.1);
    vec3 asphalt = mix(vec3(0.24, 0.24, 0.26), vec3(0.19, 0.19, 0.21), step(0.5, hash(tx + 9.0))) * t;
    float dash = step(abs(bc.y + 27.5), 0.1) * step(7.0, tin.y) * step(tin.y, 8.9) * step(mod(blk.x, 3.0), 1.5);
    asphalt = mix(asphalt, vec3(0.85, 0.72, 0.28), dash);
    float edgeLine = step(abs(bc.y + 26.5), 0.1) * step(14.0, tin.y);
    asphalt = mix(asphalt, vec3(0.8, 0.8, 0.78), edgeLine * 0.9);
    c = mix(c, asphalt, road);
    // stone-brick slabs: 2x2 bricks per block with 1-texel grout, per-brick tone
    float seam = min(step(tin.x, 0.5) + step(tin.y, 0.5) + step(abs(tin.x - 8.0), 0.4) + step(abs(tin.y - 8.0), 0.4), 1.0);
    vec2 brick = floor(tin / 8.0) + blk * 2.0;
    vec3 slab = mix(concrete_a, concrete_b, step(0.5, hash(brick))) * t;
    slab = mix(slab, grout, seam * 0.85);
    c = mix(c, slab, conc);
    // snow: everything but the road turns white (the road keeps dark tyre lanes), per-texel tone kept
    vec3 snowc = vec3(0.93, 0.95, 0.98) * t * (0.96 + 0.06 * n2);
    float lanes = road * (1.0 - step(abs(bc.y + 27.5), 0.6));
    c = mix(c, snowc, snow * (1.0 - lanes * 0.7));
    // rain: darker and glossy, puddle texels in the dirt and on the slabs
    float puddle = step(0.9, hash(floor(tx / 2.0) + 21.0)) * max(dirt, conc) * (1.0 - snow);
    c *= mix(1.0, 0.72, wet); c = mix(c, c * vec3(0.85, 0.92, 1.1), wet * puddle);
    ALBEDO = c;
    ROUGHNESS = mix(mix(0.95, 0.85, conc), mix(0.45, 0.15, puddle), wet);
}";
}
