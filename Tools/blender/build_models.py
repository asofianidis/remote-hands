"""
Remote Hands model generator. Builds every 3D asset procedurally and exports one FBX per model.

    blender --background --factory-startup --python Tools/blender/build_models.py -- <out_dir> [model ...]

Conventions (all in Unity terms, metres):
  * X right, Y up, Z depth. Equipment faces -Z (the cold aisle), like the racks in the hall.
  * Rack-mount units: origin at the bottom of the unit, centred in X, Z = 0 on the front plane,
    the chassis running back to Z = 0.9.
  * Everything else: origin at floor level, centred on its footprint.
  * Material slot names are a palette the game swaps for its own shaders at runtime
    (see Assets/Scripts/World/Models.cs), so colours here are only for previewing in Blender.
  * Each top-level object becomes a named child in Unity. Some are hidden by gameplay
    (e.g. a unit's "Bays" while a drive is being swapped), so keep those names in step with the code.

Layouts (bay grid, PSU and optic positions, DIMM banks...) mirror the formulas in the game code.
"""
import bpy, bmesh, math, os, sys
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = ARGS[0] if ARGS else os.path.join(os.path.dirname(__file__), 'out')
ONLY = set(ARGS[1:])
U = 0.0445  # one rack unit

PREVIEW = {
    'body': (0.17, 0.19, 0.23), 'bezel': (0.26, 0.29, 0.35), 'trim': (0.62, 0.64, 0.68), 'steel': (0.3, 0.36, 0.45),
    'dark': (0.03, 0.03, 0.035), 'rubber': (0.07, 0.07, 0.07), 'label': (0.9, 0.9, 0.86), 'screen': (0.2, 0.5, 0.9),
    'lcd': (0.3, 0.8, 0.5), 'sled': (0.55, 0.57, 0.6), 'feed': (0.55, 0.12, 0.1), 'cardboard': (0.7, 0.55, 0.35),
    'tape': (0.85, 0.75, 0.55), 'wood': (0.55, 0.42, 0.25), 'plastic': (0.15, 0.35, 0.6), 'red': (0.55, 0.12, 0.08),
    'yellow': (0.95, 0.75, 0.1), 'pcb': (0.08, 0.3, 0.15), 'alu': (0.72, 0.74, 0.78), 'gold': (0.85, 0.7, 0.3),
    'chip': (0.06, 0.06, 0.07), 'fabric': (0.12, 0.13, 0.16), 'diffuser': (0.95, 0.97, 1.0), 'fiber': (0.95, 0.8, 0.15),
    'blue': (0.15, 0.3, 0.8), 'port': (0.25, 0.27, 0.3), 'glass': (0.5, 0.6, 0.7),
}


def bl(v):
    """Unity (x, y, z) -> Blender (x, z, y)."""
    return Vector((v[0], v[2], v[1]))


