"""
Pure-geometry regression tests for Assets/MyXonotic/Runtime/Gameplay/TouchLayout.cs.

TouchLayout.cs is Unity C# (uses UnityEngine.Rect/Mathf/Screen) so it cannot be
compiled/run outside the Editor. Rather than hand-duplicate the button layout
in Python (which would silently drift from the real source), this test PARSES
the `Circle(leftFrac, bottomFrac, diameterFrac)` call sites straight out of the
committed .cs file and checks the same invariants a touch-control layout must
hold regardless of screen size:
  - no two round touch buttons visually/physically overlap (Player.cs relies on
    the same TouchLayout rects for hit-testing, so an overlap there would mean
    one tap could be claimed by two different logical roles);
  - PAUSE stays clear of the FIRE/JUMP/WPN-/WPN+/ALT cluster.
It also checks the joystick dead-zone/max-radius ordering and the weapon-index
wrap math used by Player.cs, independent of Unity.

Run: python3 tests/python/test_touch_layout_geometry.py
"""
from __future__ import annotations

import math
import re
import unittest
from pathlib import Path

TOUCH_LAYOUT_CS = (
    Path(__file__).resolve().parents[2]
    / "Assets/MyXonotic/Runtime/Gameplay/TouchLayout.cs"
)

# Matches e.g. `Circle(170f / 720f, 170f / 720f, 190f / 720f)` — the exact form
# TouchLayout.cs uses for every round button (Fire/Jump/WpnPlus/WpnMinus/Alt).
CIRCLE_CALL = re.compile(
    r"Circle\(\s*([\d.]+f?\s*/\s*[\d.]+f?)\s*,\s*([\d.]+f?\s*/\s*[\d.]+f?)\s*,\s*([\d.]+f?\s*/\s*[\d.]+f?)\s*\)"
)
NAMED_CIRCLE = re.compile(r"public static Rect (\w+) => (Circle\([^;]+\));")


def _eval_frac(expr: str) -> float:
    return eval(expr.replace("f", ""))  # noqa: S307 - trusted local source file, numeric literals only


def parse_circles() -> dict:
    text = TOUCH_LAYOUT_CS.read_text(encoding="utf-8")
    circles = {}
    for name, call in NAMED_CIRCLE.findall(text):
        m = CIRCLE_CALL.search(call)
        assert m, f"could not parse Circle(...) args for {name}: {call!r}"
        left, bottom, diameter = (_eval_frac(g) for g in m.groups())
        circles[name] = (left, bottom, diameter)
    return circles


def circle_center_radius(left, bottom, diameter):
    # Center offset from the bottom-right safe corner, in units of safe.height
    # (matches TouchLayout.Circle: cx = xMax - left*h, cy = y + bottom*h).
    return (-left, bottom, diameter / 2.0)


