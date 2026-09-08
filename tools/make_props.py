"""Build the blocky (Minecraft-style) props and loot models. Run: $BLENDER_BIN -b --python tools/make_props.py
Out: assets/models/props/*.glb  (origin at ground centre, Z up in Blender -> Y up in Godot)

Axis-aligned boxes only, flat shaded. Footprints (AABBs) match the previous low-poly set so the runtime colliders and
the baked navmesh — and therefore the deterministic scripted raid — do not change."""
import bpy, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib
from blib import mat, box, join

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "models", "props")
os.makedirs(OUT, exist_ok=True)

def build(name, fn):
    blib.reset()
    parts = fn()
    blib.export(parts, os.path.join(OUT, name + ".glb"))

# ---------- palette
WOOD = (0.72, 0.5, 0.27); WOOD_DARK = (0.45, 0.29, 0.14); WOOD_PALE = (0.8, 0.62, 0.38)
METAL = (0.35, 0.37, 0.4); METAL_DARK = (0.18, 0.19, 0.22)
CANVAS = (0.45, 0.5, 0.36); CANVAS_DARK = (0.34, 0.38, 0.27)
SAND = (0.78, 0.7, 0.5); SAND_DARK = (0.66, 0.58, 0.4)
LEAF = (0.3, 0.52, 0.24); LEAF_B = (0.36, 0.58, 0.27); TRUNK = (0.4, 0.27, 0.15)

def cube(name, x, y, z, s, m):
    return box(name, (x, y, z), (s, s, s), m)

def crate():
    m_wood = mat("CrateWood", WOOD, 0.9); m_dark = mat("CrateFrame", WOOD_DARK, 0.9)
    s = 1.3; t = 0.1
    parts = [box("Panel", (0, 0, s / 2), (s - 0.04, s - 0.04, s - 0.04), m_wood)]
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(box("E", (sx * (s / 2 - t / 2), sy * (s / 2 - t / 2), s / 2), (t, t, s), m_dark))
        for sz in (0, 1):
            parts.append(box("E", (sx * (s / 2 - t / 2), 0, sz * (s - t) + t / 2), (t, s, t), m_dark))
            parts.append(box("E", (0, sx * (s / 2 - t / 2), sz * (s - t) + t / 2), (s, t, t), m_dark))
    # centre cross-planks instead of diagonal braces
    for sx in (-1, 1):
        parts.append(box("B", (sx * (s / 2 - 0.02), 0, s / 2), (0.04, t, s - 0.2), m_dark))
        parts.append(box("B", (0, sx * (s / 2 - 0.02), s / 2), (t, 0.04, s - 0.2), m_dark))
    return [join(parts, "Crate")]

def barrel():
    m_body = mat("BarrelBody", (0.62, 0.22, 0.16), 0.6, 0.3)
    m_band = mat("BarrelBand", METAL, 0.5, 0.6)
    w, h = 1.0, 1.2   # same footprint as the old r=0.5 cylinder
    parts = [box("Body", (0, 0, h / 2), (w - 0.08, w - 0.08, h), m_body)]
    for z in (0.3, 0.9):
        parts.append(box("Band", (0, 0, z), (w, w, 0.1), m_band))
    parts.append(box("Rim", (0, 0, h - 0.03), (w, w, 0.06), m_band))
    parts.append(box("Lid", (0, 0, h + 0.01), (w - 0.16, w - 0.16, 0.02), mat("BarrelLid", (0.4, 0.15, 0.1), 0.6)))
    parts.append(box("Cap", (0.24, 0.12, h + 0.04), (0.14, 0.14, 0.06), m_band))
    return [join(parts, "Barrel")]

def sandbags():
    m1 = mat("SandbagA", SAND, 0.95); m2 = mat("SandbagB", SAND_DARK, 0.95)
    parts = []; i = 0
    rows = [(0.0, 5, 0), (0.28, 4, 0.2), (0.56, 3, 0.4)]
    for z, n, x0 in rows:
        for k in range(n):
            x = -0.8 + x0 + k * 0.42 + 0.21
            for y in (-0.24, 0.24):
                parts.append(box("Bag", (x, y, z + 0.14), (0.42, 0.4, 0.28), m1 if (i % 2 == 0) else m2)); i += 1
    return [join(parts, "Sandbags")]

def fence():
    m_post = mat("FencePost", WOOD_DARK, 0.9); m_rail = mat("FenceRail", WOOD_PALE, 0.9)
    L, H = 2.4, 1.15
    parts = []
    for x in (-L / 2 + 0.08, L / 2 - 0.08):
        parts.append(box("Post", (x, 0, H / 2), (0.16, 0.16, H), m_post))
        parts.append(box("Top", (x, 0, H + 0.05), (0.1, 0.1, 0.1), m_post))
    for z in (0.42, 0.88):
        parts.append(box("Rail", (0, 0.06, z), (L, 0.06, 0.12), m_rail))
    for k in range(7):
        x = -L / 2 + 0.3 + k * (L - 0.6) / 6
        hk = 1.02 + 0.08 * ((k * 7) % 3 - 1)
        parts.append(box("Picket", (x, 0.12, hk / 2 + 0.04), (0.12, 0.04, hk), m_rail))
    return [join(parts, "Fence")]