class Part:
    """One exported object: a mesh built from many primitives, each with a material slot."""

    def __init__(self, name):
        self.name, self.verts, self.faces, self.fmat, self.mats = name, [], [], [], []

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _take(self, bm, m):
        mi, off = self._mi(m), len(self.verts)
        bm.verts.index_update()
        self.verts += [tuple(v.co) for v in bm.verts]
        for f in bm.faces:
            self.faces.append([off + v.index for v in f.verts])
            self.fmat.append(mi)
        bm.free()

    @staticmethod
    def _rotate(bm, rot):
        if not rot:
            return
        axis, deg = rot
        baxis = {'x': 'X', 'y': 'Z', 'z': 'Y'}[axis]
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-deg), 3, baxis), verts=bm.verts)

    def box(self, c, s, m, bevel=0.0, rot=None):
        """Box centred at c with size s (Unity terms), optionally chamfered and rotated about its centre."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        sz = bl(s)
        bmesh.ops.scale(bm, vec=(abs(sz.x), abs(sz.y), abs(sz.z)), verts=bm.verts)
        if bevel > 0:
            r = min(bevel, min(abs(x) for x in s) * 0.3)
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=r, offset_type='OFFSET', segments=1, profile=0.5,
                            affect='EDGES', clamp_overlap=True)
        self._rotate(bm, rot)
        bmesh.ops.translate(bm, vec=bl(c), verts=bm.verts)
        self._take(bm, m)

    def span(self, a, b, m, bevel=0.0):
        """Box between two opposite corners."""
        c = [(a[i] + b[i]) / 2 for i in range(3)]
        s = [abs(b[i] - a[i]) for i in range(3)]
        self.box(c, s, m, bevel)

    def cyl(self, c, r, depth, axis, m, segs=16, r2=None, rot=None):
        """Cylinder (or cone with r2) along a Unity axis."""
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r,
                              radius2=r if r2 is None else r2, depth=depth)
        if axis == 'x':
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'), verts=bm.verts)
        elif axis == 'z':
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'), verts=bm.verts)
        self._rotate(bm, rot)
        bmesh.ops.translate(bm, vec=bl(c), verts=bm.verts)
        self._take(bm, m)


class Model:
    def __init__(self, name):
        self.name, self.parts = name, {}

    def p(self, name):
        if name not in self.parts:
            self.parts[name] = Part(name)
        return self.parts[name]


MODELS = {}


def model(fn):
    MODELS[fn.__name__] = fn
    return fn


def material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        c = PREVIEW.get(name, (0.5, 0.5, 0.5))
        m.diffuse_color = (c[0], c[1], c[2], 1.0)
        # A real base colour, so the FBX carries it (for previews and as a fallback in Unity).
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get('Principled BSDF')
        if bsdf:
            bsdf.inputs['Base Color'].default_value = (c[0], c[1], c[2], 1.0)
            bsdf.inputs['Roughness'].default_value = 0.6
    return m


def export(md):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for part in md.parts.values():
        if not part.faces:
            continue
        mesh = bpy.data.meshes.new(part.name)
        mesh.from_pydata(part.verts, [], part.faces)
        mesh.validate()
        mesh.update()
        for m in part.mats:
            mesh.materials.append(material(m))
        for poly, mi in zip(mesh.polygons, part.fmat):
            poly.material_index = mi
        obj = bpy.data.objects.new(part.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
    path = os.path.join(OUT, md.name + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'},
                             mesh_smooth_type='FACE', add_leaf_bones=False, use_mesh_modifiers=False)
    tris = sum(len(f) - 2 for p in md.parts.values() for f in p.faces)
    print(f'[models] {md.name}: {len(md.parts)} parts, {tris} tris')


# ============================================================ shared details
def grille(p, x0, x1, y0, y1, z, n, m='dark', vertical=True, depth=0.0015, fill=0.55):
    """A row of vent slots on a face at depth z (slightly proud, towards the viewer at -Z or +Z)."""
    for i in range(n):
        t0 = i / n
        if vertical:
            w = (x1 - x0) / n
            p.box(((x0 + w * (i + 0.5)), (y0 + y1) / 2, z), (w * fill, y1 - y0, depth), m)
        else:
            h = (y1 - y0) / n
            p.box(((x0 + x1) / 2, y0 + h * (i + 0.5), z), (x1 - x0, h * fill, depth), m)


def handle(p, x, y, z, w, h, out, m='trim', t=0.004, facing=-1):
    """A U-shaped pull handle standing out of a face. facing -1 sticks out towards -Z."""
    zo = z + facing * out / 2
    p.box((x - w / 2 + t / 2, y, zo), (t, h, out), m)
    p.box((x + w / 2 - t / 2, y, zo), (t, h, out), m)
    p.box((x, y, z + facing * (out - t / 2)), (w, h, t), m)


def screw(p, x, y, z, r=0.003, facing=-1, m='trim'):
    p.cyl((x, y, z + facing * 0.0008), r, 0.0016, 'z', m, segs=10)
    p.box((x, y, z + facing * 0.0017), (r * 1.4, r * 0.35, 0.0004), 'dark')


# ============================================================ rack-mount units
def bay_layout(bays, hu):
    """Drive bay grid on the front face: same formula as Catalog.Bay in the game."""
    if bays == 0:
        return []
    cols = 4 if bays <= 4 else 6 if bays <= 12 else 12
    rows = math.ceil(bays / cols)
    h = hu * U
    w, hh = 0.42 / cols, (h - 0.006) / rows
    out = []
    for i in range(bays):
        r, c = i // cols, i % cols
        out.append(((-0.21 + w * (c + 0.5), 0.003 + hh * (rows - 1 - r + 0.5)), (w - 0.004, hh - 0.003)))
    return out


def psu_height(hu):
    return max(0.03, hu * U * 0.8)


DEVICES = {
    # id: (height U, bays, sfps, kind)
    'srv1u': (1, 4, 2, 'server'), 'srv2u': (2, 12, 2, 'server'), 'sw48': (1, 0, 6, 'switch'),
    'ds4u': (4, 24, 0, 'storage'), 'cust1u': (1, 0, 0, 'appliance'), 'cust2u': (2, 8, 0, 'server'),
    'cust4u': (4, 24, 0, 'storage'), 'gpu4u': (4, 8, 2, 'gpu'), 'tl4u': (4, 0, 0, 'tape'),
}


def drive_front(p, cx, cy, w, hh, z=-0.003):
    """The visible front of a drive sled: tray, release lever, honeycomb grille."""
    p.box((cx, cy, z + 0.003), (w, hh, 0.006), 'sled', bevel=0.0008)
    lever_h = hh * 0.26
    p.box((cx, cy - hh / 2 + lever_h / 2 + hh * 0.06, z - 0.0012), (w * 0.82, lever_h, 0.0025), 'dark', bevel=0.0005)
    if hh > 0.02:
        grille(p, cx - w * 0.38, cx + w * 0.22, cy, cy + hh * 0.38, z - 0.0005, max(3, int(w / 0.006)), fill=0.5)
    else:
        grille(p, cx - w * 0.4, cx + w * 0.1, cy - hh * 0.05, cy + hh * 0.35, z - 0.0005, max(3, int(w / 0.007)), fill=0.5)


def unit(name):
    hu, bays, sfps, kind = DEVICES[name]
    h = hu * U
    md = Model(name)
    ch, bz, by, rear, psus, opt = md.p('Chassis'), md.p('Bezel'), md.p('Bays'), md.p('Rear'), md.p('PSUs'), md.p('Optics')
    # Chassis: body, rack ears with screw holes, lid seam and side vents.
    ch.box((0, h / 2, 0.45), (0.53, h - 0.004, 0.9), 'body', bevel=0.002)
    for sx in (-1, 1):
        ch.box((sx * 0.258, h / 2, -0.002), (0.026, h - 0.004, 0.004), 'trim', bevel=0.001)
        for k in range(hu):
            ch.cyl((sx * 0.262, k * U + U / 2, -0.0045), 0.0035, 0.002, 'z', 'dark', segs=8)
    for sx in (-1, 1):
        for i in range(10):
            ch.box((sx * 0.2655, h * 0.5, 0.15 + i * 0.03), (0.0015, h * 0.45, 0.012), 'dark')
    ch.box((0, h - 0.0018, 0.33), (0.52, 0.0006, 0.004), 'dark')  # lid seam

    # Front: drive bays or a front panel, depending on the unit.
    if bays:
        for (cx, cy), (w, hh) in bay_layout(bays, hu):
            drive_front(by, cx, cy, w, hh)
        # Side strips beside the bay field: vents on the left, power button and pull handle on the right.
        bz.box((-0.2275, h / 2, -0.002), (0.033, h - 0.006, 0.004), 'bezel', bevel=0.0008)
        bz.box((0.2275, h / 2, -0.002), (0.033, h - 0.006, 0.004), 'bezel', bevel=0.0008)
        grille(bz, -0.24, -0.215, h * 0.15, h * 0.85, -0.0045, 4)
        bz.cyl((0.232, h * 0.5, -0.005), 0.0045, 0.004, 'z', 'trim', segs=16)
        bz.box((-0.228, h * 0.82 if hu > 1 else h * 0.5, -0.0046), (0.018, min(0.008, h * 0.18), 0.0008), 'label')
        if hu >= 2:
            handle(bz, 0.2275, h * 0.3, -0.004, 0.022, 0.006, 0.012)
    elif kind == 'switch':
        bz.box((0, h / 2, -0.002), (0.48, h * 0.9, 0.004), 'bezel', bevel=0.0008)
        # 48 RJ45 ports in two rows, six SFP+ cages, a console port and the status LED block.
        for r in range(2):
            for i in range(24):
                x = -0.2 + i * 0.0145 + (i // 6) * 0.004
                bz.box((x, h * (0.68 - r * 0.36), -0.0045), (0.0115, h * 0.28, 0.003), 'dark')
                bz.box((x, h * (0.68 - r * 0.36) + h * 0.1, -0.0062), (0.004, h * 0.05, 0.0006), 'port')
        for i in range(6):
            bz.box((0.168 + (i % 3) * 0.022, h * (0.68 - (i // 3) * 0.36), -0.0045), (0.017, h * 0.26, 0.003), 'dark')
            bz.box((0.168 + (i % 3) * 0.022, h * (0.68 - (i // 3) * 0.36), -0.0058), (0.013, h * 0.16, 0.0008), 'trim')
        bz.box((0.236, h * 0.5, -0.0045), (0.009, h * 0.36, 0.003), 'blue')
        bz.box((-0.232, h * 0.5, -0.0044), (0.016, h * 0.6, 0.001), 'label')
    elif kind == 'appliance':
        bz.box((0, h / 2, -0.003), (0.48, h * 0.88, 0.006), 'bezel', bevel=0.0015)
        grille(bz, -0.2, 0.05, h * 0.2, h * 0.8, -0.0065, 22)
        bz.box((0.13, h * 0.5, -0.0065), (0.07, h * 0.42, 0.0012), 'dark')
        bz.box((0.13, h * 0.5, -0.0072), (0.064, h * 0.34, 0.0006), 'lcd')
        bz.box((-0.225, h * 0.5, -0.0066), (0.022, h * 0.28, 0.0008), 'label')
        bz.cyl((0.205, h * 0.5, -0.007), 0.004, 0.003, 'z', 'trim')
    elif kind == 'tape':
        # Two tape magazines with pull handles, an operator LCD and the mail slot.
        bz.box((0, h / 2, -0.002), (0.48, h * 0.92, 0.004), 'bezel', bevel=0.001)
        for sx in (-1, 1):
            bz.box((sx * 0.115, h * 0.45, -0.0055), (0.2, h * 0.66, 0.003), 'body', bevel=0.001)
            grille(bz, sx * 0.115 - 0.08, sx * 0.115 + 0.08, h * 0.2, h * 0.5, -0.0075, 14, fill=0.4)
            handle(bz, sx * 0.115, h * 0.62, -0.007, 0.07, 0.008, 0.014)
            bz.box((sx * 0.115, h * 0.74, -0.0072), (0.11, 0.007, 0.0008), 'label')
        bz.box((0, h * 0.88, -0.005), (0.07, 0.02, 0.002), 'dark')
        bz.box((0, h * 0.88, -0.0062), (0.062, 0.014, 0.0006), 'lcd')
        bz.box((0, h * 0.1, -0.005), (0.08, 0.008, 0.003), 'dark')
    cols = 4 if bays <= 4 else 6 if bays <= 12 else 12
    rows = math.ceil(bays / cols) if bays else 0
    if bays and bays < cols * rows:
        # Blank fillers in the empty bay positions of a partly filled grid.
        w, hh = 0.42 / cols, (h - 0.006) / rows
        for i in range(bays, cols * rows):
            r, c = i // cols, i % cols
            cx, cy = -0.21 + w * (c + 0.5), 0.003 + hh * (rows - 1 - r + 0.5)
            by.box((cx, cy, 0), (w - 0.004, hh - 0.003, 0.004), 'bezel')
            grille(by, cx - w * 0.35, cx + w * 0.35, cy - hh * 0.3, cy + hh * 0.3, -0.0025, 6, fill=0.45)

    # Rear: I/O and vents (left), PSUs (right) and optics, each a separate object the game can hide.
    zb = 0.9
    rear.box((0, h / 2, zb + 0.002), (0.5, h * 0.86, 0.004), 'body')
    grille(rear, -0.02, 0.05, h * 0.15, h * 0.85, zb + 0.0045, 8)
    if kind == 'gpu':
        # Four double-width GPU exhaust grilles across the top of the rear.
        for i in range(4):
            grille(rear, -0.23 + i * 0.075, -0.17 + i * 0.075, h * 0.45, h * 0.92, zb + 0.0045, 10, fill=0.5)
    if kind in ('server', 'appliance', 'gpu'):
        rear.box((-0.06, h * 0.5, zb + 0.005), (0.012, min(0.011, h * 0.4), 0.004), 'dark')  # RJ45 eth
        rear.box((-0.045, h * 0.5, zb + 0.005), (0.012, min(0.011, h * 0.4), 0.004), 'dark')
        rear.box((-0.2, h * 0.5, zb + 0.005), (0.012, min(0.011, h * 0.4), 0.004), 'dark')  # mgmt
        rear.box((-0.2, h * 0.5 + min(0.004, h * 0.15), zb + 0.0072), (0.004, 0.0015, 0.0005), 'port')
        rear.box((-0.225, h * 0.5, zb + 0.005), (0.016, min(0.008, h * 0.3), 0.004), 'blue')  # VGA
        if hu >= 2:
            for i in range(3):  # PCIe slot covers
                rear.box((-0.12 + i * 0.03, h * 0.75, zb + 0.0045), (0.022, h * 0.32, 0.001), 'trim')
                grille(rear, -0.13 + i * 0.03, -0.11 + i * 0.03, h * 0.62, h * 0.88, zb + 0.0052, 4, fill=0.4)
    elif kind == 'switch':
        for i in range(4):
            rear.cyl((-0.13 + i * 0.045, h * 0.5, zb + 0.0045), h * 0.36, 0.002, 'z', 'dark', segs=14)
            rear.box((-0.13 + i * 0.045, h * 0.5, zb + 0.0058), (h * 0.7, 0.0012, 0.0006), 'trim')
        rear.box((-0.215, h * 0.5, zb + 0.005), (0.012, h * 0.4, 0.004), 'dark')
    elif kind == 'tape':
        rear.box((-0.13, h * 0.5, zb + 0.006), (0.2, h * 0.3, 0.008), 'trim', bevel=0.001)
        rear.box((-0.17, h * 0.5, zb + 0.0105), (0.018, h * 0.12, 0.002), 'dark')
        rear.box((-0.2, h * 0.3, zb + 0.005), (0.012, min(0.011, h * 0.4), 0.004), 'dark')
    elif kind == 'storage':
        for i in range(2):  # SAS I/O modules
            y = h * (0.3 + i * 0.4)
            rear.box((-0.13, y, zb + 0.006), (0.2, h * 0.32, 0.008), 'trim', bevel=0.001)
            for k in range(4):
                rear.box((-0.19 + k * 0.03, y, zb + 0.0105), (0.018, h * 0.12, 0.002), 'dark')
            handle(rear, -0.035, y, zb + 0.01, 0.02, h * 0.18, 0.012, facing=1)
    ph = psu_height(hu)
    for i in range(2):
        x = 0.1 + i * 0.09
        psus.box((x, h / 2, zb + 0.006), (0.078, ph, 0.012), 'trim', bevel=0.0012)
        psus.cyl((x - 0.012, h / 2, zb + 0.0125), min(0.014, ph * 0.38), 0.002, 'z', 'dark', segs=16)
        psus.box((x - 0.012, h / 2, zb + 0.0136), (min(0.026, ph * 0.72), 0.0012, 0.0005), 'trim')
        psus.box((x + 0.022, h / 2, zb + 0.0125), (0.014, min(0.012, ph * 0.4), 0.002), 'dark')  # IEC inlet
        handle(psus, x + 0.002, h / 2 - ph * 0.3 if ph > 0.05 else h / 2, zb + 0.012, 0.03, 0.004, 0.01, facing=1)
    if sfps:
        gap = min(0.03, 0.2 / max(2, sfps))
        for i in range(max(2, sfps)):
            x = -0.17 + i * gap
            opt.box((x, h / 2, zb + 0.0015), (0.017, 0.012, 0.003), 'dark')  # cage mouth
            opt.box((x, h / 2, zb + 0.022), (0.014, 0.009, 0.04), 'trim', bevel=0.0008)  # optic body
            opt.box((x, h / 2 - 0.0035, zb + 0.043), (0.012, 0.002, 0.004), 'blue')  # pull tab
    return md


for _name in DEVICES:
    MODELS[_name] = (lambda n: (lambda: unit(n)))(_name)


# ============================================================ swap-game parts
def sled(name, w, hh):
    md = Model(name)
    p = md.p('Sled')
    # Origin at the sled's centre; the drive runs 0.1 deep with its face at -Z.
    p.box((0, 0, 0.004), (w, hh, 0.092), 'trim', bevel=0.001)
    drive_front(p, 0, 0, w, hh, z=-0.05)
    p.box((0, hh * 0.3, 0.02), (w * 0.9, hh * 0.25, 0.05), 'chip')
    return md


for _dev, (_hu, _bays, _s, _k) in DEVICES.items():
    if _bays:
        _cols = 4 if _bays <= 4 else 6 if _bays <= 12 else 12
        _rows = math.ceil(_bays / _cols)
        _w, _hh = 0.42 / _cols - 0.004, (_hu * U - 0.006) / _rows - 0.003
        MODELS['sled_' + _dev] = (lambda n, w, hh: (lambda: sled(n, w, hh)))('sled_' + _dev, _w, _hh)


def psu(name, ph):
    md = Model(name)
    p = md.p('PSU')
    # Origin at the module's centre; 0.3 deep, handle end facing +Z (the rear of the rack).
    p.box((0, 0, 0), (0.078, ph, 0.3), 'trim', bevel=0.0015)
    for i in range(12):
        p.box((0, ph / 2 + 0.0004, -0.12 + i * 0.02), (0.06, 0.0008, 0.008), 'dark')
    p.cyl((-0.012, 0, 0.151), min(0.014, ph * 0.38), 0.002, 'z', 'dark', segs=16)
    p.box((0.022, 0, 0.151), (0.014, min(0.012, ph * 0.4), 0.002), 'dark')
    handle(p, 0.002, 0, 0.15, 0.03, 0.004, 0.01, facing=1)
    p.box((0, -ph / 2 + 0.002, -0.1), (0.05, 0.001, 0.06), 'label')
    return md


for _hu in (1, 2, 4):
    MODELS[f'psu_{_hu}u'] = (lambda n, ph: (lambda: psu(n, ph)))(f'psu_{_hu}u', psu_height(_hu))


@model
def sfp_optic():
    md = Model('sfp_optic')
    p = md.p('Optic')
    p.box((0, 0, 0), (0.014, 0.009, 0.05), 'trim', bevel=0.0008)
    p.box((0, 0, -0.024), (0.012, 0.006, 0.004), 'dark')
    p.box((0, -0.0035, 0.026), (0.012, 0.002, 0.004), 'blue')
    p.box((0, -0.002, 0.015), (0.002, 0.001, 0.02), 'blue')
    return md


@model
def dimm():
    md = Model('dimm')
    p = md.p('DIMM')
    # 5 mm thick (X), 32 mm tall (Y), 130 mm long (Z); origin at the centre.
    p.box((0, 0.002, 0), (0.0012, 0.028, 0.13), 'pcb')
    p.box((0, -0.0135, 0), (0.0013, 0.003, 0.126), 'gold')
    for side in (-1, 1):
        for i in range(8):
            p.box((side * 0.0012, 0.004, -0.056 + i * 0.016), (0.0012, 0.012, 0.011), 'chip')
    p.box((0, 0.012, 0.06), (0.0014, 0.004, 0.006), 'label')
    return md


@model
def fan_module():
    md = Model('fan_module')
    p = md.p('Fan')
    w, h, d = 0.068, 0.04, 0.035
    # A square housing with a round throat; the spinning blades are added by the game.
    p.box((-w / 2 + 0.003, 0, 0), (0.006, h, d), 'dark')
    p.box((w / 2 - 0.003, 0, 0), (0.006, h, d), 'dark')
    p.box((0, h / 2 - 0.003, 0), (w, 0.006, d), 'dark')
    p.box((0, -h / 2 + 0.003, 0), (w, 0.006, d), 'dark')
    p.cyl((0, 0, 0.004), 0.008, 0.02, 'z', 'dark', segs=14)
    for a in (0, 90):
        p.box((0, 0, 0.012), (w * 0.9, 0.0015, 0.002), 'dark', rot=('z', a + 45))
    p.box((0, h / 2 + 0.002, -0.006), (0.03, 0.004, 0.012), 'red')  # pull tab
    return md


@model
def heatsink():
    md = Model('heatsink')
    p = md.p('Heatsink')
    hs = 0.03
    p.box((0, 0.002, 0), (0.09, 0.004, 0.09), 'alu')
    for i in range(13):
        p.box((-0.042 + i * 0.007, hs / 2 + 0.002, 0), (0.0012, hs - 0.004, 0.088), 'alu')
    return md


def open_chassis(name, hu):
    """A server slid out with its lid off: tray, motherboard, CPU sockets with heatsinks, DIMM slots,
    fan wall bracket and PSU cage. Same layout as the swap game's DIMM and fan jobs."""
    h = hu * U
    md = Model(name)
    t, b = md.p('Tray'), md.p('Board')
    t.box((0, 0.002, 0.45), (0.52, 0.004, 0.9), 'body')
    for sx in (-1, 1):
        t.box((sx * 0.258, h / 2, 0.45), (0.004, h, 0.9), 'body')
        t.box((sx * 0.258, h / 2, -0.002), (0.026, h - 0.004, 0.004), 'trim')
    t.box((0, h / 2, 0.002), (0.52, h, 0.004), 'bezel')
    t.box((0, h / 2, 0.898), (0.52, h, 0.004), 'body')
    b.box((0, 0.006, 0.6), (0.48, 0.004, 0.45), 'pcb')
    for cz in (0.36, 0.56):
        b.box((0, 0.009, cz), (0.075, 0.003, 0.075), 'dark')
        hs = min(h - 0.006, 0.03)
        b.box((0, 0.012, cz), (0.09, 0.004, 0.09), 'alu')
        for i in range(13):
            b.box((-0.042 + i * 0.007, 0.012 + hs / 2, cz), (0.0012, hs - 0.004, 0.088), 'alu')
    t.box((0, min(h - 0.006, 0.04) + 0.006, 0.2), (0.48, 0.003, 0.04), 'trim')  # fan wall bracket
    t.box((0.15, h * 0.45, 0.75), (0.18, h * 0.8, 0.3), 'trim', bevel=0.001)  # PSU cage
    b.box((-0.15, 0.012, 0.78), (0.12, 0.012, 0.1), 'chip')  # chipset/RAID card
    return md


