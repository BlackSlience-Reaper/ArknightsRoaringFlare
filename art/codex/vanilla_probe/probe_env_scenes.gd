extends SceneTree

# 探针：在导入过本模组环境美术的临时工程里，挂上游戏 PCK（提供原版荣耀背景根场景与蜂巢休息处），
# 逐个加载并实例化本模组的战斗背景根场景、全部图层场景、休息处场景，打印贴图路径与尺寸、关键节点是否存在；再加载地图三段。
# Usage: <godot> --headless --path <临时导入工程> -s <本文件> -- <game_pck>

const R := "res://ArknightsRoaringFlare/"
const ACT := "roaring_flare_act"

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if not ProjectSettings.load_resource_pack(args[0], false):
		print("FAIL cannot load game pck")
		quit(1)
		return
	var fails := 0
	var bg_root: PackedScene = load(R + "scenes/backgrounds/%s/%s_background.tscn" % [ACT, ACT])
	if bg_root == null:
		print("FAIL background root")
		fails += 1
	else:
		var n: Node = bg_root.instantiate()
		for child in ["Layer_00", "Layer_01", "Layer_02", "Layer_03", "Foreground"]:
			print("ROOT has ", child, ": ", n.has_node(child))
			if not n.has_node(child):
				fails += 1
		n.free()
	var layer_dir := R + "scenes/backgrounds/%s/layers" % ACT
	var files := ResourceLoader.list_directory(layer_dir)
	print("LAYER FILES ", files)
	for f in files:
		if f.ends_with("/"):
			print("FAIL subdirectory in layers: ", f)
			fails += 1
			continue
		var p: PackedScene = load(layer_dir + "/" + f.trim_suffix(".remap"))
		var n: Node = p.instantiate()
		var tex: Texture2D = n.texture
		print("LAYER %s node=%s tex=%s %dx%d rect=%s" % [f, n.name, tex.resource_path, tex.get_width(), tex.get_height(), Rect2(n.offset_left, n.offset_top, n.offset_right - n.offset_left, n.offset_bottom - n.offset_top)])
		n.free()
	var rest: PackedScene = load(R + "scenes/rest_site/%s_rest_site.tscn" % ACT)
	if rest == null:
		print("FAIL rest site")
		fails += 1
	else:
		var n: Node = rest.instantiate()
		for path in ["RestSiteBG", "RestSiteLLog", "RestSiteRLog", "RestSiteFireLogs", "RestSiteLighting/RestSiteLLog2", "RestSiteLighting/RestSiteRLog2", "RestSiteForegroundLeft", "RestSiteForegroundRight", "RestSiteLighting/SteppedFire"]:
			var c := n.get_node_or_null(path)
			if c == null:
				print("FAIL missing ", path)
				fails += 1
				continue
			var info := "REST %s visible=%s" % [path, c.visible]
			if c is TextureRect:
				info += " tex=%s %dx%d rect=(%.1f,%.1f,%.1f,%.1f)" % [c.texture.resource_path, c.texture.get_width(), c.texture.get_height(), c.offset_left, c.offset_top, c.offset_right, c.offset_bottom]
			print(info)
		for hidden in ["RestSiteLighting/WallLight1", "RestSiteLighting/roots", "b_blob", "overlay_vfx/bugs_1"]:
			print("REST hidden ", hidden, " visible=", n.get_node(hidden).visible)
		n.free()
	for part in ["top", "middle", "bottom"]:
		var t: Texture2D = load(R + "images/map/map_%s_%s.png" % [part, ACT])
		print("MAP %s %s" % [part, "MISSING" if t == null else "%dx%d" % [t.get_width(), t.get_height()]])
		if t == null:
			fails += 1
	print("PROBE DONE fails=", fails)
	quit(fails)
