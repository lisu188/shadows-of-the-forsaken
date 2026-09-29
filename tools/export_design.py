"""Read-only, dependency-free text export of the original design document.

Images and layout remain in the DOCX, which is the authoritative design source.
"""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
from zipfile import ZipFile


def extract(path: Path) -> str:
    ns = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    with ZipFile(path) as archive:
        root = ET.fromstring(archive.read("word/document.xml"))
    paragraphs = []
    for paragraph in root.findall(".//w:p", ns):
        text = "".join(node.text or "" for node in paragraph.findall(".//w:t", ns))
        if text.strip():
            paragraphs.append(text)
    return "\n\n".join(paragraphs)


if __name__ == "__main__":
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "Shadows of the Forsaken.docx"
    print(extract(source))
