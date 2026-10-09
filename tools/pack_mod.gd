extends SceneTree

# Usage: godot --headless --path <tool_project> -s pack_mod.gd -- <manifest_json> <output_pck> [import_project_root]
# <manifest_json> 所在目录打包到 res://<mod_id>/；本模组所有资源都在这个命名空间里，原版路径下不放任何文件
# （幕、遭遇战、能力的资源路径经 RitsuLib 的 AssetProfile 接到游戏上）。
# 给出 [import_project_root] 时，改从已用 Godot 编辑器 --import 过的工程里取 res://<mod_id>/ 内容：
# 图片源文件换成 .import 重映射 + .godot/imported/*.ctex。Spine 图集按 ResourceLoader 读贴图，
# 运行时没有导入器，裸 PNG 会加载失败。


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2 and args.size() != 3:
		push_error("Usage: godot --headless --path <tool_project> -s pack_mod.gd -- <manifest_json> <output_pck> [import_project_root]")
		quit(1)
		return

	var manifest_source := args[0]
	var output_pck := args[1]
	var import_project_root := "" if args.size() == 2 else args[2]
	var asset_root := manifest_source.get_base_dir()
	var manifest_name := manifest_source.get_file()
	var manifest_json = JSON.parse_string(FileAccess.get_file_as_string(manifest_source))
	if typeof(manifest_json) != TYPE_DICTIONARY:
		push_error("manifest json is not a JSON object")
		quit(1)
		return

	var mod_id := String(manifest_json.get("id", ""))
	if mod_id.is_empty():
		push_error("manifest json is missing id")
		quit(1)
		return

	var packer := PCKPacker.new()
	var err := packer.pck_start(output_pck)
	if err != OK:
		push_error("pck_start failed: %s" % err)
		quit(err)
		return

	if import_project_root.is_empty():
		err = _add_tree(packer, asset_root, asset_root, "res://%s" % mod_id, manifest_name, false)
	else:
		var imported_mod_root := import_project_root.path_join(mod_id)
		err = _add_tree(packer, imported_mod_root, imported_mod_root, "res://%s" % mod_id, manifest_name, true)
		var imported_cache := import_project_root.path_join(".godot/imported")
		if err == OK and DirAccess.dir_exists_absolute(imported_cache):
			err = _add_tree(packer, imported_cache, imported_cache, "res://.godot/imported", "", false)
	if err != OK:
		quit(err)
		return

	err = packer.flush(true)
	if err != OK:
		push_error("flush failed: %s" % err)
		quit(err)
		return

	print("Created ", output_pck)
	quit()


func _add_tree(packer: PCKPacker, root: String, current_dir: String, packed_root: String, skip_root_file: String, skip_imported_sources: bool) -> int:
	var dir := DirAccess.open(current_dir)
	if dir == null:
		push_error("Unable to open asset dir: %s" % current_dir)
		return ERR_CANT_OPEN

	for subdir in dir.get_directories():
		var err := _add_tree(packer, root, current_dir.path_join(subdir), packed_root, skip_root_file, skip_imported_sources)
		if err != OK:
			return err

	for file_name in dir.get_files():
		if file_name == ".DS_Store" or (current_dir == root and file_name == skip_root_file):
			continue
		if file_name.ends_with(".md5"):
			continue

		var source_path := current_dir.path_join(file_name)
		# 导入过的图片只打 .import 重映射与 .godot/imported 里的导入产物，源文件运行时用不到。
		# 音频不在此列：Boss 音乐由 FMOD 读原文件，.import 设为 keep（不导入），原文件必须进包。
		if skip_imported_sources and _is_imported_source_file(source_path) and FileAccess.file_exists(source_path + ".import"):
			continue

		var relative_path := source_path.trim_prefix(root + "/")
		var packed_path := "%s/%s" % [packed_root, relative_path]
		var err := packer.add_file(packed_path, source_path)
		if err != OK:
			push_error("add_file failed for %s -> %s: %s" % [source_path, packed_path, err])
			return err
		print("packed ", packed_path)

	return OK


func _is_imported_source_file(path: String) -> bool:
	return path.get_extension().to_lower() in ["png", "jpg", "jpeg", "webp", "svg"]
