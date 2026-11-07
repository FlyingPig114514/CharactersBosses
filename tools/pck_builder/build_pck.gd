extends SceneTree

## 打包 mod 的 pck。
##
## 用法:
##   godot --headless --path <pck_builder> --script build_pck.gd -- \
##         <manifest> <output_pck> [content_dir] [res_prefix] [extras_manifest]
##
## content_dir 下的文件会被递归打进 pck，资源路径为 res://<res_prefix>/<相对路径>。
## 例: content/localization/zhs/monsters.json
##     → res://CharacterBosses/localization/zhs/monsters.json
## （游戏用 res://<modId>/localization/<lang>/<表名>.json 去找 mod 的本地化表。）
##
## extras_manifest 是可选的「额外文件清单」，每行 `res路径|本地路径`（# 开头为注释）。
## 用来打包需要**精确 res:// 路径**的文件 —— 主要是贴图：Godot 运行时靠
## `xxx.png.import` 里的 remap 去找 `res://.godot/imported/*.ctex`，
## 所以 png、png.import、ctex 三者必须各自落在固定路径上（见 tools/art/pck_extras.txt）。

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Usage: -- <manifest> <output_pck> [content_dir] [res_prefix] [extras]")
		quit(1)
		return

	var source_manifest := args[0]
	var output_pck := args[1]
	var content_dir := args[2] if args.size() > 2 else ""
	var res_prefix := args[3] if args.size() > 3 else ""
	var extras_manifest := args[4] if args.size() > 4 else ""

	var packer := PCKPacker.new()
	var start_error := packer.pck_start(output_pck)
	if start_error != OK:
		push_error("Failed to start PCK packer: %s" % start_error)
		quit(1)
		return

	# ⚠ 不再把 mod_manifest.json 打进 pck（用户 2026-10-27 实测报「存在多个 id 为 CharacterBosses 的模组！」）
	# 磁盘上的 `<模组名>.json` 已经是一份清单；pck 里再放一份同 id 清单会被加载器当成**第二个模组**并报重复 id。
	# 对照：原版/崩坠的 `Downfall.pck` 里没有 `res://mod_manifest.json`，清单只以磁盘文件形式存在。

	var packed := 0
	if content_dir != "" and DirAccess.dir_exists_absolute(content_dir):
		packed = _add_dir(packer, content_dir, res_prefix)
		print("Packed %d content file(s) from %s" % [packed, content_dir])
	elif content_dir != "":
		push_warning("content dir not found, skipping: %s" % content_dir)

	if extras_manifest != "":
		var extra_count := _add_extras(packer, extras_manifest)
		print("Packed %d extra file(s) from %s" % [extra_count, extras_manifest])

	var flush_error := packer.flush()
	if flush_error != OK:
		push_error("Failed to finalize PCK: %s" % flush_error)
		quit(1)
		return

	print("Packed %s" % output_pck)
	quit(0)


func _add_dir(packer: PCKPacker, dir_path: String, res_prefix: String) -> int:
	var count := 0
	var dir := DirAccess.open(dir_path)
	if dir == null:
		push_error("Cannot open dir: %s" % dir_path)
		return 0

	dir.list_dir_begin()
	var entry := dir.get_next()
	while entry != "":
		if entry != "." and entry != ".." and not entry.ends_with(".import"):
			var full := dir_path.path_join(entry)
			if dir.current_is_dir():
				count += _add_dir(packer, full, res_prefix.path_join(entry))
			else:
				var res_path := "res://" + res_prefix.path_join(entry) if res_prefix != "" else "res://" + entry
				var err := packer.add_file(res_path, full)
				if err == OK:
					count += 1
				else:
					push_error("Failed to add %s: %s" % [res_path, err])
		entry = dir.get_next()
	dir.list_dir_end()
	return count


func _add_extras(packer: PCKPacker, manifest_path: String) -> int:
	var f := FileAccess.open(manifest_path, FileAccess.READ)
	if f == null:
		push_warning("extras manifest not found: %s" % manifest_path)
		return 0

	var count := 0
	while not f.eof_reached():
		var line := f.get_line().strip_edges()
		if line == "" or line.begins_with("#"):
			continue

		var parts := line.split("|")
		if parts.size() < 2:
			push_warning("bad extras line: %s" % line)
			continue

		var res_path := parts[0].strip_edges()
		var fs_path := parts[1].strip_edges()
		if not FileAccess.file_exists(fs_path):
			push_warning("extras file missing: %s" % fs_path)
			continue

		var err := packer.add_file(res_path, fs_path)
		if err == OK:
			count += 1
		else:
			push_error("Failed to add %s: %s" % [res_path, err])

	f.close()
	return count