MODELS['open_srv1u'] = lambda: open_chassis('open_srv1u', 1)
MODELS['open_srv2u'] = lambda: open_chassis('open_srv2u', 2)


# ============================================================ the rack and its PDUs
@model
def rack():
    md = Model('rack')
    f, rails, holes = md.p('Frame'), md.p('Rails'), md.p('Holes')
    W, D, H = 0.6, 1.1, 2.05
    # Side panels with a perforated band, roof with cable entries, plinth with levelling feet.
    for sx in (-1, 1):
        f.box((sx * (W / 2 - 0.01), H / 2, 0), (0.02, H - 0.02, D), 'body', bevel=0.003)
        for i in range(14):
            f.box((sx * (W / 2 + 0.0002), 0.35 + i * 0.1, 0), (0.0012, 0.05, D * 0.8), 'dark')
    f.box((0, H - 0.01, 0), (W, 0.02, D), 'body', bevel=0.003)
    for z in (-0.25, 0.25):
        f.box((0, H + 0.0005, z), (0.3, 0.002, 0.08), 'dark')
    f.box((0, 0.05, 0), (W, 0.1, D), 'body', bevel=0.004)
    for sx in (-1, 1):
        for sz in (-1, 1):
            f.cyl((sx * 0.25, 0.006, sz * 0.5), 0.02, 0.012, 'y', 'rubber', segs=12)
    # Front and rear frame lips (where doors would hang).
    for sz in (-1, 1):
        f.box((0, H - 0.04, sz * (D / 2 - 0.01)), (W, 0.06, 0.02), 'body')
        for sx in (-1, 1):
            f.box((sx * (W / 2 - 0.02), H / 2, sz * (D / 2 - 0.01)), (0.04, H - 0.1, 0.02), 'body')
    # Label holders above the front and rear openings.
    f.box((0, H + 0.075, -0.48), (0.4, 0.15, 0.02), 'dark', bevel=0.003)
    f.box((0, H + 0.075, 0.48), (0.4, 0.15, 0.02), 'dark', bevel=0.003)
    f.box((-0.15, H + 0.03, -0.48), (0.02, 0.06, 0.02), 'body')
    f.box((0.15, H + 0.03, -0.48), (0.02, 0.06, 0.02), 'body')
    f.box((-0.15, H + 0.03, 0.48), (0.02, 0.06, 0.02), 'body')
    f.box((0.15, H + 0.03, 0.48), (0.02, 0.06, 0.02), 'body')
    # Mounting rails with a square hole every U.
    for sz in (-1, 1):
        z = sz * (D / 2 - 0.04)
        for sx in (-1, 1):
            x = sx * 0.255
            rails.box((x, H / 2, z), (0.025, H - 0.1, 0.02), 'trim', bevel=0.001)
            rails.box((x + sx * 0.012, H / 2, z + sz * 0.03), (0.002, H - 0.1, 0.06), 'trim')
            for u in range(42):
                y = 0.1 + u * U + U / 2
                holes.box((x - sx * 0.004, y, z - sz * 0.0105), (0.006, 0.006, 0.001), 'dark')
    return md


