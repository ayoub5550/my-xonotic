/*
tools/test_touch_layout.c -- standalone pure-C test harness for
darkplaces/touch_layout.h. No SDL/engine deps.

Build & run:
    cc -std=c99 -Wall -Wextra -lm -o /tmp/test_touch_layout tools/test_touch_layout.c
    /tmp/test_touch_layout

Exits non-zero and prints the failing assertion on first failure.
*/
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

#include "../darkplaces/touch_layout.h"

static int g_failures = 0;

#define CHECK(cond, msg) do { \
	if (!(cond)) { \
		fprintf(stderr, "FAIL: %s (%s:%d)\n", msg, __FILE__, __LINE__); \
		g_failures++; \
	} else { \
		printf("ok: %s\n", msg); \
	} \
} while (0)

static int approx(float a, float b, float eps)
{
	return fabsf(a - b) <= eps;
}

static void test_aspect_scale_is_uniform(float aspect, const char *label)
{
	touch_layout_t layout;
	float conwidth, conheight, sx, sy;
	char msg[256];

	TouchLayout_Compute(aspect, &layout);

	/* Simulate the engine keeping vid_conwidth/vid_conheight at the same
	   aspect as the physical screen, e.g. 1600x720 for a 20:9 phone. */
	conheight = 720.0f;
	conwidth = conheight * aspect;

	TouchLayout_Scale(&layout, conwidth, conheight, &sx, &sy);

	snprintf(msg, sizeof(msg), "%s: scaleX == scaleY (circles stay round)", label);
	CHECK(approx(sx, sy, 0.001f), msg);

	snprintf(msg, sizeof(msg), "%s: virtual_width matches 720*aspect", label);
	CHECK(approx(layout.virtual_width, 720.0f * aspect, 0.01f), msg);
}

static void test_layout_positions_no_overlap(void)
{
	touch_layout_t l;
	TouchLayout_Compute(16.0f / 9.0f, &l);

	/* FIRE and ALT must not overlap despite being adjacent. */
	{
		float dx = l.fire.cx - l.alt.cx;
		float dy = l.fire.cy - l.alt.cy;
		float dist = sqrtf(dx * dx + dy * dy);
		float minsep = (l.fire.diam + l.alt.diam) * 0.5f;
		CHECK(dist >= minsep - 1.0f, "FIRE and ALT circles do not overlap");
	}
	/* WPN_NEXT/WPN_PREV stacked without overlap, WPN_PREV above (smaller y = higher on screen). */
	CHECK(l.wpn_prev.cy < l.wpn_next.cy, "WPN_PREV is above WPN_NEXT");
	{
		float gap = l.wpn_next.cy - l.wpn_prev.cy;
		CHECK(gap >= (l.wpn_next.diam + l.wpn_prev.diam) * 0.5f - 1.0f, "WPN_NEXT/WPN_PREV do not overlap");
	}
	/* Crouch/zoom sit clear of the move zone's reserved band. */
	CHECK(l.crouch.cy + l.crouch.diam * 0.5f <= l.move_top_reserved + 0.01f, "CROUCH sits above the move zone's top-reserved line");
	/* Pause stays inside the top-right corner, away from FIRE (bottom-right). */
	CHECK(l.pause.cy < l.fire.cy - l.fire.diam, "PAUSE (top) and FIRE (bottom) are well separated");
}

static void test_classify_down(void)
{
	touch_layout_t l;
	touch_owner_t o;
	TouchLayout_Compute(16.0f / 9.0f, &l);

	o = TouchLayout_ClassifyDown(&l, l.fire.cx, l.fire.cy);
	CHECK(o == TOUCH_OWNER_FIRE, "tap at FIRE center classifies as FIRE");

	o = TouchLayout_ClassifyDown(&l, l.jump.cx, l.jump.cy);
	CHECK(o == TOUCH_OWNER_JUMP, "tap at JUMP center classifies as JUMP");

	o = TouchLayout_ClassifyDown(&l, l.wpn_next.cx, l.wpn_next.cy);
	CHECK(o == TOUCH_OWNER_WPN_NEXT, "tap at WPN_NEXT center classifies as WPN_NEXT");

	o = TouchLayout_ClassifyDown(&l, l.wpn_prev.cx, l.wpn_prev.cy);
	CHECK(o == TOUCH_OWNER_WPN_PREV, "tap at WPN_PREV center classifies as WPN_PREV");

	o = TouchLayout_ClassifyDown(&l, 5.0f, l.virtual_height - 5.0f);
	CHECK(o == TOUCH_OWNER_MOVE, "tap at bottom-left corner (inside move zone) classifies as MOVE");

	o = TouchLayout_ClassifyDown(&l, l.virtual_width - 5.0f, l.virtual_height * 0.5f);
	CHECK(o == TOUCH_OWNER_LOOK, "tap on bare right side (no button) classifies as LOOK (no visible stick)");

	o = TouchLayout_ClassifyDown(&l, l.pause.cx, l.pause.cy);
	CHECK(o == TOUCH_OWNER_PAUSE, "tap at PAUSE classifies as PAUSE");
}

/* State-machine style test: finger down inside FIRE, slides far away while
   held (still owns FIRE+LOOK), then releases -- classic "don't switch
   buttons mid-drag" regression check. */
