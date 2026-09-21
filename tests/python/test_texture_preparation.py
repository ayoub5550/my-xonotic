import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

path = Path(__file__).resolve().parents[2] / "tools/content/prepare_unity_textures.py"
spec = importlib.util.spec_from_file_location("texture_preparation", path)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)
try:
    from PIL import Image
except ImportError:
    Image = None


class PathTests(unittest.TestCase):
    def test_content_path(self):
        self.assertEqual(tool.safe_texture_name("models/weapons/laser.dds"), "models/weapons/laser")

    def test_reject_unsafe(self):
        for value in ["../secret", "/absolute", r"..\secret", "C:/secret", "", "models/../x", "."]:
            with self.assertRaises(ValueError, msg=value):
                tool.safe_texture_name(value)


@unittest.skipIf(Image is None, "Pillow required for DDS conversion tests")
class DecodeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.root, self.output = self.base / "root", self.base / "decoded"
        (self.root / "dds/models").mkdir(parents=True)
        for name in ("one", "two"):
            Image.new("RGBA", (4, 4), (10, 20, 30, 255)).save(
                self.root / ("dds/models/" + name + ".dds"))

    def test_conversion_and_accumulated_provenance(self):
        first = tool.convert(self.root, self.output, ["models/one"])
        self.assertEqual(len(first), 1)
        tool.convert(self.root, self.output, ["models/two"])
        manifest = json.loads((self.output / "conversion-manifest.json").read_text())
        self.assertEqual(len(manifest), 2)
        with Image.open(self.output / "models/one.png") as image:
            self.assertEqual(image.getpixel((0, 0)), (10, 20, 30, 255))

    def test_missing_source_does_not_partially_convert(self):
        with self.assertRaises(FileNotFoundError):
            tool.convert(self.root, self.output, ["models/one", "models/missing"])
        self.assertFalse((self.output / "models/one.png").exists())

    def test_reject_spoofed_extension(self):
        Image.new("RGB", (4, 4)).save(self.root / "dds/models/fake.dds", format="PNG")
        with self.assertRaises(ValueError):
            tool.convert(self.root, self.output, ["models/one", "models/fake"])
        self.assertFalse((self.output / "models/one.png").exists())

    def test_symlink_escape(self):
        (self.root / "dds/escape").symlink_to(self.base, target_is_directory=True)
        with self.assertRaises(ValueError):
            tool.convert(self.root, self.output, ["escape/secret"])


if __name__ == "__main__":
    unittest.main()
