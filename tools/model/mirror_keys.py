"""Gives every right leg (LegR...) the keyframes of its left partner (LegL...), mirrored, on the same frames.

Usage: python3 mirror_keys.py <shape.json> <animation code> [replace]
"replace" drops keyframes the right legs already have; without it the script stops if there are any.
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vsmc_writer

path, code = sys.argv[1], sys.argv[2]
replace = len(sys.argv) > 3 and sys.argv[3] == "replace"
shape, newline, tail = vsmc_writer.load(path)
anim = [a for a in shape["animations"] if a["code"] == code][0]
order = ["LegR%d%s" % (n, part) for n in (1, 2, 3, 4) for part in "AB"]

for kf in anim["keyframes"]:
    els = kf["elements"]
    old = [n for n in els if n.startswith("LegR")]
    if old and not replace:
        sys.exit("frame %d already has right-leg keyframes; pass 'replace' to overwrite them" % kf["frame"])
    for n in old:
        del els[n]
    right = {}
    for name, el in els.items():
        if name.startswith("LegL"):
            m = copy.deepcopy(el)
            # Mirroring left/right (the Z axis): the sideways offset and the rotations around X and Y change sign,
            # the rotation around Z (the axis that points sideways) stays.
            for key in ("offsetZ", "rotationX", "rotationY"):
                if key in m:
                    m[key] = -m[key] + 0.0
            right["LegR" + name[4:]] = m
    items = list(els.items())
    lefts = [i for i, (n, _) in enumerate(items) if n.startswith("LegL")]
    at = (max(lefts) + 1) if lefts else len(items)
    mine = [(n, right[n]) for n in order if n in right]
    kf["elements"] = dict(items[:at] + mine + items[at:])
    print("frame %2d: %s" % (kf["frame"], " ".join(n for n, _ in mine)))

vsmc_writer.save(path, shape, newline, tail)
