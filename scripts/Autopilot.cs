using Godot;
using System.Collections.Generic;
namespace Duckov;

/// Scripted driver for headless proof captures. Feeds intents to the Player exactly like a human would.
public partial class Autopilot : Node
{
    public string Scenario = "raid";
    public static bool Finished;
    public static string Status = "";

    abstract class Step { public string Name; public abstract bool Tick(Autopilot ap, float dt); }

    class MoveTo : Step
    {
        Vector3 _goal; float _tol; Vector3[] _path; int _idx; float _repath, _timeout = 25f;
        public MoveTo(float x, float z, float tol = 0.9f) { _goal = new Vector3(x, 0, z); _tol = tol; Name = $"move({x},{z})"; }
        public override bool Tick(Autopilot ap, float dt)
        {
            var p = ap._player;
            _timeout -= dt; _repath -= dt;
            var flat = _goal - p.GlobalPosition; flat.Y = 0;
            if (flat.Length() < _tol || _timeout <= 0) { p.MoveInput = Vector2.Zero; return true; }
            if (_path == null || _repath <= 0) { _path = Game.I.Arena.GetPath(p.GlobalPosition, _goal); _idx = 0; _repath = 0.7f; }
            while (_idx < _path.Length - 1 && (new Vector3(_path[_idx].X - p.GlobalPosition.X, 0, _path[_idx].Z - p.GlobalPosition.Z)).Length() < 0.6f) _idx++;
            var next = _idx < _path.Length ? _path[_idx] : _goal;
            var d = next - p.GlobalPosition; d.Y = 0;
            if (d.Length() > 0.01f) { d = d.Normalized(); p.MoveInput = new Vector2(d.X, d.Z); ap._moveDir = d; }
            return false;
        }
    }

    class Pickup : Step
    {
        float _t;
        public Pickup() { Name = "pickup"; }
        public override bool Tick(Autopilot ap, float dt)
        {
            _t += dt;
            var p = ap._player;
            p.MoveInput = Vector2.Zero;
            if (p.NearbyLoot != null && _t > 0.15f) p.InteractPressed = true;
            return _t > 0.6f || (p.NearbyLoot == null && _t > 0.3f);
        }
    }

    class Wait : Step
    {
        float _t;
        public Wait(float s) { _t = s; Name = $"wait({s})"; }
        public override bool Tick(Autopilot ap, float dt) { ap._player.MoveInput = Vector2.Zero; _t -= dt; return _t <= 0; }
    }

    class WaitForState : Step
    {
        Game.State _s; float _timeout;
        public WaitForState(Game.State s, float timeout) { _s = s; _timeout = timeout; Name = $"waitfor({s})"; }
        public override bool Tick(Autopilot ap, float dt) { _timeout -= dt; if (ap._player != null) ap._player.MoveInput = Vector2.Zero; return Game.I.Current == _s || _timeout <= 0; }
    }

    class Restart : Step
    {
        public Restart() { Name = "restart"; }
        public override bool Tick(Autopilot ap, float dt) { Game.I.Restart(); ap._done = true; Finished = true; return true; }
    }

    readonly Queue<Step> _steps = new();
    Player _player;
    Vector3 _moveDir = new(0, 0, -1);
    bool _combat = true;
    bool _done;
    float _reaction;

