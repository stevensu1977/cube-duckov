using Godot;
namespace Duckov;

/// The player duck. Intents (move/aim/fire/reload/interact) are read from Input unless External is set (autopilot).
public partial class Player : CharacterBody3D, IDamageable
{
    public float Speed = 6.5f;
    public int Hp = 100, MaxHp = 100;
    public float PickupRange = 2.2f;

    // weapons: the current one's ammo is exposed through the old Mag/Reserve/... names so the HUD and autopilot read as before
    public readonly System.Collections.Generic.List<WeaponState> Weapons = new();
    public int Slot { get; private set; }
    public WeaponState W => Weapons[Slot];
    public WeaponDef WeaponDef => W.Def;
    public int Mag { get => W.Mag; set => W.Mag = value; }
    public int Reserve { get => W.Reserve; set => W.Reserve = value; }
    public int MagSize => W.Def.MagSize;
    public float FireInterval => W.Def.FireInterval;
    public float ReloadTime => W.Def.ReloadTime;
    public int Damage => W.Def.Damage;
    public float BulletSpeed => W.Def.BulletSpeed;
    /// Requested weapon (input or autopilot); applied at the next physics tick.
    public WeaponId? WantWeapon;
    public float SlowT { get; private set; }

    // intents
    public Vector2 MoveInput;          // world XZ
    public Vector3 AimPoint;           // world position to face/shoot at
    public bool FireHeld, ReloadPressed, InteractPressed;
    public bool External;

    public bool Dead { get; private set; }
    public float ReloadLeft { get => W.ReloadLeft; private set => W.ReloadLeft = value; }
    public bool Reloading => ReloadLeft > 0;
    public Loot NearbyLoot { get; private set; }
    public float HurtFlash { get; private set; }

    float _yaw;
    DuckVisual _visual;

