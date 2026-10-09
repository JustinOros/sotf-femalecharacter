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


def recolor(obj, rgb, filename):
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
        px[..., :3] = np.clip(lum[..., None] / mean * color, 0.0, 1.0)
        px, _, _ = shrink(px, w, h)
        n.image = save_image(filename, px)


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

if "skintint" in args:
    tint(basemesh, args["skintint"], f"{NAME}_skin.png")

eye_png = os.path.join(D, "eyes", "materials", args["eyecolor"] + "_eye.png")
for m in eyes.data.materials:
    for n in m.node_tree.nodes:
        if n.type == "TEX_IMAGE":
            n.image = bpy.data.images.load(eye_png, check_existing=True)
            break

r, g, b = args["hairrgb"]
recolor(hair, args["hairrgb"], f"hair_{args['hair']}_{r}_{g}_{b}.png")

piece_users = {}
for outfit, pieces in outfits.items():
    for asset, rgb in pieces:
        key = (asset, json.dumps(rgb))
        piece_users.setdefault(key, []).append(outfit)

outfit_groups = {o: set() for o in outfits}
for (asset, rgbjson), users in piece_users.items():
    rgb = json.loads(rgbjson)
    before = set(g.name for g in basemesh.vertex_groups)
    obj = add("Clothes", f"clothes/{asset}/{asset}.mhclo")
    created = set(g.name for g in basemesh.vertex_groups) - before
    group = "Delete." + asset.replace(" ", "_")
    for o in users:
        outfit_groups[o].add(group)
        outfit_groups[o].update(created)
    tag = "camo" if rgb == "camo" else ("orig" if rgb is None else "_".join(str(v) for v in rgb))
    if rgb is not None:
        recolor(obj, rgb, f"{asset}_{tag}.png")
    obj.name = f"piece__{'+'.join(users)}__{asset}"
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

body_users = {}
for outfit, groups in outfit_groups.items():
    body_users.setdefault(frozenset(g for g in groups if g in basemesh.vertex_groups), []).append(outfit)

for groups, users in body_users.items():
    body = basemesh.copy()
    body.data = basemesh.data.copy()
    bpy.context.scene.collection.objects.link(body)
    body.name = f"body__{'+'.join(users)}"
    idx = [body.vertex_groups[g].index for g in groups if g in body.vertex_groups]
    kill = []
    if idx:
        for v in body.data.vertices:
            for ge in v.groups:
                if ge.group in idx and ge.weight > 0.5:
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
    print("BODY", body.name, len(kill), "removed", len(body.data.vertices), "left")

bpy.data.objects.remove(basemesh, do_unlink=True)

if args.get("blend"):
    bpy.ops.wm.save_as_mainfile(filepath=OUT.replace(".fbx", ".blend"))

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
