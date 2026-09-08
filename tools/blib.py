"""Shared Blender helpers for the headless model scripts (make_duck.py builds its own; props use these)."""
import bpy, bmesh, math

_mats = {}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def mat(name, rgb, rough=0.85, metal=0.0, emit=None, emit_strength=1.0):
    """rgb is authored in sRGB (what you see); it is converted to the linear values Blender/glTF store."""
    if name in _mats: return _mats[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*[srgb_to_linear(c) for c in rgb], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    _mats[name] = m
    return m

def _finish(obj, name, m, smooth):
    obj.name = name
    if m is not None: obj.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth() if smooth else bpy.ops.object.shade_flat()
    return obj

def _apply(o, rot, scale=None):
    o.rotation_euler = [math.radians(a) for a in rot]
    if scale is not None: o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

def sphere(name, loc, scale, m, segs=12, rings=8, smooth=True, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=0.5, location=loc)
    o = bpy.context.object; _apply(o, rot, scale)
    return _finish(o, name, m, smooth)

def ico(name, loc, scale, m, subdiv=1, smooth=False, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=0.5, location=loc)
    o = bpy.context.object; _apply(o, rot, scale)
    return _finish(o, name, m, smooth)

def box(name, loc, size, m, bevel=0.0, rot=(0, 0, 0), smooth=False, segments=1):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.object; _apply(o, rot, size)
    if bevel > 0:
        b = o.modifiers.new("Bevel", 'BEVEL'); b.width = bevel; b.segments = segments; b.limit_method = 'ANGLE'
    return _finish(o, name, m, smooth)

def cyl(name, loc, r, h, m, verts=12, rot=(0, 0, 0), smooth=True, r2=None):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc)
    else: bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=loc)
    o = bpy.context.object; _apply(o, rot)
    return _finish(o, name, m, smooth)

def strut(name, p0, p1, r, m, verts=6):
    """Cylinder from point p0 to p1 (tripod legs, braces)."""
    from mathutils import Vector
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length, location=(a + b) / 2)
    o = bpy.context.object
    o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return _finish(o, name, m, True)

def cone(name, loc, r, h, m, verts=8, smooth=False, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=0.0, depth=h, location=loc)
    o = bpy.context.object; _apply(o, rot)
    return _finish(o, name, m, smooth)

def torus(name, loc, major, minor, m, rot=(0, 0, 0), scale=(1, 1, 1), segs=(14, 6)):
    bpy.ops.mesh.primitive_torus_add(major_segments=segs[0], minor_segments=segs[1], major_radius=major, minor_radius=minor, location=loc)
    o = bpy.context.object; _apply(o, rot, scale)
    return _finish(o, name, m, True)

def prism(name, pts2d, z0, thickness, m, loc=(0, 0, 0), smooth=False, axis='z'):
    """Flat polygon (pairs) extruded by thickness. axis='z': polygon in XY, extruded up. axis='x': polygon in YZ (pairs are y,z), extruded along +X."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    if axis == 'z': vs = [bm.verts.new((x, y, 0)) for x, y in pts2d]
    else: vs = [bm.verts.new((0, y, z)) for y, z in pts2d]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    up = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, thickness) if axis == 'z' else (thickness, 0, 0), verts=up)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.location = (loc[0], loc[1], loc[2] + z0)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.ops.object.transform_apply(location=True)
    return _finish(o, name, m, smooth)

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.object; o.name = name
    return o

def jitter_verts(obj, amount, seed=1):
    import random
    rnd = random.Random(seed)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    for v in bm.verts:
        v.co.x += rnd.uniform(-amount, amount); v.co.y += rnd.uniform(-amount, amount); v.co.z += rnd.uniform(-amount, amount)
    bm.to_mesh(obj.data); bm.free()

def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_yup=True,
                              export_materials='EXPORT', export_normals=True, export_texcoords=False,
                              export_animations=False, export_skins=False)
    print(f"[blib] wrote {path}")
