extends SceneTree

# 探针：游戏自带的 spine-godot 能否经 ResourceLoader 按 .spjson 读 JSON 骨骼文件。
# Usage: <game_bin> --headless --path <dir> -s probe_spjson.gd -- <dir_with_files> <name>

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var dir: String = args[0].trim_suffix("/") + "/"
	var name: String = args[1]
	var file = ResourceLoader.load(dir + name + ".spjson")
	print("spjson resource=", file, " class=", file.get_class() if file else "null")
	var atlas := SpineAtlasResource.new()
	atlas.load_from_atlas_file(dir + name + ".atlas")
	var data := SpineSkeletonDataResource.new()
	data.atlas_res = atlas
	data.skeleton_file_res = file
	print("loaded=", data.is_skeleton_data_loaded())
	if data.is_skeleton_data_loaded():
		for a in data.get_animations():
			print("anim ", a.get_name(), " ", a.get_duration())
	quit()
