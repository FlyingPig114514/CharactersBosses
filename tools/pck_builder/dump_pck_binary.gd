extends SceneTree

## 从某个 pck 里「二进制」导出文件（dump_pck_file.gd 只做了文本，png 会坏）。
##
## 用法:
##   godot --headless --path <pck_builder> --script dump_pck_binary.gd -- <pck> list <res_dir> [filter]
##   godot --headless --path <pck_builder> --script dump_pck_binary.gd -- <pck> copy <res_path> <out_path> [<res_path> <out_path> ...]
##
## 例:
##   ... -- <pck> list res://images/powers power
##   ... -- <pck> copy res://images/powers/doom_power.png D:/out/doom_power.png

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 3:
		push_error("Usage: -- <pck> <list|copy> ...")
		quit(1)
		return

	var pck := args[0]
	if not ProjectSettings.load_resource_pack(pck, false):
		print("load_resource_pack FAILED: ", pck)
		quit(1)
		return

	var mode := args[1]

	if mode == "list":
		var root := args[2]
		var filter := args[3].to_lower() if args.size() > 3 else ""
		var n := _walk(root, filter, 0)
		print("listed=", n)
		quit(0)
		return

	if mode == "copy":
		var made := 0
		var i := 2
		while i + 1 < args.size():
			var res_path := args[i]
			var out_path := args[i + 1]
			i += 2
			var src := FileAccess.open(res_path, FileAccess.READ)
			if src == null:
				print("MISS: ", res_path)
				continue
			var bytes := src.get_buffer(src.get_length())
			src.close()
			DirAccess.make_dir_recursive_absolute(out_path.get_base_dir())
			var dst := FileAccess.open(out_path, FileAccess.WRITE)
			if dst == null:
				print("WRITE FAIL: ", out_path)
				continue
			dst.store_buffer(bytes)
			dst.close()
			print("OK: ", res_path, " -> ", out_path, " (", bytes.size(), " bytes)")
			made += 1
		print("copied=", made)
		quit(0)
		return

	if mode == "png":
		# 走 Godot 的资源加载（.png 已被导入成 .ctex）⇒ 解回真 PNG
		var made2 := 0
		var j := 2
		while j + 1 < args.size():
			var res_path2 := args[j]
			var out_path2 := args[j + 1]
			j += 2
			var res := ResourceLoader.load(res_path2)
			if res == null:
				print("MISS: ", res_path2)
				continue
			var img: Image = null
			if res is AtlasTexture:
				# 图集里的一个区域：直接把整张图集解出来、解压，再裁 region
				# （直接调 AtlasTexture.get_image() 在图集是压缩格式时会报
				#   "Cannot blit_rect in compressed image formats"）
				var at := res as AtlasTexture
				if at.atlas == null:
					print("NO-ATLAS: ", res_path2)
					continue
				var atlas_img := at.atlas.get_image()
				if atlas_img == null:
					print("NO-ATLAS-IMG: ", res_path2)
					continue
				if atlas_img.is_compressed():
					atlas_img.decompress()
				var reg := Rect2i(
					int(round(at.region.position.x)), int(round(at.region.position.y)),
					int(round(at.region.size.x)), int(round(at.region.size.y)))
				if reg.size.x <= 0 or reg.size.y <= 0:
					print("BAD-REGION: ", res_path2)
					continue
				img = atlas_img.get_region(reg)
			elif res is Texture2D:
				img = (res as Texture2D).get_image()
				if img != null and img.is_compressed():
					img.decompress()
			elif res is Image:
				img = res
			if img == null:
				print("NOT-IMAGE: ", res_path2)
				continue
			DirAccess.make_dir_recursive_absolute(out_path2.get_base_dir())
			var err := img.save_png(out_path2)
			if err != OK:
				print("SAVE FAIL: ", out_path2, " err=", err)
				continue
			print("OK: ", res_path2, " -> ", out_path2, " (", img.get_width(), "x", img.get_height(), ")")
			made2 += 1
		print("pngs=", made2)
		quit(0)
		return

	push_error("unknown mode: " + mode)
	quit(1)


func _walk(dir_path: String, filter: String, n: int) -> int:
	var d := DirAccess.open(dir_path)
	if d == null:
		print("!! cannot open ", dir_path)
		return n
	d.list_dir_begin()
	var entry := d.get_next()
	while entry != "":
		if entry.begins_with("."):
			entry = d.get_next()
			continue
		var full := dir_path.path_join(entry) if not dir_path.ends_with("/") else dir_path + entry
		if d.current_is_dir():
			n = _walk(full, filter, n)
		else:
			if filter == "" or full.to_lower().find(filter) >= 0:
				print(full)
				n += 1
		entry = d.get_next()
	d.list_dir_end()
	return n