def pallet():
    m = mat("PalletWood", WOOD_PALE, 0.95); md = mat("PalletDark", WOOD_DARK, 0.95)
    parts = [box("Runner", (0, y, 0.05), (1.2, 0.1, 0.1), md) for y in (-0.42, 0, 0.42)]
    for k in range(6):
        parts.append(box("Plank", (-0.55 + k * 0.22, 0, 0.125), (0.14, 1.0, 0.05), m))
    return [join(parts, "Pallet")]

def tent():
    """Stepped canvas roof (stairs) along X: same 3.2 x 2.6 x 1.9 envelope as the old prism."""
    m_c = mat("TentCanvas", CANVAS, 0.95); m_d = mat("TentDark", CANVAS_DARK, 0.95); m_p = mat("TentPole", WOOD_DARK, 0.9)
    L, W, H = 3.2, 2.6, 1.9
    steps = 5
    parts = []
    for i in range(steps):
        w = W * (1 - i / steps)                   # width shrinks toward the ridge
        z0 = H * i / steps
        parts.append(box("Step", (0, 0, z0 + H / steps / 2), (L, w, H / steps), m_c))
    parts.append(box("Ridge", (0, 0, H + 0.03), (L + 0.3, 0.12, 0.12), m_p))
    parts.append(box("Flap", (L / 2 - 0.02, 0, 0.6), (0.06, 1.0, 1.2), m_d))
    for x in (-L / 2 - 0.5, L / 2 + 0.5):
        for y in (-W / 2 - 0.4, W / 2 + 0.4):
            parts.append(box("Peg", (x, y, 0.1), (0.1, 0.1, 0.25), m_p))
    return [join(parts, "Tent")]

def beacon():
    m_metal = mat("BeaconMetal", METAL_DARK, 0.5, 0.6); m_box = mat("BeaconBox", (0.2, 0.42, 0.28), 0.7)
    m_light = mat("BeaconLight", (0.5, 1.0, 0.6), 0.3, emit=(0.25, 1.0, 0.45), emit_strength=1.6)
    m_stripe = mat("BeaconStripe", (0.95, 0.85, 0.3), 0.6)
    parts = []
    for k in range(3):   # three vertical legs on the old tripod footprint
        a = math.radians(k * 120 + 30)
        parts.append(box("Leg", (math.cos(a) * 0.8, math.sin(a) * 0.8, 0.62), (0.12, 0.12, 1.25), m_metal))
        parts.append(box("Foot", (math.cos(a) * 0.9, math.sin(a) * 0.9, 0.04), (0.2, 0.2, 0.08), m_metal))
        parts.append(box("Brace", (math.cos(a) * 0.45, math.sin(a) * 0.45, 1.2), (0.9 if abs(math.cos(a)) > 0.3 else 0.12, 0.9 if abs(math.sin(a)) > 0.3 else 0.12, 0.08), m_metal))
    parts.append(box("Unit", (0, 0, 1.45), (0.7, 0.55, 0.55), m_box))
    parts.append(box("Stripe", (0, 0, 1.45), (0.72, 0.57, 0.1), m_stripe))
    parts.append(box("Mast", (0, 0, 2.2), (0.08, 0.08, 1.1), m_metal))
    parts.append(box("Dish", (0.14, 0, 2.05), (0.24, 0.3, 0.3), m_metal))
    parts.append(box("Lamp", (0, 0, 2.85), (0.32, 0.32, 0.32), m_light))
    parts.append(box("LampBase", (0, 0, 2.68), (0.22, 0.22, 0.08), m_metal))
    return [join(parts, "Beacon")]

def tree_round():
    """Minecraft oak: one-block trunk, leaf canopy of cubes with the corners knocked off."""
    m_trunk = mat("Trunk", TRUNK, 0.95); m_leaf = mat("LeafA", LEAF, 0.95); m_leaf2 = mat("LeafB", LEAF_B, 0.95)
    parts = [box("Trunk", (0, 0, 1.6), (0.5, 0.5, 3.2), m_trunk)]
    s = 0.9
    for (i, j, k) in [(i, j, k) for i in range(-1, 2) for j in range(-1, 2) for k in range(0, 3)]:
        if k == 2 and (abs(i) + abs(j) > 1): continue          # rounded top
        if k == 0 and abs(i) + abs(j) == 2 and (i + j) % 2 == 0: continue   # some corners missing
        m = m_leaf if (i + j + k) % 2 == 0 else m_leaf2
        parts.append(cube("Leaf", i * s, j * s, 2.4 + k * s + s / 2, s, m))
    parts.append(cube("Leaf", 0, 1.8, 3.3, s, m_leaf2)); parts.append(cube("Leaf", -1.8, 0, 3.3, s, m_leaf))
    return [join(parts, "TreeRound")]

