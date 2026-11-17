"""Replays the keyframes of the left front leg (LegL1A, LegL1B) on the seven other legs of one animation, as an
alternating gait: L1, R2, L3, R4 move together, and L2, R1, L4, R3 half a cycle later. Right legs are mirrored.
All earlier keyframes of those seven legs are replaced.

Usage: python3 replay_leg.py <shape.json> <animation code>
"""
import copy
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vsmc_writer

path, code = sys.argv[1], sys.argv[2]
shape, newline, tail = vsmc_writer.load(path)
anim = [a for a in shape["animations"] if a["code"] == code][0]
total = anim["quantityframes"]
half = total // 2
order = ["Leg%s%d%s" % (side, n, part) for side in "LR" for n in (1, 2, 3, 4) for part in "AB"]

master = {}
for kf in anim["keyframes"]:
    for part in "AB":
        if "LegL1" + part in kf["elements"]:
            master.setdefault(part, {})[kf["frame"]] = kf["elements"]["LegL1" + part]
for kf in anim["keyframes"]:
    for name in list(kf["elements"].keys()):
        if name in order and not name.startswith("LegL1"):
            del kf["elements"][name]

by_frame = {kf["frame"]: kf for kf in anim["keyframes"]}
for side in "LR":
    for n in (1, 2, 3, 4):
        if side == "L" and n == 1:
            continue
        shift = half if n in (2, 4) else 0
        if side == "R":
            shift = (shift + half) % total
        for part, keys in master.items():
            for frame, el in keys.items():
                e = copy.deepcopy(el)
                if side == "R":
                    for k in ("offsetZ", "rotationX", "rotationY"):
                        if k in e:
                            e[k] = -e[k] + 0.0
                target = (frame + shift) % total
                if target not in by_frame:
                    by_frame[target] = {"frame": target, "elements": {}}
                    anim["keyframes"].append(by_frame[target])
                by_frame[target]["elements"]["Leg%s%d%s" % (side, n, part)] = e
        print("Leg%s%d: leg L1%s, %d frames later" % (side, n, " mirrored" if side == "R" else "", shift))

anim["keyframes"].sort(key=lambda k: k["frame"])
for kf in anim["keyframes"]:
    els = kf["elements"]
    kf["elements"] = {n: els[n] for n in order if n in els} | {n: v for n, v in els.items() if n not in order}
vsmc_writer.save(path, shape, newline, tail)