@model
def pdu():
    md = Model('pdu')
    p = md.p('PDU')
    # A zero-U strip, 1.8 m tall; outlets on the +Z face (towards the hot aisle).
    p.box((0, 0, 0), (0.036, 1.8, 0.05), 'feed', bevel=0.003)
    p.box((0, 0.84, 0.0255), (0.028, 0.08, 0.002), 'dark')
    p.box((0, 0.85, 0.0268), (0.02, 0.025, 0.0008), 'lcd')
    for i in range(2):
        p.box((0, 0.775 - i * 0.03, 0.0265), (0.016, 0.02, 0.003), 'dark')
    for i in range(24):
        y = 0.7 - i * 0.06
        p.box((0, y, 0.0258), (0.022, 0.032, 0.0018), 'dark', bevel=0.0006)
        p.box((0, y + 0.004, 0.0269), (0.012, 0.004, 0.0004), 'port')
    p.box((0, -0.875, 0.0262), (0.026, 0.02, 0.0008), 'label')
    return md


# ============================================================ storage room and dock
@model
def shelf():
    md = Model('shelf')
    p = md.p('Shelf')
    W, H, D = 1.6, 2.0, 0.6
    for sx in (-1, 1):
        for sz in (-1, 1):
            x, z = sx * (W / 2 - 0.02), sz * (D / 2 - 0.02)
            p.box((x, H / 2, z), (0.04, H, 0.004), 'steel')
            p.box((x - sx * 0.018, H / 2, z), (0.004, H, 0.04), 'steel')
            for k in range(30):
                p.box((x, 0.1 + k * 0.064, z + (-0.0022 if sz < 0 else 0.0022)), (0.006, 0.012, 0.001), 'dark')
            p.box((x, 0.004, z), (0.06, 0.008, 0.06), 'rubber')
    for s in range(4):
        y = 0.15 + s * 0.55
        p.box((0, y, 0), (W - 0.02, 0.012, D - 0.03), 'steel', bevel=0.002)
        p.box((0, y - 0.02, -D / 2 + 0.02), (W - 0.06, 0.03, 0.004), 'steel')
        p.box((0, y - 0.02, D / 2 - 0.02), (W - 0.06, 0.03, 0.004), 'steel')
    for sign in (-1, 1):  # back X-brace
        p.box((0, H / 2, D / 2 - 0.01), (math.hypot(W, H) * 0.92, 0.012, 0.003), 'steel', rot=('z', sign * math.degrees(math.atan2(H, W))))
    return md


