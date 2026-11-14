extends Node


func _ready() -> void:
	if not "--smoke-test" in OS.get_cmdline_user_args():
		return
	var bridge = get_parent().get_node("DomainBridge")
	var status: Dictionary = bridge.GetStatus()
	if status["datum_id"] != "SAO_DATUM_LOCAL_V1" or status["missions_module"] != "Gsa.Missions":
		fail("Facade did not load the expected domain modules")
		return
	var converted: Dictionary = bridge.MapToLocal(500000.125, 7400000.25, 100.5)
	if converted["status"] != "accepted" or converted["position"] != Vector3(0.125, 0.5, -0.25):
		fail("ENU precision or axis convention failed through the C# facade")
		return
	var restored: Dictionary = bridge.LocalToMap(converted["position"])
	if restored["east"] != 500000.125 or restored["north"] != 7400000.25 or restored["up"] != 100.5:
		fail("ENU inverse failed through the C# facade")
		return
	var invalid: Dictionary = bridge.MapToLocal(NAN, 0.0, 0.0)
	if invalid["status"] != "invalid_input":
		fail("Facade accepted a nonfinite map position")
		return
	status["datum_id"] = "mutated_by_caller"
	if bridge.GetStatus()["datum_id"] != "SAO_DATUM_LOCAL_V1":
		fail("Facade exposed shared mutable state")
		return
	print("GSA_BRIDGE_SMOKE_OK")


func fail(reason: String) -> void:
	push_error(reason)
	get_tree().quit(1)
