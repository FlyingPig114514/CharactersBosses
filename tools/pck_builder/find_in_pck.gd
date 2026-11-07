extends SceneTree

## 在某个 pck 里全文搜索一个字符串（只看 .json / .txt / .csv / .cfg 这类文本表）。
##
## 用途：想知道某个中文卡名/关键词在别的 mod（例如 mods\Downfall）里到底叫什么键，
## 用它把 pck 挂进来扫一遍即可，不用解包。
##
## 用法:
##   godot --headless --path <pck_builder> --script find_in_pck.gd -- <pck> <needle> [max_hits]

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Usage: -- <pck> <needle> [max_hits]")
		quit(1)
		return

	var pck := args[0]
	var needle := args[1]
	var max_hits := 40
	if args.size() > 2:
		max_hits = int(args[2])

	if not ProjectSettings.load_resource_pack(pck, false):
		print("load_resource_pack FAILED: ", pck)
		quit(1)
		return

	var hits := _walk("res://", needle, max_hits, 0)
	print("done, hits=", hits)
	quit(0)


func _walk(dir_path: String, needle: String, max_hits: int, hits: int) -> int:
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return hits

	dir.list_dir_begin()
	var entry := dir.get_next()
	while entry != "":
		if entry.begins_with("."):
			entry = dir.get_next()
			continue

		var full := dir_path.path_join(entry) if not dir_path.ends_with("/") else dir_path + entry
		if dir.current_is_dir():
			hits = _walk(full, needle, max_hits, hits)
		else:
			var lower := entry.to_lower()
			if lower.ends_with(".json") or lower.ends_with(".txt") or lower.ends_with(".csv") or lower.ends_with(".cfg"):
				var file := FileAccess.open(full, FileAccess.READ)
				if file != null:
					var text := file.get_as_text()
					file.close()
					if text.find(needle) >= 0:
						for line in text.split("\n"):
							if line.find(needle) >= 0:
								if hits >= max_hits:
									break
								hits += 1
								print(full, " :: ", line.strip_edges())
		if hits >= max_hits:
			dir.list_dir_end()
			return hits
		entry = dir.get_next()

	dir.list_dir_end()
	return hits
