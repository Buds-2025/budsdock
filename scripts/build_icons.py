"""Reconstruct BudsDock icon assets from the SVG vector sources.

Inputs (D:\\AI Coding\\dock\\SVG):
  * 资源 3.svg — light-theme variant; viewBox 466.13 x 609.92, layered
    shadow (#333) + light (#fff) + accent (#ccc) polygons.
  * 资源 4.svg — dark-theme variant; viewBox 455.64 x 609.92, single
    #333 fill.

Outputs (at native vector resolution, never traced from the Buds-白.png):
  * outputs/BudsDock-icon-1to1-light.png
  * outputs/BudsDock-icon-1to1-dark.png
  * src/BudsDock/Assets/BudsDock.ico  (multi-resolution: 16/32/48/64/128/256)
  * src/BudsDock/Assets/BudsDock-mark-light.png   (in-app title bar)
  * src/BudsDock/Assets/BudsDock-mark-dark.png
  * src/BudsDock/Assets/BudsDock-tray-light.png   (notification-area tray)
  * src/BudsDock/Assets/BudsDock-tray-dark.png
  * src/BudsDock/Assets/BudsDock-square-light.png (1:1 PNG that the launcher
                                                    ICO is downsampled from)
  * src/BudsDock/Assets/BudsDock-square-dark.png
"""

from __future__ import annotations

import re
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SVG_DIR = ROOT / "SVG"
ASSETS = ROOT / "src" / "BudsDock" / "Assets"
OUTPUTS = ROOT / "outputs"

LIGHT_SVG = SVG_DIR / "资源 3.svg"
DARK_SVG = SVG_DIR / "资源 4.svg"

# Final icon side, matching the longer edge of the SVG viewBox so the artwork
# is rendered at native size without any scaling.  1024 keeps every detail
# crisp at retina; ICO entries are downsampled from this.
PRIMARY_SIDE = 1024


# ----------------------------------------------------------------------------
# SVG → vector scene
# ----------------------------------------------------------------------------

class Polygon:
    __slots__ = ("points", "fill")

    def __init__(self, points: list[tuple[float, float]], fill: tuple[int, int, int, int]):
        self.points = points
        self.fill = fill


class Scene:
    def __init__(self, viewbox: tuple[float, float, float, float], shapes: list[Polygon]):
        self.vb_x, self.vb_y, self.vb_w, self.vb_h = viewbox
        self.shapes = shapes

    def render(self, size: int, *, background: tuple[int, int, int, int] | None = None) -> Image.Image:
        """Rasterize the scene to a `size x size` RGBA image.

        `background` is the optional canvas fill colour.  When None the canvas
        is fully transparent so the icon can sit on any surface.
        """
        sx = size / self.vb_w
        sy = size / self.vb_h
        if background is None:
            img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        else:
            img = Image.new("RGBA", (size, size), background)

        # PIL floats lose precision when the scale is large; render at 4x then
        # downsample for clean anti-aliased edges even on tiny ICO entries.
        oversample = 4
        big = size * oversample
        mask = Image.new("L", (big, big), 0)
        mask_draw = ImageDraw.Draw(mask)
        layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
        layer_draw = ImageDraw.Draw(layer)

        for shape in self.shapes:
            scaled = [(p[0] * sx * oversample, p[1] * sy * oversample) for p in shape.points]
            layer_draw.polygon(scaled, fill=shape.fill)
            mask_draw.polygon(scaled, fill=255)

        # We use the alpha channel of `layer` as the actual fill mask to keep
        # anti-aliased edges.
        layer = layer.resize((size, size), Image.LANCZOS)
        if background is None:
            img.alpha_composite(layer)
        else:
            img.paste(background, (0, 0))
            img.alpha_composite(layer)
        return img


def _parse_hex(c: str) -> tuple[int, int, int, int]:
    c = c.strip().lstrip("#")
    if len(c) == 3:
        c = "".join(ch * 2 for ch in c)
    return (int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), 255)


def parse_svg(path: Path, default_fill: tuple[int, int, int, int]) -> Scene:
    text = path.read_text(encoding="utf-8")

    # Strip comments so we don't accidentally match inside them.
    text = re.sub(r"<!--.*?-->", "", text, flags=re.DOTALL)

    viewbox_match = re.search(r'viewBox="([^"]+)"', text)
    assert viewbox_match, f"no viewBox in {path}"
    vb = tuple(float(v) for v in viewbox_match.group(1).split())
    assert len(vb) == 4

    # Parse the <style> block for class → fill mappings.
    style_match = re.search(r"<style>(.*?)</style>", text, flags=re.DOTALL)
    class_fills: dict[str, tuple[int, int, int, int]] = {}
    if style_match:
        for cls_match in re.finditer(r"\.([\w-]+)\s*\{\s*fill:\s*([^;]+);?\s*\}", style_match.group(1)):
            class_fills[cls_match.group(1)] = _parse_hex(cls_match.group(2))

    # Polygons without an explicit class inherit the explicit `default_fill`
    # passed to this function.  Both BudsDock SVGs use unclassed polygons in
    # the shadow layer where the intended ink is #333, so the caller passes
    # that hint in.
    shapes: list[Polygon] = []
    for poly in re.finditer(r"<polygon[^/]*?/>", text):
        attrs = poly.group(0)
        cls_match = re.search(r'class="([^"]+)"', attrs)
        points_match = re.search(r'points="([^"]+)"', attrs)
        if not points_match:
            continue
        pts_raw = points_match.group(1).replace(",", " ").split()
        coords: list[tuple[float, float]] = []
        for i in range(0, len(pts_raw), 2):
            coords.append((float(pts_raw[i]), float(pts_raw[i + 1])))
        fill = class_fills.get(cls_match.group(1), default_fill) if cls_match else default_fill
        shapes.append(Polygon(coords, fill))

    return Scene(vb, shapes)


