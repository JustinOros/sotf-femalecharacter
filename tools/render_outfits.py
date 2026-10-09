import bpy, sys, mathutils
blend, out, names = sys.argv[-3], sys.argv[-2], sys.argv[-1].split(",")
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.device = "CPU"
sc.cycles.samples = 16
sc.render.resolution_x, sc.render.resolution_y = 500, 750
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.37, 0.4, 1)
for loc, e in [((2, -3, 3), 400), ((-3, -2, 2), 200), ((0, 3, 3), 200)]:
    l = bpy.data.lights.new("l", "AREA"); l.energy = e; l.size = 2
    o = bpy.data.objects.new("l", l); o.location = loc; sc.collection.objects.link(o)
    o.rotation_euler = (mathutils.Vector((0, 0, 1.0)) - mathutils.Vector(loc)).to_track_quat("-Z", "Y").to_euler()
cam = bpy.data.cameras.new("c"); cam.lens = 70
co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co); sc.camera = co
co.location = (0, -5.2, 0.9)
co.rotation_euler = (mathutils.Vector((0, 0, 0.85)) - co.location).to_track_quat("-Z", "Y").to_euler()
for name in names:
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        n = o.name
        if n.startswith("body__") or n.startswith("piece__"):
            show = name in n.split("__")[1].split("+")
        else:
            show = True
        o.hide_render = not show
    sc.render.filepath = f"{out}_{name}.png"
    bpy.ops.render.render(write_still=True)