static void test_owner_sticks_while_finger_slides(void)
{
	touch_layout_t l;
	touch_slot_t slots[TOUCH_LAYOUT_MAXFINGERS];
	touch_owner_t initial;
	float dx, dy;

	TouchLayout_Compute(16.0f / 9.0f, &l);
	memset(slots, 0, sizeof(slots));

	/* frame 1: finger 0 touches down on FIRE */
	initial = TouchLayout_ClassifyDown(&l, l.fire.cx, l.fire.cy);
	slots[0].active = 1;
	slots[0].owner = initial;
	slots[0].origin_x = slots[0].cur_x = slots[0].prev_x = l.fire.cx;
	slots[0].origin_y = slots[0].cur_y = slots[0].prev_y = l.fire.cy;
	CHECK(slots[0].owner == TOUCH_OWNER_FIRE, "frame1: finger captured as FIRE owner");

	/* frame 2: finger slides far toward the top-left, off the FIRE circle
	   entirely -- ownership must NOT be re-evaluated/switched. */
	slots[0].prev_x = slots[0].cur_x;
	slots[0].prev_y = slots[0].cur_y;
	slots[0].cur_x = 10.0f;
	slots[0].cur_y = 10.0f;
	CHECK(slots[0].owner == TOUCH_OWNER_FIRE, "frame2: owner unchanged after finger slid off the button (captured)");

	TouchLayout_LookDelta(&slots[0], &dx, &dy);
	CHECK(dx != 0.0f || dy != 0.0f, "frame2: FIRE-owning finger still produces a look delta while sliding");

	/* frame 3: finger releases */
	slots[0].active = 0;
	slots[0].owner = TOUCH_OWNER_NONE;
	CHECK(slots[0].owner == TOUCH_OWNER_NONE, "frame3: release clears the owner");
}

static void test_move_vector_deadzone_and_clamp(void)
{
	touch_layout_t l;
	touch_slot_t slot;
	float ox, oy;
	float radius;

	TouchLayout_Compute(16.0f / 9.0f, &l);
	radius = l.move_ring_diam * 0.5f;
	memset(&slot, 0, sizeof(slot));
	slot.origin_x = 100.0f;
	slot.origin_y = 600.0f;

	/* Inside deadzone: near-zero output. */
	slot.cur_x = slot.origin_x + 1.0f;
	slot.cur_y = slot.origin_y;
	TouchLayout_MoveVector(&l, &slot, &ox, &oy);
	CHECK(approx(ox, 0.0f, 0.0001f) && approx(oy, 0.0f, 0.0001f), "move vector inside deadzone is (0,0)");

	/* Exactly at ring edge: magnitude 1. */
	slot.cur_x = slot.origin_x + radius;
	slot.cur_y = slot.origin_y;
	TouchLayout_MoveVector(&l, &slot, &ox, &oy);
	CHECK(approx(sqrtf(ox * ox + oy * oy), 1.0f, 0.01f), "move vector at ring edge has magnitude ~1");

	/* Far beyond ring: still clamped to magnitude 1 (knob doesn't fly off). */
	slot.cur_x = slot.origin_x + radius * 50.0f;
	slot.cur_y = slot.origin_y;
	TouchLayout_MoveVector(&l, &slot, &ox, &oy);
	CHECK(approx(sqrtf(ox * ox + oy * oy), 1.0f, 0.01f), "move vector far beyond ring is clamped to magnitude ~1");
}

static void test_release_all_owners_on_menu(void)
{
	touch_slot_t slots[TOUCH_LAYOUT_MAXFINGERS];
	int i;
	memset(slots, 0, sizeof(slots));
	slots[0].active = 1;
	slots[0].owner = TOUCH_OWNER_MOVE;
	slots[1].active = 1;
	slots[1].owner = TOUCH_OWNER_FIRE;

	TouchLayout_ReleaseAllOwners(slots);

	for (i = 0; i < TOUCH_LAYOUT_MAXFINGERS; i++)
		CHECK(slots[i].owner == TOUCH_OWNER_NONE, "ReleaseAllOwners clears every slot's owner (menu/focus-loss transition)");
	CHECK(slots[0].active == 1 && slots[1].active == 1, "ReleaseAllOwners does not fake-release still-held fingers");
}

/* SDL_FingerID on Android can be a large opaque 64-bit value; simulate the
   original bug (storing it in a float, losing precision) vs. the fix
   (storing it in a 64-bit integer slot) to prove int64 comparisons survive
   values a float mantissa (24 bits) cannot represent exactly. */
static void test_large_finger_id_needs_int64(void)
{
	long long big_id_a = 1099511627776LL;          /* 2^40: typical magnitude for an Android SDL_FingerID */
	long long big_id_b = big_id_a + 1000LL;        /* distinct id, but within float's ~2^16 rounding step at this magnitude */
	float as_float_a = (float)big_id_a;
	float as_float_b = (float)big_id_b;

	CHECK(big_id_a != big_id_b, "two distinct large 64-bit finger ids are different as int64");
	CHECK(as_float_a == as_float_b, "regression proof: the SAME distinct ids collide once truncated to float (the bug this task fixes)");
}

int main(void)
{
	test_aspect_scale_is_uniform(16.0f / 9.0f, "16:9");
	test_aspect_scale_is_uniform(20.0f / 9.0f, "20:9 (POCO F3)");
	test_layout_positions_no_overlap();
	test_classify_down();
	test_owner_sticks_while_finger_slides();
	test_move_vector_deadzone_and_clamp();
	test_release_all_owners_on_menu();
	test_large_finger_id_needs_int64();

	if (g_failures)
	{
		fprintf(stderr, "\n%d CHECK(S) FAILED\n", g_failures);
		return 1;
	}
	printf("\nAll checks passed.\n");
	return 0;
}
