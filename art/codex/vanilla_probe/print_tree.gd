extends SceneTree

# 打印原版场景的节点树（类型、可见性、贴图/材质），核对继承场景时要覆盖哪些节点。
# Usage: <game_bin> --headless --path <dir> -s print_tree.gd -- <game_pck> <res_scene> [...]

func _dump(node: Node, depth: int) -> void:
	var extra := ""
	if node is CanvasItem:
		extra += " visible=%s" % node.visible
	if "texture" in node and node.texture != null:
		extra += " tex=%s" % node.texture.resource_path
	if node is Control:
		extra += " pos=%s size=%s" % [node.position, node.size]
	elif node is Node2D:
		extra += " pos=%s scale=%s" % [node.position, node.scale]
	if node.get_script() != null:
		extra += " script=%s" % node.get_script().resource_path
	print("  ".repeat(depth), node.name, " <", node.get_class(), ">", extra)
	for c in node.get_children():
		_dump(c, depth + 1)


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0])
	for i in range(1, args.size()):
		print("## ", args[i])
		var packed = ResourceLoader.load(args[i])
		if packed == null:
			print("MISSING")
			continue
		var inst: Node = packed.instantiate()
		_dump(inst, 0)
		inst.free()
	quit()
