#!/usr/bin/env python3
"""Rasterise the first-person weapon OBJ dumps written by LocalTests.WeaponPlacement
(Artifacts/weapons/<Type>.obj, WeaponView-local coordinates) as the player camera
would see them: vertical FOV 85, near 0.05, WeaponView at RigRestLocalPosition.

The sandbox cannot render with Unity (no GPU), so this is the closest thing to a
screenshot: flat-shaded triangles + bone lines, one tile per weapon.

usage: python3 tools/weapon_snapshot.py [Artifacts/weapons] [out.png]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

FOV_V = 85.0
NEAR = 0.05
ASPECT = 2400 / 1080    # ayoub's phone (landscape)
TILE_W, TILE_H = 640, 320
REST = np.array([0.0, 0.04, 0.05])   # WeaponView.RigRestLocalPosition


def load_obj(path):
    verts, faces, lines = [], [], []
    for ln in open(path):
        parts = ln.split()
        if not parts:
            continue
        if parts[0] == "v":
            verts.append([float(x) for x in parts[1:4]])
        elif parts[0] == "f":
            faces.append([int(p.split("/")[0]) - 1 for p in parts[1:4]])
        elif parts[0] == "l":
            lines.append([int(p) - 1 for p in parts[1:3]])
    return np.array(verts, dtype=float), np.array(faces, dtype=int), np.array(lines, dtype=int)


def project(p):
    """Camera-space point -> pixel (x, y) or None if behind the near plane."""
    z = p[2]
    if z < NEAR:
        return None
    f = 1.0 / math.tan(math.radians(FOV_V) / 2)
    ndc_x = (p[0] / z) * f / ASPECT
    ndc_y = (p[1] / z) * f
    return ((ndc_x + 1) * 0.5 * TILE_W, (1 - ndc_y) * 0.5 * TILE_H)


def clip_near(tri):
    """Sutherland-Hodgman clip of one triangle against z = NEAR; returns a polygon or None."""
    out = []
    n = len(tri)
    for i in range(n):
        a, b = tri[i], tri[(i + 1) % n]
        ina, inb = a[2] >= NEAR, b[2] >= NEAR
        if ina:
            out.append(a)
        if ina != inb:
            t = (NEAR - a[2]) / (b[2] - a[2])
            out.append(a + (b - a) * t)
    return np.array(out) if len(out) >= 3 else None


def render(path, title):
    verts, faces, lines = load_obj(path)
    cam = verts + REST
    img = Image.new("RGB", (TILE_W, TILE_H), (28, 32, 40))
    d = ImageDraw.Draw(img)
    # crosshair + horizon
    d.line([(TILE_W / 2 - 10, TILE_H / 2), (TILE_W / 2 + 10, TILE_H / 2)], fill=(90, 200, 90))
    d.line([(TILE_W / 2, TILE_H / 2 - 10), (TILE_W / 2, TILE_H / 2 + 10)], fill=(90, 200, 90))
    # painter's algorithm, far -> near
    if len(faces):
        depth = cam[faces].mean(axis=1)[:, 2]
        order = np.argsort(-depth)
        light = np.array([0.3, 0.8, -0.5]); light /= np.linalg.norm(light)
        clipped = 0
        for fi in order:
            tri = clip_near(cam[faces[fi]])
            if tri is None:
                clipped += 1
                continue
            pts = [project(p) for p in tri]
            if any(p is None for p in pts):
                continue
            n = np.cross(tri[1] - tri[0], tri[2] - tri[0])
            ln = np.linalg.norm(n)
            if ln < 1e-12:
                continue
            n /= ln
            shade = 0.35 + 0.65 * abs(float(n @ light))
            col = (int(180 * shade), int(170 * shade), int(150 * shade))
            d.polygon(pts, fill=col, outline=(int(col[0] * 0.6), int(col[1] * 0.6), int(col[2] * 0.6)))
    for a, b in lines:
        pa, pb = project(cam[a]), project(cam[b])
        if pa and pb:
            d.line([pa, pb], fill=(255, 80, 80), width=2)
            d.ellipse([pb[0] - 3, pb[1] - 3, pb[0] + 3, pb[1] + 3], fill=(255, 200, 60))
    behind = int((cam[:, 2] < NEAR).sum())
    d.text((8, 6), f"{title}  verts={len(verts)} tris={len(faces)} behind-near={behind}", fill=(240, 240, 240))
    mn, mx = cam.min(axis=0), cam.max(axis=0)
    d.text((8, TILE_H - 16), f"x {mn[0]:+.2f}..{mx[0]:+.2f}  y {mn[1]:+.2f}..{mx[1]:+.2f}  z {mn[2]:+.2f}..{mx[2]:+.2f}",
           fill=(200, 200, 200))
    return img


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "Artifacts/weapons"
    out = sys.argv[2] if len(sys.argv) > 2 else "Artifacts/weapon-snapshot.png"
    files = sorted(f for f in os.listdir(src) if f.endswith(".obj"))
    tiles = [render(os.path.join(src, f), f[:-4]) for f in files]
    cols = 2
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (TILE_W * cols, TILE_H * rows), (0, 0, 0))
    for i, t in enumerate(tiles):
        sheet.paste(t, ((i % cols) * TILE_W, (i // cols) * TILE_H))
    sheet.save(out)
    print("wrote", out, "tiles", len(tiles))


if __name__ == "__main__":
    main()
