"""T272: execute the production verifier against both committed marker encodings."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("unit_glyphs", ROOT / "scripts/generate-unit-glyphs.py")
glyphs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(glyphs)


class MarkerStatureTests(unittest.TestCase):
    def test_current_encodings_measure_the_motif_not_opaque_field(self):
        self.assertEqual([], glyphs.verify_marker_stature(ROOT))
        for pack, expected in (("authored", [25, 28]), ("placeholder", [22, 27])):
            actual = [glyphs.marker_height((ROOT / "assets/packs" / pack / "army" / f"tier{tier}.bmp").read_bytes(),
                                           purple_field=pack == "authored") for tier in (1, 2)]
            self.assertEqual(expected, actual)

    def test_empty_and_equal_height_authored_motifs_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for pack in ("authored", "placeholder"):
                target = root / "assets/packs" / pack / "army"
                target.mkdir(parents=True)
                for tier in (1, 2):
                    (target / f"tier{tier}.bmp").write_bytes((ROOT / "assets/packs" / pack / "army" / f"tier{tier}.bmp").read_bytes())
            first = root / "assets/packs/authored/army/tier1.bmp"
            second = first.with_name("tier2.bmp")
            second.write_bytes(first.read_bytes())
            self.assertEqual(1, len(glyphs.verify_marker_stature(root)))
            first.write_bytes(glyphs.bmp_bytes(glyphs.Raster(32, glyphs.PURPLE)))
            self.assertEqual(1, len(glyphs.verify_marker_stature(root)))

    def test_placeholder_geometry_remains_exact_and_bad_headers_refuse(self):
        with self.assertRaises(ValueError):
            glyphs.marker_height(b"not a BMP")
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for pack in ("authored", "placeholder"):
                target = root / "assets/packs" / pack / "army"
                target.mkdir(parents=True)
                for tier in (1, 2):
                    (target / f"tier{tier}.bmp").write_bytes((ROOT / "assets/packs" / pack / "army" / f"tier{tier}.bmp").read_bytes())
            target = root / "assets/packs/placeholder/army"
            (target / "tier2.bmp").write_bytes((target / "tier1.bmp").read_bytes())
            self.assertEqual(1, len(glyphs.verify_marker_stature(root)))


if __name__ == "__main__":
    unittest.main()
