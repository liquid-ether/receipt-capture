#!/usr/bin/env python3
"""Renders the receipt images the web tests run OCR against.

Real receipt photos cannot go in the repository — they are somebody's actual
purchases — so the fixtures are generated: a monospaced slip in English and one
in French, laid out the way a thermal printer does it.

    pip install Pillow
    python3 tools/generate-test-receipts.py
"""

import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    sys.exit("Pillow is required: pip install Pillow")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'tests', 'ReceiptCapture.Web.Tests', 'TestData')

FONT_CANDIDATES = [
    '/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf',
    '/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf',
    '/Library/Fonts/Courier New.ttf',
    'C:\\Windows\\Fonts\\consola.ttf',
]

ENGLISH = [
    "THE COFFEE HOUSE",
    "123 Main Street",
    "Toronto, ON  M5V 2T6",
    "",
    "2026-03-14   10:42",
    "",
    "Latte                   4.75",
    "Croissant               3.50",
    "Muffin                  2.95",
    "----------------------------",
    "SUBTOTAL               11.20",
    "HST 13%                 1.46",
    "TOTAL                  12.66",
    "",
    "VISA                   12.66",
    "CHANGE                  0.00",
]

FRENCH = [
    "BOULANGERIE DU COIN",
    "456, rue Saint-Denis",
    "Montreal (Quebec)  H2J 2L1",
    "",
    "Facture no 00123",
    "Date: 14/03/2026",
    "",
    "Pain au chocolat        3,50",
    "Baguette                4,25",
    "Cafe                    2,75",
    "----------------------------",
    "Sous-total             10,50",
    "TPS 5%                  0,53",
    "TVQ 9,975%              1,05",
    "TOTAL                  12,08",
    "",
    "Comptant               20,00",
    "Monnaie                 7,92",
]


def find_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    sys.exit('No monospaced font found; install DejaVu or Liberation fonts.')


def render(lines, path, width=760, size=26, padding=40, line_height=38):
    font = find_font(size)
    image = Image.new('L', (width, padding * 2 + line_height * len(lines)), 255)
    draw = ImageDraw.Draw(image)

    y = padding
    for line in lines:
        draw.text((padding, y), line, font=font, fill=20)
        y += line_height

    image.save(path, optimize=True)
    print('wrote %s (%d bytes)' % (path, os.path.getsize(path)))


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    render(ENGLISH, os.path.join(OUT, 'receipt-en.png'))
    render(FRENCH, os.path.join(OUT, 'receipt-fr.png'))
