import bpy, os, sys, json
import numpy as np

args = json.loads(sys.argv[-1])
outfits = json.load(open(args["outfits"]))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.preferences.addon_enable(module="bl_ext.user_default.mpfb")
from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.targetservice import TargetService
from bl_ext.user_default.mpfb.services.exportservice import ExportService
from bl_ext.user_default.mpfb.services.locationservice import LocationService

D = LocationService.get_user_data()
OUT = args["out"]
TEX = os.path.join(os.path.dirname(OUT), "textures")
os.makedirs(TEX, exist_ok=True)
MAX = args.get("maxtex", 1024)
NAME = args["name"]
WEB = bool(args.get("web"))
if WEB:
    TEX = os.path.dirname(OUT)


def shrink(px, w, h):
    step = max(1, max(w, h) // MAX)
    if step > 1:
        px = px[::step, ::step]
    return px, px.shape[1], px.shape[0]


def save_image(filename, px):
    h, w = px.shape[0], px.shape[1]
    path = os.path.join(TEX, filename)
    if filename in bpy.data.images:
        return bpy.data.images[filename]
    img = bpy.data.images.new(filename, w, h, alpha=True)
    img.pixels[:] = px.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def base_nodes(obj):
    for m in obj.data.materials:
        if not m or not m.use_nodes:
            continue
        base = set()
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                for link in n.outputs["Color"].links:
                    if link.to_socket.name == "Base Color":
                        base.add(n.image.name)
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image and n.image.name in base:
                yield n


def camo(w, h):
    from scipy.ndimage import gaussian_filter
    rng = np.random.default_rng(7)
    field = np.zeros((h, w), dtype=np.float32)
    for scale in (8, 16, 32):
        g = rng.random((scale, scale)).astype(np.float32)
        field += g[np.ix_(np.arange(h) * scale // h, np.arange(w) * scale // w)] / scale
    field = gaussian_filter(field, sigma=w / 96)
    q = np.quantile(field, [0.3, 0.55, 0.8])
    palette = np.array([[0.20, 0.24, 0.14], [0.36, 0.38, 0.22], [0.30, 0.24, 0.16], [0.12, 0.12, 0.10]], dtype=np.float32) * 1.6
    return palette[np.digitize(field, q)]


def flatten(lum, alpha, sigma):
    from scipy.ndimage import gaussian_filter
    a = (alpha > 0.05).astype(np.float32)
    num = gaussian_filter(lum * a, sigma)
    den = gaussian_filter(a, sigma)
    local = np.where(den > 1e-3, num / np.maximum(den, 1e-3), 1.0)
    detail = np.where(a > 0, lum / np.maximum(local, 1e-3), 1.0)
    detail = 1.0 + (np.clip(detail, 0.0, 3.0) - 1.0) * 0.6
    return detail


chest_band = None


def fix_normals(obj):
    import bmesh
    from mathutils.kdtree import KDTree
    bverts = basemesh.data.vertices
    tree = KDTree(len(bverts))
    for i, v in enumerate(bverts):
        tree.insert(basemesh.matrix_world @ v.co, i)
    tree.balance()
    nm = basemesh.matrix_world.to_3x3().inverted().transposed()
    m = obj.matrix_world
    om = m.to_3x3().inverted().transposed()
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    flip = []
    for f in bm.faces:
        c = m @ f.calc_center_median()
        p, idx, dist = tree.find(c)
        if p is None or dist > 0.08 or chest_band is None:
            continue
        if not ((abs(p.z - chest_band) <= 0.1 and abs(p.x) <= 0.14) or (p.z > chest_band + 0.05 and abs(p.x) < 0.18)):
            continue
        bn = (nm @ bverts[idx].normal).normalized()
        if (om @ f.normal).dot(bn) < -0.2:
            flip.append(f)
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
        print("FLIP", obj.name, len(flip), "of", len(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def recolor(obj, rgb, filename, flat=False):
    for n in base_nodes(obj):
        if filename in bpy.data.images:
            n.image = bpy.data.images[filename]
            continue
        w, h = n.image.size
        px = np.array(n.image.pixels[:], dtype=np.float32).reshape(h, w, 4)
        lum = px[..., :3] @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
        uv = np.array([d.uv[:] for d in obj.data.uv_layers.active.data], dtype=np.float32) % 1.0
        samples = lum[np.clip((uv[:, 1] * h).astype(int), 0, h - 1), np.clip((uv[:, 0] * w).astype(int), 0, w - 1)]
        samples = samples[(samples > 0.001) & (samples < 0.9)]
        mean = float(np.median(samples)) if samples.size else 0.1
        color = camo(w, h) if rgb == "camo" else np.array(rgb, dtype=np.float32) / 255.0
        if flat:
            px[..., :3] = np.clip(flatten(lum, px[..., 3], max(w, h) / 24)[..., None] * color, 0.0, 1.0)
        else:
            px[..., :3] = np.clip(lum[..., None] / mean * color, 0.0, 1.0)
        px, _, _ = shrink(px, w, h)
        n.image = save_image(filename, px)


def lum_texture(obj, filename, also_orig=None, camo_name=None):
    for n in base_nodes(obj):
        w, h = n.image.size
        px = np.array(n.image.pixels[:], dtype=np.float32).reshape(h, w, 4)
        if also_orig and also_orig not in bpy.data.images:
            o, _, _ = shrink(px.copy(), w, h)
            save_image(also_orig, o)
        lum = px[..., :3] @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
        uv = np.array([d.uv[:] for d in obj.data.uv_layers.active.data], dtype=np.float32) % 1.0
        samples = lum[np.clip((uv[:, 1] * h).astype(int), 0, h - 1), np.clip((uv[:, 0] * w).astype(int), 0, w - 1)]
        samples = samples[(samples > 0.001) & (samples < 0.9)]
        mean = float(np.median(samples)) if samples.size else 0.1
        if camo_name and camo_name not in bpy.data.images:
            c = px.copy()
            c[..., :3] = np.clip(lum[..., None] / mean * camo(w, h), 0.0, 1.0)
            c, _, _ = shrink(c, w, h)
            save_image(camo_name, c)
        g = np.clip(lum / mean * 0.5, 0.0, 1.0)
        px[..., 0] = g
        px[..., 1] = g
        px[..., 2] = g
        px, _, _ = shrink(px, w, h)
        n.image = bpy.data.images[filename] if filename in bpy.data.images else save_image(filename, px)


def inflate(obj, offset):
    if not offset:
        return
    obj.data.update()
    for v in obj.data.vertices:
        v.co = v.co + v.normal * (offset / max(obj.scale[0], 1e-6))


def tint(obj, mult, filename):
    for n in base_nodes(obj):
        if filename in bpy.data.images:
            n.image = bpy.data.images[filename]
            continue
        w, h = n.image.size
        px = np.array(n.image.pixels[:], dtype=np.float32).reshape(h, w, 4)
        px[..., :3] = np.clip(px[..., :3] * np.array(mult, dtype=np.float32), 0.0, 1.0)
        px, _, _ = shrink(px, w, h)
        n.image = save_image(filename, px)


macro = TargetService.get_default_macro_info_dict()
macro.update(args["macro"])
macro["race"] = args["race"]
basemesh = HumanService.create_human(macro_detail_dict=macro, scale=0.1)
basemesh.name = NAME

feminine = {"chin/chin-width-decr": 0.5, "chin/chin-prominent-decr": 0.3, "chin/chin-jaw-drop-decr": 0.3, "nose/nose-scale-horiz-decr": 0.3, "nose/nose-volume-decr": 0.3, "eyebrows/eyebrows-trans-up": 0.2, "head/head-oval": 0.4, "neck/neck-scale-horiz-decr": 0.4, "mouth/mouth-upperlip-volume-incr": 0.2}
targets = {k: v * args.get("feminine", 1.0) for k, v in feminine.items()}
targets.update(args.get("targets", {}))
for target, weight in targets.items():
    target = target if target.endswith(".target.gz") else target + ".target.gz"
    TargetService.load_target(basemesh, os.path.join(LocationService.get_mpfb_data("targets"), target), weight=weight, name=os.path.basename(target).replace(".target.gz", ""))

rig = HumanService.add_builtin_rig(basemesh, "mixamo", import_weights=True)
rig.name = "Armature"
HumanService.set_character_skin(os.path.join(D, "skins", args["skin"], args["skin"] + ".mhmat"), basemesh, skin_type="GAMEENGINE", material_instances=False)


def add(kind, rel):
    return HumanService.add_mhclo_asset(os.path.join(D, rel), basemesh, asset_type=kind, subdiv_levels=0, material_type="GAMEENGINE")


eyes = add("Eyes", "eyes/high-poly/high-poly.mhclo")
eyes.name = f"{NAME}.eyes"
add("Eyebrows", f"eyebrows/{args['eyebrows']}/{args['eyebrows']}.mhclo").name = f"{NAME}.eyebrows"
add("Eyelashes", f"eyelashes/{args['eyelashes']}/{args['eyelashes']}.mhclo").name = f"{NAME}.eyelashes"
add("Teeth", "teeth/teeth_base/teeth_base.mhclo").name = f"{NAME}.teeth"
add("Tongue", "tongue/tongue01/tongue01.mhclo").name = f"{NAME}.tongue"
hair = add("Hair", f"hair/{args['hair']}/{args['hair']}.mhclo")
hair.name = f"{NAME}.hair"

if WEB:
    for n in base_nodes(basemesh):
        w, h = n.image.size
        px, _, _ = shrink(np.array(n.image.pixels[:], dtype=np.float32).reshape(h, w, 4), w, h)
        n.image = save_image(f"skin_{args['skin']}.png", px)
elif "skintint" in args:
    tint(basemesh, args["skintint"], f"{NAME}_skin.png")

eye_png = os.path.join(D, "eyes", "materials", args["eyecolor"] + "_eye.png")
for m in eyes.data.materials:
    for n in m.node_tree.nodes:
        if n.type == "TEX_IMAGE":
            n.image = bpy.data.images.load(eye_png, check_existing=True)
            break

if WEB:
    lum_texture(hair, f"lum_hair_{args['hair']}.png")
else:
    r, g, b = args["hairrgb"]
    recolor(hair, args["hairrgb"], f"hair_{args['hair']}_{r}_{g}_{b}_even.png", flat=True)

def entry_offset(e):
    return float(e[2]) if len(e) > 2 and e[2] else 0.0


def entry_layer(e):
    return int(e[3]) if len(e) > 3 and e[3] is not None else 30


piece_users = {}
entry_key = {}
for outfit, pieces in outfits.items():
    for entry in pieces:
        asset, rgb = entry[0], entry[1]
        offset = entry_offset(entry)
        if WEB:
            key = (asset, "web", 0.0, "")
        else:
            above = sorted(json.dumps([e[0], e[1], entry_offset(e), entry_layer(e)]) for e in pieces if entry_layer(e) > entry_layer(entry))
            key = (asset, json.dumps(rgb), offset, json.dumps([entry_layer(entry)] + above))
        piece_users.setdefault(key, []).append(outfit)
        entry_key[(outfit, asset)] = key

camo_assets = set(e[0] for ps in outfits.values() for e in ps if len(e) > 1 and e[1] == "camo")
outfit_groups = {o: set() for o in outfits}
key_object = {}
for (asset, rgbjson, offset, context), users in piece_users.items():
    rgb = None if WEB else json.loads(rgbjson)
    before = set(g.name for g in basemesh.vertex_groups)
    obj = add("Clothes", f"clothes/{asset}/{asset}.mhclo")
    created = set(g.name for g in basemesh.vertex_groups) - before
    group = "Delete." + asset.replace(" ", "_")
    for o in users:
        outfit_groups[o].add(group)
        outfit_groups[o].update(created)
    if WEB:
        lum_texture(obj, f"lum_{asset}.png", also_orig=f"orig_{asset}.png", camo_name=f"camo_{asset}.png" if asset in camo_assets else None)
    else:
        inflate(obj, offset)
        tag = "camo" if rgb == "camo" else ("orig" if rgb is None else "_".join(str(v) for v in rgb))
        if rgb is not None:
            recolor(obj, rgb, f"{asset}_{tag}.png")
    obj.name = f"piece__{'+'.join(users)}__{asset}"
    key_object[(asset, rgbjson, offset, context)] = obj
    print("PIECE", obj.name)

for mod in list(basemesh.modifiers):
    if mod.type == "MASK":
        basemesh.modifiers.remove(mod)

ExportService.bake_modifiers_remove_helpers(basemesh, bake_masks=False, bake_subdiv=False, remove_helpers=True, also_proxy=False)

for o in list(bpy.data.objects):
    if o.type == "MESH" and o.data.shape_keys:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)

from mathutils import Vector
from mathutils.kdtree import KDTree

if not WEB:
    _bm = [basemesh.matrix_world @ v.co for v in basemesh.data.vertices]
    _top = max(c.z for c in _bm)
    _front = [c for c in _bm if 0.03 < abs(c.x) < 0.16 and 0.62 * _top < c.z < 0.82 * _top]
    if _front:
        chest_band = min(_front, key=lambda c: c.y).z
    for _obj in set(key_object.values()):
        fix_normals(_obj)


def world_data(obj):
    m = obj.matrix_world
    nm = m.to_3x3().inverted().transposed()
    cos = [m @ v.co for v in obj.data.vertices]
    nos = [(nm @ v.normal).normalized() for v in obj.data.vertices]
    return cos, nos


def write_back(obj, cos):
    inv = obj.matrix_world.inverted()
    for v, c in zip(obj.data.vertices, cos):
        v.co = inv @ c
    obj.data.update()


def push_inside(low, high, reach=0.05, gap=0.006, side=0.02, only=None):
    hc, hn = world_data(high)
    tree = KDTree(len(hc))
    for i, c in enumerate(hc):
        tree.insert(c, i)
    tree.balance()
    lc, ln = world_data(low)
    moved = 0
    for i, v in enumerate(lc):
        if only is not None and not only(v):
            continue
        p, idx, dist = tree.find(v)
        if p is None or dist > reach:
            continue
        n = ln[i]
        if n.dot(hn[idx]) < 0:
            n = hn[idx]
        d = (v - p).dot(n)
        t2 = dist * dist - d * d
        if d > -gap and t2 < side * side:
            lc[i] = v - n * (d + gap)
            moved += 1
    if moved:
        write_back(low, lc)
    return moved


if not WEB:
    ordered = sorted(key_object.items(), key=lambda kv: -json.loads(kv[0][3])[0])
    for key, obj in ordered:
        outfit = piece_users[key][0]
        mine = json.loads(key[3])[0]
        for e in outfits[outfit]:
            if entry_layer(e) > mine:
                high = key_object[entry_key[(outfit, e[0])]]
                n = push_inside(obj, high)
                if n:
                    print("LAYER", obj.name, "under", high.name, n)

hair_axis = 0.0
bm_cos = [basemesh.matrix_world @ v.co for v in basemesh.data.vertices]
ys = sorted(c.y for c in bm_cos if 1.1 < c.z < 1.4 and abs(c.x) < 0.12)
if ys:
    hair_axis = ys[len(ys) // 2]


def push_outside(hair_obj, pieces, reach=0.15, gap=0.008, slices=128, cell=0.01):
    import math
    grid = {}
    for piece in pieces:
        pc, _ = world_data(piece)
        for c in pc:
            ang = math.atan2(c.y - hair_axis, c.x)
            k = (int((ang + math.pi) / (2 * math.pi) * slices), int(math.floor(c.z / cell)))
            grid.setdefault(k, []).append(math.hypot(c.x, c.y - hair_axis))
    hc, _ = world_data(hair_obj)
    moved = 0
    for i, c in enumerate(hc):
        r = math.hypot(c.x, c.y - hair_axis)
        if r < 1e-4:
            continue
        ang = math.atan2(c.y - hair_axis, c.x)
        s0 = int((ang + math.pi) / (2 * math.pi) * slices)
        h0 = int(math.floor(c.z / cell))
        best = -1.0
        for ds in (-1, 0, 1):
            for dh in (-1, 0, 1):
                for pr in grid.get(((s0 + ds) % slices, h0 + dh), ()):
                    if pr < r + reach and pr > best:
                        best = pr
        if best < 0 or r >= best + gap:
            continue
        k = (best + gap) / r
        hc[i] = Vector((c.x * k, hair_axis + (c.y - hair_axis) * k, c.z))
        moved += 1
    if moved:
        write_back(hair_obj, hc)
    return moved


if not WEB:
    hair_users = {}
    for outfit in outfits:
        objs = frozenset(key_object[entry_key[(outfit, e[0])]].name for e in outfits[outfit])
        hair_users.setdefault(objs, []).append(outfit)
    for names, users in hair_users.items():
        h = hair.copy()
        h.data = hair.data.copy()
        bpy.context.scene.collection.objects.link(h)
        h.name = f"hair__{'+'.join(users)}"
        n = push_outside(h, [bpy.data.objects[nm] for nm in names])
        print("HAIR", h.name, n)
    bpy.data.objects.remove(hair, do_unlink=True)

bm_all = [basemesh.matrix_world @ v.co for v in basemesh.data.vertices]
chest_z = chest_band


def in_keep(c):
    if chest_z is None:
        return False
    return (abs(c.z - chest_z) < 0.1 and abs(c.x) < 0.16) or (c.z > chest_z + 0.05 and abs(c.x) < 0.2)


keep = set()
if chest_z is not None:
    for i, c in enumerate(bm_all):
        if in_keep(c):
            keep.add(i)
print("CHEST", chest_z, len(keep))


def outfit_pieces(outfit):
    if WEB:
        return ()
    return tuple(sorted(key_object[entry_key[(outfit, e[0])]].name for e in outfits[outfit]))


body_users = {}
for outfit, groups in outfit_groups.items():
    gk = frozenset(g for g in groups if g in basemesh.vertex_groups)
    body_users.setdefault((gk, outfit_pieces(outfit) if gk else ()), []).append(outfit)

for (groups, piece_names), users in body_users.items():
    body = basemesh.copy()
    body.data = basemesh.data.copy()
    bpy.context.scene.collection.objects.link(body)
    body.name = f"body__{'+'.join(users)}"
    idx = [body.vertex_groups[g].index for g in groups if g in body.vertex_groups]
    kill = []
    tuck = set()
    if idx:
        for v in body.data.vertices:
            for ge in v.groups:
                if ge.group in idx and ge.weight > 0.5:
                    if v.index in keep:
                        c = body.matrix_world @ v.co
                        tuck.add((round(c.x, 5), round(c.y, 5), round(c.z, 5)))
                    else:
                        kill.append(v.index)
                    break
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in kill], context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    for vg in list(body.vertex_groups):
        if vg.name.startswith("Delete."):
            body.vertex_groups.remove(vg)
    for pn in piece_names:
        n = push_inside(body, bpy.data.objects[pn], reach=0.06, gap=0.004, side=0.02, only=lambda c: (round(c.x, 5), round(c.y, 5), round(c.z, 5)) in tuck)
        if n:
            print("CHEST", body.name, "under", pn, n)
    print("BODY", body.name, len(kill), "removed", len(body.data.vertices), "left")

bpy.data.objects.remove(basemesh, do_unlink=True)

if args.get("blend"):
    bpy.ops.wm.save_as_mainfile(filepath=OUT.replace(".fbx", ".blend"))

if WEB:
    for o in bpy.data.objects:
        o.name = o.name.replace(".", "_")
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if o.type in ("MESH", "ARMATURE"):
            o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLTF_SEPARATE", use_selection=True, export_texture_dir="", export_draco_mesh_compression_enable=True, export_draco_mesh_compression_level=7, export_animations=False, export_morph=False, export_image_format="AUTO")
    print("EXPORTED", OUT)
    sys.exit(0)

bpy.ops.object.select_all(action="DESELECT")
for o in bpy.data.objects:
    if o.type in ("MESH", "ARMATURE"):
        o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, path_mode="STRIP", embed_textures=False, apply_scale_options="FBX_SCALE_ALL", bake_anim=False, mesh_smooth_type="FACE")

used = set()
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    for m in o.data.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image and n.image.filepath:
                    used.add(bpy.path.abspath(n.image.filepath))
import shutil
from PIL import Image as PILImage
for src in used:
    dst = os.path.join(TEX, os.path.basename(src))
    if os.path.abspath(src) != os.path.abspath(dst) and os.path.exists(src) and not os.path.exists(dst):
        shutil.copyfile(src, dst)
    if os.path.exists(dst):
        im = PILImage.open(dst)
        if max(im.size) > MAX:
            im.thumbnail((MAX, MAX))
            im.save(dst)
print("EXPORTED", OUT, len([o for o in bpy.data.objects if o.type == "MESH"]), "meshes")