def carton(name, w, h, d, label=True):
    md = Model(name)
    p = md.p('Carton')
    p.box((0, h / 2, 0), (w, h, d), 'cardboard', bevel=0.004)
    p.box((0, h + 0.0006, 0), (w * 0.18, 0.0012, d + 0.002), 'tape')
    p.box((0, h / 2, -d / 2 - 0.0006), (w * 0.18, h + 0.002, 0.0012), 'tape')
    if label:
        p.box((w * 0.25, h * 0.5, -d / 2 - 0.0012), (w * 0.3, h * 0.5, 0.001), 'label')
        p.box((w * 0.25, h * 0.4, -d / 2 - 0.0018), (w * 0.22, h * 0.08, 0.0005), 'dark')
    return md


MODELS['carton_small'] = lambda: carton('carton_small', 0.3, 0.12, 0.25)
MODELS['carton_1u'] = lambda: carton('carton_1u', 0.45, 0.09, 0.5)
MODELS['carton_2u'] = lambda: carton('carton_2u', 0.45, 0.18, 0.5)
for _hu, _h in ((1, 0.16), (2, 0.22), (4, 0.34)):
    # Customer shipments: bigger boxes with a tape cross and a shipping label; the game adds the customer's sticker.
    MODELS[f'crate_{_hu}u'] = (lambda n, h: (lambda: carton(n, 0.75, h, 0.9)))(f'crate_{_hu}u', _h)


@model
def returns_bin():
    md = Model('returns_bin')
    p = md.p('Bin')
    W, H, D = 1.2, 0.9, 0.8
    p.box((0, H / 2 - 0.02, 0), (W - 0.04, H - 0.04, D - 0.04), 'plastic', bevel=0.02)
    p.box((0, H - 0.015, 0), (W, 0.03, D), 'plastic', bevel=0.01)
    p.box((0, H + 0.01, 0.05), (W - 0.06, 0.02, D - 0.16), 'plastic', bevel=0.008)
    for sx in (-1, 1):
        p.box((sx * (W / 2 - 0.005), H * 0.75, 0), (0.02, 0.04, 0.2), 'dark', bevel=0.005)
        for sz in (-1, 1):
            p.cyl((sx * (W / 2 - 0.1), 0.03, sz * (D / 2 - 0.1)), 0.03, 0.025, 'x', 'rubber', segs=12)
    for i in range(4):
        p.box((0, 0.2 + i * 0.12, -D / 2 + 0.018), (W - 0.2, 0.012, 0.002), 'dark')
    p.box((0, H * 0.62, -D / 2 + 0.015), (0.5, 0.14, 0.002), 'label')
    return md


