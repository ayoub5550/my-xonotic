"""HTTP range failure cases, mocked only; real hashes are checked by the fetch tool."""
import io
from pathlib import Path
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/content"))
import fetch_reference as fetch


class Response(io.BytesIO):
    def __init__(self, data, status, content_range):
        super().__init__(data)
        self.status = status
        self.headers = {"Content-Range": content_range}


class ReferenceRanges(unittest.TestCase):
    def test_exact_range_accepted_and_budgeted(self):
        reply = Response(b"IBSP", 206, f"bytes 0-3/{fetch.ARCHIVE_BYTES}")
        budget = [0]
        with mock.patch.object(fetch.urllib.request, "urlopen", return_value=reply):
            self.assertEqual(fetch.RangeReader(budget).read(4), b"IBSP")
        self.assertEqual(budget, [4])

    def test_ignored_range_refused_without_reading_payload(self):
        reply = Response(b"IBSP", 200, "")
        with mock.patch.object(fetch.urllib.request, "urlopen", return_value=reply):
            with self.assertRaises(ValueError):
                fetch.RangeReader([0]).read(4)

    def test_wrong_content_range_refused(self):
        reply = Response(b"IBSP", 206, f"bytes 4-7/{fetch.ARCHIVE_BYTES}")
        with mock.patch.object(fetch.urllib.request, "urlopen", return_value=reply):
            with self.assertRaises(ValueError):
                fetch.RangeReader([0]).read(4)

    def test_short_range_refused(self):
        reply = Response(b"IB", 206, f"bytes 0-3/{fetch.ARCHIVE_BYTES}")
        with mock.patch.object(fetch.urllib.request, "urlopen", return_value=reply):
            with self.assertRaises(ValueError):
                fetch.RangeReader([0]).read(4)

    def test_budget_refused_before_network(self):
        with mock.patch.object(fetch.urllib.request, "urlopen") as network:
            with self.assertRaises(ValueError):
                fetch.RangeReader([fetch.MAX_TRANSFER]).read(1)
            network.assert_not_called()


if __name__ == "__main__":
    unittest.main()
