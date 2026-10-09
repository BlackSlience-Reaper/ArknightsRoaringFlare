extends SceneTree

# 探针：从模组 PCK 里按 res:// 路径加载贴图，打印尺寸（核对导入产物进包、路径对得上）。
# Usage: <game_bin> --headless --path <dir> -s probe_textures.gd -- <pck> <res_path> [<res_path> ...]

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0])
	for i in range(1, args.size()):
		var tex = ResourceLoader.load(args[i])
		if tex == null:
			print("MISSING ", args[i])
		else:
			print("OK ", args[i], " ", tex.get_width(), "x", tex.get_height())
	quit()
