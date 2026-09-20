#!/usr/bin/env python3
"""Generate the bundled zz-xonotic-android-touch.pk3 (touch-control icons + mobile cfg)."""
import io, zipfile, os, sys
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "android/app/src/main/assets/zz-xonotic-android-touch.pk3"

def icon(size, draw_fn):
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    draw_fn(d, size)
    return img

def ring(d, s, color=(255, 255, 255, 170)):
    w, h = s
    m = 6
    d.ellipse([m, m, w - m, h - m], outline=color, width=6)

def label(d, s, text, color=(255, 255, 255, 230)):
    w, h = s
    try:
        f = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", int(h * 0.42))
    except Exception:
        f = ImageFont.load_default()
    bbox = d.textbbox((0, 0), text, font=f)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((w - tw) / 2 - bbox[0], (h - th) / 2 - bbox[1]), text, font=f, fill=color)

def button(d, s, text, fill=(20, 24, 32, 150)):
    w, h = s
    d.rounded_rectangle([4, 4, w - 4, h - 4], radius=18, fill=fill, outline=(255, 255, 255, 190), width=4)
    label(d, s, text)

def stick(d, s, text):
    ring(d, s)
    w, h = s
    c = (w // 2, h // 2); r = w // 6
    d.ellipse([c[0]-r, c[1]-r, c[0]+r, c[1]+r], fill=(255, 255, 255, 120))
    label(d, (w, h // 3), text)

icons = {
    "gfx/touch_menu.tga":          icon((128, 128), lambda d, s: button(d, s, "≡")),
    "gfx/touch_keyboard.tga":      icon((128, 128), lambda d, s: button(d, s, "⌨")),
    "gfx/touch_movebutton.tga":    icon((256, 256), lambda d, s: stick(d, s, "MOVE")),
    "gfx/touch_aimbutton.tga":     icon((256, 256), lambda d, s: stick(d, s, "AIM")),
    "gfx/touch_jumpbutton.tga":    icon((256, 128), lambda d, s: button(d, s, "JUMP")),
    "gfx/touch_attackbutton.tga":  icon((256, 128), lambda d, s: button(d, s, "FIRE", (140, 30, 30, 170))),
    "gfx/touch_attack2button.tga": icon((256, 128), lambda d, s: button(d, s, "ALT", (30, 60, 140, 170))),
    "gfx/touch_crouchbutton.tga":  icon((256, 128), lambda d, s: button(d, s, "CROUCH")),
    "gfx/touch_zoombutton.tga":    icon((128, 128), lambda d, s: button(d, s, "ZOOM")),
    "gfx/touch_weapnextbutton.tga": icon((256, 128), lambda d, s: button(d, s, "WPN+")),
    "gfx/touch_weapprevbutton.tga": icon((256, 128), lambda d, s: button(d, s, "WPN-")),
}

cfg = """// xonotic-android: mobile defaults (loaded after default.cfg via autoexec chain)
vid_touchscreen 1
vid_touchscreen_density 2
cl_showfps 1
r_shadow_realtimeworld 0
r_shadow_realtimedlight 0
r_bloom 0
r_motionblur 0
vid_samples 1
// Explicit mobile bindings; upstream SHIFT is crouch, not zoom.
bind CTRL +crouch
bind SHIFT +zoom
bind SPACE +jump
bind MOUSE1 +attack
bind MOUSE2 +attack2
bind MWHEELUP weapnext
bind MWHEELDOWN weapprev
// Stick presses must not also trigger upstream weaplast / grapple hook.
unbind MOUSE4
unbind MOUSE5
"""

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_STORED) as z:
    for name, img in icons.items():
        buf = io.BytesIO(); img.save(buf, format="TGA"); z.writestr(name, buf.getvalue())
    z.writestr("android.cfg", cfg)
print("wrote", OUT, os.path.getsize(OUT), "bytes")
