#!/usr/bin/env python3
"""Generate AudioWinFix.ico from code.

Mirrors AudioWinFix.svg (speaker + padlock). We draw with Pillow instead of
rasterizing the SVG because the local SVG toolchains (cairosvg/librsvg) are
flaky here, and ImageMagick's built-in renderer silently drops the shackle.
Supersampled 8x then downscaled (LANCZOS) into a multi-size .ico.

    python3 build/make_icon.py
"""
from PIL import Image, ImageDraw

SS = 8  # supersample factor over the 256-unit design space
BLUE = (59, 130, 246, 255)    # #3B82F6 speaker
AMBER = (245, 158, 11, 255)   # #F59E0B lock body
AMBER_HI = (251, 191, 36, 255)  # #FBBF24 shackle
HOLE = (124, 45, 18, 255)     # #7C2D12 keyhole


def s(v):
    return int(round(v * SS))


def rounded(draw, box, r, fill):
    draw.rounded_rectangle([s(box[0]), s(box[1]), s(box[2]), s(box[3])], radius=s(r), fill=fill)


def draw_icon():
    img = Image.new("RGBA", (s(256), s(256)), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Speaker: base box + cone triangle, same blue.
    rounded(d, (30, 98, 80, 158), 9, BLUE)
    d.polygon([(s(74), s(96)), (s(128), s(50)), (s(128), s(206)), (s(74), s(160))], fill=BLUE)

    # Padlock shackle (drawn first so the body covers the leg ends).
    w = s(15)
    d.arc([s(164), s(111), s(214), s(161)], start=180, end=360, fill=AMBER_HI, width=w)
    for x in (164, 214):  # the two legs going down into the body
        d.line([(s(x), s(133)), (s(x), s(154))], fill=AMBER_HI, width=w)

    # Lock body + keyhole.
    rounded(d, (150, 150, 228, 216), 14, AMBER)
    d.ellipse([s(181.5), s(170.5), s(196.5), s(185.5)], fill=HOLE)
    rounded(d, (185.5, 181, 192.5, 197), 3.5, HOLE)

    return img


def main():
    master = draw_icon()
    sizes = [256, 128, 64, 48, 32, 24, 16]
    frames = [master.resize((n, n), Image.Resampling.LANCZOS) for n in sizes]
    frames[0].save(
        "AudioWinFix.ico",
        format="ICO",
        sizes=[(n, n) for n in sizes],
        append_images=frames[1:],
    )
    print("wrote AudioWinFix.ico", sizes)


if __name__ == "__main__":
    main()
