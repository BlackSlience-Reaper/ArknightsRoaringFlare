extends SceneTree

# 只读打印原版场景节点树（TextureRect 的偏移、贴图尺寸、拉伸模式），并可把贴图导出成 PNG。
# Usage: <godot> --headless --path <vanilla_probe> -s dump_scene.gd -- <game_pck> <out_dir|-> <res_scene> [...]

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0], false)
	var out: String = args[1]
	if out != "-":
		DirAccess.make_dir_recursive_absolute(out)
	for i in range(2, args.size()):
		print("=== ", args[i])
		var r: Node = load(args[i]).instantiate()
		_walk(r, args[i].get_file().get_basename(), out, "")
		r.free()
	quit(0)

func _walk(node: Node, scene: String, out: String, path: String) -> void:
	var here := path + "/" + str(node.name)
	var info := "NODE %s type=%s visible=%s" % [here, node.get_class(), str(node.get("visible"))]
	if node is Control:
		info += " offsets=(%.2f,%.2f,%.2f,%.2f) anchors=(%s,%s,%s,%s) scale=%s" % [node.offset_left, node.offset_top, node.offset_right, node.offset_bottom, node.anchor_left, node.anchor_top, node.anchor_right, node.anchor_bottom, node.scale]
	elif node is Node2D:
		info += " pos=%s scale=%s" % [node.position, node.scale]
	if node is TextureRect:
		info += " expand=%d stretch=%d" % [node.expand_mode, node.stretch_mode]
	var tex: Texture2D = null
	if node is TextureRect or node is Sprite2D:
		tex = node.texture
	if tex != null:
		info += " tex=%s %dx%d" % [tex.resource_path, tex.get_width(), tex.get_height()]
		if out != "-":
			var img := tex.get_image()
			if img != null:
				if img.is_compressed():
					img.decompress()
				img.save_png("%s/%s__%s.png" % [out, scene, str(node.name)])
	print(info)
	for child in node.get_children():
		_walk(child, scene, out, here)
