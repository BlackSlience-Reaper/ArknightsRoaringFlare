extends SceneTree

# 从原版 PCK 导出贴图做画风参考（只放 art/reference/，不入库）。
# Usage: <game_bin> --headless --path <dir> -s export_vanilla.gd -- <game_pck> <out_dir> <res_path> [<res_path> ...]
# res_path 可以是贴图（png/tres）或场景（tscn：导出其中所有 TextureRect/Sprite2D 的贴图，文件名 <场景>__<节点>.png）。

func _save(tex: Texture2D, out: String) -> void:
	if tex == null:
		print("NULL ", out)
		return
	var img: Image
	if tex is AtlasTexture:
		# 图集是压缩格式，AtlasTexture.get_image 会在压缩图上 blit 失败（得到空图）：先解压整张图集再取子区域。
		var atlas_img: Image = tex.atlas.get_image()
		if atlas_img.is_compressed():
			atlas_img.decompress()
		img = atlas_img.get_region(Rect2i(tex.region))
	else:
		img = tex.get_image()
	if img == null:
		print("NOIMG ", out)
		return
	if img.is_compressed():
		img.decompress()
	img.save_png(out)
	print("OK ", out, " ", img.get_width(), "x", img.get_height())


func _walk(node: Node, base: String, out_dir: String) -> void:
	var tex = null
	if node is TextureRect:
		tex = node.texture
	elif node is Sprite2D:
		tex = node.texture
	if tex != null:
		var r := ""
		if node is Control:
			r = " rect=%s,%s size=%s" % [node.position, node.get_rect().end, node.size]
		print("NODE ", base, " ", node.get_path(), " ", tex.resource_path, r)
		_save(tex, "%s/%s__%s.png" % [out_dir, base, String(node.name)])
	for c in node.get_children():
		_walk(c, base, out_dir)


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0])
	var out_dir: String = args[1]
	DirAccess.make_dir_recursive_absolute(out_dir)
	for i in range(2, args.size()):
		var p: String = args[i]
		var res = ResourceLoader.load(p)
		if res == null:
			print("MISSING ", p)
			continue
		var base := p.get_file().get_basename()
		if res is PackedScene:
			var inst: Node = res.instantiate()
			_walk(inst, base, out_dir)
			inst.free()
		elif res is Texture2D:
			_save(res, "%s/%s.png" % [out_dir, base])
	quit()
