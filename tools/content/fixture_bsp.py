"""
Generates a small, entirely original, synthetic IBSP version 46 file for
testing the importer. This is NOT extracted from any Xonotic/Quake asset —
every byte is produced by this generator so it can be committed to the repo
without any content-provenance concern.

Layout of the generated map: one axis-aligned quad (2 triangles) as
worldspawn geometry using a single named shader, one quadratic patch (a
flat 3x3 control grid, i.e. a degenerate "curved" plane) to exercise patch
tessellation, one inline brush submodel (model #1, unused by any face) to
exercise "submodels must not become static world", and two entities:
worldspawn + info_player_deathmatch.

Stdlib only (struct + io), no runtime dependencies.
"""
from __future__ import annotations

import struct

MAGIC = b"IBSP"
VERSION = 46
LUMP_COUNT = 17

(
    LUMP_ENTITIES, LUMP_SHADERS, LUMP_PLANES, LUMP_NODES, LUMP_LEAFS,
    LUMP_LEAFFACES, LUMP_LEAFBRUSHES, LUMP_MODELS, LUMP_BRUSHES,
    LUMP_BRUSHSIDES, LUMP_VERTEXES, LUMP_MESHVERTS, LUMP_EFFECTS,
    LUMP_FACES, LUMP_LIGHTMAPS, LUMP_LIGHTVOLS, LUMP_VISDATA,
) = range(LUMP_COUNT)


def _pack_vertex(pos, uv=(0.0, 0.0), lm_uv=(0.0, 0.0), normal=(0.0, 0.0, 1.0), color=(255, 255, 255, 255)):
    return struct.pack(
        "<3f2f2f3f4B",
        pos[0], pos[1], pos[2],
        uv[0], uv[1],
        lm_uv[0], lm_uv[1],
        normal[0], normal[1], normal[2],
        *color,
    )


def _pack_shader(name: bytes, content_flags: int, surface_flags: int) -> bytes:
    """Packs a Q3 shader_t: char name[64]; int surfaceFlags; int contentFlags;
    (surface flags precede content flags — verified against a real Xonotic
    map's caulk shader, which decodes correctly only with this order)."""
    name = name[:63]
    return struct.pack("<64s2i", name, surface_flags, content_flags)


def _pack_face(texture, effect, ftype, vertex, n_vertexes, meshvert, n_meshverts,
               lm_index=-1, patch_w=0, patch_h=0) -> bytes:
    return struct.pack(
        "<26i",
        texture, effect, ftype, vertex, n_vertexes, meshvert, n_meshverts,
        lm_index,
        0, 0,          # lm_start[2]
        0, 0,          # lm_size[2]
        0, 0, 0,       # lm_origin
        0, 0, 0, 0, 0, 0,  # lm_vecs[2][3]
        0, 0, 1,       # normal
        patch_w, patch_h,
    )


def build_fixture_bsp(*, solid: bool = True) -> bytes:
    CONTENTS_SOLID = 0x1

    entities_text = (
        '{\n'
        '"classname" "worldspawn"\n'
        '"message" "MyXonotic synthetic test fixture"\n'
        '}\n'
        '{\n'
        '"classname" "info_player_deathmatch"\n'
        '"origin" "64 0 0"\n'
        '"angle" "90"\n'
        '}\n'
    ).encode("ascii") + b"\x00"

    shaders = _pack_shader(b"myx/fixture_floor", CONTENTS_SOLID if solid else 0, 0)

    # --- Quad face (2 triangles, model 0) ---
    quad_vertices = [
        _pack_vertex((0.0, 0.0, 0.0)),
        _pack_vertex((64.0, 0.0, 0.0)),
        _pack_vertex((64.0, 64.0, 0.0)),
        _pack_vertex((0.0, 64.0, 0.0)),
    ]
    quad_meshverts = struct.pack("<6i", 0, 1, 2, 0, 2, 3)
    quad_face = _pack_face(texture=0, effect=-1, ftype=1, vertex=0, n_vertexes=4, meshvert=0, n_meshverts=6)

    # --- Patch face (3x3 flat control grid, model 0) ---
    patch_vertices = []
    for gy in range(3):
        for gx in range(3):
            patch_vertices.append(_pack_vertex((gx * 32.0, gy * 32.0, 16.0)))
    patch_face = _pack_face(
        texture=0, effect=-1, ftype=2, vertex=len(quad_vertices), n_vertexes=9,
        meshvert=0, n_meshverts=0, patch_w=3, patch_h=3,
    )

    vertexes = b"".join(quad_vertices) + b"".join(patch_vertices)
    meshverts = quad_meshverts
    faces = quad_face + patch_face

    # --- Models: model 0 = worldspawn (both faces), model 1 = an inline
    # brush submodel with zero faces (represents a door/mover): must never
    # be treated as static world geometry by the importer. ---
    model0 = struct.pack("<6f4i", 0, 0, 0, 64, 64, 32, 0, 2, 0, 0)
    model1 = struct.pack("<6f4i", 0, 0, 0, 8, 8, 8, 2, 0, 0, 0)
    models = model0 + model1

    lumps_data = [b""] * LUMP_COUNT
    lumps_data[LUMP_ENTITIES] = entities_text
    lumps_data[LUMP_SHADERS] = shaders
    lumps_data[LUMP_MODELS] = models
    lumps_data[LUMP_VERTEXES] = vertexes
    lumps_data[LUMP_MESHVERTS] = meshverts
    lumps_data[LUMP_FACES] = faces
    # Remaining lumps (planes/nodes/leafs/brushes/etc.) are intentionally
    # left empty: this importer does not consume them, and BspReader must
    # tolerate zero-length lumps for anything it doesn't need.

    header_size = 8 + LUMP_COUNT * 8
    offset = header_size
    dir_entries = []
    body = b""
    for data in lumps_data:
        dir_entries.append((offset, len(data)))
        body += data
        offset += len(data)

    header = MAGIC + struct.pack("<i", VERSION)
    for off, length in dir_entries:
        header += struct.pack("<2i", off, length)

    return header + body


