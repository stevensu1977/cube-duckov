"""Build blocky (Minecraft-style) ducks — player + soldier variant — and export GLBs with materials.

Run:  $BLENDER_BIN -b --python tools/make_duck.py
Out:  assets/models/duck_player.glb, assets/models/duck_soldier.glb

Everything is an axis-aligned box on a rough 1/8 m grid, flat shaded, no bevels — the pixel-texel look is added at
runtime by scripts/Blocky.cs (nearest-filtered triplanar noise on every material).
Blender axes: duck faces +Y, Z up.  glTF export converts to Godot's Y-up / -Z forward.
Parts are separate objects named Body, Head, WingL, WingR, FootL, FootR, Gun, (Helmet, Vest | Backpack, Bandana)
so Godot can animate them procedurally (scripts/DuckVisual.cs).  Origin is on the ground between the feet.
Material names Body / BodyDark / Accent / AccentDark are re-tinted per instance in Godot.
"""
import bpy, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blib
from blib import mat, box, join

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "models")
os.makedirs(OUT, exist_ok=True)

def foot(name, x, beak_m):
    sole = box(name, (x, 0.1, 0.03), (0.3, 0.42, 0.06), beak_m)
    toe1 = box(name + "_t", (x, 0.34, 0.03), (0.16, 0.08, 0.06), beak_m)
    leg = box(name + "_leg", (x, -0.02, 0.17), (0.1, 0.1, 0.28), beak_m)
    return join([sole, toe1, leg], name)

