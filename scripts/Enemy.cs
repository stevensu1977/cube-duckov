using Godot;
namespace Duckov;

/// Patrolling enemy duck. Sees the player within a radius with line of sight, chases and shoots, drops loot sometimes.
public partial class Enemy : CharacterBody3D, IDamageable
{
    public Vector3[] Waypoints = System.Array.Empty<Vector3>();
    /// Deterministic per-enemy seed (route index); instance ids differ between the still capture and the movie-writer run.
    public int Seed;
    public int Hp = 40;
    public float PatrolSpeed = 2.6f, ChaseSpeed = 4.6f;
    public float DetectRadius = 14f, LoseRadius = 24f, FireRange = 17f, PreferredRange = 7f;
    public float FireInterval = 0.85f;
    public int Damage = 8;
    /// Which gun this soldier carries (set by Game per route): changes tracer, flash, pellets, and what it drops.
    public WeaponId WeaponKind = WeaponId.Rifle;
    WeaponDef _wdef; int _pellets = 1; float _spread = 0f, _bulletSpeed = 34f, _range = 60f;
    float _slowT;
    public bool Dead { get; private set; }

    enum EState { Patrol, Chase, Search }
    EState _state = EState.Patrol;
    int _wp;
    float _waitT, _fireCd, _repathT, _searchT, _corpseT;
    Vector3 _lastKnown;
    Vector3[] _path = System.Array.Empty<Vector3>();
    int _pathIdx;
    Vector3 _pathGoal = new(9999, 0, 9999);
    DuckVisual _visual;
    RandomNumberGenerator _rng;
    float _yaw;