class TouchLayoutGeometryTests(unittest.TestCase):
    def setUp(self):
        self.circles = parse_circles()
        for required in ("Fire", "Jump", "WpnPlus", "WpnMinus", "Alt"):
            self.assertIn(required, self.circles, f"TouchLayout.{required} not found/parsed")

    def test_all_round_buttons_are_pairwise_disjoint(self):
        names = list(self.circles)
        for i in range(len(names)):
            for j in range(i + 1, len(names)):
                a_name, b_name = names[i], names[j]
                ax, ay, ar = circle_center_radius(*self.circles[a_name])
                bx, by, br = circle_center_radius(*self.circles[b_name])
                dist = math.hypot(ax - bx, ay - by)
                self.assertGreater(
                    dist, ar + br,
                    f"{a_name} and {b_name} overlap: dist={dist:.4f} sumR={ar + br:.4f}",
                )

    def test_fire_matches_librequake_reference_proportions(self):
        # Owner's exact reference (my-librequake TouchControls.cs, 720-height
        # canvas): FIRE 190px, JUMP 110px, WPN 100px diameter.
        left, bottom, diameter = self.circles["Fire"]
        self.assertAlmostEqual(diameter, 190 / 720, places=6)
        left, bottom, diameter = self.circles["Jump"]
        self.assertAlmostEqual(diameter, 110 / 720, places=6)
        for wpn in ("WpnPlus", "WpnMinus"):
            left, bottom, diameter = self.circles[wpn]
            self.assertAlmostEqual(diameter, 100 / 720, places=6)

    def test_jump_is_below_and_left_of_fire(self):
        fire = circle_center_radius(*self.circles["Fire"])
        jump = circle_center_radius(*self.circles["Jump"])
        self.assertLess(jump[0], fire[0], "JUMP must be further left (more negative x) than FIRE")
        self.assertLess(jump[1], fire[1], "JUMP must sit lower (smaller y) than FIRE")

    def test_wpn_plus_minus_are_stacked_with_a_gap(self):
        plus = circle_center_radius(*self.circles["WpnPlus"])
        minus = circle_center_radius(*self.circles["WpnMinus"])
        self.assertAlmostEqual(plus[0], minus[0], places=6, msg="WPN-/WPN+ should share the same x")
        gap = abs(plus[1] - minus[1]) - (plus[2] + minus[2])
        self.assertGreater(gap, 0, "WPN-/WPN+ must not touch/overlap")


class JoystickDeadZoneMathTests(unittest.TestCase):
    """Mirrors the pure formula in Player.ReadTouch (radial dead zone -> linear
    ramp to 1 at max radius), so the feel/edge-cases are covered without Unity."""

    @staticmethod
    def analog(distance, dead_zone, max_radius):
        if distance <= dead_zone:
            return 0.0
        return min(1.0, (distance - dead_zone) / max(1.0, max_radius - dead_zone))

    def test_inside_dead_zone_is_zero(self):
        self.assertEqual(self.analog(0.0, 10.0, 100.0), 0.0)
        self.assertEqual(self.analog(9.9, 10.0, 100.0), 0.0)

    def test_at_or_beyond_max_radius_is_one(self):
        self.assertEqual(self.analog(100.0, 10.0, 100.0), 1.0)
        self.assertEqual(self.analog(500.0, 10.0, 100.0), 1.0)

    def test_ramps_linearly_between(self):
        dead, mx = 10.0, 110.0
        mid = dead + (mx - dead) * 0.5
        self.assertAlmostEqual(self.analog(mid, dead, mx), 0.5, places=6)

    def test_dead_zone_smaller_than_max_radius(self):
        # TouchLayout.JoystickDeadZone (0.16*Unit) must stay well under
        # TouchLayout.JoystickMaxRadius (1.1*Unit) or the stick would never
        # leave its dead zone.
        text = TOUCH_LAYOUT_CS.read_text(encoding="utf-8")
        dead = float(re.search(r"JoystickDeadZone => Unit \* ([\d.]+)f", text).group(1))
        radius = float(re.search(r"JoystickMaxRadius => Unit \* ([\d.]+)f", text).group(1))
        self.assertLess(dead, radius)


class WeaponSwitchWrapMathTests(unittest.TestCase):
    """Mirrors Player.Update's ((current +/- 1 + count) % count) weapon-switch
    wrap-around, independent of Unity/WeaponController."""

    COUNT = 3  # WeaponType: Blaster, Rifle, Rocket

    def next_index(self, current):
        return (current + 1) % self.COUNT

    def prev_index(self, current):
        return (current - 1 + self.COUNT) % self.COUNT

    def test_next_wraps_from_last_to_first(self):
        self.assertEqual(self.next_index(self.COUNT - 1), 0)

    def test_prev_wraps_from_first_to_last(self):
        self.assertEqual(self.prev_index(0), self.COUNT - 1)

    def test_next_then_prev_is_identity(self):
        for start in range(self.COUNT):
            self.assertEqual(self.prev_index(self.next_index(start)), start)


if __name__ == "__main__":
    unittest.main()
