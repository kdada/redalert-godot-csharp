extends SceneTree

# Decorative pixels only; all navigation/controls are independent Godot code.
# Input sources and attribution are recorded separately in the research manifest.
func _initialize() -> void:
	var folder = "res://Assets/Generated/ShellChrome"
	DirAccess.make_dir_recursive_absolute(folder)
	var main = Image.load_from_file("res://Assets/Reference/FrontEndResearch/ra2-main-menu-mobygames-20001029.png")
	var options = Image.load_from_file("res://Assets/Reference/FrontEndResearch/ra2-options-mobygames-20010410.png")
	main.convert(Image.FORMAT_RGBA8)
	options.convert(Image.FORMAT_RGBA8)
	main.resize(640,480,Image.INTERPOLATE_LANCZOS)
	options.resize(800,600,Image.INTERPOLATE_LANCZOS)
	main.save_png(folder+"/main-reference-surface.png")
	options.save_png(folder+"/options-reference-surface.png")
	var housing = main.get_region(Rect2i(482,195,150,43))
	# Original English caption is covered by the raw original SHP red face at runtime.
	housing.save_png(folder+"/button-housing.png")
	var header = options.get_region(Rect2i(632,0,168,198))
	header.fill_rect(Rect2i(25,5,130,27),Color("030203"))
	header.save_png(folder+"/rail-header.png")
	var plate = options.get_region(Rect2i(640,285,151,42))
	plate.save_png(folder+"/rail-plate.png")
	var setup = Image.load_from_file("res://Assets/Reference/FrontEndResearch/ra2-skirmish-setup-mobygames-20040208.png")
	setup.convert(Image.FORMAT_RGBA8)
	setup.resize(640,480,Image.INTERPOLATE_LANCZOS)
	var setup_header = setup.get_region(Rect2i(472,0,168,240))
	setup_header.fill_rect(Rect2i(17,4,135,18),Color("030203"))
	setup_header.fill_rect(Rect2i(8,38,151,115),Color("030203"))
	setup_header.resize(168,290,Image.INTERPOLATE_LANCZOS)
	setup_header.save_png(folder+"/setup-header.png")
	var rail = options.get_region(Rect2i(632,0,168,600))
	rail.fill_rect(Rect2i(25,5,130,27),Color("030203"))
	# Cover the three source button captions with empty metal plates, leaving chrome intact.
	for y in [198,240,536]:
		rail.blit_rect(plate,Rect2i(0,0,151,42),Vector2i(8,y))
	rail.save_png(folder+"/rail.png")
	var lobby = Image.load_from_file("res://Assets/Reference/FrontEndResearch/ra2-lan-lobby-tlukas-2013.png")
	lobby.convert(Image.FORMAT_RGBA8)
	lobby = lobby.get_region(Rect2i(0,0,640,480))
	# The actual lobby has a nearly unobscured original world-map plate. Clear only
	# captured names and thin control edges, retaining the original geographic art.
	for rect in [Rect2i(115,59,169,27),Rect2i(393,59,42,29),Rect2i(71,89,40,17),Rect2i(246,89,44,17),Rect2i(77,111,350,31),Rect2i(412,256,20,143),Rect2i(76,380,355,20)]:
		clear_control_region(lobby,rect)
	for rect in [Rect2i(75,109,172,144),Rect2i(251,109,178,144),Rect2i(75,257,354,141)]:
		erase_outline(lobby,rect)
	var blank = lobby
	blank.resize(800,600,Image.INTERPOLATE_LANCZOS)
	blank.blit_rect(rail,Rect2i(0,0,168,600),Vector2i(632,0))
	blank.save_png(folder+"/dialog-surface.png")
	var main_large = blank.duplicate()
	var monitor = main.get_region(Rect2i(0,0,472,450))
	monitor.resize(632,570,Image.INTERPOLATE_LANCZOS)
	main_large.blit_rect(monitor,Rect2i(0,0,632,570),Vector2i.ZERO)
	var main_header = main.get_region(Rect2i(472,0,168,195))
	main_header.resize(168,246,Image.INTERPOLATE_LANCZOS)
	main_header.fill_rect(Rect2i(23,5,131,31),Color("030203"))
	main_large.blit_rect(main_header,Rect2i(0,0,168,246),Vector2i(632,0))
	main_large.save_png(folder+"/main-surface.png")
	# Preserve genuine decorative body artwork for dialogs without copying any operation code.
	var report = {"source_main":"ra2-main-menu-mobygames-20001029.png","source_options":"ra2-options-mobygames-20010410.png","source_lobby":"ra2-lan-lobby-tlukas-2013.png","source_setup":"ra2-skirmish-setup-mobygames-20040208.png","reference_manifest":"Assets/Reference/FrontEndResearch/provenance.json","normalized_main_reference":[640,480],"main_canvas":[800,600],"dialog_canvas":[800,600],"main_overlay":"Assets/Generated/AuthenticShell/ra2ts_l/menu-loop.ogv","policy":"Screenshot-derived decorative chrome, original English text cleared for live controls; reconstructed background only where old controls occluded map. No executable code or dialog implementation copied."}
	var file = FileAccess.open(folder+"/derivation.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	file.close()
	print("SHELL CHROME DERIVED")
	quit()

func clear_control_region(image:Image,rect:Rect2i) -> void:
	for y in range(rect.position.y,rect.end.y):
		var a = image.get_pixel(rect.position.x-3,y)
		var b = image.get_pixel(rect.end.x+3,y)
		for x in range(rect.position.x,rect.end.x):
			var fraction = float(x-rect.position.x)/float(rect.size.x)
			var c = a.lerp(b,fraction)
			image.set_pixel(x,y,Color(minf(c.r,0.18),minf(c.g,0.028),minf(c.b,0.032),1))

func erase_outline(image:Image,rect:Rect2i) -> void:
	for x in range(rect.position.x-1,rect.end.x+2):
		for d in range(-1,2):
			image.set_pixel(x,rect.position.y+d,image.get_pixel(x,rect.position.y-3))
			image.set_pixel(x,rect.end.y+d,image.get_pixel(x,rect.end.y+3))
	for y in range(rect.position.y-1,rect.end.y+2):
		for d in range(-1,2):
			image.set_pixel(rect.position.x+d,y,image.get_pixel(rect.position.x-3,y))
			image.set_pixel(rect.end.x+d,y,image.get_pixel(rect.end.x+3,y))
