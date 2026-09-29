"""Tests for documentation tooling; these do not run Unity or gameplay code."""
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from export_design import extract


class DesignExportTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.source = Path(directory.name) / "design.docx"

    def document(self, body):
        xml = ('<w:document xmlns:w="http://schemas.openxmlformats.org/'
               'wordprocessingml/2006/main"><w:body>' + body +
               '</w:body></w:document>')
        with ZipFile(self.source, "w") as archive:
            archive.writestr("word/document.xml", xml.encode("utf-8"))
        return self.source

    def test_joins_runs_and_preserves_polish_text(self):
        self.document('<w:p><w:r><w:t>Sala </w:t></w:r>'
                      '<w:r><w:t>tronowa — dźwignia</w:t></w:r></w:p>')
        self.assertEqual("Sala tronowa — dźwignia", extract(self.source))

    def test_skips_empty_paragraphs(self):
        self.document('<w:p/><w:p><w:r><w:t>   </w:t></w:r></w:p>'
                      '<w:p><w:r><w:t>Start</w:t></w:r></w:p>'
                      '<w:p><w:r><w:t>Wyjście</w:t></w:r></w:p>')
        self.assertEqual("Start\n\nWyjście", extract(self.source))

    def test_includes_table_paragraphs_in_document_order(self):
        self.document('<w:p><w:r><w:t>Przed</w:t></w:r></w:p>'
                      '<w:tbl><w:tr><w:tc><w:p><w:r><w:t>W tabeli</w:t>'
                      '</w:r></w:p></w:tc></w:tr></w:tbl>'
                      '<w:p><w:r><w:t>Po</w:t></w:r></w:p>')
        self.assertEqual("Przed\n\nW tabeli\n\nPo", extract(self.source))

    def test_does_not_invent_text_for_embedded_images(self):
        self.document('<w:p><w:r><w:drawing/></w:r></w:p>')
        self.assertEqual("", extract(self.source))

    def test_invalid_zip_fails_instead_of_returning_empty_success(self):
        self.source.write_bytes(b"not a DOCX")
        with self.assertRaises(BadZipFile):
            extract(self.source)

    def test_missing_document_part_fails(self):
        with ZipFile(self.source, "w") as archive:
            archive.writestr("other.xml", "<root/>")
        with self.assertRaises(KeyError):
            extract(self.source)

    def test_malformed_xml_fails(self):
        with ZipFile(self.source, "w") as archive:
            archive.writestr("word/document.xml", "<unclosed>")
        with self.assertRaises(ET.ParseError):
            extract(self.source)

    def test_cli_accepts_explicit_document_path(self):
        self.document('<w:p><w:r><w:t>Dziedziniec</w:t></w:r></w:p>')
        result = subprocess.run(
            [sys.executable, str(ROOT / "tools/export_design.py"), str(self.source)],
            capture_output=True, encoding="utf-8", check=False, timeout=10,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("Dziedziniec\n", result.stdout)

    def test_repository_source_contains_all_eight_sections(self):
        text = extract(ROOT / "Shadows of the Forsaken.docx")
        headings = (
            "1. Opis Gry", "2. Temat Poziomu", "3. Założenia Poziomu",
            "4. Elementy Wyróżniające", "5. Referencje Graficzne",
            "6. Flow Chart Poziomu", "7. Punkty Koncentracji",
            "8. Mapa 2D Top-Down",
        )
        for heading in headings:
            with self.subTest(heading=heading):
                self.assertIn(heading, text)

    def test_readme_local_links_resolve(self):
        text = (ROOT / "README.md").read_text(encoding="utf-8")
        links = re.findall(r"\[[^\]]*\]\(([^\s)]+)\)", text)
        self.assertTrue(links, "README must link to its authoritative source")
        self.assertIn("Shadows%20of%20the%20Forsaken.docx", links)
        for link in links:
            parts = urlsplit(link)
            if parts.scheme or parts.netloc or not parts.path:
                continue
            with self.subTest(link=link):
                self.assertTrue((ROOT / unquote(parts.path)).exists(), link)


if __name__ == "__main__":
    unittest.main()
