extends SceneTree

## 把 pck 里的某个文件**整份复制**到本地磁盘（不像 find_in_pck 只打匹配行）。
##
## 用法:
##   godot --headless --path <pck_builder> --script dump_pck_file.gd -- <pck> <res_path> <out_path> [<res_path> <out_path> ...]

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 3:
		push_error("Usage: -- <pck> <res_path> <out_path> [<res_path> <out_path> ...]")
		quit(1)
		return

	var pck := args[0]
	if not ProjectSettings.load_resource_pack(pck, false):
		print("load_resource_pack FAILED: ", pck)
		quit(1)
		return

	var i := 1
	while i + 1 < args.size():
		var res_path := args[i]
		var out_path := args[i + 1]
		i += 2
		var file := FileAccess.open(res_path, FileAccess.READ)
		if file == null:
			print("MISS: ", res_path)
			continue
		var text := file.get_as_text()
		file.close()
		var w := FileAccess.open(out_path, FileAccess.WRITE)
		if w == null:
			print("WRITE FAIL: ", out_path)
			continue
		w.store_string(text)
		w.close()
		print("OK: ", res_path, " -> ", out_path, " (", text.length(), " chars)")
	quit(0)