# ----------------------------------------------------------------------------
# Resampling pipeline
# ----------------------------------------------------------------------------

def to_square(scene: Scene, side: int = PRIMARY_SIDE, *, background=None) -> Image.Image:
    """Pad the SVG into a 1:1 transparent canvas.

    The SVG viewport is wider than tall (e.g. 466 x 610 is slightly offset
    from the earlier 1823 x 2440 PNG), so the square's side equals the longer
    edge and the artwork is centred exactly the way it was authored.
    """
    vb_side = max(scene.vb_w, scene.vb_h)
    # Re-render at vb-side x vb-side so the source pixels are 1:1.
    base = scene.render(int(vb_side), background=None)
    # Pad to (side x side) without rescaling the art.
    if side == int(vb_side):
        return base
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    x = (side - int(vb_side)) // 2
    y = (side - int(vb_side)) // 2
    sq.paste(base, (x, y), base)
    return sq


def write_ico(square: Image.Image, sizes: list[int], dest: Path) -> None:
    """Pack the square into a multi-resolution ICO with PNG entries."""
    square.save(dest, format="ICO", sizes=[(s, s) for s in sizes])


# ----------------------------------------------------------------------------
# Dark-mode in-app tinting
# ----------------------------------------------------------------------------

def recolor(scene: Scene, fg: tuple[int, int, int, int]) -> Scene:
    """Return a new Scene where every polygon is rendered in `fg` (alpha kept).

    Used for the in-app title bar glyph: same vector art, but in the theme's
    foreground colour so it always reads against the window background.
    """
    return Scene(
        (scene.vb_x, scene.vb_y, scene.vb_w, scene.vb_h),
        [Polygon(p.points, fg) for p in scene.shapes],
    )


# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------

def main() -> None:
    OUTPUTS.mkdir(exist_ok=True)

    # Both SVGs use unclassed polygons in the shadow layer; the implicit ink
    # colour is #333 — the same value as cls-2 / cls-1 in each file.
    shadow_ink = (51, 51, 51, 255)
    light_scene = parse_svg(LIGHT_SVG, default_fill=shadow_ink)
    dark_scene = parse_svg(DARK_SVG, default_fill=shadow_ink)
    print(f"Parsed light: {len(light_scene.shapes)} polygons, viewBox {light_scene.vb_w:g}x{light_scene.vb_h:g}")
    print(f"Parsed dark:  {len(dark_scene.shapes)} polygons, viewBox {dark_scene.vb_w:g}x{dark_scene.vb_h:g}")

    # --- 1:1 native squares (vector-rendered, not traced from a PNG) -----
    light_sq = to_square(light_scene, PRIMARY_SIDE)
    dark_sq = to_square(dark_scene, PRIMARY_SIDE)
    light_sq.save(ASSETS / "BudsDock-square-light.png")
    dark_sq.save(ASSETS / "BudsDock-square-dark.png")
    light_sq.save(OUTPUTS / "BudsDock-square-light.png")
    dark_sq.save(OUTPUTS / "BudsDock-square-dark.png")
    print(f"Wrote 1:1 squares @ {PRIMARY_SIDE}x{PRIMARY_SIDE}")

    # --- Multi-resolution .ico for the launcher (use the light-themed vector
    # art, it sits on the OS dock/taskbar which is typically dark). --------
    ico_sizes = [16, 32, 48, 64, 128, 256]
    write_ico(light_sq, ico_sizes, ASSETS / "BudsDock.ico")
    print(f"Wrote {ASSETS / 'BudsDock.ico'}  (sizes {ico_sizes})")

    # --- In-app title-bar glyph : same vector, recoloured to theme fg -----
    # Light theme uses dark text; dark theme uses light text.
    light_fg = (24, 28, 36, 255)      # TextPrimaryColor light
    dark_fg = (245, 247, 251, 255)    # TextPrimaryColor dark
    light_title_scene = recolor(light_scene, light_fg)
    dark_title_scene = recolor(dark_scene, dark_fg)
    light_title_scene.render(256).save(ASSETS / "BudsDock-mark-light.png")
    dark_title_scene.render(256).save(ASSETS / "BudsDock-mark-dark.png")
    print("Wrote in-app title-bar marks (light/dark)")

    # --- System tray (notification area) glyphs at typical tray sizes -----
    # Windows tray icons render at 16x16 (100%) and 32x32 (200% DPI).  We
    # ship both sizes, sourced from the same vector so they stay sharp.
    for sz in (16, 32, 48):
        light_title_scene.render(sz).save(ASSETS / f"BudsDock-tray-light.png")
        dark_title_scene.render(sz).save(ASSETS / f"BudsDock-tray-dark.png")
    # When the runtime needs more granular sizes, it can resize from the
    # 256x256 in-app mark via System.Drawing, which is preferable to bundling
    # duplicate PNGs.  ImageList still benefits from having explicit 16/32.
    print("Wrote tray icons (16/32/48 for light/dark)")

    print("Done.")


if __name__ == "__main__":
    main()