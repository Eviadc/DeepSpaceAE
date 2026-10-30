import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("extract_log", Path(__file__).with_name("extract-load-log.py"))
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)
WARNING = r"[Warning] The character with Unicode value \u6D3B was not found in the [OfficeCodePro-Regular SDF] font asset or any potential fallbacks. It was replaced by Unicode character \u0020 in text object [Text]."


class LogExtractionTests(unittest.TestCase):
    def test_glyph_flood_does_not_consume_original_exception_context(self):
        report = tool.extract(["[DeepSpaceAE] Error: Harmony patch failed", *([WARNING] * 20000), "at GameItem.Destroy()", "[ModLoader] Init failed"], context=2)
        self.assertIn("20000", report)
        self.assertIn("at GameItem.Destroy()", report)
        self.assertIn("Error: Harmony patch failed", report)
        self.assertLess(len(report), 1400)

    def test_other_fonts_objects_and_real_errors_are_kept(self):
        for line in (WARNING.replace("OfficeCodePro-Regular SDF", "Noto Sans"), WARNING.replace("[Text]", "[Node (Text)]"), "Error: " + WARNING):
            self.assertFalse(tool.console_glyph_warning(line))
        self.assertTrue(tool.console_glyph_warning(WARNING))

    def test_missing_translation_is_not_confused_with_missing_glyph(self):
        line = "[LocHelper] No translation found for key 中文键"
        self.assertIn(line, tool.extract([line]))

    def test_report_is_bounded_and_source_remains_unchanged(self):
        lines = ["[ModLoader] " + str(i) for i in range(10000)]
        original = tuple(lines)
        report = tool.extract(iter(lines), limit=20)
        self.assertIn("Report truncated: True", report)
        self.assertEqual(original, tuple(lines))
        self.assertLess(len(report), 1000)


if __name__ == "__main__":
    unittest.main()
