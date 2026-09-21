#!/usr/bin/env python3
"""Rasterize the owner's LibreQuake-style controls; deterministic UI, not screenshots.

Palette matches TouchControls.cs/UIKit.cs from the owner's LibreQuake reference.
Uses DejaVu Sans (the reference UI's available fallback) with a dark text shadow.
"""
import io
import os
from pathlib import Path
import sys
import zipfile
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "android/app/src/main/assets/zz-xonotic-android-touch.pk3"
PALETTE = {
    "fire": (204, 51, 26, 140), "jump": (51, 128, 230, 128),
    "utility": (255, 255, 255, 89), "ring": (255, 255, 255, 64),
    "knob": (255, 230, 179, 128), "text": (255, 217, 153, 255),
    "shadow": (0, 0, 0, 230), "menu": (64, 46, 31, 235),
    "border": (204, 153, 77, 204),
}
FONT = os.environ.get("TOUCH_FONT", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
SCALE = 3

def make_icon(label="", color="utility", kind="circle", size=(256, 256)):
    w, h = size
    im = Image.new("RGBA", (w * SCALE, h * SCALE))
    d = ImageDraw.Draw(im)
    inset = 3 * SCALE
    box = (inset, inset, w*SCALE-inset-1, h*SCALE-inset-1)
    if kind == "ring":
        d.ellipse(box, outline=PALETTE["ring"], width=round(w * .045 * SCALE))
    elif kind == "menu":
        d.rectangle(box, fill=PALETTE["menu"], outline=PALETTE["border"], width=2*SCALE)
    else:
        d.ellipse(box, fill=PALETTE[color])
    if label:
        f = ImageFont.truetype(FONT, round(min(w,h)*(.42 if kind=="menu" else .22)*SCALE))
        while d.textbbox((0,0),label,font=f)[2] > w*SCALE*.86:
            f = ImageFont.truetype(FONT, f.size-1)
        b = d.textbbox((0,0), label, font=f)
        x = (w*SCALE-(b[2]-b[0]))/2-b[0]
        y = (h*SCALE-(b[3]-b[1]))/2-b[1]
        d.text((x+2*SCALE,y+2*SCALE),label,font=f,fill=PALETTE["shadow"])
        d.text((x,y),label,font=f,fill=PALETTE["text"])
    return im.resize(size, Image.Resampling.LANCZOS)

def make_icons():
    return {
        "gfx/touch_menu.tga": make_icon("II",kind="menu",size=(256,192)),
        "gfx/touch_keyboard.tga": make_icon("KEY"),
        "gfx/touch_movebutton.tga": make_icon(kind="ring"),
        "gfx/touch_moveknob.tga": make_icon(color="knob"),
        "gfx/touch_jumpbutton.tga": make_icon("JUMP","jump"),
        "gfx/touch_attackbutton.tga": make_icon("FIRE","fire"),
        "gfx/touch_attack2button.tga": make_icon("ALT","fire"),
        "gfx/touch_crouchbutton.tga": make_icon("CROUCH"),
        "gfx/touch_zoombutton.tga": make_icon("ZOOM"),
        "gfx/touch_weapnextbutton.tga": make_icon("WPN +"),
        "gfx/touch_weapprevbutton.tga": make_icon("WPN -"),
    }

CFG = """// Xonotic Android mobile defaults and explicit touch bindings.
vid_touchscreen 1
vid_touchscreen_density 2
cl_showfps 1
r_shadow_realtimeworld 0
r_shadow_realtimedlight 0
r_bloom 0
r_motionblur 0
vid_samples 1
bind CTRL +crouch
bind SHIFT +zoom
bind SPACE +jump
bind MOUSE1 +attack
bind MOUSE2 +attack2
bind MWHEELUP weapnext
bind MWHEELDOWN weapprev
unbind MOUSE4
unbind MOUSE5
"""

def main():
    Path(OUT).parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, img in make_icons().items():
            buf = io.BytesIO()
            img.save(buf, format="TGA")
            entry = zipfile.ZipInfo(name, (2026, 9, 21, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(entry, buf.getvalue(), compresslevel=9)
        entry = zipfile.ZipInfo("android.cfg", (2026, 9, 21, 0, 0, 0))
        entry.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(entry, CFG, compresslevel=9)
    print("wrote", OUT, Path(OUT).stat().st_size, "bytes")

if __name__ == "__main__":
    main()
