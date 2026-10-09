extends SceneTree

# Usage: <game_bin> --headless --path <mod>/tools -s res://measure_idle_bounds.gd -- <pck> <scene_path> [<scene_path> ...]
# 量 creature_visuals 场景里 Idle（preview_animation）整段循环的附件包围盒，换算到场景根坐标（已含 Visuals 的位置与缩放、
# 负 X 翻转），用来定 Bounds 与标记点。SpineSkeleton.get_bounds 按当前姿势的全部可见附件算，含特效贴图（火焰、光效），
# 所以宽度常比身体宽；高度比骨骼点估算可信。每个场景在 Idle 上均匀取 12 个时刻求并集，另给第 0 帧单独的包围盒。


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if not ProjectSettings.load_resource_pack(args[0]):
		push_error("Failed to load PCK: %s" % args[0])
		quit(2)
		return
	for i in range(1, args.size()):
		await _measure(args[i])
	quit(0)


func _to_root(visuals: Node2D, rect: Rect2) -> Rect2:
	var a: Vector2 = visuals.transform * rect.position
	var b: Vector2 = visuals.transform * rect.end
	return Rect2(a.min(b), (a - b).abs())


func _fmt(r: Rect2) -> String:
	return "x=%d..%d y=%d..%d w=%d h=%d" % [r.position.x, r.end.x, r.position.y, r.end.y, r.size.x, r.size.y]


func _measure(scene_path: String) -> void:
	var instance: Node = ResourceLoader.load(scene_path).instantiate()
	root.add_child(instance)
	var visuals = instance.get_node("%Visuals")
	var idle: String = visuals.preview_animation
	var state = visuals.get_animation_state()
	var skeleton = visuals.get_skeleton()
	var duration: float = visuals.skeleton_data_res.find_animation(idle).get_duration()
	# 进树后先跑几帧：get_bounds 依赖 SpineSprite 首次更新时分配的顶点缓冲，提前调用会触发 godot-cpp 越界断言（进程直接退出）。
	state.set_animation(idle, true, 0)
	for _frame in range(3):
		await process_frame
	var union := Rect2()
	var first := Rect2()
	for step in range(12):
		# 手动 state.apply + update_world_transform 后立刻 get_bounds 同样会触发上面的断言，所以改成设好时刻后等一帧让 SpineSprite 自己更新。
		var entry = state.set_animation(idle, true, 0)
		entry.set_track_time(duration * step / 12.0)
		await process_frame
		var r := _to_root(visuals, skeleton.get_bounds())
		if step == 0:
			first = r
			union = r
		else:
			union = union.merge(r)
	print("[measure_idle_bounds] %s idle=%s(%.2fs) frame0: %s | loop union: %s" % [
		scene_path, idle, duration, _fmt(first), _fmt(union)])
	instance.queue_free()
	await process_frame
