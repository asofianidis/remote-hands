"""
Render contact sheets of the exported models (Workbench engine, material colours), to eyeball them.

    blender --background --factory-startup --python Tools/blender/preview.py -- <models_dir> <out_dir> [model ...]
"""
import bpy, math, os, sys
from mathutils import Vector

ARGS = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = ARGS[0], ARGS[1]
ONLY = ARGS[2:]
os.makedirs(OUT, exist_ok=True)


def render(names, path, cols=4):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = False
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
    scene.world = bpy.data.worlds.new('w')
    scene.world.color = (0.18, 0.2, 0.23)
    cell = 0
    rows = math.ceil(len(names) / cols)
    for n in names:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, n + '.fbx'))
        objs = [o for o in bpy.data.objects if o not in before]
        lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
        for o in objs:
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        size = max((hi - lo).x, (hi - lo).y, (hi - lo).z, 1e-3)
        k = 1.0 / size
        centre = (lo + hi) / 2
        cx, cy = cell % cols, cell // cols
        for o in objs:
            if o.parent is None:
                o.location = (o.location - centre) * k + Vector((cx * 1.4, 0, -cy * 1.4))
                o.scale = o.scale * k
        bpy.ops.object.text_add(location=(cx * 1.4 - 0.55, 0, -cy * 1.4 - 0.65))
        t = bpy.context.active_object
        t.data.body = n
        t.data.size = 0.12
        t.rotation_euler = (math.radians(90), 0, 0)
        cell += 1
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(cols * 1.45, rows * 1.45 * 1.6)
    mid = Vector(((cols - 1) * 0.7, 0, -(rows - 1) * 0.7))
    # Blender -Y is the models' front (Unity -Z); look from front-left, a little above.
    cam.location = mid + Vector((-6, -10, 4))
    cam.rotation_euler = (Vector(mid - cam.location)).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('[preview]', path)


names = ONLY or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith('.fbx'))
for i in range(0, len(names), 12):
    render(names[i:i + 12], os.path.join(OUT, f'sheet_{i // 12:02d}.png'))