@model
def workbench():
    md = Model('workbench')
    p = md.p('Bench')
    W, D, top = 2.2, 0.8, 0.88
    p.box((0, top - 0.03, 0), (W, 0.06, D), 'wood', bevel=0.004)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box((sx * (W / 2 - 0.05), (top - 0.06) / 2, sz * (D / 2 - 0.05)), (0.05, top - 0.06, 0.05), 'steel', bevel=0.003)
    p.box((0, 0.15, 0), (W - 0.08, 0.025, D - 0.08), 'steel')
    p.box((0, 0.6, D / 2 - 0.05), (W - 0.1, 0.05, 0.03), 'steel')
    # Drawer pedestal on the left, with three drawers.
    p.box((-0.6, 0.47, 0), (0.55, 0.62, D - 0.12), 'steel', bevel=0.004)
    for i in range(3):
        y = 0.3 + i * 0.2
        p.box((-0.6, y, -D / 2 + 0.058), (0.5, 0.17, 0.004), 'steel', bevel=0.002)
        handle(p, -0.6, y + 0.04, -D / 2 + 0.056, 0.14, 0.012, 0.025)
    # Pegboard and a lamp at the back.
    p.box((0.2, top + 0.4, D / 2 - 0.02), (1.6, 0.8, 0.015), 'wood')
    for i in range(14):
        for k in range(7):
            p.box((-0.55 + i * 0.11, top + 0.08 + k * 0.1, D / 2 - 0.029), (0.008, 0.008, 0.002), 'dark')
    p.box((0.2, top + 0.003, -0.05), (0.5, 0.004, 0.35), 'rubber')  # ESD mat
    return md


@model
def shredder():
    md = Model('shredder')
    p = md.p('Shredder')
    W, H, D = 0.6, 0.45, 0.5
    p.box((0, H / 2, 0), (W, H, D), 'red', bevel=0.012)
    p.box((0, H + 0.002, 0), (0.42, 0.004, 0.12), 'dark')
    p.box((0, H * 0.38, -D / 2 - 0.002), (W * 0.7, H * 0.4, 0.004), 'dark', bevel=0.004)
    p.box((0, H * 0.38, -D / 2 - 0.004), (W * 0.62, H * 0.32, 0.002), 'glass')
    p.box((0.19, H - 0.03, -0.12), (0.12, 0.004, 0.08), 'dark')
    for i in range(3):
        p.cyl((0.155 + i * 0.03, H - 0.026, -0.12), 0.008, 0.006, 'y', ['lcd', 'yellow', 'red'][i], segs=10)
    p.box((-0.18, H * 0.85, -D / 2 - 0.002), (0.14, 0.05, 0.002), 'label')
    return md


@model
def crash_cart():
    """Monitor display centre at (0, 1.25, 0.062), 0.56 x 0.315, facing -Z: the console jobs draw onto it."""
    md = Model('crash_cart')
    p, disp = md.p('Cart'), md.p('Display')
    p.box((0, 0.08, 0), (0.7, 0.04, 0.5), 'dark', bevel=0.006)
    p.box((0, 0.95, 0), (0.7, 0.03, 0.5), 'dark', bevel=0.006)
    p.box((0, 0.5, 0), (0.66, 0.02, 0.46), 'dark')
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.cyl((sx * 0.32, 0.5, sz * 0.22), 0.018, 0.9, 'y', 'steel', segs=10)
            p.cyl((sx * 0.3, 0.035, sz * 0.2), 0.035, 0.025, 'x', 'rubber', segs=12)
            p.box((sx * 0.3, 0.06, sz * 0.2), (0.03, 0.03, 0.03), 'steel')
    handle(p, 0, 0.88, 0.25, 0.4, 0.02, 0.06, m='steel', t=0.012, facing=1)
    p.box((0, 0.56, 0.05), (0.43, 0.088, 0.35), 'body', bevel=0.003)  # KVM switch
    for i in range(8):
        p.box((-0.15 + i * 0.04, 0.56, -0.126), (0.012, 0.012, 0.002), 'dark')
    p.box((0, 0.97, -0.1), (0.45, 0.02, 0.16), 'dark', bevel=0.003)  # keyboard
    for r in range(4):
        p.box((0, 0.981, -0.155 + r * 0.035), (0.42, 0.002, 0.028), 'port')
    p.box((0.27, 0.968, -0.1), (0.05, 0.016, 0.08), 'dark', bevel=0.006)  # mouse
    p.box((0, 1.25, 0.08), (0.6, 0.355, 0.03), 'dark', bevel=0.006)  # monitor housing
    p.box((0, 1.1, 0.1), (0.04, 0.3, 0.03), 'steel')
    p.box((0, 1.08, 0.1), (0.25, 0.012, 0.18), 'steel')
    disp.box((0, 1.25, 0.064), (0.56, 0.315, 0.002), 'screen')
    p.box((0.26, 1.088, 0.064), (0.012, 0.006, 0.002), 'lcd')
    return md


@model
def pallet():
    md = Model('pallet')
    p = md.p('Pallet')
    W, D = 1.1, 1.0
    for i in range(7):
        p.box((-W / 2 + 0.07 + i * (W - 0.14) / 6, 0.13, 0), (0.1, 0.022, D), 'wood', bevel=0.002)
    for z in (-D / 2 + 0.05, 0, D / 2 - 0.05):
        p.box((0, 0.1, z), (W, 0.022, 0.1), 'wood', bevel=0.002)
        for x in (-W / 2 + 0.05, 0, W / 2 - 0.05):
            p.box((x, 0.05, z), (0.1, 0.08, 0.1), 'wood', bevel=0.003)
    for i in range(3):
        p.box((-W / 2 + 0.1 + i * (W - 0.2) / 2, 0.011, 0), (0.12, 0.022, D), 'wood', bevel=0.002)
    return md


@model
def outbound_sign():
    md = Model('outbound_sign')
    p = md.p('Sign')
    p.cyl((0, 1.0, 0), 0.025, 2.0, 'y', 'steel', segs=12)
    p.box((0, 0.01, 0), (0.3, 0.02, 0.3), 'steel', bevel=0.004)
    p.box((0, 1.9, -0.03), (1.0, 0.3, 0.03), 'plastic', bevel=0.006)
    p.box((0, 1.9, -0.012), (0.08, 0.06, 0.02), 'steel')
    return md