    public override void _Ready()
    {
        _player = Game.I.Player;
        switch (Scenario)
        {
            case "kia":
                _combat = false;
                _steps.Enqueue(new MoveTo(-22, 14)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(-11, 11)); _steps.Enqueue(new MoveTo(0, 11)); _steps.Enqueue(new MoveTo(0, 0, 1.2f));
                _steps.Enqueue(new WaitForState(Game.State.Kia, 40f));
                _steps.Enqueue(new Wait(2.5f));
                _steps.Enqueue(new Restart());
                break;
            default: // raid — same loot run as before, plus the four guns lying on the route
                _steps.Enqueue(new MoveTo(-23.5f, 19.5f)); _steps.Enqueue(new Pickup());                       // Frost SR-3M
                _steps.Enqueue(new MoveTo(-22, 14)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(-24, 2)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(-12.5f, -15)); _steps.Enqueue(new MoveTo(-17, -12)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(-19.5f, -14.5f)); _steps.Enqueue(new Pickup());                      // Saiga-12K
                _steps.Enqueue(new MoveTo(-22, -17)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(-12.5f, -15)); _steps.Enqueue(new MoveTo(-9.3f, -22.5f)); _steps.Enqueue(new Pickup());   // Firework Gun
                _steps.Enqueue(new MoveTo(-4, -26)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(0, -11)); _steps.Enqueue(new MoveTo(0, 0, 1.2f)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(5, 4, 1.2f)); _steps.Enqueue(new Pickup());                          // TS-128
                _steps.Enqueue(new MoveTo(5, -4)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(10.5f, 0)); _steps.Enqueue(new MoveTo(14, -27)); _steps.Enqueue(new Pickup());
                _steps.Enqueue(new MoveTo(24, -24, 1.0f));
                _steps.Enqueue(new WaitForState(Game.State.Extracted, 12f));
                _steps.Enqueue(new Wait(2.5f));
                _steps.Enqueue(new Restart());
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_done) { Finished = true; return; }
        if (_player == null || !IsInstanceValid(_player)) return;
        if (_steps.Count == 0) { _done = true; Finished = true; return; }

        var step = _steps.Peek();
        Status = step.Name;
        if (step.Tick(this, dt)) _steps.Dequeue();

        // combat layer: aim and shoot at the closest visible enemy, otherwise look where we're going
        Enemy target = _combat ? FindVisibleEnemy() : null;
        if (target != null)
        {
            _reaction += dt;
            var aim = target.GlobalPosition + new Vector3(0, 0.8f, 0) + target.Velocity * 0.08f;
            _player.AimPoint = aim;
            ChooseWeapon(target.GlobalPosition.DistanceTo(_player.GlobalPosition));
            _player.FireHeld = _reaction > 0.45f && !_player.Reloading;
        }
        else
        {
            _reaction = 0;
            _player.FireHeld = false;
            _player.AimPoint = _player.GlobalPosition + _moveDir * 6f + new Vector3(0, 0.8f, 0);
            if (_player.Mag < _player.MagSize / 2 && _player.Reserve > 0 && !_player.Reloading) _player.ReloadPressed = true;
        }
    }

    /// Pick the gun for the range: shotgun up close, firework shells at mid range, the sniper far out, the ice SMG in
    /// between, rifle as the fallback; never a gun without ammo.
    void ChooseWeapon(float dist)
    {
        bool Ok(WeaponId id) { var w = _player.Get(id); return w != null && (w.Mag > 0 || w.Reserve > 0); }
        WeaponId want = WeaponId.Rifle;
        if (dist < 8f && Ok(WeaponId.Shotgun)) want = WeaponId.Shotgun;
        else if (dist > 9f && dist < 15f && Ok(WeaponId.Firework) && _player.Get(WeaponId.Firework).Mag > 0) want = WeaponId.Firework;
        else if (dist >= 12f && Ok(WeaponId.ElecSniper)) want = WeaponId.ElecSniper;
        else if (dist < 12f && Ok(WeaponId.IceSmg)) want = WeaponId.IceSmg;
        else if (!Ok(WeaponId.Rifle)) foreach (var w in _player.Weapons) if (w.Mag > 0 || w.Reserve > 0) { want = w.Def.Id; break; }
        if (want != _player.WeaponDef.Id) _player.WantWeapon = want;
    }

    Enemy FindVisibleEnemy()
    {
        Enemy best = null; float bestD = 17f;
        var eye = _player.GlobalPosition + new Vector3(0, 1.0f, 0);
        var space = _player.GetWorld3D().DirectSpaceState;
        foreach (var e in Game.I.Enemies)
        {
            if (!IsInstanceValid(e) || e.Dead) continue;
            float d = e.GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (d >= bestD) continue;
            var q = PhysicsRayQueryParameters3D.Create(eye, e.GlobalPosition + new Vector3(0, 0.9f, 0), Layers.World);
            if (space.IntersectRay(q).Count == 0) { best = e; bestD = d; }
        }
        return best;
    }
}
