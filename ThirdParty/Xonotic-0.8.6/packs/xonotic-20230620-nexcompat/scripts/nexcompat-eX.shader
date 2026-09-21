//----------------------------------------------------------------------//
// 						 eX texture set									//
// 				"eX" is evil lair's texture set #10						//
// 					 http://www.evillair.net							//
//					   el@evillair.net								//
//----------------------------------------------------------------------//

textures/eX/eX_lightpanel_01
{
	qer_EditorImage	textures/exx/light/light_panel01.tga
	{
		map textures/exx/light/light_panel01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_light_u201
{
	qer_EditorImage	textures/exx/light/light_u201.tga
	{
		map textures/exx/light/light_u201
	}
	{
		map $lightmap
	}
}



textures/eX/eX_clangfloor_01
{
	qer_EditorImage	textures/exx/floor/floor_clang01.tga
	{
		map textures/exx/floor/floor_clang01
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_cretebase_01
{
	qer_EditorImage		textures/exx/base/base_crete01.tga
	{
		map textures/exx/base/base_crete01.tga
	}
	{
		map $lightmap
	}
}

textures/eX/eX_cretefloor_01
{
	qer_EditorImage		textures/exx/floor/floor_crete01.tga
	{
		map textures/exx/floor/floor_crete01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_cretewall_01
{
	qer_EditorImage		textures/exx/wall/wall_crete01.tga
	{
		map textures/exx/wall/wall_crete01
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_cretewall_02
{
	qer_EditorImage		textures/exx/wall/wall_crete02.tga
	{
		map textures/exx/wall/wall_crete02
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_cretewall_03
{
	qer_EditorImage		textures/exx/wall/wall_crete03.tga
	{
		map textures/exx/wall/wall_crete03
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_cretewall_03b
{
	qer_EditorImage		textures/exx/wall/wall_crete03b.tga
	{
		map textures/exx/wall/wall_crete03b
	}
	{
		map $lightmap
	}	
}



textures/eX/eX_deswall_01
{
	qer_EditorImage		textures/exx/wall/wall_des01.tga
	{
		map textures/exx/wall/wall_des01
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_floor_grate_03
{
	qer_EditorImage		textures/exx/floor/floor_grate02.tga
	{
		map textures/exx/floor/floor_grate02
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_floor_grate_03_128
{
	qer_EditorImage		textures/exx/floor/floor_grate03.tga
	{
		map textures/exx/floor/floor_grate03
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_floor_grate03
{
	qer_editorimage textures/exx/floor/floor_grate01.tga
	surfaceparm alphashadow
	surfaceparm trans
	surfaceparm nomarks
	surfaceparm metalsteps
	cull disable
	nopicmip
	{
		map textures/exx/floor/floor_grate01.tga
		rgbGen identity
		depthWrite
		alphaFunc GE128
	}
	{
		map $lightmap
		blendfunc blend
		rgbGen identity
		tcGen lightmap
		depthFunc equal
	}
}

textures/eX/eX_floor_mtl_grate_01
{
	qer_EditorImage		textures/exx/floor/floor_grate04.tga
	{
		map textures/exx/floor/floor_grate04
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_floor_mtl_wrn_01
{
	qer_EditorImage		textures/exx/floor/floor_wrn01.tga
	{
		map textures/exx/floor/floor_wrn01
	}
	{
		map $lightmap
	}	
}

textures/eX/eX_floor_simple_05
{
	qer_EditorImage textures/exx/floor/floor_simple05.tga
	{
		map textures/exx/floor/floor_simple05
	}
	{
		map $lightmap
	}
}

textures/eX/eX_floor_simplines
{
	qer_EditorImage textures/exx/floor/floor_simplines.tga
	{
		map textures/exx/floor/floor_simplines
	}
	{
		map $lightmap
	}
}

textures/eX/eX_floor_tile_03
{
	qer_EditorImage textures/exx/floor/floor_tile03.tga
	{
		map textures/exx/floor/floor_tile03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_floor_tread_01
{
	qer_EditorImage textures/exx/floor/floor_tread01.tga
	{
		map textures/exx/floor/floor_tread01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_floorpanel_01
{
	qer_EditorImage textures/exx/floor/floor_panel01.tga
	{
		map textures/exx/floor/floor_panel01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_metalplate_01
{
	qer_EditorImage textures/exx/panel/panel_metal05.tga
	{
		map textures/exx/panel/panel_metal05
	}
	{
		map $lightmap
	}
}

textures/eX/eX_metalSupp01
{
	qer_EditorImage textures/exx/trim/trim_metalsupp01.tga
	{
		map textures/exx/trim/trim_metalsupp01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_metalwall02
{
	qer_EditorImage textures/exx/wall/wall_metal02.tga
	{
		map textures/exx/wall/wall_metal02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_mtl_bigplate_04
{
	qer_EditorImage textures/exx/panel/panel_metalbig04.tga
	{
		map textures/exx/panel/panel_metalbig04
	}
	{
		map $lightmap
	}
}

textures/eX/eX_mtl_bigplate_04b
{
	qer_EditorImage textures/exx/panel/panel_metalbig04b.tga
	{
		map textures/exx/panel/panel_metalbig04b
	}
	{
		map $lightmap
	}
}

textures/eX/eX_mtl_panel_02
{
	qer_EditorImage textures/exx/panel/panel_metal02.tga
	{
		map textures/exx/panel/panel_metal02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_mtl_panel_03
{
	qer_EditorImage textures/exx/panel/panel_metal03.tga
	{
		map textures/exx/panel/panel_metal03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_mtl_panel_04
{
	qer_EditorImage textures/exx/panel/panel_metal04.tga
	{
		map textures/exx/panel/panel_metal04
	}
	{
		map $lightmap
	}
}

textures/eX/eX_q2_01
{
	qer_EditorImage textures/exx/panel/panel_q201.tga
	{
		map textures/exx/panel/panel_q201
	}
	{
		map $lightmap
	}
}

textures/eX/eX_q2_01b
{
	qer_EditorImage textures/exx/panel/panel_q201b.tga
	{
		map textures/exx/panel/panel_q201b
	}
	{
		map $lightmap
	}
}

textures/eX/eX_q2_01c
{
	qer_EditorImage textures/exx/panel/panel_q201c.tga
	{
		map textures/exx/panel/panel_q201c
	}
	{
		map $lightmap
	}
}

textures/eX/eX_q2_01d
{
	qer_EditorImage textures/exx/panel/panel_q201d.tga
	{
		map textures/exx/panel/panel_q201d
	}
	{
		map $lightmap
	}
}

textures/eX/eX_q2_01e
{
	qer_EditorImage textures/exx/panel/panel_q201e.tga
	{
		map textures/exx/panel/panel_q201e
	}
	{
		map $lightmap
	}
}

textures/eX/eX_rndfloor_01
{
	qer_EditorImage textures/exx/floor/floor_rnd01.tga
	{
		map textures/exx/floor/floor_rnd01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_rndfloor_02
{
	qer_EditorImage textures/exx/floor/floor_rnd02.tga
	{
		map textures/exx/floor/floor_rnd02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_rplates_01
{
	qer_EditorImage textures/exx/panel/panel_rs01.tga
	{
		map textures/exx/panel/panel_rs01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_steptop_01
{
	qer_EditorImage textures/exx/trim/trim_steptop01.tga
	{
		map textures/exx/trim/trim_steptop01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_01
{
	qer_EditorImage textures/exx/trim/trim_01.tga
	{
		map textures/exx/trim/trim_01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_baseboard_02
{
	qer_EditorImage textures/exx/trim/trim_baseboard02.tga
	{
		map textures/exx/trim/trim_baseboard02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_baseboard_03
{
	qer_EditorImage textures/exx/trim/trim_baseboard03.tga
	{
		map textures/exx/trim/trim_baseboard03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_baseboard
{
	qer_EditorImage textures/exx/trim/trim_baseboard.tga
	{
		map textures/exx/trim/trim_baseboard
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_psimple_04
{
	qer_EditorImage textures/exx/trim/trim_psimple04.tga
	{
		map textures/exx/trim/trim_psimple04
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_psimple_05
{
	qer_EditorImage textures/exx/trim/trim_psimple05.tga
	{
		map textures/exx/trim/trim_psimple05
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_simple03
{
	qer_EditorImage textures/exx/trim/trim_simple03.tga
	{
		map textures/exx/trim/trim_simple03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_simple_01
{
	qer_EditorImage textures/exx/trim/trim_simple01.tga
	{
		map textures/exx/trim/trim_simple01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_support_03
{
	qer_EditorImage textures/exx/trim/trim_support03.tga
	{
		map textures/exx/trim/trim_support03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_trim_vert_01
{
	qer_EditorImage textures/exx/trim/trim_vert01.tga
	{
		map textures/exx/trim/trim_vert01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_01
{
	qer_EditorImage textures/exx/wall/wall_metal01.tga
	{
		map textures/exx/wall/wall_metal01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_b01
{
	qer_EditorImage textures/exx/wall/wall_metalb01.tga
	{
		map textures/exx/wall/wall_metalb01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_bigrib_02
{
	qer_EditorImage textures/exx/wall/wall_bigrib02.tga
	{
		map textures/exx/wall/wall_bigrib02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_bplate_06
{
	qer_EditorImage textures/exx/wall/wall_bplate06.tga
	{
		map textures/exx/wall/wall_bplate06
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_panel_05
{
	qer_EditorImage textures/exx/wall/wall_panel05.tga
	{
		map textures/exx/wall/wall_panel05
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_panels_08
{
	qer_EditorImage textures/exx/wall/wall_panels08.tga
	{
		map textures/exx/wall/wall_panels08
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_panels_08b
{
	qer_EditorImage textures/exx/wall/wall_panels08b.tga
	{
		map textures/exx/wall/wall_panels08b
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_pipe
{
	qer_EditorImage textures/exx/wall/wall_pipe.tga
	{
		map textures/exx/wall/wall_pipe
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_u207
{
	qer_EditorImage textures/exx/wall/wall_u207.tga
	{
		map textures/exx/wall/wall_u207
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetal_plate01
{
	qer_EditorImage textures/exx/panel/panel_metal01.tga
	{
		map textures/exx/panel/panel_metal01
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetal_plate01b
{
	qer_EditorImage textures/exx/panel/panel_metal01b.tga
	{
		map textures/exx/panel/panel_metal01b
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetal_plate01B
{
	qer_EditorImage textures/exx/panel/panel_metal01b.tga
	{
		map textures/exx/panel/panel_metal01b
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetal_plate01c
{
	qer_EditorImage textures/exx/panel/panel_metal01c.tga
	{
		map textures/exx/panel/panel_metal01c
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase01
{
	qer_EditorImage textures/exx/base/base_metal01.tga
	{
		map textures/exx/base/base_metal01
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase02
{
	qer_EditorImage textures/exx/base/base_metal02.tga
	{
		map textures/exx/base/base_metal02
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase03
{
	qer_EditorImage textures/exx/base/base_metal03.tga
	{
		map textures/exx/base/base_metal03
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase04
{
	qer_EditorImage textures/exx/base/base_metal04.tga
	{
		map textures/exx/base/base_metal04
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase05Rust
{
	qer_EditorImage textures/exx/base/base_metal05rust.tga
	{
		map textures/exx/base/base_metal05rust
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalFloor02
{
	qer_EditorImage textures/exx/floor/floor_metal02.tga
	{
		map textures/exx/floor/floor_metal02
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalrib01
{
	qer_EditorImage textures/exx/wall/wall_metalrib01.tga
	{
		map textures/exx/wall/wall_metalrib01
	}
	{
		map $lightmap
	}
}

textures/eX/eX_clangfloor_01b
{
	qer_EditorImage textures/exx/floor/floor_clang01b.tga
	{
		map textures/exx/floor/floor_clang01b
	}
	{
		map $lightmap
	}
}

textures/eX/eX_cretebase_02
{
	qer_EditorImage textures/exx/base/base_crete02.tga
	{
		map textures/exx/base/base_crete02
	}
	{
		map $lightmap
	}
}

textures/eX/eX_cretebase_03_dark
{
	qer_EditorImage textures/exx/base/base_crete03.tga
	{
		map textures/exx/base/base_crete03
	}
	{
		map $lightmap
	}
}

textures/eX/eX_cretefloor_01b
{
	qer_EditorImage textures/exx/floor/floor_crete01b.tga
	{
		map textures/exx/floor/floor_crete01b
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase06rust
{
	qer_EditorImage textures/exx/base/base_metal06rust.tga
	{
		map textures/exx/base/base_metal06rust
	}
	{
		map $lightmap
	}
}

textures/eX/eXmetalBase07rust
{
	qer_EditorImage textures/exx/base/base_metal07rust.tga
	{
		map textures/exx/base/base_metal07rust
	}
	{
		map $lightmap
	}
}

textures/eX/eX_wall_01b
{
	qer_EditorImage textures/exx/wall/wall_metal01b.tga
	{
		map textures/exx/wall/wall_metal01b
	}
	{
		map $lightmap
	}
}
