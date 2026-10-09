extends SceneTree

# 把几只原版怪物的 creature_visuals 场景并排渲染成一张图（画风参考，只放 art/reference/，不入库）。
# 只能窗口模式（headless 拿不到画面）。
# Usage: <game_bin> --path <dir> -s render_vanilla.gd -- <game_pck> <out_png> <name> [<name> ...]

const CELL := Vector2(420, 460)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0])
	var out_png: String = args[1]
	var names := args.slice(2)
	root.size = Vector2i(int(CELL.x) * names.size(), int(CELL.y))
	var bg := ColorRect.new()
	bg.color = Color(0.32, 0.34, 0.36)
	bg.size = Vector2(root.size)
	root.add_child(bg)
	var i := 0
	for n in names:
		var packed = ResourceLoader.load("res://scenes/creature_visuals/%s.tscn" % n)
		if packed == null:
			print("missing ", n)
			continue
		var inst: Node2D = packed.instantiate()
		root.add_child(inst)
		inst.position = Vector2(CELL.x * (i + 0.5), CELL.y - 40)
		var vis = inst.get_node_or_null("%Visuals")
		if vis != null and vis.get_class() == "SpineSprite":
			var data = vis.skeleton_data_res
			var idle := ""
			for a in data.get_animations():
				var an: String = a.get_name()
				print(n, " anim ", an)
				if idle == "" and an.to_lower().contains("idle"):
					idle = an
			if idle != "":
				vis.get_animation_state().set_animation(idle, true, 0)
		i += 1
	for _f in range(40):
		await process_frame
	var img := root.get_texture().get_image()
	img.save_png(out_png)
	print("saved ", out_png)
	quit()
