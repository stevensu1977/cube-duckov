"""Print node names, triangle count and materials of GLB files: python3 tools/glb_stats.py assets/models/*.glb"""
import struct, json, sys
for f in sys.argv[1:]:
    d = open(f, 'rb').read()
    ln = struct.unpack('<I', d[12:16])[0]; js = json.loads(d[20:20 + ln])
    tris = sum(js['accessors'][p['indices']]['count'] // 3 for m in js['meshes'] for p in m['primitives'])
    print(f"{f}: {tris} tris, {len(d)//1024} KB, nodes={[n['name'] for n in js['nodes']]}, mats={[m['name'] for m in js.get('materials', [])]}")