def build_shader_asymmetry_bsp() -> bytes:
    """Regression fixture for the surfaceFlags/contentFlags field-order bug:
    two shaders with deliberately asymmetric, non-zero, DIFFERENT values in
    each field so a byte-swap regression is caught immediately instead of
    silently passing on a fixture where both fields happen to look similar.
    Shader 0: surface=0x0,      content=0x1   (CONTENTS_SOLID only)
    Shader 1: surface=0x80,     content=0x0   (SURF_NODRAW only, not solid)
    """
    CONTENTS_SOLID = 0x1
    SURF_NODRAW = 0x80

    entities_text = b'{\n"classname" "worldspawn"\n}\n\x00'
    shaders = _pack_shader(b"myx/solid_only", CONTENTS_SOLID, 0x0) + \
              _pack_shader(b"myx/nodraw_only", 0x0, SURF_NODRAW)

    v = [_pack_vertex((0.0, 0.0, 0.0)), _pack_vertex((32.0, 0.0, 0.0)), _pack_vertex((32.0, 32.0, 0.0))]
    v += [_pack_vertex((0.0, 0.0, 8.0)), _pack_vertex((32.0, 0.0, 8.0)), _pack_vertex((32.0, 32.0, 8.0))]
    meshverts = struct.pack("<6i", 0, 1, 2, 0, 1, 2)
    face0 = _pack_face(texture=0, effect=-1, ftype=1, vertex=0, n_vertexes=3, meshvert=0, n_meshverts=3)
    face1 = _pack_face(texture=1, effect=-1, ftype=1, vertex=3, n_vertexes=3, meshvert=3, n_meshverts=3)
    faces = face0 + face1
    vertexes = b"".join(v)
    model0 = struct.pack("<6f4i", 0, 0, 0, 32, 32, 8, 0, 2, 0, 0)

    lumps_data = [b""] * LUMP_COUNT
    lumps_data[LUMP_ENTITIES] = entities_text
    lumps_data[LUMP_SHADERS] = shaders
    lumps_data[LUMP_MODELS] = model0
    lumps_data[LUMP_VERTEXES] = vertexes
    lumps_data[LUMP_MESHVERTS] = meshverts
    lumps_data[LUMP_FACES] = faces

    header_size = 8 + LUMP_COUNT * 8
    offset = header_size
    dir_entries = []
    body = b""
    for data in lumps_data:
        dir_entries.append((offset, len(data)))
        body += data
        offset += len(data)
    header = MAGIC + struct.pack("<i", VERSION)
    for off, length in dir_entries:
        header += struct.pack("<2i", off, length)
    return header + body


def build_truncated_header() -> bytes:
    """Too short to even contain a full lump directory."""
    return MAGIC + struct.pack("<i", VERSION) + b"\x00" * 10


def build_bad_magic() -> bytes:
    return b"NOPE" + struct.pack("<i", VERSION) + b"\x00" * (8 * LUMP_COUNT)


def build_unsupported_version() -> bytes:
    base = bytearray(build_fixture_bsp())
    struct.pack_into("<i", base, 4, 999)
    return bytes(base)


def build_huge_lump_count_attack() -> bytes:
    """Vertex lump length claims a gigantic record count while the file
    itself stays tiny (classic malicious-length attack): offset/length
    point past EOF, which BspReader must reject outright."""
    base = bytearray(build_fixture_bsp())
    header_size = 8 + LUMP_COUNT * 8
    entry_off = 8 + LUMP_VERTEXES * 8
    # Point at a huge length far beyond the actual file size.
    struct.pack_into("<2i", base, entry_off, header_size, 0x7FFFFFF0)
    return bytes(base)


def build_nan_vertex_attack() -> bytes:
    """A vertex position containing NaN, which must be rejected."""
    base = bytearray(build_fixture_bsp())
    # Locate vertex lump via its directory entry and overwrite the first
    # vertex's X coordinate with a NaN bit pattern.
    entry_off = 8 + LUMP_VERTEXES * 8
    voff, vlen = struct.unpack_from("<2i", base, entry_off)
    nan_bits = struct.pack("<f", float("nan"))
    base[voff:voff + 4] = nan_bits
    return bytes(base)