def tree_pine():
    m_trunk = mat("Trunk", TRUNK, 0.95); m_leaf = mat("Pine", (0.2, 0.42, 0.28), 0.95)
    parts = [box("Trunk", (0, 0, 0.8), (0.4, 0.4, 1.6), m_trunk)]
    for z, w in [(1.6, 3.0), (2.4, 2.4), (3.2, 1.8), (4.0, 1.2), (4.7, 0.6)]:
        parts.append(box("Tier", (0, 0, z + 0.4), (w, w, 0.8), m_leaf))
    return [join(parts, "TreePine")]

def bush():
    m1 = mat("BushA", (0.33, 0.52, 0.25), 0.95); m2 = mat("BushB", (0.28, 0.46, 0.22), 0.95)
    parts = [box("Blob", (0, 0, 0.5), (1.1, 1.1, 1.0), m1), box("Blob", (0.5, 0.25, 0.35), (0.8, 0.8, 0.7), m2),
             box("Blob", (-0.45, -0.3, 0.38), (0.8, 0.8, 0.76), m2), box("Blob", (0.1, -0.5, 0.3), (0.6, 0.6, 0.6), m1)]
    return [join(parts, "Bush")]

def rock():
    m = mat("Rock", (0.5, 0.5, 0.48), 0.95)
    parts = [box("Rock", (0, 0, 0.3), (1.1, 0.8, 0.6), m), box("Rock2", (0.2, 0.1, 0.55), (0.6, 0.5, 0.3), m)]
    return [join(parts, "Rock")]

def loot_cash():
    m_green = mat("Cash", (0.24, 0.6, 0.32), 0.7); m_band = mat("CashBand", (0.95, 0.9, 0.75), 0.8)
    parts = [box("Stack", (0, 0, 0.1), (0.55, 0.28, 0.2), m_green), box("Stack2", (0.06, 0.03, 0.29), (0.5, 0.26, 0.16), m_green),
             box("Band", (0, 0, 0.1), (0.14, 0.3, 0.21), m_band), box("Band2", (0.06, 0.03, 0.29), (0.13, 0.28, 0.17), m_band)]
    return [join(parts, "Cash")]

def loot_medkit():
    m_w = mat("MedWhite", (0.95, 0.95, 0.93), 0.5); m_r = mat("MedRed", (0.85, 0.12, 0.12), 0.5); m_h = mat("MedHandle", (0.25, 0.25, 0.28), 0.6)
    parts = [box("Case", (0, 0, 0.18), (0.62, 0.42, 0.36), m_w),
             box("Cross1", (0, 0, 0.37), (0.28, 0.09, 0.02), m_r), box("Cross2", (0, 0, 0.37), (0.09, 0.28, 0.02), m_r),
             box("Cross3", (0, -0.215, 0.2), (0.2, 0.02, 0.07), m_r), box("Cross4", (0, -0.215, 0.2), (0.07, 0.02, 0.2), m_r),
             box("Handle", (0, 0.24, 0.3), (0.26, 0.05, 0.05), m_h)]
    return [join(parts, "Medkit")]

def loot_ammo():
    m_o = mat("AmmoOlive", (0.36, 0.42, 0.24), 0.8); m_y = mat("AmmoYellow", (0.9, 0.75, 0.2), 0.6); m_m = mat("AmmoMetal", METAL_DARK, 0.5, 0.5)
    parts = [box("Can", (0, 0, 0.17), (0.56, 0.38, 0.34), m_o), box("Lid", (0, 0, 0.36), (0.58, 0.4, 0.05), m_o),
             box("Stripe", (0, 0, 0.2), (0.57, 0.39, 0.06), m_y), box("Latch", (0, -0.2, 0.3), (0.12, 0.03, 0.08), m_m),
             box("Handle", (0, 0, 0.41), (0.3, 0.05, 0.04), m_m)]
    return [join(parts, "Ammo")]

def loot_gold():
    m = mat("Gold", (1.0, 0.78, 0.2), 0.35, 0.8)
    return [join([box("Bar", (0, 0, 0.1), (0.7, 0.3, 0.2), m), box("Bar", (0.1, 0.05, 0.3), (0.7, 0.3, 0.2), m)], "Gold")]

for name, fn in [("crate", crate), ("barrel", barrel), ("sandbags", sandbags), ("fence", fence), ("pallet", pallet),
                 ("tent", tent), ("beacon", beacon), ("tree_round", tree_round), ("tree_pine", tree_pine), ("bush", bush), ("rock", rock),
                 ("loot_cash", loot_cash), ("loot_medkit", loot_medkit), ("loot_ammo", loot_ammo), ("loot_gold", loot_gold)]:
    build(name, fn)
