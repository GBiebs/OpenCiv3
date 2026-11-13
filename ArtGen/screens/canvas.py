"""A small drawing kit for the screens' sprites: shapes in Civ3's pixel
units, drawn supersampled at twice Civ3's resolution (as all of ModernArt
is), lit from the upper left, with thin dark contours and transparent
backgrounds.
"""
import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SCALE = 2  # ModernArt has twice the pixels of Civ3's art


class Canvas:
    """A transparent sprite `w` by `h` Civ3 pixels. Shapes are given in Civ3
    pixels (floats), so a drawing matches the original's footprint; it's
    rendered `ss` times finer than the output and scaled down."""

    def __init__(self, w, h, ss=8):
        self.w, self.h = w, h
        self.k = SCALE * ss
        self.nw, self.nh = int(round(w * self.k)), int(round(h * self.k))
        ys, xs = np.mgrid[0:self.nh, 0:self.nw].astype(np.float32)
        self.xs, self.ys = (xs + 0.5) / self.k, (ys + 0.5) / self.k
        # Premultiplied color and coverage.
        self.p = np.zeros((self.nh, self.nw, 3), np.float32)
        self.a = np.zeros((self.nh, self.nw), np.float32)

    # ---- Masks (0..1 floats at the fine resolution) ----

    def _mask(self, draw):
        img = Image.new('L', (self.nw, self.nh), 0)
        draw(ImageDraw.Draw(img), self.k)
        return np.asarray(img).astype(np.float32) / 255

    def poly(self, points):
        return self._mask(lambda d, k: d.polygon([(x * k, y * k) for x, y in points], fill=255))

    def ellipse(self, box):
        return self._mask(lambda d, k: d.ellipse([v * k for v in box], fill=255))

    def rect(self, box, radius=0):
        return self._mask(lambda d, k: d.rounded_rectangle([v * k for v in box], radius=radius * k, fill=255))

    def arc(self, box, start, end, width):
        return self._mask(lambda d, k: d.arc([v * k for v in box], start, end, fill=255, width=max(1, int(width * k))))

    def bar(self, a, b, width):
        (x0, y0), (x1, y1) = a, b
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy)
        nx, ny = -dy / length * width / 2, dx / length * width / 2
        return self.poly([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)])

    def grow(self, mask, by):
        """The mask widened by `by` Civ3 pixels."""
        n = int(by * self.k) * 2 + 1
        if n < 3:
            return mask
        return np.asarray(Image.fromarray((mask * 255).astype(np.uint8)).filter(
            ImageFilter.MaxFilter(n))).astype(np.float32) / 255

    # ---- Painting ----

    def over(self, color, alpha):
        """Lays a color (RGB 0..1, or an HxWx3 array) over everything, with
        the coverage `alpha` (HxW)."""
        c = np.asarray(color, np.float32)
        if c.ndim == 1:
            c = c[None, None, :]
        a = np.clip(alpha, 0, 1)[..., None]
        self.p = c * a + self.p * (1 - a)
        self.a = a[..., 0] + self.a * (1 - a[..., 0])

    def shadow(self, mask, offset=(0.8, 1.0), radius=0.7, strength=0.35):
        """A soft black shadow of a shape, down and to the right."""
        ox, oy = int(offset[0] * self.k), int(offset[1] * self.k)
        shifted = np.zeros_like(mask)
        shifted[oy:, ox:] = mask[:self.nh - oy, :self.nw - ox]
        s = np.asarray(Image.fromarray((shifted * 255).astype(np.uint8)).filter(
            ImageFilter.GaussianBlur(radius * self.k))).astype(np.float32) / 255
        self.over((0, 0, 0), s * strength)

    def fill(self, mask, color, light=0.25, outline=0.45, contour=(46, 36, 26), gloss=0.0):
        """Paints a shape: its color lighter towards the upper left of its
        own extent, a thin dark contour round it, and with `gloss` a soft
        highlight near its upper left."""
        c = np.asarray(color, np.float32) / 255
        ys, xs = np.nonzero(mask > 0.5)
        if len(xs) == 0:
            return
        x0, x1 = xs.min() / self.k, xs.max() / self.k
        y0, y1 = ys.min() / self.k, ys.max() / self.k
        t = (self.xs - x0) / max(0.5, x1 - x0) * 0.45 + (self.ys - y0) / max(0.5, y1 - y0) * 0.55
        shade = 1 + light * (0.5 - t)
        col = np.clip(c[None, None, :] * shade[..., None], 0, 1)
        if gloss > 0:
            cx, cy = x0 + (x1 - x0) * 0.32, y0 + (y1 - y0) * 0.28
            r = max(1.0, min(x1 - x0, y1 - y0) * 0.35)
            g = np.clip(1 - np.hypot(self.xs - cx, self.ys - cy) / r, 0, 1) ** 2 * gloss
            col = col + (1 - col) * g[..., None]
        if outline > 0:
            self.over(np.asarray(contour, np.float32) / 255, self.grow(mask, outline))
        self.over(col, mask)

    def paint(self, mask, color, alpha=1.0):
        """Flat color with no contour, for details."""
        self.over(np.asarray(color, np.float32) / 255, mask * alpha)

    def erase(self, mask):
        keep = 1 - np.clip(mask, 0, 1)
        self.p *= keep[..., None]
        self.a *= keep

    def image(self):
        """The sprite at twice Civ3's size, as a PIL RGBA image."""
        w, h = self.w * SCALE, self.h * SCALE
        planes = [np.asarray(Image.fromarray(self.p[..., i], 'F').resize((w, h), Image.BOX)) for i in range(3)]
        a = np.asarray(Image.fromarray(self.a, 'F').resize((w, h), Image.BOX))
        rgb = np.dstack(planes) / np.maximum(a, 1e-6)[..., None]
        out = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)[..., None]])
        out = (out * 255 + 0.5).astype(np.uint8)
        out[out[..., 3] == 0, :3] = 0
        return Image.fromarray(out, 'RGBA')
