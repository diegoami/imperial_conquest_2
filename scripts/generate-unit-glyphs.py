#!/usr/bin/env python3
"""Owner-designed T182 glyphs: no originals, network, randomness or billed generation.

Run without arguments to recreate the 21 icons in each shipped pack. --verify compares
all 42 BMP byte streams without writing anything. --board-dir writes PNG review boards
only (including 16/24px stress views, native 32px and enlarged nearest-neighbour views).
Geometry and BMP writing use only Python standard library. Pillow, the existing
authoring dependency, is optional and used only for PNG review boards.
The authored pack uses the same deliberately simple symbols as the placeholder pack.
Battle cells stay opaque with exact tint anchors and frame; strategic sprites stay
neutral and transparent. Strategic three-colour tint (#891) is outside this generator.
"""
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TYPES = ("light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry")
SIZES = ("small", "medium", "large")
MOTIFS = dict(zip(TYPES, ("sword", "shield", "bow", "horse", "horse above shield")))
PURPLE, WHITE, BLUE, LIME = (128, 0, 128, 255), (255, 255, 255, 255), (0, 0, 255, 255), (0, 255, 0, 255)


class Raster:
    """Small integer rasterizer; ordinary pack generation needs no image library.

    Polygons use an even/odd pixel-centre fill plus explicit boundary segments;
    lines use Bresenham with an integer square pen. No antialiasing or partial alpha.
    """
    def __init__(self, side, fill=(0,0,0,0)):
        self.side = side
        self.pixels = [fill] * (side*side)

    def point(self, xy, fill):
        x,y = xy
        if 0 <= x < self.side and 0 <= y < self.side:
            self.pixels[y*self.side+x] = fill

    def rectangle(self, box, fill):
        left,top,right,bottom = box
        for y in range(top,bottom+1):
            for x in range(left,right+1):
                self.point((x,y),fill)

    def line(self, points, fill, width=1):
        for (x,y),(endx,endy) in zip(points,points[1:]):
            dx,dy = abs(endx-x),-abs(endy-y)
            stepx,stepy = (1 if x<endx else -1),(1 if y<endy else -1)
            error=dx+dy
            while True:
                lo=-(width//2)
                self.rectangle((x+lo,y+lo,x+lo+width-1,y+lo+width-1),fill)
                if x==endx and y==endy:
                    break
                twice=2*error
                if twice>=dy:
                    error+=dy
                    x+=stepx
                if twice<=dx:
                    error+=dx
                    y+=stepy

    def polygon(self, points, fill):
        for y in range(self.side):
            for x in range(self.side):
                inside=False
                px,py=x+0.5,y+0.5
                for (ax,ay),(bx,by) in zip(points,points[1:]+points[:1]):
                    if (ay>py)!=(by>py) and px < (bx-ax)*(py-ay)/(by-ay)+ax:
                        inside=not inside
                if inside:
                    self.point((x,y),fill)
        self.line(points+points[:1],fill)

    def ellipse(self, box, fill):
        left,top,right,bottom=box
        # Integer doubled coordinates avoid platform floating point at the ellipse edge.
        rx,ry=right-left+1,bottom-top+1
        for y in range(top,bottom+1):
            for x in range(left,right+1):
                dx,dy=2*x-left-right,2*y-top-bottom
                if dx*dx*ry*ry+dy*dy*rx*rx <= rx*rx*ry*ry:
                    self.point((x,y),fill)

    def resize(self, side):
        result=Raster(side)
        for y in range(side):
            for x in range(side):
                sx=min(self.side-1,((2*x+1)*self.side)//(2*side))
                sy=min(self.side-1,((2*y+1)*self.side)//(2*side))
                result.pixels[y*side+x]=self.pixels[sy*self.side+sx]
        return result


def keys():
    return [f"battle.unit.{unit}.{size}" for unit in TYPES for size in SIZES] + [
        f"{kind}.tier{tier}.icon" for kind in ("army", "fleet") for tier in (1, 2, 3)]


def relative_path(key):
    parts = key.split(".")
    return parts[0] + "/" + "_".join(parts[1:]).removesuffix("_icon") + ".bmp"


def motif(name):
    """Integer geometry on a common 26px design grid, scaled as one silhouette.

    White is the main symbol, blue is its structural accent. Black outlines are added
    after scaling so even the smallest glyph keeps a one-pixel separating edge.
    """
    image = Raster(26)
    d = image

    def shield(box=(4, 2, 21, 24)):
        x, y, r, b = box
        c = (x+r)//2
        d.polygon([(x,y),(r,y),(r,b-7),(c,b),(x,b-7)], fill=WHITE)
        d.line([(c,y+2),(c,b-4)], fill=BLUE, width=3)
        d.line([(x+3,y+7),(r-3,y+7)], fill=BLUE, width=3)

    def horse(box=(2, 2, 23, 24)):
        x,y,r,b = box
        # Facing right: ears, forehead, muzzle, throat, body, tail and four legs.
        def p(points):
            return [(round(x+u*(r-x)/21),round(y+v*(b-y)/22)) for u,v in points]
        d.polygon(p([(1,10),(4,8),(11,8),(13,3),(13,0),(16,3),(18,2),
                     (19,5),(21,7),(21,9),(17,9),(15,13),(15,20),(13,22),
                     (11,22),(12,14),(8,14),(6,21),(3,22),(4,14),(2,13),(0,16),(0,12)]), fill=WHITE)
        d.line(p([(13,4),(15,7),(13,12),(7,11)]), fill=BLUE, width=2)

    if name == "light_infantry":
        d.polygon([(13,1),(17,6),(15,18),(11,18),(10,6)], fill=WHITE)
        d.line([(13,6),(13,16)], fill=BLUE, width=2)
        d.rectangle((6,17,20,19), fill=WHITE)
        d.rectangle((11,20,15,23), fill=WHITE)
        d.rectangle((10,23,16,25), fill=BLUE)
    elif name == "heavy_infantry":
        shield()
    elif name == "archers":
        # Curved stave, taut string and a separate horizontal arrow with a broad head.
        d.line([(8,1),(14,4),(18,9),(19,13),(18,17),(14,22),(8,25)], fill=WHITE, width=3)
        d.line([(8,2),(8,24)], fill=WHITE, width=2)
        d.line([(2,13),(23,13)], fill=BLUE, width=3)
        d.polygon([(20,9),(25,13),(20,17)], fill=WHITE)
    elif name == "light_cavalry":
        horse()
    elif name == "heavy_cavalry":
        # Separate horse above the shield: no overlap that could turn it into a rider.
        horse((3,0,22,11))
        shield((5,14,21,25))
    elif name == "army":
        # Helmeted soldier with spear, shield, tunic and separated feet.
        d.ellipse((8,1,16,8), fill=WHITE)
        d.rectangle((7,2,16,4), fill=WHITE)
        d.polygon([(9,9),(16,9),(18,18),(7,18)], fill=WHITE)
        d.line([(10,18),(8,24)], fill=WHITE, width=3)
        d.line([(15,18),(18,24)], fill=WHITE, width=3)
        d.line([(21,3),(21,24)], fill=WHITE, width=2)
        d.polygon([(21,0),(18,5),(23,5)], fill=WHITE)
        d.ellipse((2,10,10,19), fill=WHITE)
    elif name == "fleet":
        d.polygon([(1,18),(24,18),(20,24),(7,24)], fill=WHITE)
        d.line([(13,1),(13,18)], fill=WHITE, width=2)
        d.polygon([(15,3),(23,15),(15,15)], fill=WHITE)
        d.polygon([(11,5),(3,15),(11,15)], fill=WHITE)
        d.line([(4,25),(22,25)], fill=WHITE, width=1)
    else:
        raise ValueError(name)
    return image


def glyph(key):
    battle = key.startswith("battle.")
    parts = key.split(".")
    name = parts[2] if battle else parts[0]
    tier = SIZES.index(parts[3]) if battle else int(parts[1][-1])-1
    side = (16, 21, 26)[tier]
    symbol = motif(name).resize(side)
    layer = Raster(32)
    offset = (32-side)//2
    for y in range(side):
        for x in range(side):
            layer.point((x+offset,y+offset),symbol.pixels[y*side+x])
    if not battle:
        layer.pixels = [WHITE if p[3] else (0,0,0,0) for p in layer.pixels]
        return layer
    result = Raster(32, PURPLE)
    for y in range(32):
        for x in range(32):
            if layer.pixels[y*32+x][3]:
                result.rectangle((x-1,y-1,x+1,y+1),(0,0,0,255))
    for i,pixel in enumerate(layer.pixels):
        if pixel[3]:
            result.pixels[i]=pixel
    for i in range(0,32,2):
        for point in ((i,0),(i,31),(0,i),(31,i)):
            result.point(point,LIME)
    return result


def bmp_bytes(image):
    """Fixed BI_RGB header and bottom-up BGRA rows, including strategic alpha."""
    header = struct.pack("<2sIHHI", b"BM", 4150, 0, 0, 54)
    header += struct.pack("<IiiHHIIiiII",40,32,32,1,32,0,4096,0,0,0,0)
    pixels=bytearray()
    for y in range(31,-1,-1):
        for x in range(32):
            r,g,b,a=image.pixels[y*32+x]
            pixels.extend((b,g,r,a))
    return header+pixels


def write_key(out_dir, key):
    path = Path(out_dir) / relative_path(key)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(bmp_bytes(glyph(key)))


def boards(directory):
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        raise SystemExit("PNG review boards require the existing Pillow authoring dependency; glyph generation/--verify do not.")
    nearest = Image.Resampling.NEAREST

    def image_for(key):
        return Image.frombytes("RGBA",(32,32),bytes(channel for pixel in glyph(key).pixels for channel in pixel))

    directory.mkdir(parents=True, exist_ok=True)
    # Read only configured nation palettes from the shipped world. This does not add any
    # measured-original art claim: all glyph geometry above is owner-designed.
    world = json.loads((ROOT / "data/worlds/classical-mediterranean.json").read_text(encoding="utf-8"))
    owners = {nation["id"]: nation for nation in world["nations"]}

    def rgba(value):
        return tuple(bytes.fromhex(value.removeprefix("#"))) + (255,)

    palettes = (None,) + tuple(tuple(rgba(value) for value in owners[name]["battleColorsHex"])
                              for name in ("numidia", "macedonia"))
    strategic = (None,) + tuple((rgba(owners[name]["colorHex"]), rgba(owners[name]["glyphColorHex"]))
                               for name in ("numidia", "macedonia"))
    for pack in ("authored", "placeholder"):
        board = Image.new("RGB", (1150, 70+len(keys())*86), (38,43,48))
        d = ImageDraw.Draw(board)
        d.text((12,10),f"T182 {pack} | owner-designed | adjacent tiers | native 32 + 16/24px stress views",fill="white")
        d.text((300,35),"Rome/neutral: 16 / 24 / 32 / 64px       Numidia: 16 / 24 / 32       Macedonia: 16 / 24 / 32",fill="white")
        for row,key in enumerate(keys()):
            y=65+row*86
            name = key.split('.')[2] if key.startswith('battle.') else key.split('.')[0]
            d.text((12,y+12),key,fill="white")
            d.text((12,y+32),MOTIFS.get(name, "soldier" if name=="army" else "ship"),fill=(200,210,220))
            x=300
            for owner_index,palette in enumerate(palettes):
                im=image_for(key)
                if palette:
                    pixels=[]
                    for p in (im.get_flattened_data() if hasattr(im, "get_flattened_data") else im.getdata()):
                        if key.startswith("battle."):
                            pixels.append(dict(zip((PURPLE,WHITE,BLUE),palette)).get(p,p))
                        else:
                            pixels.append(strategic[owner_index][1] if p[3] else p)
                    im.putdata(pixels)
                for size in ((16,24,32,64) if palette is None else (16,24,32)):
                    background = strategic[owner_index][0][:3] if palette and not key.startswith("battle.") else (86,100,68)
                    d.rectangle((x,y,x+size-1,y+size-1),fill=background)
                    board.paste(im.resize((size,size),nearest),(x,y),im.resize((size,size),nearest))
                    d.text((x,y+66),str(size),fill="white")
                    x+=size+12
                x+=24
        board.save(directory / f"{pack}-glyphs.png")
        # Actual map renderer lower bounds: army 2*2.5px, fleet 2*2px.
        minimums = Image.new("RGB", (850, 550), (38,43,48))
        md = ImageDraw.Draw(minimums)
        md.text((12,12),f"{pack}: actual map lower bounds | army 5px, fleet 4px | enlarged views are inspection only",fill="white")
        for row,key in enumerate(k for k in keys() if not k.startswith("battle.")):
            y = 55 + row*80
            md.text((12,y+8),key,fill="white")
            x = 165
            for size in (4,5,8,16,24,32,64):
                im = image_for(key).resize((size,size),nearest)
                md.rectangle((x,y,x+size-1,y+size-1),fill=(86,100,68))
                minimums.paste(im,(x,y),im)
                md.text((x,y+66),str(size),fill="white")
                x += size+40
        minimums.save(directory / f"{pack}-strategic-minimums.png")
    (directory / "README.txt").write_text(
        "Owner-designed review boards, not original-game evidence. Both packs intentionally share glyphs.\n"
        "16/24px are stress views; 32px is native, 64px is inspection magnification.\n"
        "Strategic-minimums boards include actual map lower bounds (army5px/fleet4px).\n"
        "At these overview sizes motif recognition is limited; review the real zoomed context too.\n"
        "Rome/Numidia/Macedonia use configured shipped battle palettes; strategic views use existing\n"
        "single-colour foreground/background pairs from the same world, not strategic three-colour tint.\n"
        "Human recognition/readability approval is required. Selection/actor/target overlay context\n"
        "requires T180 integration and actual windowed captures; these boards do not replace it.\n", encoding="utf-8")


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify",action="store_true",help="compare all 42 committed BMPs without mutation")
    parser.add_argument("--board-dir",type=Path,help="write labelled native/recolour/scale review boards only")
    parser.add_argument("--pack-dir",type=Path,help="recreate 21 glyphs in an ordinary generator's output directory")
    args=parser.parse_args()
    if args.verify:
        if args.board_dir or args.pack_dir:
            parser.error("--verify cannot be combined with writing options")
        failed=[]
        for pack in ("authored","placeholder"):
            for key in keys():
                p=ROOT/"assets/packs"/pack/relative_path(key)
                if not p.exists() or p.read_bytes()!=bmp_bytes(glyph(key)):
                    failed.append(str(p.relative_to(ROOT)))
        if failed:
            print("MISMATCH:\n"+"\n".join(failed))
            return 1
        print("Verified 42 glyphs byte-identical; no files written.")
    elif args.board_dir:
        boards(args.board_dir)
        print(f"Review boards: {args.board_dir}")
    else:
        packs=[args.pack_dir] if args.pack_dir else [ROOT/"assets/packs"/p for p in ("authored","placeholder")]
        for pack in packs:
            for key in keys():
                write_key(pack,key)
        print(f"Generated {21*len(packs)} deterministic glyphs.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