# ============================================================ the meet-me room, NOC and fit-out
@model
def mmr_frame():
    """Faces -Z; the game rotates it to face into the hall. 2.6 m wide, 2.25 m tall, 0.3 m deep."""
    md = Model('mmr_frame')
    p, panels = md.p('Frame'), md.p('Panels')
    W, H, D = 2.6, 2.25, 0.3
    for sx in (-1, 1):
        p.box((sx * (W / 2 - 0.04), H / 2, 0), (0.08, H, D), 'body', bevel=0.004)
        for k in range(20):  # cable management fingers
            p.box((sx * (W / 2 - 0.12), 0.2 + k * 0.095, -D / 2 + 0.03), (0.08, 0.012, 0.03), 'dark')
    p.box((0, H - 0.04, 0), (W, 0.08, D), 'body', bevel=0.004)
    p.box((0, 0.03, 0), (W, 0.06, D), 'body')
    p.box((0, H + 0.06, -0.02), (W - 0.1, 0.12, 0.2), 'yellow', bevel=0.01)  # fiber trough
    p.box((0, H + 0.12, -0.02), (W - 0.12, 0.002, 0.18), 'dark')
    p.box((0, H / 2, D / 2 - 0.01), (W - 0.16, H - 0.1, 0.02), 'body')
    for r in range(9):
        y = 0.32 + r * 0.205
        panels.box((0, y, -D / 2 + 0.03), (W - 0.32, 0.17, 0.02), 'dark', bevel=0.002)
        for k in range(48):
            panels.box((-1.08 + k * 0.046, y + 0.035, -D / 2 + 0.019), (0.012, 0.012, 0.001), 'port')
            panels.box((-1.08 + k * 0.046, y - 0.035, -D / 2 + 0.019), (0.012, 0.012, 0.001), 'port')
        panels.box((-1.12, y, -D / 2 + 0.019), (0.04, 0.12, 0.001), 'label')
    # Some jumpers already looping down from the panels into the managers.
    for i in range(14):
        x = -1.0 + i * 0.15
        p.box((x, 1.3, -D / 2 - 0.004), (0.004, 1.6 - (i % 4) * 0.25, 0.004), ['fiber', 'blue', 'fiber', 'fiber'][i % 4])
    return md


@model
def noc_desk():
    """Operator side at -Z, monitors along the back edge facing -Z. 2.4 m long, 1.0 m deep."""
    md = Model('noc_desk')
    p, screens = md.p('Desk'), md.p('Screens')
    L, D, top = 2.4, 1.0, 0.75
    p.box((0, top - 0.025, 0), (L, 0.05, D), 'body', bevel=0.006)
    for sx in (-1, 1):
        p.box((sx * (L / 2 - 0.04), (top - 0.05) / 2, 0), (0.06, top - 0.05, D - 0.1), 'dark', bevel=0.004)
    p.box((0, top * 0.55, D / 2 - 0.06), (L - 0.12, top * 0.7, 0.02), 'dark')
    for i, x in enumerate((-0.65, 0, 0.65)):
        p.box((x, top + 0.02, 0.3), (0.25, 0.012, 0.18), 'dark', bevel=0.004)
        p.box((x, top + 0.22, 0.32), (0.05, 0.4, 0.03), 'steel')
        yaw = (-18, 0, 18)[i]
        p.box((x, top + 0.42, 0.28), (0.62, 0.38, 0.03), 'dark', bevel=0.006, rot=('y', yaw))
        screens.box((x - math.sin(math.radians(yaw)) * 0.017, top + 0.42, 0.28 - math.cos(math.radians(yaw)) * 0.017),
                    (0.58, 0.34, 0.002), 'screen', rot=('y', yaw))
    p.box((0, top + 0.012, -0.15), (0.45, 0.02, 0.15), 'dark', bevel=0.004)
    p.box((0.32, top + 0.01, -0.15), (0.06, 0.018, 0.1), 'dark', bevel=0.008)
    p.box((-0.7, top + 0.03, -0.1), (0.2, 0.05, 0.2), 'dark', bevel=0.01)  # desk phone
    p.box((-0.7, top + 0.06, -0.15), (0.06, 0.02, 0.18), 'dark', bevel=0.008)
    return md


@model
def office_chair():
    md = Model('office_chair')
    p = md.p('Chair')
    for k in range(5):
        a = k * 72
        p.box((math.sin(math.radians(a)) * 0.15, 0.05, math.cos(math.radians(a)) * 0.15), (0.04, 0.03, 0.3), 'dark', rot=('y', a))
        p.cyl((math.sin(math.radians(a)) * 0.3, 0.025, math.cos(math.radians(a)) * 0.3), 0.025, 0.03, 'x', 'rubber', segs=10)
    p.cyl((0, 0.27, 0), 0.025, 0.42, 'y', 'steel', segs=12)
    p.box((0, 0.5, 0), (0.5, 0.08, 0.48), 'fabric', bevel=0.03)
    p.box((0, 0.85, 0.23), (0.46, 0.55, 0.07), 'fabric', bevel=0.03, rot=('x', -8))
    for sx in (-1, 1):
        p.box((sx * 0.27, 0.66, 0), (0.05, 0.03, 0.3), 'dark', bevel=0.01)
        p.box((sx * 0.27, 0.58, 0.05), (0.03, 0.14, 0.03), 'dark')
    return md


@model
def light_panel():
    md = Model('light_panel')
    p = md.p('Panel')
    p.box((0, 0, 0), (1.6, 0.04, 0.35), 'trim', bevel=0.004)
    p.box((0, -0.0205, 0), (1.54, 0.002, 0.29), 'diffuser')
    return md


@model
def cable_tray():
    """A 6.18 m ladder tray for one row (along X), 0.4 m wide, with hanger rods up to the ceiling."""
    md = Model('cable_tray')
    p = md.p('Tray')
    L = 6.18
    for sz in (-1, 1):
        p.box((0, 0.05, sz * 0.2), (L, 0.1, 0.006), 'yellow', bevel=0.002)
        p.box((0, 0.003, sz * 0.19), (L, 0.006, 0.026), 'yellow')
    for i in range(int(L / 0.3) + 1):
        p.box((-L / 2 + 0.05 + i * 0.3, 0.008, 0), (0.03, 0.012, 0.4), 'yellow')
    for i in range(5):
        x = -L / 2 + 0.3 + i * (L - 0.6) / 4
        for sz in (-1, 1):
            p.cyl((x, 0.55, sz * 0.23), 0.005, 1.1, 'y', 'steel', segs=8)
        p.box((x, -0.01, 0), (0.04, 0.02, 0.5), 'steel')
    # A few bundles lying in the tray.
    for k, c in enumerate(('blue', 'fiber', 'blue', 'dark')):
        p.box((0, 0.02 + (k % 2) * 0.012, -0.12 + k * 0.08), (L - 0.1, 0.018, 0.04), c, bevel=0.006)
    return md


@model
def row_sign():
    md = Model('row_sign')
    p = md.p('Sign')
    p.box((0, 0, 0), (0.04, 0.42, 1.1), 'dark', bevel=0.006)
    p.box((0, 0, 0), (0.046, 0.38, 1.06), 'body')
    for sz in (-1, 1):
        p.cyl((0, 0.45, sz * 0.4), 0.004, 0.5, 'y', 'steel', segs=8)
    return md


