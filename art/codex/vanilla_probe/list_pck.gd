extends SceneTree

# 列出原版 PCK 里某个目录下的文件（只读）。
# Usage: <game_bin> --headless --path <dir> -s list_pck.gd -- <game_pck> <res_dir>

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	ProjectSettings.load_resource_pack(args[0])
	var dir := DirAccess.open(args[1])
	if dir == null:
		print("cannot open ", args[1])
		quit(1)
		return
	for d in dir.get_directories():
		print("D ", d)
	for f in dir.get_files():
		print("F ", f)
	quit()
