# Converts an FBX into a GLB the mod can embed as an item pickup model (GlbLoader reads it at runtime).
# Keeps meshes only (cameras/lights dropped), bakes object transforms, and leaves images out: the
# mod textures the model with the item's embedded icon instead.
#
# Usage:
#   "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b --factory-startup -P tools\fbx_to_glb.py -- in.fbx out.glb
import sys

import bpy
import io_scene_fbx.import_fbx as fbx

# Blender 5.1's FBX importer crashes on lights (Cycles dropped lamp.cycles.cast_shadow); skip that line.
src = open(fbx.__file__, encoding="utf-8").read().replace("lamp.cycles.cast_shadow = lamp.use_shadow", "pass")
exec(compile(src, fbx.__file__, "exec"), fbx.__dict__)

src_path, out_path = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src_path)
for o in list(bpy.data.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)
# Bake each mesh's world transform into a fresh object. (Baking in place isn't enough: the glTF
# exporter still writes the FBX object's original scale onto the node.)
for o in list(bpy.data.objects):
    mesh = o.data.copy()
    mesh.transform(o.matrix_world)
    baked = bpy.data.objects.new(o.name, mesh)
    bpy.context.scene.collection.objects.link(baked)
    bpy.data.objects.remove(o)
bpy.context.view_layer.update()
bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', export_image_format='NONE',
                          export_cameras=False, export_lights=False, export_animations=False)
print("wrote", out_path)
