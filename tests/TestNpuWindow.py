"""No capture: check native-pixel tiling, edge reconstruction, cancellation and scope."""
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "PrismNpu"))
from window_preview import codex_package, snapshot, tiled_enhance, window_identity


class WindowChecks(unittest.TestCase):
    def test_native_pixels_and_partial_edges(self):
        rng = np.random.default_rng(24)
        image = rng.integers(0, 256, (251, 459, 3), dtype=np.uint8)
        calls = []
        def infer(tile):
            calls.append(tile.shape)
            return tile.repeat(4, axis=0).repeat(4, axis=1)
        output, count = tiled_enhance(image, infer, threading.Event())
        np.testing.assert_array_equal(output, image.repeat(2, axis=0).repeat(2, axis=1))
        self.assertEqual(count, 4)
        self.assertEqual(calls, [(270, 480, 3)] * 4)

    def test_cancel_before_inference(self):
        cancel = threading.Event()
        cancel.set()
        with self.assertRaises(InterruptedError):
            tiled_enhance(np.zeros((1, 1, 3), np.uint8), lambda tile: self.fail(), cancel)

    def test_cancel_before_capture(self):
        cancel = threading.Event()
        cancel.set()
        with patch("window_preview.capture_codex") as capture:
            with self.assertRaises(InterruptedError):
                snapshot(1, None, cancel, lambda n, total: None)
            capture.assert_not_called()

    def test_invalid_model_shape(self):
        with self.assertRaises(ValueError):
            tiled_enhance(np.zeros((1, 1, 3), np.uint8), lambda tile: tile, threading.Event())

    def test_codex_package_only(self):
        self.assertTrue(codex_package("OpenAI.Codex_26.1027.3384.1_x64__2p2nqsd0c76g0"))
        for name in ("OpenAI.ChatGPT_1.0_x64__2p2nqsd0c76g0", "OpenAI.Codex_1.0_x64__other", "Codex.exe", ""):
            self.assertFalse(codex_package(name))

    def test_tool_overlay_rejected_before_process_access(self):
        with patch("window_preview.ct.windll") as api:
            api.user32.IsWindowVisible.return_value = True
            api.user32.IsIconic.return_value = False
            api.user32.GetWindowLongPtrW.return_value = 0x2800A8
            with self.assertRaisesRegex(ValueError, "工具浮层"):
                window_identity(1)
            api.kernel32.OpenProcess.assert_not_called()


if __name__ == "__main__":
    unittest.main()