    public override void _Ready()
    {
        CollisionLayer = Layers.Player;
        CollisionMask = Layers.World | Layers.Enemy;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.5f, Height = 1.6f }, Position = new Vector3(0, 0.8f, 0) });
        // the chosen avatar (autopilot / capture runs always use the classic yellow duck so the proof stills match)
        var avatar = AvatarDef.Get(Game.AvatarOverride ?? Profile.Avatar);
        _visual = DuckVisual.Create(avatar);
        AddChild(_visual);
        Weapons.Add(new WeaponState(WeaponDef.Get(WeaponId.Rifle), 4));   // 12 | 48, as before
        _visual.SetWeapon(WeaponDef);
        AimPoint = GlobalPosition + new Vector3(0, 0.8f, -5f);
        _yaw = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        HurtFlash = Mathf.Max(0, HurtFlash - dt * 3f);
        SlowT = Mathf.Max(0, SlowT - dt);
        var game = Game.I;
        if (Dead || game.Current != Game.State.Playing)
        {
            Velocity = Velocity.MoveToward(Vector3.Zero, 30f * dt);
            MoveAndSlide();
            _visual.Animate(0, dt);
            return;
        }
        if (!External) ReadInput();
        if (WantWeapon.HasValue) { SwitchTo(WantWeapon.Value); WantWeapon = null; }

        // movement (ice rounds slow the duck)
        var wish = new Vector3(MoveInput.X, 0, MoveInput.Y);
        if (wish.LengthSquared() > 1f) wish = wish.Normalized();
        var target = wish * Speed * (SlowT > 0 ? 0.6f : 1f);
        var horiz = new Vector3(Velocity.X, 0, Velocity.Z).MoveToward(target, 40f * dt);
        Velocity = horiz;
        MoveAndSlide();
        GlobalPosition = new Vector3(GlobalPosition.X, 0, GlobalPosition.Z);

        // facing
        var d = AimPoint - GlobalPosition; d.Y = 0;
        if (d.LengthSquared() > 0.05f)
        {
            float t = Mathf.Atan2(-d.X, -d.Z);
            _yaw = Mathf.LerpAngle(_yaw, t, 1f - Mathf.Exp(-18f * dt));
            Rotation = new Vector3(0, _yaw, 0);
        }

        // reload
        if (ReloadLeft > 0)
        {
            ReloadLeft -= dt;
            if (ReloadLeft <= 0)
            {
                int n = Mathf.Min(MagSize - Mag, Reserve);
                Mag += n; Reserve -= n;
                ReloadLeft = 0;
            }
        }
        else if (ReloadPressed && Mag < MagSize && Reserve > 0)
        {
            ReloadLeft = ReloadTime;
        }

        // fire
        W.FireCd -= dt;
        if (FireHeld && W.FireCd <= 0 && !Reloading)
        {
            if (Mag > 0) Fire();
            else if (Reserve > 0) ReloadLeft = ReloadTime;
        }

        // loot proximity
        var nearest = game.FindNearestLoot(GlobalPosition, PickupRange);
        if (NearbyLoot != null && (!IsInstanceValid(NearbyLoot) || NearbyLoot.Collected)) NearbyLoot = null; // online: freed by the server's answer
        if (nearest != NearbyLoot)
        {
            NearbyLoot?.SetHighlighted(false);
            NearbyLoot = nearest;
            NearbyLoot?.SetHighlighted(true);
        }
        if (InteractPressed && NearbyLoot != null)
        {
            var l = NearbyLoot;
            NearbyLoot = null; l.Pickup(this);
        }

        _visual.Animate(Mathf.Clamp(horiz.Length() / Speed, 0, 1), dt);
        ReloadPressed = false; InteractPressed = false;
    }

    void ReadInput()
    {
        var v = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        MoveInput = v;
        FireHeld = Input.IsActionPressed("fire");
        if (Input.IsActionJustPressed("reload")) ReloadPressed = true;
        if (Input.IsActionJustPressed("interact")) InteractPressed = true;
        if (Input.IsActionJustPressed("weapon_next")) WantWeapon = Weapons[(Slot + 1) % Weapons.Count].Def.Id;
        for (int i = 1; i <= 5; i++)
            if (Input.IsActionJustPressed($"weapon_{i}"))
                foreach (var w in Weapons) if (w.Def.Slot == i) WantWeapon = w.Def.Id;
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            var mouse = GetViewport().GetMousePosition();
            var from = cam.ProjectRayOrigin(mouse);
            var dir = cam.ProjectRayNormal(mouse);
            var plane = new Plane(Vector3.Up, 0.8f);
            var hit = plane.IntersectsRay(from, dir);
            if (hit.HasValue) AimPoint = hit.Value;
        }
    }

    void Fire()
    {
        var def = WeaponDef;
        W.FireCd = def.FireInterval;
        Mag--;
        var muzzle = _visual.Muzzle.GlobalPosition;
        var target = AimPoint; target.Y = muzzle.Y;
        var dir = target - muzzle;
        // if the aim point is behind/too close to the muzzle, shoot straight ahead
        var fwd = -GlobalTransform.Basis.Z;
        if (dir.LengthSquared() < 1f || dir.Normalized().Dot(fwd) < 0.3f) dir = fwd;
        dir = dir.Normalized();
        var rng = Game.Rng;
        if (def.Rocket)
        {
            var r = new Rocket { Velocity = (dir + Vector3.Up * 0.1f).Normalized() * def.BulletSpeed, Damage = def.Damage, FromPlayer = true };
            Game.I.AddChild(r);
            r.GlobalPosition = muzzle;
        }
        else
        {
            for (int i = 0; i < def.Pellets; i++)
            {
                var d = dir;
                if (def.SpreadDeg > 0)
                {
                    float s = Mathf.DegToRad(def.SpreadDeg);
                    d = d.Rotated(Vector3.Up, rng.RandfRange(-s, s));
                    d.Y += rng.RandfRange(-s, s) * 0.25f;
                    d = d.Normalized();
                }
                var b = new Bullet { Velocity = d * def.BulletSpeed, Damage = def.Damage, HitMask = Layers.World | Layers.Enemy, FromPlayer = true, Shooter = this, Weapon = def, Pierce = def.Pierce, MaxRange = def.MaxRange };
                Game.I.AddChild(b);
                b.GlobalPosition = muzzle;
            }
        }
        Fx.MuzzleFlash(Game.I, muzzle, dir, def);
        Game.I.Cam?.Kick(def.Kick);
        Game.I.OnPlayerShot(GlobalPosition);
    }

    /// Switch to an owned weapon (cancels a reload in progress).
    public void SwitchTo(WeaponId id)
    {
        for (int i = 0; i < Weapons.Count; i++)
            if (Weapons[i].Def.Id == id && i != Slot)
            {
                W.ReloadLeft = 0;
                Slot = i;
                _visual.SetWeapon(WeaponDef);
                Game.I?.Notify("weapon");
                return;
            }
    }

    public bool Has(WeaponId id) { foreach (var w in Weapons) if (w.Def.Id == id) return true; return false; }
    public WeaponState Get(WeaponId id) { foreach (var w in Weapons) if (w.Def.Id == id) return w; return null; }

    /// Picked up a weapon: a new one is equipped at once with two spare magazines; a duplicate adds a magazine.
    public void AddWeapon(WeaponId id)
    {
        var have = Get(id);
        if (have != null) { have.Reserve += have.Def.MagSize; return; }
        Weapons.Add(new WeaponState(WeaponDef.Get(id), 2));
        Weapons.Sort((a, b) => a.Def.Slot.CompareTo(b.Def.Slot));
        Slot = Weapons.FindIndex(w => w.Def.Id == WeaponDef.Id);   // keep the current one selected during the sort
        SwitchTo(id);
    }

    public void ApplySlow(float seconds) { SlowT = Mathf.Max(SlowT, seconds); _visual.Flash(new Color(0.6f, 0.9f, 1f), 0.4f); }

    public void Heal(int amount) { Hp = Mathf.Min(MaxHp, Hp + amount); }
    /// An ammo box refills the current weapon by its own box amount (rifle 24, shotgun 10, SMG 30, sniper 4, launcher 3).
    public void AddReserve(int amount) { Reserve += W.Def.AmmoBox; }

    public void TakeDamage(int amount, Vector3 fromPosition, bool fromPlayer)
    {
        if (Dead || fromPlayer) return;
        Hp -= amount;
        HurtFlash = 1f;
        _visual.Flash();
        Game.I.Cam?.Kick(0.25f);
        Game.I.Notify("player-hit");
        if (Hp <= 0)
        {
            Hp = 0;
            Dead = true;
            _visual.Fall();
            CollisionLayer = 0;
            Game.I.OnPlayerDied();
        }
    }
}
