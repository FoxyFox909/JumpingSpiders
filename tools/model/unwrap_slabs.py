"""Lays out the UVs of a model made of flat boxes (slabs and panels) and puts one texture on every face.

For each box, the two big faces (the largest opposite pair) share one area of the texture, and its four thin
edge faces share one strip. No two boxes overlap. Each box is marked "autoUnwrap": false and each face
"autoUv": false, so VS Model Creator keeps this layout instead of recalculating it.

Usage: python3 unwrap_slabs.py <shape.json> <texture code> [guide.png]
The layout is printed as a table; guide.png (needs pillow) is a picture of it, 16 pixels per texture unit.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vsmc_writer

path, code = sys.argv[1], sys.argv[2]
guide = sys.argv[3] if len(sys.argv) > 3 else None
shape, newline, tail = vsmc_writer.load(path)
W, H = shape["textureWidth"], shape["textureHeight"]

elements = []


def walk(els):
    for e in els:
        elements.append(e)
        walk(e.get("children", []))


walk(shape["elements"])

# What each face shows of its box, as (width, height) in texture units: north and south show X by Y,
# east and west Z by Y, up and down X by Z.
rects = []  # (name, kind, width, height, element, faces)
for e in elements:
    w, h, d = [e["to"][i] - e["from"][i] for i in range(3)]
    dims = {"north": (w, h), "south": (w, h), "east": (d, h), "west": (d, h), "up": (w, d), "down": (w, d)}
    pairs = [("north", "south"), ("east", "west"), ("up", "down")]
    big = max(pairs, key=lambda p: dims[p[0]][0] * dims[p[0]][1])
    thin = [f for p in pairs if p != big for f in p]
    e["_dims"] = dims
    rects.append((e["name"], "big", dims[big[0]][0], dims[big[0]][1], e, list(big)))
    rects.append((e["name"], "edges", max(dims[f][0] for f in thin), max(dims[f][1] for f in thin), e, thin))

# Shelf packing on whole units, tallest first.
rects.sort(key=lambda r: (-math.ceil(r[3]), -math.ceil(r[2])))
x = y = shelf = 0
placed = []
for name, kind, rw, rh, e, faces in rects:
    cw, ch = math.ceil(rw), math.ceil(rh)
    if x + cw > W:
        x = 0
        y += shelf
        shelf = 0
    if y + ch > H:
        sys.exit("the layout does not fit a %d x %d texture" % (W, H))
    placed.append((name, kind, x, y, rw, rh, e, faces))
    for f in faces:
        fw, fh = e["_dims"][f]
        old = e["faces"][f]
        new = {"texture": "#" + code, "uv": [float(x), float(y), float(x + fw), float(y + fh)], "autoUv": False}
        if old.get("enabled", True) is False:
            new["enabled"] = False
        e["faces"][f] = new
    x += cw
    shelf = max(shelf, ch)

for e in elements:
    del e["_dims"]
    # keep the editor's key order: name, from, to, then the unwrap settings
    rest = {k: v for k, v in e.items() if k not in ("name", "from", "to", "unwrapMode", "autoUnwrap")}
    keep = {"name": e["name"], "from": e["from"], "to": e["to"], "autoUnwrap": False}
    e.clear()
    e.update(keep)
    e.update(rest)

vsmc_writer.save(path, shape, newline, tail)

print("texture: %d x %d units" % (W, H))
for name, kind, px, py, rw, rh, e, faces in sorted(placed, key=lambda r: (r[3], r[2])):
    print("%-9s %-5s x %2d..%-4g y %2d..%-4g size %4g x %-4g faces %s" % (name, kind, px, px + rw, py, py + rh, rw, rh, ", ".join(faces)))

if guide:
    from PIL import Image, ImageDraw
    S = 16
    img = Image.new("RGB", (W * S, H * S), "#f4f1ea")
    draw = ImageDraw.Draw(img)
    for gx in range(0, W + 1):
        draw.line([(gx * S, 0), (gx * S, H * S)], fill="#e2ddd0")
    for gy in range(0, H + 1):
        draw.line([(0, gy * S), (W * S, gy * S)], fill="#e2ddd0")
    colours = ["#c96f53", "#5b8bbf", "#6aa56a", "#b08a3c", "#8a6bb5", "#4f9c9c", "#b5607f", "#7d8b3a"]
    names = []
    for name, kind, px, py, rw, rh, e, faces in placed:
        if name not in names:
            names.append(name)
        col = colours[names.index(name) % len(colours)]
        box = [px * S, py * S, (px + rw) * S - 1, (py + rh) * S - 1]
        draw.rectangle(box, fill=col if kind == "big" else "#ffffff", outline=col, width=2)
        label = "%s %s  %gx%g" % (name, "big faces" if kind == "big" else "edges", rw, rh)
        draw.text((px * S + 4, py * S + 2), label, fill="#ffffff" if kind == "big" else "#222222")
    img.save(guide)
