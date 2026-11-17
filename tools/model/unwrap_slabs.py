"""Lays out the UVs of a model made of flat boxes (slabs and panels) and puts one texture on every face.

For each box, the two big faces (the largest opposite pair) share one area of the texture, and its four thin
edge faces share one strip. No two boxes overlap. Each box is marked "autoUnwrap": false and each face
"autoUv": false, so VS Model Creator keeps this layout instead of recalculating it.

Sizes are rounded to whole texture units, so every area starts and ends on a pixel boundary; a box that is
14.1 long gets a 14 unit area (stretched by under 1 %).

Run it again after changing the model: an area that still fits where it was stays there (big faces first), and
only what no longer fits is moved to free space.

Usage: python3 unwrap_slabs.py <shape.json> <texture code> [guide.png]
The layout is printed as a table; guide.png (needs pillow) is a picture of it, 16 pixels per texture unit.
"""
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


def whole(v):
    return max(1, int(round(v)))


# What each face shows of its box, as (width, height) in texture units: north and south show X by Y,
# east and west Z by Y, up and down X by Z.
rects = []
for e in elements:
    w, h, d = [e["to"][i] - e["from"][i] for i in range(3)]
    dims = {"north": (w, h), "south": (w, h), "east": (d, h), "west": (d, h), "up": (w, d), "down": (w, d)}
    pairs = [("north", "south"), ("east", "west"), ("up", "down")]
    big = max(pairs, key=lambda p: dims[p[0]][0] * dims[p[0]][1])
    thin = [f for p in pairs if p != big for f in p]
    dims = {f: (whole(a), whole(b)) for f, (a, b) in dims.items()}
    for kind, faces in (("big", list(big)), ("edges", thin)):
        rw = max(dims[f][0] for f in faces)
        rh = max(dims[f][1] for f in faces)
        # where this group was before, if every face of it already carries this texture
        old = None
        if all(e["faces"][f].get("texture") == "#" + code for f in faces):
            old = (int(min(e["faces"][f]["uv"][0] for f in faces)), int(min(e["faces"][f]["uv"][1] for f in faces)))
        rects.append({"name": e["name"], "kind": kind, "w": rw, "h": rh, "e": e, "faces": faces, "dims": dims, "old": old})

placed = []


def free(x, y, w, h):
    if x < 0 or y < 0 or x + w > W or y + h > H:
        return False
    for r in placed:
        if x < r["x"] + r["w"] and r["x"] < x + w and y < r["y"] + r["h"] and r["y"] < y + h:
            return False
    return True


def first_fit(w, h):
    for y in range(0, H - h + 1):
        for x in range(0, W - w + 1):
            if free(x, y, w, h):
                return x, y
    sys.exit("the layout does not fit a %d x %d texture" % (W, H))


for kind in ("big", "edges"):
    group = [r for r in rects if r["kind"] == kind]
    moving = []
    for r in group:
        if r["old"] is not None and free(r["old"][0], r["old"][1], r["w"], r["h"]):
            r["x"], r["y"] = r["old"]
            r["moved"] = False
            placed.append(r)
        else:
            moving.append(r)
    for r in sorted(moving, key=lambda r: (-r["h"], -r["w"])):
        r["x"], r["y"] = first_fit(r["w"], r["h"])
        r["moved"] = True
        placed.append(r)

for r in placed:
    e = r["e"]
    for f in r["faces"]:
        fw, fh = r["dims"][f]
        was = e["faces"][f]
        new = {"texture": "#" + code, "uv": [float(r["x"]), float(r["y"]), float(r["x"] + fw), float(r["y"] + fh)], "autoUv": False}
        if was.get("enabled", True) is False:
            new["enabled"] = False
        e["faces"][f] = new

for e in elements:
    # keep the editor's key order: name, from, to, then the unwrap settings
    rest = {k: v for k, v in e.items() if k not in ("name", "from", "to", "unwrapMode", "autoUnwrap")}
    keep = {"name": e["name"], "from": e["from"], "to": e["to"], "autoUnwrap": False}
    e.clear()
    e.update(keep)
    e.update(rest)

vsmc_writer.save(path, shape, newline, tail)

print("texture: %d x %d units" % (W, H))
for r in sorted(placed, key=lambda r: (r["y"], r["x"])):
    sizes = ", ".join("%s %dx%d" % (f, r["dims"][f][0], r["dims"][f][1]) for f in r["faces"])
    note = ""
    if r["old"] is None:
        note = "new"
    elif r["moved"]:
        note = "moved from %d, %d" % r["old"]
    print("%-9s %-5s at %2d, %-2d  %2d x %-2d  %-22s %s" % (r["name"], r["kind"], r["x"], r["y"], r["w"], r["h"], note, sizes))

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
    for r in placed:
        if r["name"] not in names:
            names.append(r["name"])
        col = colours[names.index(r["name"]) % len(colours)]
        box = [r["x"] * S, r["y"] * S, (r["x"] + r["w"]) * S - 1, (r["y"] + r["h"]) * S - 1]
        draw.rectangle(box, fill=col if r["kind"] == "big" else "#ffffff", outline=col, width=2)
        label = "%s %s %dx%d" % (r["name"], "big faces" if r["kind"] == "big" else "edges", r["w"], r["h"])
        if r["w"] < 6:
            label = r["name"][:2]
        draw.text((r["x"] * S + 4, r["y"] * S + 2), label, fill="#ffffff" if r["kind"] == "big" else "#222222")
    img.save(guide)
