"""Minimal dependency-free PNG writer + 5x7 bitmap text.

stdlib only (zlib + struct), matching the rest of tools/. Pillow/matplotlib are
NOT installed here (and MSYS binary wheels are flaky), so the PopTracker map and
icon images are rasterized by hand.

That is cheap because of what we actually have to draw: PopTracker renders the
location markers itself from `map_locations`, so a background only ever needs
axis-aligned filled rectangles, 1px frames, straight lines and short uppercase
labels. No curves, no polygons, no font engine.

Usage:
    c = Canvas(320, 200, (18, 20, 26))
    c.fill_rect(10, 10, 100, 40, (60, 90, 140))
    c.frame(10, 10, 100, 40, (200, 210, 230))
    c.text(14, 14, "08C SPACE", (255, 255, 255), scale=2)
    c.write_png("out.png")
"""

import struct
import zlib

# --- 5x7 glyphs, column-major, bit 0 = top row -------------------------------
# The classic 5x7 LCD font. Only the characters that can appear in a generated
# label are present; text() silently skips anything else (see _GLYPH fallback).
_FONT = {
    " ": (0x00, 0x00, 0x00, 0x00, 0x00),
    "&": (0x36, 0x49, 0x55, 0x22, 0x50),
    "(": (0x00, 0x1C, 0x22, 0x41, 0x00),
    ")": (0x00, 0x41, 0x22, 0x1C, 0x00),
    "+": (0x08, 0x08, 0x3E, 0x08, 0x08),
    "-": (0x08, 0x08, 0x08, 0x08, 0x08),
    ".": (0x00, 0x60, 0x60, 0x00, 0x00),
    "/": (0x20, 0x10, 0x08, 0x04, 0x02),
    "0": (0x3E, 0x51, 0x49, 0x45, 0x3E),
    "1": (0x00, 0x42, 0x7F, 0x40, 0x00),
    "2": (0x42, 0x61, 0x51, 0x49, 0x46),
    "3": (0x21, 0x41, 0x45, 0x4B, 0x31),
    "4": (0x18, 0x14, 0x12, 0x7F, 0x10),
    "5": (0x27, 0x45, 0x45, 0x45, 0x39),
    "6": (0x3C, 0x4A, 0x49, 0x49, 0x30),
    "7": (0x01, 0x71, 0x09, 0x05, 0x03),
    "8": (0x36, 0x49, 0x49, 0x49, 0x36),
    "9": (0x06, 0x49, 0x49, 0x29, 0x1E),
    ":": (0x00, 0x36, 0x36, 0x00, 0x00),
    "A": (0x7E, 0x11, 0x11, 0x11, 0x7E),
    "B": (0x7F, 0x49, 0x49, 0x49, 0x36),
    "C": (0x3E, 0x41, 0x41, 0x41, 0x22),
    "D": (0x7F, 0x41, 0x41, 0x22, 0x1C),
    "E": (0x7F, 0x49, 0x49, 0x49, 0x41),
    "F": (0x7F, 0x09, 0x09, 0x09, 0x01),
    "G": (0x3E, 0x41, 0x49, 0x49, 0x7A),
    "H": (0x7F, 0x08, 0x08, 0x08, 0x7F),
    "I": (0x00, 0x41, 0x7F, 0x41, 0x00),
    "J": (0x20, 0x40, 0x41, 0x3F, 0x01),
    "K": (0x7F, 0x08, 0x14, 0x22, 0x41),
    "L": (0x7F, 0x40, 0x40, 0x40, 0x40),
    "M": (0x7F, 0x02, 0x0C, 0x02, 0x7F),
    "N": (0x7F, 0x04, 0x08, 0x10, 0x7F),
    "O": (0x3E, 0x41, 0x41, 0x41, 0x3E),
    "P": (0x7F, 0x09, 0x09, 0x09, 0x06),
    "Q": (0x3E, 0x41, 0x51, 0x21, 0x5E),
    "R": (0x7F, 0x09, 0x19, 0x29, 0x46),
    "S": (0x46, 0x49, 0x49, 0x49, 0x31),
    "T": (0x01, 0x01, 0x7F, 0x01, 0x01),
    "U": (0x3F, 0x40, 0x40, 0x40, 0x3F),
    "V": (0x1F, 0x20, 0x40, 0x20, 0x1F),
    "W": (0x3F, 0x40, 0x38, 0x40, 0x3F),
    "X": (0x63, 0x14, 0x08, 0x14, 0x63),
    "Y": (0x07, 0x08, 0x70, 0x08, 0x07),
    "Z": (0x61, 0x51, 0x49, 0x45, 0x43),
}
_MISSING = (0x7F, 0x41, 0x41, 0x41, 0x7F)   # hollow box for unmapped chars

GLYPH_W, GLYPH_H, GLYPH_GAP = 5, 7, 1