def build_duck(soldier, body_rgb, accent_rgb):
    blib.reset()
    body_m = mat("Body", body_rgb, 0.8)
    dark_m = mat("BodyDark", tuple(c * 0.8 for c in body_rgb), 0.85)
    beak_m = mat("Beak", (1.0, 0.55, 0.12), 0.7)
    beak_dark_m = mat("BeakDark", (0.9, 0.42, 0.08), 0.7)
    white_m = mat("EyeWhite", (0.97, 0.97, 0.97), 0.5)
    black_m = mat("EyeBlack", (0.03, 0.03, 0.04), 0.4)
    gun_m = mat("Gun", (0.14, 0.15, 0.18), 0.5, 0.4)
    gunwood_m = mat("GunWood", (0.42, 0.27, 0.14), 0.75)
    accent_m = mat("Accent", accent_rgb, 0.9)
    accent_dark_m = mat("AccentDark", tuple(c * 0.7 for c in accent_rgb), 0.9)
    strap_m = mat("Strap", (0.12, 0.1, 0.09), 0.8)
    red_m = mat("Red", (0.62, 0.08, 0.08), 0.75)

    parts = []
    # --- body: one block plus a raised tail block
    body = box("Body", (0, -0.05, 0.64), (0.9, 1.15, 0.72), body_m)
    tail = box("Tail", (0, -0.72, 0.88), (0.36, 0.3, 0.22), dark_m)
    chest = box("Chest", (0, 0.5, 0.52), (0.62, 0.2, 0.4), body_m)
    parts.append(join([body, tail, chest], "Body"))

    # --- head: cube, flat beak, square eyes on the front face
    head = box("Head", (0, 0.3, 1.32), (0.7, 0.7, 0.64), body_m)
    beak = box("Beak", (0, 0.8, 1.2), (0.34, 0.36, 0.14), beak_m)
    beak_low = box("BeakLow", (0, 0.74, 1.1), (0.28, 0.26, 0.08), beak_dark_m)
    eyes = []
    for sx in (-1, 1):
        eyes.append(box("EyeW", (sx * 0.2, 0.655, 1.42), (0.18, 0.02, 0.18), white_m))
        eyes.append(box("EyeB", (sx * 0.22, 0.67, 1.42), (0.09, 0.02, 0.09), black_m))
    parts.append(join([head, beak, beak_low] + eyes, "Head"))

    # --- wings: flat slabs on the flanks
    for nm, sx in (("WingL", -1), ("WingR", 1)):
        parts.append(box(nm, (sx * 0.52, -0.12, 0.66), (0.14, 0.78, 0.44), dark_m))

    # --- feet
    parts.append(foot("FootL", -0.22, beak_m)); parts.append(foot("FootR", 0.22, beak_m))

    # --- gun (held on the right, pointing +Y); muzzle at (0.36, 0.95, 0.82) -> Godot (0.36, 0.82, -0.95)
    recv = box("GunBody", (0.36, 0.2, 0.8), (0.12, 0.56, 0.14), gun_m)
    barrel = box("Barrel", (0.36, 0.72, 0.82), (0.07, 0.5, 0.07), gun_m)
    barrel2 = box("Barrel2", (0.36, 0.55, 0.82), (0.1, 0.2, 0.1), gun_m)
    magz = box("Mag", (0.36, 0.05, 0.66), (0.08, 0.14, 0.2), gun_m)
    grip = box("Grip", (0.36, -0.15, 0.67), (0.08, 0.1, 0.18), gunwood_m)
    stock = box("Stock", (0.36, -0.4, 0.78), (0.1, 0.32, 0.12), gunwood_m)
    sight = box("Sight", (0.36, 0.3, 0.9), (0.04, 0.12, 0.06), gun_m)
    parts.append(join([recv, barrel, barrel2, magz, grip, stock, sight], "Gun"))

    if soldier:
        helm = box("Helmet", (0, 0.3, 1.66), (0.8, 0.8, 0.3), accent_m)
        brim = box("Brim", (0, 0.3, 1.53), (0.9, 0.9, 0.06), accent_m)
        strap = box("ChinStrap", (0, 0.3, 1.2), (0.76, 0.04, 0.04), strap_m)
        strap2 = box("ChinStrap2", (0, 0.66, 1.3), (0.04, 0.02, 0.3), strap_m)
        parts.append(join([helm, brim, strap], "Helmet"))
        front = box("Vest", (0, 0.5, 0.64), (0.8, 0.14, 0.56), accent_m)
        back = box("VestBack", (0, -0.62, 0.68), (0.8, 0.1, 0.5), accent_m)
        collar = box("Collar", (0, 0.05, 1.02), (0.5, 0.6, 0.08), accent_m)
        pouches = [box("Pouch", (sx * 0.2, 0.62, 0.56), (0.18, 0.12, 0.18), accent_dark_m) for sx in (-1, 1)]
        belt = box("Belt", (0, 0.5, 0.36), (0.82, 0.16, 0.08), accent_dark_m)
        parts.append(join([front, back, collar, belt] + pouches, "Vest"))
    else:
        pack = box("Backpack", (0, -0.66, 0.86), (0.54, 0.28, 0.5), accent_m)
        flap = box("Flap", (0, -0.66, 1.04), (0.52, 0.3, 0.14), accent_dark_m)
        roll = box("Roll", (0, -0.6, 0.56), (0.52, 0.18, 0.18), mat("Roll", (0.35, 0.45, 0.35), 0.9))
        s1 = box("StrapL", (-0.25, -0.1, 1.01), (0.08, 0.7, 0.04), strap_m)
        s2 = box("StrapR", (0.25, -0.1, 1.01), (0.08, 0.7, 0.04), strap_m)
        parts.append(join([pack, flap, roll, s1, s2], "Backpack"))
        band = box("Bandana", (0, 0.3, 1.56), (0.74, 0.74, 0.1), red_m)
        knot = box("Knot", (0, -0.1, 1.56), (0.12, 0.1, 0.1), red_m)
        tail1 = box("Tail1", (0.08, -0.22, 1.47), (0.07, 0.2, 0.04), red_m)
        tail2 = box("Tail2", (-0.07, -0.24, 1.5), (0.07, 0.18, 0.04), red_m)
        parts.append(join([band, knot, tail1, tail2], "Bandana"))

    name = "duck_soldier" if soldier else "duck_player"
    blib.export(parts, os.path.join(OUT, name + ".glb"))
    print(f"[make_duck] wrote {name}: {len(parts)} parts")

build_duck(False, (1.0, 0.82, 0.16), (0.36, 0.26, 0.16))
build_duck(True, (0.56, 0.6, 0.42), (0.28, 0.34, 0.24))
