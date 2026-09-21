using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content.Bsp;

/// <summary>
/// Standalone (non-Unity) test driver, in the same spirit as ParserTests.cs:
/// reads a REAL Xonotic .bsp (e.g. ThirdParty/Xonotic/maps-pk3/maps/boil.bsp)
/// with the pure-.NET BspReader and inventories its item_*/weapon_* entities
/// as DATA ONLY -- classnames, counts, and that each entity's origin is
/// parsable/finite and converts to a distinct Unity position.
///
/// Deliberately does NOT duplicate BspPickupImporter's classname->PickupType
/// mapping table (that importer lives in the Editor assembly, which is
/// UnityEngine-dependent and not compilable/runnable by plain mcs/mono here).
/// A mirrored copy of that table would only prove this file agrees with
/// itself, not with the real importer; the actual supported/unsupported
/// mapping is exercised against BspPickupImporter.ClassToPlan directly in
/// Assets/MyXonotic/Editor/Import/PickupTests.cs (needs the Unity Editor).
/// This file only establishes the raw ground truth (what the real map
/// actually contains and where) that mapping test relies on.
///
/// Run with (see tests/run_all.sh for the exact invocation pattern):
///   mcs -target:exe -out:PickupEntityTests.exe \
///     Assets/MyXonotic/Runtime/Content/Bsp/*.cs tests/csharp/PickupEntityTests.cs
///   mono PickupEntityTests.exe ThirdParty/Xonotic/maps-pk3/maps/boil.bsp
///
/// Exit code 0 = all checks passed; non-zero = first failure's message is
/// printed to stderr, matching ParserTests.cs's convention.
/// </summary>
public static class PickupEntityTests
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: PickupEntityTests <real-map.bsp>");
            return 2;
        }

        try
        {
            var bytes = File.ReadAllBytes(args[0]);
            var doc = BspReader.Read(bytes);

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entity in doc.Entities)
            {
                string classname = entity.Get("classname");
                if (string.IsNullOrEmpty(classname)) continue;
                counts.TryGetValue(classname, out int n);
                counts[classname] = n + 1;
            }

            Console.WriteLine("Entity classes found: " + counts.Count);
            foreach (var kv in counts)
            {
                Console.WriteLine("  " + kv.Key + " x" + kv.Value);
            }

            int itemOrWeaponClasses = 0;
            foreach (var kv in counts)
            {
                if (kv.Key.StartsWith("item_", StringComparison.Ordinal) ||
                    kv.Key.StartsWith("weapon_", StringComparison.Ordinal))
                {
                    itemOrWeaponClasses++;
                }
            }
            Assert(itemOrWeaponClasses > 0, "real map should contain at least one item_*/weapon_* entity");

            // Every item_*/weapon_* entity must have a parsable, finite
            // origin, and repeated instances of the same classname must not
            // collapse onto the same converted Unity position (no fake/
            // uniform placement in the source data itself).
            foreach (var kv in counts)
            {
                if (!kv.Key.StartsWith("item_", StringComparison.Ordinal) &&
                    !kv.Key.StartsWith("weapon_", StringComparison.Ordinal))
                {
                    continue;
                }

                var seenPositions = new HashSet<string>();
                int matched = 0;
                foreach (var entity in doc.Entities)
                {
                    if (entity.Get("classname") != kv.Key) continue;
                    string originStr = entity.Get("origin");
                    Assert(originStr != null, kv.Key + " entity must have an origin");
                    var parts = originStr.Split(' ');
                    Assert(parts.Length == 3, kv.Key + " origin must have 3 components");
                    float x = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    Assert(!float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z), kv.Key + " origin must be finite");

                    var unity = BspCoordinateSpace.QuakeToUnity(new BspVec3(x, y, z));
                    Assert(!float.IsNaN(unity.X) && !float.IsNaN(unity.Y) && !float.IsNaN(unity.Z),
                        kv.Key + " converted origin must be finite");
                    seenPositions.Add(unity.X.ToString("F3") + "," + unity.Y.ToString("F3") + "," + unity.Z.ToString("F3"));
                    matched++;
                }
                Assert(matched == kv.Value, kv.Key + " every counted entity was actually iterated");
                if (matched > 1)
                {
                    Assert(seenPositions.Count == matched,
                        kv.Key + " has " + matched + " real placements that must convert to " + matched + " distinct Unity positions, got " + seenPositions.Count);
                }
            }

            Console.WriteLine("PickupEntityTests (data-only inventory): " + _checks + " checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
