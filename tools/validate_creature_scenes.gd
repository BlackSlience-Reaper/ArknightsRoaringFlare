extends SceneTree

# Usage: <game_bin> --headless --path <mod>/tools -s res://validate_creature_scenes.gd -- <pck> <scene_path> [<scene_path> ...]
# 用游戏自带引擎（带 Spine 运行时）直接加载 creature_visuals 场景，核对：
#   骨架真正加载（版本不对时 is_skeleton_data_loaded 为 false，引擎另报 "Skeleton version ... does not match"）、
#   图集贴图非空、列出 skel 里的动画名、播 待机动画后量 Idle 包围盒（已乘 SpineSprite 缩放，用来定 Bounds/标记点）。
# 场景根上的 NCreatureVisuals.cs 在这个独立工程里解析不到，会报一条脚本缺失，不影响 Spine 检查。

var _failures := 0


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Usage: -s res://validate_creature_scenes.gd -- <pck> <scene_path> [<scene_path> ...]")
		quit(1)
		return

	if not ProjectSettings.load_resource_pack(args[0]):
		push_error("Failed to load PCK: %s" % args[0])
		quit(2)
		return

	for i in range(1, args.size()):
		await _check_scene(args[i])

	print("[validate_creature_scenes] finished with %d failure(s)" % _failures)
	quit(1 if _failures > 0 else 0)


func _fail(scene_path: String, message: String) -> void:
	_failures += 1
	print("[validate_creature_scenes] FAIL %s: %s" % [scene_path, message])


func _check_scene(scene_path: String) -> void:
	var packed = ResourceLoader.load(scene_path)
	if packed == null:
		_fail(scene_path, "cannot load scene")
		return

	var instance: Node = packed.instantiate()
	var visuals = instance.get_node_or_null("%Visuals")
	if visuals == null or visuals.get_class() != "SpineSprite":
		_fail(scene_path, "no %Visuals SpineSprite")
		instance.free()
		return

	var data = visuals.skeleton_data_res
	if data == null or not data.is_skeleton_data_loaded():
		_fail(scene_path, "skeleton data not loaded")
		instance.free()
		return

	var textures: Array = data.atlas_res.get_textures()
	var texture_sizes := []
	for texture in textures:
		if texture == null or texture.get_width() <= 0:
			_fail(scene_path, "atlas texture missing")
		else:
			texture_sizes.append("%dx%d" % [texture.get_width(), texture.get_height()])

	var animation_names := []
	for animation in data.get_animations():
		animation_names.append("%s(%.2fs)" % [animation.get_name(), animation.get_duration()])

	root.add_child(instance)
	var idle: String = visuals.preview_animation
	visuals.get_animation_state().set_animation(idle, true, 0)
	for _frame in range(10):
		await process_frame

	# SpineSkeleton.get_bounds 在这个运行时里会触发 godot-cpp 越界断言，改用骨骼世界坐标估算：
	# get_world_x/y 是骨架本地坐标（Godot 朝向、原点在脚底），y >= 0 的是根/地面控制骨，剔除。
	# 骨骼点不含贴图外沿，只作 Bounds 的下限参考。
	var skeleton = visuals.get_skeleton()
	var min_point := Vector2(INF, INF)
	var max_point := Vector2(-INF, -INF)
	for bone in skeleton.get_bones():
		var local := Vector2(bone.get_world_x(), bone.get_world_y())
		if local.y >= 0:
			continue
		var point: Vector2 = instance.to_local(visuals.to_global(local))
		min_point = min_point.min(point)
		max_point = max_point.max(point)
	var extent := max_point - min_point
	print("[validate_creature_scenes] OK %s textures=%s idle=%s bone_extent=(x=%d..%d, y=%d..%d, w=%d, h=%d) anims=%s" % [
		scene_path, texture_sizes, idle,
		min_point.x, max_point.x, min_point.y, max_point.y, extent.x, extent.y,
		", ".join(animation_names)])
	instance.queue_free()
	await process_frame
