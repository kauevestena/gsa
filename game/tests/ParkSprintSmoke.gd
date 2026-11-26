extends Node
## Ride the course with the normal input map and city physics, including a missed gate and a retry.

var capture_dir: String

func _ready() -> void:
	run.call_deferred()


func run() -> void:
	var tree := get_tree()
	var menu := tree.current_scene
	capture_dir = OS.get_environment("GSA_PARK_CAPTURE_DIR")
	var records_dir := OS.get_environment("GSA_PARK_TEST_DIR")
	if records_dir.is_empty() or not DirAccess.dir_exists_absolute(records_dir):
		return fail("set GSA_PARK_TEST_DIR to a temporary directory for this test")
	var career_files := DirAccess.get_files_at("user://career-main") if DirAccess.dir_exists_absolute("user://career-main") else PackedStringArray()
	var launch := menu.get_node("%PlayChallenge") as Button
	menu.get_node("%Portuguese").pressed.emit()
	if launch.text != "Desafio da Gimbalina":
		return fail("Portuguese launch action")
	menu.get_node("%English").pressed.emit()
	if launch.text != "Gimbalina’s Park Sprint":
		return fail("English launch action")
	await capture("menu")
	launch.pressed.emit()
	await tree.scene_changed
	var city := tree.current_scene
	var hud := city.get_node("ParkRunLayer/ParkRunHud")
	var result := hud.get_node("Result") as Control
	var rider := city.get_node("PlayerAndTraffic/PlayerRig") as Node3D
	var course := city.get_node("CityBaseVisuals/GimbalinaCourse")
	var grid := rider.position
	await capture("grid")
	Input.action_press("gsa_accelerate")
	for tick in range(900):
		await tree.physics_frame
		if result.visible:
			break
	Input.action_release("gsa_accelerate")
	if not result.visible or result.find_child("Details", true, false).text != tr("park.missed").format([1], "{_}"):
		return fail("riding straight must miss the first offset gate")
	result.find_child("Retry", true, false).pressed.emit()
	if rider.position.distance_to(grid) > 0.01 or result.visible:
		return fail("retry must return the bicycle to the grid")

	var gate := 0
	Input.action_press("gsa_accelerate")
	for tick in range(5400):
		await tree.physics_frame
		if result.visible:
			break
		if tick == 540:
			await capture("ride")
		var target := (course.get_node("Gate%d" % gate) as Node3D).global_position
		if gate < 4 and rider.position.x >= target.x:
			gate += 1
			target = (course.get_node("Gate%d" % gate) as Node3D).global_position
		var offset := target - rider.position
		var heading := rider.rotation.y + PI / 2
		var error := angle_difference(heading, atan2(-offset.z, offset.x))
		Input.action_release("gsa_steer_left")
		Input.action_release("gsa_steer_right")
		if absf(error) > .035 and offset.x > .5:
			Input.action_press("gsa_steer_left" if error > 0 else "gsa_steer_right")
	Input.action_release("gsa_accelerate")
	Input.action_release("gsa_steer_left")
	Input.action_release("gsa_steer_right")
	var grade: String = result.find_child("Grade", true, false).text
	var time: String = result.find_child("FinishTime", true, false).text
	if not result.visible or grade not in [tr("park.gold"), tr("park.silver"), tr("park.bronze")] or gate != 4:
		return fail("ride ended at gate %d, position %s, result %s" % [gate, rider.position, grade])

	await capture("finish")
	result.find_child("Menu", true, false).pressed.emit()
	await tree.scene_changed
	tree.current_scene.get_node("%PlayChallenge").pressed.emit()
	await tree.scene_changed
	var target_time := tree.current_scene.get_node("ParkRunLayer/ParkRunHud").find_child("Target", true, false) as Label
	if not target_time.text.contains(tr("park.best").format([time], "{_}")):
		return fail("new ride did not show saved record " + time)
	if not FileAccess.file_exists(records_dir.path_join("park-records.cfg")):
		return fail("missing isolated record")
	var after := DirAccess.get_files_at("user://career-main") if DirAccess.dir_exists_absolute("user://career-main") else PackedStringArray()
	if after != career_files:
		return fail("quick play changed the career slot")
	# A player can leave this fresh attempt directly using the normal cancel binding.
	await tree.create_timer(.4).timeout
	var leave := InputEventKey.new()
	leave.keycode = KEY_ESCAPE
	leave.physical_keycode = KEY_ESCAPE
	leave.pressed = true
	Input.parse_input_event(leave)
	await tree.scene_changed
	leave.pressed = false
	Input.parse_input_event(leave)
	if not tree.current_scene.has_node("%PlayChallenge"):
		return fail("Escape did not return to the title screen")
	await tree.create_timer(.4).timeout
	print("GSA_PARK_SPRINT_SMOKE_OK medal=%s time=%s missed=1 retry=1 record=1" % [grade, time])
	tree.quit()


func capture(frame_name: String) -> void:
	if capture_dir.is_empty() or DisplayServer.get_name() == "headless":
		return
	await RenderingServer.frame_post_draw
	var error := get_viewport().get_texture().get_image().save_png(capture_dir.path_join(frame_name + ".png"))
	if error != OK:
		fail("could not capture " + frame_name)


func fail(message: String) -> void:
	push_error("Park sprint: " + message)
	get_tree().quit(1)
