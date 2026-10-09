extends SceneTree

# 只读导出原版第三幕“荣耀”的战斗背景图层、背景根场景、休息处场景与地图底图，供本模组美术对照（输出到 art/reference/，不入库）。
# Usage: <godot> --headless --path <vanilla_probe> -s extract_glory.gd -- <game_pck> <out_dir>

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out: String = args[1]
	DirAccess.make_dir_recursive_absolute(out + "/layers")
	DirAccess.make_dir_recursive_absolute(out + "/rest_site")
	DirAccess.make_dir_recursive_absolute(out + "/map")
	if not ProjectSettings.load_resource_pack(args[0], false):
		push_error("cannot load pck")
		quit(1)
		return
	var dir := "res://scenes/backgrounds/glory/layers"
	var names := []
	for f in ResourceLoader.list_directory(dir):
		var n: String = f.trim_suffix(".remap")
		if n.ends_with(".tscn") or n.ends_with(".scn"):
			names.append(n)
	names.sort()
	for n in names:
		var packed = load(dir + "/" + n)
		var root: Node = packed.instantiate()
		_walk(root, n.get_basename(), out + "/layers", "")
		root.free()
	for scene in ["res://scenes/backgrounds/glory/glory_background.tscn", "res://scenes/rest_site/glory_rest_site.tscn"]:
		print("=== ", scene)
		var p = load(scene)
		var r: Node = p.instantiate()
		_walk(r, scene.get_file().get_basename(), out + "/rest_site", "")
		r.free()
	for part in ["top", "middle", "bottom"]:
		var path := "res://images/packed/map/map_bgs/glory/map_%s_glory.png" % part
		var tex: Texture2D = load(path)
		var img := tex.get_image()
		if img.is_compressed():
			img.decompress()
		img.save_png("%s/map/map_%s_glory.png" % [out, part])
		print("MAP ", path, " ", img.get_size())
	quit(0)

func _walk(node: Node, scene: String, out: String, path: String) -> void:
	var here := path + "/" + str(node.name)
	var info := "NODE %s %s type=%s visible=%s" % [scene, here, node.get_class(), str(node.get("visible"))]
	if node is Control:
		info += " pos=%s size=%s scale=%s offsets=(%s,%s,%s,%s) anchors=(%s,%s,%s,%s)" % [node.position, node.size, node.scale, node.offset_left, node.offset_top, node.offset_right, node.offset_bottom, node.anchor_left, node.anchor_top, node.anchor_right, node.anchor_bottom]
	elif node is Node2D:
		info += " pos=%s scale=%s" % [node.position, node.scale]
	if node.get_script() != null:
		info += " script=%s" % node.get_script().resource_path
	var tex: Texture2D = null
	if node is TextureRect or node is Sprite2D:
		tex = node.texture
	if tex != null:
		info += " tex=%s" % tex.resource_path
		var img := tex.get_image()
		if img != null:
			if img.is_compressed():
				img.decompress()
			img.save_png("%s/%s__%s.png" % [out, scene, str(node.name)])
			info += " img=%s" % img.get_size()
	print(info)
	for child in node.get_children():
		_walk(child, scene, out, here)
