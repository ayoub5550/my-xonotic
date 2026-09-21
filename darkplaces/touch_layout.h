/*
touch_layout.h -- pure C, engine-independent touch control layout + finger
ownership state machine for the Android LibreQuake/Xonotic touch HUD.

Design notes (see AGENTS.md task history for the request this implements):

 - All positions are computed in a "virtual" coordinate space with a FIXED
   height of 720 units and a WIDTH that varies with the physical screen
   aspect ratio: virtualWidth = 720 * (screenWidth / screenHeight). Using a
   width that already bakes in the device aspect ratio means the transform
   into DarkPlaces' vid_conwidth/vid_conheight console space:
       scaleX = vid_conwidth  / virtualWidth
       scaleY = vid_conheight / 720
   comes out equal (because vid_conwidth/vid_conheight is kept at the same
   aspect ratio as the physical screen), so circles stay circles at any
   conwidth/conheight/vid_pixelheight combination and at any device aspect
   (tested logically for 16:9 and 20:9).

 - This file has NO dependency on SDL or any engine header so it can be
   compiled standalone by tools/test_touch_layout.c and included as-is by
   darkplaces/vid_sdl.c.

Layout (bottom-right anchored, matches the requested LibreQuake reference):
   FIRE       cx = W-265         cy = H-265          diam 190   (big, translucent red)
   ALT (fire2)cx = fire.cx-165   cy = fire.cy         diam 110   (smaller, left of FIRE)
   JUMP       cx = W-395         cy = H-125           diam 110   (blue, below-left of FIRE)
   WPN_NEXT   cx = W-120         cy = H-390           diam 100   (white, lower of the pair)
   WPN_PREV   cx = W-120         cy = H-500           diam 100   (white, above WPN_NEXT)
   CROUCH     cx = 96            cy = H-560           diam  72   (unobtrusive, upper-left, above move zone)
   ZOOM       cx = 184           cy = H-560           diam  72   (unobtrusive, next to crouch)
   PAUSE      cx = W-44          cy = 36              64 x 48    (top-right, top-anchored)

   MOVE (dynamic joystick): any finger-down in the left 40% of the screen
   (x < 0.40 * W), below a top strip reserved for CROUCH/ZOOM/PAUSE and not
   already inside another button, becomes the joystick origin. Ring
   diameter 190 (visually matches FIRE), knob diameter 84, deadzone 12% of
   ring radius.

   LOOK (drag-look, no visible stick): any finger-down at x >= 0.40 * W that
   is not consumed by a button becomes a look-drag owner; its frame-to-frame
   motion feeds mouse-look. The FIRE finger ALSO drives look while held
   (owner TOUCH_OWNER_FIRE contributes both the attack button state and a
   look delta), matching "FIRE finger must also drag look while held".
*/
#ifndef TOUCH_LAYOUT_H
#define TOUCH_LAYOUT_H

#include <math.h>