    public override void _Ready()
    {
        _rng = new RandomNumberGenerator { Seed = (ulong)(1000003 + Seed * 7919) };
        CollisionLayer = Layers.Enemy;
        CollisionMask = Layers.World | Layers.Enemy | Layers.Player;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.5f, Height = 1.6f }, Position = new Vector3(0, 0.8f, 0) });
        var palette = new[] { new Color(0.55f, 0.6f, 0.42f), new Color(0.62f, 0.55f, 0.45f), new Color(0.5f, 0.55f, 0.5f) };
        _visual = DuckVisual.Create(palette[_rng.RandiRange(0, palette.Length - 1)], new Color(0.22f, 0.3f, 0.2f), soldier: true);
        AddChild(_visual);
        _waitT = _rng.RandfRange(0f, 1.5f);
        _repathT = _rng.RandfRange(0.3f, 0.8f);
        _fireCd = 0.6f;
        if (Waypoints.Length > 0) _wp = 0;
        _yaw = _rng.RandfRange(0, Mathf.Tau);
        Rotation = new Vector3(0, _yaw, 0);
        // enemy-tuned stats per gun (the player's numbers would be lethal)
        _wdef = WeaponDef.Get(WeaponKind);
        switch (WeaponKind)
        {
            case WeaponId.Shotgun: FireInterval = 1.5f; Damage = 4; _pellets = 6; _spread = 10f; _bulletSpeed = 32f; _range = 12f; break;
            case WeaponId.IceSmg: FireInterval = 0.2f; Damage = 3; _spread = 5f; break;
            case WeaponId.ElecSniper: FireInterval = 2.2f; Damage = 16; _bulletSpeed = 90f; FireRange = 22f; PreferredRange = 12f; break;
        }
        _visual.SetWeapon(_wdef);
    }

    /// Ice rounds: slowed to 45 % for a while, with a frosty tint.
    public void ApplySlow(float seconds) { _slowT = Mathf.Max(_slowT, seconds); _visual.Flash(new Color(0.6f, 0.9f, 1f), 0.5f); }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Dead)
        {
            _corpseT += dt;
            _visual.Animate(0, dt);
            if (_corpseT > 25f) QueueFree();
            return;
        }
        var game = Game.I;
        var player = game.Player;
        bool playerAlive = player != null && !player.Dead && game.Current == Game.State.Playing;
        Vector3 speedTarget = Vector3.Zero;
        float maxSpeed = PatrolSpeed;

        var eye = GlobalPosition + new Vector3(0, 1.2f, 0);
        float dist = playerAlive ? GlobalPosition.DistanceTo(player.GlobalPosition) : 9999f;
        bool canSee = false;
        if (playerAlive)
        {
            float radius = (_state == EState.Patrol ? DetectRadius : LoseRadius) * (Game.I.Weather?.VisibilityFactor ?? 1f);
            if (dist < radius) canSee = HasLineOfSight(eye, player.GlobalPosition + new Vector3(0, 0.9f, 0));
        }

        _fireCd -= dt;
        _repathT -= dt;

        switch (_state)
        {
            case EState.Patrol:
                if (canSee) { Alert(player.GlobalPosition); break; }
                if (Waypoints.Length == 0) break;
                if (_waitT > 0) { _waitT -= dt; break; }
                var wpTarget = Waypoints[_wp];
                if (FlatDist(GlobalPosition, wpTarget) < 0.7f)
                {
                    _wp = (_wp + 1) % Waypoints.Length;
                    _waitT = _rng.RandfRange(0.6f, 1.8f);
                    _path = System.Array.Empty<Vector3>();
                    break;
                }
                speedTarget = Steer(wpTarget, PatrolSpeed);
                break;

            case EState.Chase:
                maxSpeed = ChaseSpeed;
                if (canSee)
                {
                    _lastKnown = player.GlobalPosition;
                    _searchT = 4f;
                    // hold at preferred range, otherwise close in
                    if (dist > PreferredRange) speedTarget = Steer(_lastKnown, ChaseSpeed);
                    else speedTarget = Vector3.Zero;
                    FacePoint(_lastKnown, dt, 10f);
                    if (dist < FireRange && _fireCd <= 0) Shoot(player);
                }
                else
                {
                    _state = EState.Search;
                    _searchT = 4f;
                }
                break;

            case EState.Search:
                maxSpeed = ChaseSpeed;
                if (canSee) { _state = EState.Chase; break; }
                _searchT -= dt;
                if (FlatDist(GlobalPosition, _lastKnown) > 1.2f && _searchT > 0) speedTarget = Steer(_lastKnown, ChaseSpeed * 0.9f);
                else if (_searchT <= 0) { _state = EState.Patrol; _path = System.Array.Empty<Vector3>(); _waitT = 0.5f; }
                break;
        }

        // motion
        if (_slowT > 0) { _slowT -= dt; speedTarget *= 0.45f; }
        var vel = Velocity;
        var horiz = new Vector3(vel.X, 0, vel.Z);
        horiz = horiz.MoveToward(speedTarget, 18f * dt);
        Velocity = new Vector3(horiz.X, 0, horiz.Z);
        MoveAndSlide();
        GlobalPosition = new Vector3(GlobalPosition.X, 0, GlobalPosition.Z);

        if (!(_state == EState.Chase && canSee) && horiz.LengthSquared() > 0.1f)
            FaceDirection(horiz, dt, 6f);
        _visual.Animate(Mathf.Clamp(horiz.Length() / ChaseSpeed, 0, 1), dt);
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.Y = 0; b.Y = 0; return a.DistanceTo(b); }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.World);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        return hit.Count == 0;
    }

    /// Follow a navmesh path towards goal; returns desired horizontal velocity.
    Vector3 Steer(Vector3 goal, float speed)
    {
        if (_repathT <= 0 || _path.Length == 0 || FlatDist(goal, _pathGoal) > 1.5f)
        {
            _path = Game.I.Arena.GetPath(GlobalPosition, goal);
            _pathIdx = 0;
            _pathGoal = goal;
            _repathT = 0.6f;
        }
        while (_pathIdx < _path.Length - 1 && FlatDist(GlobalPosition, _path[_pathIdx]) < 0.6f) _pathIdx++;
        var next = _pathIdx < _path.Length ? _path[_pathIdx] : goal;
        var dir = next - GlobalPosition; dir.Y = 0;
        if (dir.LengthSquared() < 0.0001f) return Vector3.Zero;
        return dir.Normalized() * speed;
    }

    void FacePoint(Vector3 p, float dt, float rate)
    {
        var d = p - GlobalPosition; d.Y = 0;
        if (d.LengthSquared() > 0.001f) FaceDirection(d, dt, rate);
    }

    void FaceDirection(Vector3 d, float dt, float rate)
    {
        float target = Mathf.Atan2(-d.X, -d.Z);
        _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-rate * dt));
        Rotation = new Vector3(0, _yaw, 0);
    }

    void Shoot(Player player)
    {
        _fireCd = FireInterval * _rng.RandfRange(0.8f, 1.3f);
        var muzzle = _visual.Muzzle.GlobalPosition;
        var target = player.GlobalPosition + new Vector3(0, 0.8f, 0);
        // lead a little, then add spread that grows with distance
        target += player.Velocity * 0.12f;
        var dir = (target - muzzle);
        float dist = dir.Length();
        dir = dir.Normalized();
        float spread = Mathf.DegToRad(3f + dist * 0.45f);
        dir = dir.Rotated(Vector3.Up, _rng.RandfRange(-spread, spread));
        dir.Y += _rng.RandfRange(-0.02f, 0.02f);
        dir = dir.Normalized();
        for (int i = 0; i < _pellets; i++)
        {
            var d = dir;
            if (_spread > 0) { float s = Mathf.DegToRad(_spread); d = d.Rotated(Vector3.Up, _rng.RandfRange(-s, s)); d.Y += _rng.RandfRange(-s, s) * 0.25f; d = d.Normalized(); }
            var b = new Bullet { Velocity = d * _bulletSpeed, Damage = Damage, HitMask = Layers.World | Layers.Player, FromPlayer = false, Shooter = this, Weapon = _wdef, MaxRange = _range };
            Game.I.AddChild(b);
            b.GlobalPosition = muzzle;
        }
        Fx.MuzzleFlash(Game.I, muzzle, dir, _wdef);
        Game.I.Notify("enemy-shot");
    }

    public void Alert(Vector3 where)
    {
        if (Dead) return;
        if (_state == EState.Patrol) Game.I.Notify("alert");
        _lastKnown = where;
        _searchT = 5f;
        if (_state != EState.Chase) _state = EState.Chase;
        _path = System.Array.Empty<Vector3>();
        _repathT = 0;
    }

    public void TakeDamage(int amount, Vector3 fromPosition, bool fromPlayer)
    {
        if (Dead) return;
        Hp -= amount;
        _visual.Flash();
        if (fromPlayer && Game.I.Player != null) Alert(Game.I.Player.GlobalPosition);
        if (Hp <= 0) Die();
    }

    void Die()
    {
        Dead = true;
        _visual.Fall();
        CollisionLayer = 0;
        CollisionMask = 0;
        Velocity = Vector3.Zero;
        Game.I.OnEnemyKilled(this);
        if (WeaponKind != WeaponId.Rifle) Game.I.SpawnWeaponLoot(GlobalPosition + new Vector3(-0.7f, 0, 0.4f), WeaponKind);
        if (_rng.Randf() < 0.55f)
        {
            var kind = _rng.Randf() < 0.5f ? Loot.Kind.Ammo : (_rng.Randf() < 0.5f ? Loot.Kind.Medkit : Loot.Kind.Cash);
            Game.I.SpawnLoot(GlobalPosition + new Vector3(0.6f, 0, 0.3f), kind, false);
        }
    }
}