def text_width(s, scale=1):
    """Pixel width of `s` rendered at `scale` (matches Canvas.text exactly)."""
    if not s:
        return 0
    return (len(s) * (GLYPH_W + GLYPH_GAP) - GLYPH_GAP) * scale


def text_height(scale=1):
    return GLYPH_H * scale


class Canvas:
    """An RGB pixel buffer that can serialize itself to a PNG.

    Coordinates are integer pixels, origin top-left. Every draw call clips to
    the canvas, so callers never have to bounds-check.
    """

    def __init__(self, width, height, bg=(0, 0, 0)):
        if width <= 0 or height <= 0:
            raise ValueError(f"bad canvas size {width}x{height}")
        self.w = int(width)
        self.h = int(height)
        self._px = bytearray(bytes(bg) * (self.w * self.h))

    # --- primitives ----------------------------------------------------------
    def set_px(self, x, y, color):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 3
            self._px[i:i + 3] = bytes(color)

    def fill_rect(self, x, y, w, h, color):
        x0, y0 = max(0, int(x)), max(0, int(y))
        x1, y1 = min(self.w, int(x) + int(w)), min(self.h, int(y) + int(h))
        if x1 <= x0 or y1 <= y0:
            return
        row = bytes(color) * (x1 - x0)
        for yy in range(y0, y1):
            i = (yy * self.w + x0) * 3
            self._px[i:i + (x1 - x0) * 3] = row

    def blend_rect(self, x, y, w, h, color, alpha):
        """Alpha-composite a solid colour over the existing pixels (0.0-1.0).

        Used for sub-area tints, which must stay legible where two sub-area
        bounding boxes overlap -- and in this game they do overlap (chambers 05
        and 06 share a y band).
        """
        a = max(0.0, min(1.0, float(alpha)))
        if a <= 0.0:
            return
        if a >= 1.0:
            return self.fill_rect(x, y, w, h, color)
        x0, y0 = max(0, int(x)), max(0, int(y))
        x1, y1 = min(self.w, int(x) + int(w)), min(self.h, int(y) + int(h))
        cr, cg, cb = color
        inv = 1.0 - a
        for yy in range(y0, y1):
            base = (yy * self.w + x0) * 3
            for k in range(0, (x1 - x0) * 3, 3):
                i = base + k
                self._px[i] = int(self._px[i] * inv + cr * a)
                self._px[i + 1] = int(self._px[i + 1] * inv + cg * a)
                self._px[i + 2] = int(self._px[i + 2] * inv + cb * a)

    def frame(self, x, y, w, h, color, t=1):
        t = max(1, int(t))
        self.fill_rect(x, y, w, t, color)
        self.fill_rect(x, y + h - t, w, t, color)
        self.fill_rect(x, y, t, h, color)
        self.fill_rect(x + w - t, y, t, h, color)

    def hline(self, x, y, w, color, t=1):
        self.fill_rect(x, y, w, max(1, int(t)), color)

    def vline(self, x, y, h, color, t=1):
        self.fill_rect(x, y, max(1, int(t)), h, color)

    def text(self, x, y, s, color, scale=1):
        """Draw `s` (case-insensitive; folded to upper) with the 5x7 font."""
        scale = max(1, int(scale))
        cx = int(x)
        for ch in str(s).upper():
            cols = _FONT.get(ch, _MISSING)
            for col, bits in enumerate(cols):
                if not bits:
                    continue
                for row in range(GLYPH_H):
                    if bits & (1 << row):
                        self.fill_rect(cx + col * scale, int(y) + row * scale,
                                       scale, scale, color)
            cx += (GLYPH_W + GLYPH_GAP) * scale

    def text_centered(self, cx, y, s, color, scale=1):
        self.text(int(cx) - text_width(s, scale) // 2, y, s, color, scale)

    # --- output --------------------------------------------------------------
    def _chunk(self, tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    def to_png_bytes(self):
        # Filter type 0 (None) on every scanline: our images are flat colour
        # blocks, so zlib already compresses them well and skipping adaptive
        # filtering keeps the output byte-stable across runs.
        raw = bytearray()
        stride = self.w * 3
        for y in range(self.h):
            raw.append(0)
            raw += self._px[y * stride:(y + 1) * stride]
        ihdr = struct.pack(">IIBBBBB", self.w, self.h, 8, 2, 0, 0, 0)
        # No tIME chunk -- a timestamp would churn git on every regeneration.
        return (b"\x89PNG\r\n\x1a\n"
                + self._chunk(b"IHDR", ihdr)
                + self._chunk(b"IDAT", zlib.compress(bytes(raw), 9))
                + self._chunk(b"IEND", b""))

    def write_png(self, path):
        with open(path, "wb") as f:
            f.write(self.to_png_bytes())


def hex_rgb(s):
    """'#4c6fb0' or '4c6fb0' -> (76, 111, 176)."""
    s = s.lstrip("#")
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16))


def mix(a, b, t):
    """Linear blend between two RGB tuples; t=0 -> a, t=1 -> b."""
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