#ifdef __cplusplus
extern "C" {
#endif

#define TOUCH_LAYOUT_MAXFINGERS 11
#define TOUCH_REFERENCE_HEIGHT 720.0f

typedef struct touch_circle_s
{
	float cx, cy;   /* virtual-space center, H=720 reference */
	float diam;     /* virtual-space diameter */
} touch_circle_t;

typedef struct touch_rect_s
{
	float cx, cy;   /* virtual-space center */
	float w, h;     /* virtual-space size */
} touch_rect_t;

typedef struct touch_layout_s
{
	float virtual_width;   /* 720 * aspect */
	float virtual_height;  /* always 720 */
	touch_circle_t fire;
	touch_circle_t alt;
	touch_circle_t jump;
	touch_circle_t wpn_next;
	touch_circle_t wpn_prev;
	touch_circle_t crouch;
	touch_circle_t zoom;
	touch_rect_t   pause;
	float move_zone_width;   /* virtual-space: left edge of the LOOK zone */
	float move_ring_diam;
	float move_knob_diam;
	float move_deadzone_frac; /* fraction of ring radius, 0..1 */
	float move_top_reserved;  /* virtual-space Y below which the move zone starts (avoids crouch/zoom/pause) */
} touch_layout_t;

/* Fills a layout purely from the device aspect ratio (width/height, >0). */
static inline void TouchLayout_Compute(float aspect, touch_layout_t *out)
{
	float W, H;
	if (aspect <= 0.0f)
		aspect = 16.0f / 9.0f;
	H = TOUCH_REFERENCE_HEIGHT;
	W = H * aspect;

	out->virtual_width = W;
	out->virtual_height = H;

	out->fire.cx = W - 265.0f;
	out->fire.cy = H - 265.0f;
	out->fire.diam = 190.0f;

	out->alt.cx = out->fire.cx - 165.0f;
	out->alt.cy = out->fire.cy;
	out->alt.diam = 110.0f;

	out->jump.cx = W - 395.0f;
	out->jump.cy = H - 125.0f;
	out->jump.diam = 110.0f;

	out->wpn_next.cx = W - 120.0f;
	out->wpn_next.cy = H - 390.0f;
	out->wpn_next.diam = 100.0f;

	out->wpn_prev.cx = W - 120.0f;
	out->wpn_prev.cy = H - 500.0f;
	out->wpn_prev.diam = 100.0f;

	out->crouch.cx = 96.0f;
	out->crouch.cy = H - 560.0f;
	out->crouch.diam = 72.0f;

	out->zoom.cx = 184.0f;
	out->zoom.cy = H - 560.0f;
	out->zoom.diam = 72.0f;

	out->pause.cx = W - 44.0f;
	out->pause.cy = 36.0f;
	out->pause.w = 64.0f;
	out->pause.h = 48.0f;

	out->move_zone_width = W * 0.40f;
	out->move_ring_diam = 190.0f;
	out->move_knob_diam = 84.0f;
	out->move_deadzone_frac = 0.12f;
	out->move_top_reserved = out->crouch.cy + out->crouch.diam * 0.5f + 12.0f; /* strip below crouch/zoom row */
}

/* Uniform scale from virtual space to vid_conwidth/vid_conheight pixel
   space. Callers should assert fabs(scalex-scaley) is small; both are
   returned so misconfigured (non-aspect-matching) conwidth/conheight still
   degrade gracefully instead of crashing. */
static inline void TouchLayout_Scale(const touch_layout_t *virt, float conwidth, float conheight, float *scalex, float *scaley)
{
	*scalex = (virt->virtual_width > 0.0f) ? (conwidth / virt->virtual_width) : 1.0f;
	*scaley = (virt->virtual_height > 0.0f) ? (conheight / virt->virtual_height) : 1.0f;
}

/* ---- finger ownership state machine ---- */

typedef enum touch_owner_e
{
	TOUCH_OWNER_NONE = 0,
	TOUCH_OWNER_MOVE,
	TOUCH_OWNER_LOOK,
	TOUCH_OWNER_FIRE,       /* attack + look */
	TOUCH_OWNER_ALT,        /* secondary attack */
	TOUCH_OWNER_JUMP,
	TOUCH_OWNER_WPN_NEXT,
	TOUCH_OWNER_WPN_PREV,
	TOUCH_OWNER_CROUCH,
	TOUCH_OWNER_ZOOM,
	TOUCH_OWNER_PAUSE,
	TOUCH_OWNER_MENU_CURSOR
} touch_owner_t;

typedef struct touch_slot_s
{
	int active;             /* 1 while the finger is down */
	touch_owner_t owner;    /* assigned once on finger-down, kept until release */
	float origin_x, origin_y;   /* virtual-space position at finger-down (move joystick center) */
	float cur_x, cur_y;         /* virtual-space current position */
	float prev_x, prev_y;       /* virtual-space previous-frame position (for look delta) */
} touch_slot_t;

static inline int TouchCircle_Hit(const touch_circle_t *c, float x, float y)
{
	float dx = x - c->cx;
	float dy = y - c->cy;
	float r = c->diam * 0.5f;
	return (dx * dx + dy * dy) <= (r * r);
}

static inline int TouchRect_Hit(const touch_rect_t *r, float x, float y)
{
	float hw = r->w * 0.5f, hh = r->h * 0.5f;
	return (x >= r->cx - hw && x <= r->cx + hw && y >= r->cy - hh && y <= r->cy + hh);
}

/* Pure hit-test/ownership decision for a brand-new finger-down at (x,y) in
   virtual space. Buttons take priority over the move/look zones. Returns
   the owner that should be captured; does not mutate anything. */
static inline touch_owner_t TouchLayout_ClassifyDown(const touch_layout_t *layout, float x, float y)
{
	if (TouchCircle_Hit(&layout->fire, x, y))
		return TOUCH_OWNER_FIRE;
	if (TouchCircle_Hit(&layout->alt, x, y))
		return TOUCH_OWNER_ALT;
	if (TouchCircle_Hit(&layout->jump, x, y))
		return TOUCH_OWNER_JUMP;
	if (TouchCircle_Hit(&layout->wpn_next, x, y))
		return TOUCH_OWNER_WPN_NEXT;
	if (TouchCircle_Hit(&layout->wpn_prev, x, y))
		return TOUCH_OWNER_WPN_PREV;
	if (TouchCircle_Hit(&layout->crouch, x, y))
		return TOUCH_OWNER_CROUCH;
	if (TouchCircle_Hit(&layout->zoom, x, y))
		return TOUCH_OWNER_ZOOM;
	if (TouchRect_Hit(&layout->pause, x, y))
		return TOUCH_OWNER_PAUSE;

	if (x < layout->move_zone_width && y >= layout->move_top_reserved)
		return TOUCH_OWNER_MOVE;

	return TOUCH_OWNER_LOOK;
}

/* Computes the joystick's relative move vector (-1..1 each axis) for a MOVE
   slot, given the ring radius (virtual units) and deadzone fraction. */
static inline void TouchLayout_MoveVector(const touch_layout_t *layout, const touch_slot_t *slot, float *outx, float *outy)
{
	float radius = layout->move_ring_diam * 0.5f;
	float dx = slot->cur_x - slot->origin_x;
	float dy = slot->cur_y - slot->origin_y;
	float dist2, dist, nx, ny;

	dist2 = dx * dx + dy * dy;
	dist = (dist2 > 0.0f) ? sqrtf(dist2) : 0.0f;

	if (dist < radius * layout->move_deadzone_frac)
	{
		*outx = 0.0f;
		*outy = 0.0f;
		return;
	}

	if (dist > radius)
	{
		nx = dx / dist;
		ny = dy / dist;
	}
	else
	{
		nx = dx / radius;
		ny = dy / radius;
	}
	*outx = nx;
	*outy = ny;
}

/* Look delta since previous frame, in virtual units (caller applies sensitivity). */
static inline void TouchLayout_LookDelta(const touch_slot_t *slot, float *outdx, float *outdy)
{
	*outdx = slot->cur_x - slot->prev_x;
	*outdy = slot->cur_y - slot->prev_y;
}

/* Releases every slot's owner (e.g. on menu transition / focus loss) without
   forcing SDL_FINGERUP; slots stay 'active' if the finger is still down so a
   held finger doesn't get silently reassigned mid-touch on the same frame,
   but its owner becomes NONE until the finger is lifted and pressed again. */
static inline void TouchLayout_ReleaseAllOwners(touch_slot_t slots[TOUCH_LAYOUT_MAXFINGERS])
{
	int i;
	for (i = 0; i < TOUCH_LAYOUT_MAXFINGERS; i++)
		slots[i].owner = TOUCH_OWNER_NONE;
}

#ifdef __cplusplus
}
#endif

#endif /* TOUCH_LAYOUT_H */
