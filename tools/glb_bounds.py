"""Print the world-space AABB (metres, glTF Y-up) of GLB files, walking node transforms:
python3 tools/glb_bounds.py assets/models/kenney/*/*.glb"""
import struct, json, sys, math

def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]

def node_matrix(n):
    if 'matrix' in n:
        m = n['matrix']; return [[m[c * 4 + r] for c in range(4)] for r in range(4)]
    t = n.get('translation', [0, 0, 0]); q = n.get('rotation', [0, 0, 0, 1]); s = n.get('scale', [1, 1, 1])
    x, y, z, w = q
    R = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    M = [[R[r][c] * s[c] for c in range(3)] + [t[r]] for r in range(3)] + [[0, 0, 0, 1]]
    return M

def bounds(path):
    d = open(path, 'rb').read()
    ln = struct.unpack('<I', d[12:16])[0]; js = json.loads(d[20:20 + ln])
    lo = [math.inf] * 3; hi = [-math.inf] * 3
    def walk(ni, parent):
        n = js['nodes'][ni]; M = mat_mul(parent, node_matrix(n))
        if 'mesh' in n:
            for p in js['meshes'][n['mesh']]['primitives']:
                acc = js['accessors'][p['attributes']['POSITION']]
                for cx in (acc['min'][0], acc['max'][0]):
                    for cy in (acc['min'][1], acc['max'][1]):
                        for cz in (acc['min'][2], acc['max'][2]):
                            v = [sum(M[r][c] * (cx, cy, cz, 1)[c] for c in range(4)) for r in range(3)]
                            for i in range(3): lo[i] = min(lo[i], v[i]); hi[i] = max(hi[i], v[i])
        for c in n.get('children', []): walk(c, M)
    I = [[1 if i == j else 0 for j in range(4)] for i in range(4)]
    for s in js['scenes'][0]['nodes']: walk(s, I)
    return lo, hi

for f in sys.argv[1:]:
    lo, hi = bounds(f)
    size = [hi[i] - lo[i] for i in range(3)]
    print(f"{f.split('/')[-1]:32s} size x={size[0]:.2f} y={size[1]:.2f} z={size[2]:.2f}   min y={lo[1]:.2f}  centre x={(lo[0]+hi[0])/2:.2f} z={(lo[2]+hi[2])/2:.2f}")
