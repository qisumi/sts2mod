extends SceneTree


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Usage: godot --headless --path <tool_project> -s verify_texture_load.gd -- <pck_path> <texture_path> [texture_path...]")
		quit(1)
		return

	var pck_path := args[0]
	if not ProjectSettings.load_resource_pack(pck_path):
		push_error("Unable to load PCK: %s" % pck_path)
		quit(1)
		return

	for i in range(1, args.size()):
		var path := args[i]
		var texture := ResourceLoader.load(path, "Texture2D")
		if texture == null:
			push_error("Unable to load texture: %s" % path)
			quit(1)
			return
		print("Loaded texture: ", path)

	quit(0)