@model
def rollup_door():
    """5.6 m wide, 2.7 m tall slatted curtain in steel guides, coil housing above. Faces -Z (into the hall)."""
    md = Model('rollup_door')
    p = md.p('Door')
    W, H = 5.6, 2.7
    for i in range(27):
        y = 0.05 + i * 0.1
        p.box((0, y, 0), (W, 0.094, 0.03), 'steel', bevel=0.004)
        p.box((0, y - 0.047, -0.016), (W, 0.006, 0.004), 'dark')
    for sx in (-1, 1):
        p.box((sx * (W / 2 + 0.06), H / 2 + 0.15, 0), (0.12, H + 0.3, 0.12), 'trim', bevel=0.004)
    p.box((0, H + 0.1, -0.2), (W + 0.3, 0.5, 0.45), 'trim', bevel=0.01)
    p.box((0, 0.025, -0.02), (W, 0.05, 0.05), 'rubber')
    handle(p, 0, 0.6, -0.015, 0.25, 0.03, 0.05, m='dark', t=0.02)
    p.box((W / 2 + 0.25, 1.3, -0.02), (0.12, 0.2, 0.06), 'yellow', bevel=0.01)  # control box
    p.box((W / 2 + 0.25, 1.33, -0.052), (0.03, 0.03, 0.004), 'lcd')
    p.box((W / 2 + 0.25, 1.27, -0.052), (0.03, 0.03, 0.004), 'red')
    return md


# ============================================================ building plant
@model
def crac():
    """A computer-room air conditioner: 1.8 m wide, 1.95 m tall, 0.85 m deep, facing -Z. The front
    doors ("Panel") come off for filter service; the filter bank sits right behind them."""
    md = Model('crac')
    c, pnl = md.p('Cabinet'), md.p('Panel')
    W, H, D = 1.8, 1.95, 0.85
    c.box((0, H / 2, 0), (W, H, D), 'body', bevel=0.004)
    c.box((0, 0.05, 0), (W - 0.02, 0.1, D - 0.02), 'dark')
    for i in range(12):  # return-air intake on the roof
        c.box((-0.8 + i * 0.145, H + 0.002, 0), (0.1, 0.004, D - 0.12), 'dark')
    c.box((0, 1.38, -D / 2 - 0.001), (W - 0.12, 0.6, 0.004), 'dark')  # filter cavity behind the doors
    c.box((0, 0.55, -D / 2 - 0.001), (W - 0.12, 0.7, 0.004), 'steel')  # fan section
    for i in range(3):
        c.cyl((-0.55 + i * 0.55, 0.55, -D / 2 - 0.004), 0.22, 0.004, 'z', 'dark', segs=20)
    # The doors: two leaves with grilles, handles and the controller display.
    pnl.box((0, H / 2 + 0.03, -D / 2 - 0.007), (W - 0.02, H - 0.16, 0.012), 'body', bevel=0.002)
    pnl.box((0, H / 2 + 0.03, -D / 2 - 0.0135), (0.004, H - 0.18, 0.001), 'dark')
    for sx in (-1, 1):
        grille(pnl, sx * 0.45 - 0.36, sx * 0.45 + 0.36, 1.12, 1.66, -D / 2 - 0.014, 10, fill=0.45, vertical=False)
        handle(pnl, sx * 0.05, 1.0, -D / 2 - 0.013, 0.014, 0.2, 0.03)
    pnl.box((0.62, 1.78, -D / 2 - 0.0145), (0.24, 0.13, 0.003), 'dark')
    pnl.box((0.62, 1.78, -D / 2 - 0.0162), (0.2, 0.1, 0.0006), 'lcd')
    pnl.box((-0.62, 1.78, -D / 2 - 0.0142), (0.2, 0.05, 0.001), 'label')
    return md


@model
def plant_panel():
    """The plant wall by the entrance, facing -Z: generator 1's controller cabinet (+X) and the fire
    alarm panel (-X), with conduit up to the ceiling. Back flat against the wall at +Z."""
    md = Model('plant_panel')
    p = md.p('Panels')
    d = 0.24
    # Generator controller.
    p.box((0.45, 1.25, 0), (0.8, 1.3, d), 'steel', bevel=0.004)
    p.box((0.45, 1.5, -d / 2 - 0.002), (0.46, 0.3, 0.004), 'dark')
    p.box((0.45, 1.5, -d / 2 - 0.0045), (0.42, 0.26, 0.0006), 'lcd')
    p.cyl((0.16, 1.08, -d / 2 - 0.012), 0.035, 0.024, 'z', 'red', segs=18)  # E-stop mushroom
    p.box((0.16, 1.08, -d / 2 - 0.002), (0.1, 0.1, 0.004), 'yellow')
    for i in range(4):
        p.cyl((0.36 + i * 0.09, 1.08, -d / 2 - 0.006), 0.018, 0.012, 'z', 'trim' if i else 'blue', segs=14)
    p.box((0.45, 0.85, -d / 2 - 0.0015), (0.5, 0.06, 0.002), 'label')
    p.box((0.45, 1.97, -d / 2 - 0.0015), (0.3, 0.05, 0.002), 'label')
    # Fire alarm panel.
    p.box((-0.5, 1.45, 0.03), (0.56, 0.72, d - 0.06), 'red', bevel=0.004)
    p.box((-0.5, 1.6, -d / 2 + 0.028), (0.3, 0.14, 0.004), 'dark')
    p.box((-0.5, 1.6, -d / 2 + 0.0255), (0.27, 0.11, 0.0006), 'lcd')
    for i in range(5):
        p.box((-0.66 + i * 0.08, 1.35, -d / 2 + 0.027), (0.05, 0.035, 0.006), 'trim')
    p.box((-0.5, 1.2, -d / 2 + 0.028), (0.4, 0.06, 0.002), 'label')
    # Conduit to the ceiling.
    for x in (0.25, 0.65, -0.5):
        p.box((x, 2.75, 0.06), (0.05, 1.7, 0.05), 'steel')
    return md


@model
def kiosk():
    """The visitor sign-in kiosk: a pedestal with an angled touchscreen and a badge printer, facing -Z."""
    md = Model('kiosk')
    p, s = md.p('Kiosk'), md.p('Screen')
    p.box((0, 0.03, 0), (0.6, 0.06, 0.5), 'dark', bevel=0.01)
    p.box((0, 0.55, 0.02), (0.42, 1.0, 0.34), 'body', bevel=0.01)
    p.box((0, 1.07, -0.02), (0.56, 0.05, 0.46), 'steel', bevel=0.008, rot=('x', 12))
    p.box((0, 1.27, 0.08), (0.46, 0.32, 0.04), 'dark', bevel=0.006, rot=('x', -18))
    s.box((0, 1.27, 0.058), (0.42, 0.28, 0.002), 'screen', rot=('x', -18))
    p.box((0.2, 1.12, -0.12), (0.12, 0.08, 0.14), 'trim', bevel=0.006)  # badge printer
    p.box((0.2, 1.165, -0.17), (0.08, 0.004, 0.04), 'dark')
    p.box((0, 0.7, -0.152), (0.3, 0.12, 0.002), 'label')
    return md


# ============================================================ build
if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    names = [n for n in MODELS if not ONLY or n in ONLY]
    for n in names:
        export(MODELS[n]())
    print(f'[models] wrote {len(names)} models to {OUT}')
