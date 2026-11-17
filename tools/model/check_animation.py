"""Plays one animation of a shape with the game's rules and checks that the right legs are mirror images of the
left legs on every frame. Optionally writes a looping preview GIF (needs numpy, matplotlib and pillow).

Usage: python3 check_animation.py <shape.json> <animation code> [phase] [preview.gif]
phase: how many frames the right legs run behind the left ones (0 for poses, half the length for a gait).

The game's rules, from its source (ShapeElement.GetLocalTransformMatrix, Animation.GenerateFrameForElement):
an element's transform is T(rotationOrigin) * Rx*Ry*Rz * T(from - rotationOrigin), children are placed relative to
the parent's "from" corner; a keyframe's rotation is added to the element's own, its offset to "from"; each
element is interpolated linearly between the keyframes that name it, wrapping around the end.
"""
import json
import math
import re
import sys

import numpy as np

shape = json.loads(re.sub(r",(\s*[\]}])", r"\1", open(sys.argv[1]).read()))
anim = [a for a in shape["animations"] if a["code"] == sys.argv[2]][0]
phase = int(sys.argv[3]) if len(sys.argv) > 3 else 0
gif = sys.argv[4] if len(sys.argv) > 4 else None
N = anim["quantityframes"]
kfs = sorted(anim["keyframes"], key=lambda k: k["frame"])
KEYS = ("offsetX", "offsetY", "offsetZ", "rotationX", "rotationY", "rotationZ")


def pose(name, frame):
    have = [k for k in kfs if name in k["elements"]]
    if not have:
        return [0.0] * 6
    right = next((k for k in have if k["frame"] > frame), have[0])
    left = have[(have.index(right) - 1) % len(have)]
    if left is right:
        t = 0.0
    elif right["frame"] < left["frame"]:
        t = ((frame - left["frame"]) % N) / (right["frame"] + N - left["frame"])
    else:
        t = (frame - left["frame"]) / (right["frame"] - left["frame"])
    a, b = left["elements"][name], right["elements"][name]
    return [a.get(k, 0) + (b.get(k, 0) - a.get(k, 0)) * t for k in KEYS]


def T(x, y, z):
    m = np.eye(4)
    m[:3, 3] = [x, y, z]
    return m


def R(rx, ry, rz):
    rx, ry, rz = [math.radians(a) for a in (rx, ry, rz)]
    X = np.array([[1, 0, 0], [0, math.cos(rx), -math.sin(rx)], [0, math.sin(rx), math.cos(rx)]])
    Y = np.array([[math.cos(ry), 0, math.sin(ry)], [0, 1, 0], [-math.sin(ry), 0, math.cos(ry)]])
    Z = np.array([[math.cos(rz), -math.sin(rz), 0], [math.sin(rz), math.cos(rz), 0], [0, 0, 1]])
    m = np.eye(4)
    m[:3, :3] = X @ Y @ Z
    return m


def boxes_at(get_pose):
    out = {}

    def walk(e, parent):
        f, t = e["from"], e["to"]
        o = e.get("rotationOrigin", [0, 0, 0])
        p = get_pose(e["name"])
        m = (parent @ T(*o) @ R(e.get("rotationX", 0) + p[3], e.get("rotationY", 0) + p[4], e.get("rotationZ", 0) + p[5])
             @ T(f[0] - o[0] + p[0], f[1] - o[1] + p[1], f[2] - o[2] + p[2]))
        size = [t[i] - f[i] for i in range(3)]
        out[e["name"]] = np.array([(m @ np.array([i * size[0], j * size[1], k * size[2], 1]))[:3]
                                   for i in (0, 1) for j in (0, 1) for k in (0, 1)])
        for c in e.get("children", []):
            walk(c, m)

    for e in shape["elements"]:
        walk(e, np.eye(4))
    return out


# The legs hang on the Cephalothorax and are symmetric about the middle of its 6-unit width.
plane = [e for e in shape["elements"] if e["name"] == "Cephalothorax"][0]["from"][2] + 3.0
worst = 0.0
low = 99.0
for frame in range(N):
    now = boxes_at(lambda n: pose(n, frame))

    # The same moment with the body posed as its own mirror image and the right legs taken "phase" frames later.
    def mirrored(n):
        if n.startswith("LegR"):
            return pose(n, (frame + phase) % N)
        p = pose(n, frame)
        return [p[0], p[1], -p[2], -p[3], -p[4], p[5]]

    later = boxes_at(mirrored)
    for name, c in now.items():
        if name.startswith("LegL"):
            refl = c.copy()
            refl[:, 2] = 2 * plane - refl[:, 2]
            r = later["LegR" + name[4:]]
            worst = max(worst, max(min(np.linalg.norm(r - p, axis=1)) for p in refl))
        if name.startswith("Leg"):
            low = min(low, c[:, 1].min())
print("frames checked: %d" % N)
print("right legs, %d frames later, against the mirrored left legs: largest gap %.5f units" % (phase, worst))
print("lowest point any leg reaches: y = %.2f units" % low)

if gif:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.animation import FuncAnimation, PillowWriter
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection

    faces = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)]
    fig = plt.figure(figsize=(9, 4.4), dpi=80)
    views = [("From above (head on the left)", 90, 0), ("Three-quarter view", 25, -55)]
    axes = [fig.add_subplot(1, 2, n + 1, projection="3d") for n in range(2)]

    def draw(frame):
        b = boxes_at(lambda n: pose(n, frame))
        for ax, (title, elev, azim) in zip(axes, views):
            ax.clear()
            for name, c in b.items():
                # cyclic axis swap, so left and right stay the right way round: plot X = model Z, Y = model X, Z = model Y
                p = c[:, [2, 0, 1]]
                col = "#c9a36b"
                if name.startswith("LegR"):
                    col = "#e07a5f"
                elif name.startswith("LegL"):
                    col = "#6b9ac9"
                elif name.startswith("Eye"):
                    col = "#222222"
                ax.add_collection3d(Poly3DCollection([[p[k] for k in f] for f in faces], facecolor=col, edgecolor="#333333", linewidth=0.4))
            ax.set_xlim(0, 16); ax.set_ylim(-0.5, 15.5); ax.set_zlim(-5, 11)
            ax.set_box_aspect((1, 1, 1)); ax.view_init(elev=elev, azim=azim); ax.set_axis_off(); ax.set_title(title, fontsize=10, y=0.95)
        fig.suptitle("%s, frame %d of %d   (blue = left legs, red = right legs)" % (sys.argv[2], frame, N), fontsize=10)

    fig.subplots_adjust(left=0, right=1, bottom=0, top=0.92, wspace=0)
    steps = list(range(0, N, 2)) + ([N - 1] * 8 if anim.get("onAnimationEnd") == "Hold" else [])
    FuncAnimation(fig, lambda i: draw(steps[i]), frames=len(steps)).save(gif, writer=PillowWriter(fps=15))
