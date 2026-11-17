"""Reads and writes VS Model Creator shape files in the editor's own layout, so a scripted change shows up as a
small diff. Used by the other scripts in this folder."""
import json
import re
import sys


def load(path):
    """Returns (shape, newline, tail). Refuses files this writer would not write back unchanged."""
    raw = open(path, newline="").read()
    newline = "\r\n" if "\r\n" in raw else "\n"
    body = raw.rstrip("\r\n")
    tail = raw[len(body):]
    # The editor sometimes leaves a comma before a closing bracket; the game accepts that, strict JSON does not.
    strict = re.sub(r",(\s*[\]}])", r"\1", raw)
    data = json.loads(strict)
    if strict == raw and shape(data).replace("\n", newline) != body:
        sys.exit("vsmc_writer does not reproduce " + path + " exactly; not touching it")
    return data, newline, tail


def save(path, data, newline, tail):
    open(path, "w", newline="").write(shape(data).replace("\n", newline) + tail)


def num(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return str(v)
    v = round(v, 4)
    if v == 0:
        v = 0.0  # never "-0.0"
    return repr(v)


def inline(v):
    if isinstance(v, dict):
        parts = []
        for k, x in v.items():
            if k in ("windMode", "windData"):
                # the editor writes a face's wind values without spaces: [-1,-1,-1,-1]
                parts.append(json.dumps(k) + ": [" + ",".join(num(n) for n in x) + "]")
            else:
                parts.append(json.dumps(k) + ": " + inline(x))
        return "{ " + ", ".join(parts) + " }"
    if isinstance(v, list):
        return "[ " + ", ".join(inline(x) for x in v) + " ]"
    if isinstance(v, str):
        return json.dumps(v)
    return num(v)


def element(e, depth):
    t = "\t" * depth
    lines = []
    for k, v in e.items():
        if k == "faces":
            rows = [t + "\t\t" + json.dumps(n) + ": " + inline(f) for n, f in v.items()]
            lines.append(t + "\t\"faces\": {\n" + ",\n".join(rows) + "\n" + t + "\t}")
        elif k == "children":
            lines.append(t + "\t\"children\": [\n" + ",\n".join(element(c, depth + 2) for c in v) + "\n" + t + "\t]")
        else:
            lines.append(t + "\t" + json.dumps(k) + ": " + inline(v))
    return t + "{\n" + ",\n".join(lines) + "\n" + t + "}"


def keyframe(kf):
    lines = []
    for k, v in kf.items():
        if k == "elements":
            rows = ["\t\t\t\t\t\t" + json.dumps(n) + ": " + inline(x) for n, x in v.items()]
            lines.append("\t\t\t\t\t\"elements\": {\n" + ",\n".join(rows) + "\n\t\t\t\t\t}")
        else:
            lines.append("\t\t\t\t\t" + json.dumps(k) + ": " + inline(v))
    return "\t\t\t\t{\n" + ",\n".join(lines) + "\n\t\t\t\t}"


def animation(a):
    lines = []
    for k, v in a.items():
        if k == "keyframes":
            lines.append("\t\t\t\"keyframes\": [\n" + ",\n".join(keyframe(x) for x in v) + "\n\t\t\t]")
        else:
            lines.append("\t\t\t" + json.dumps(k) + ": " + inline(v))
    return "\t\t{\n" + ",\n".join(lines) + "\n\t\t}"


def shape(s):
    parts = []
    keys = list(s.keys())
    for k, v in s.items():
        if k == "elements":
            parts.append("\t\"elements\": [\n" + ",\n".join(element(e, 2) for e in v) + "\n\t]")
        elif k == "animations":
            # the editor leaves a blank line between two animations
            parts.append("\t\"animations\": [\n" + ",\n\n".join(animation(a) for a in v) + "\n\t]")
        elif isinstance(v, dict):
            inner = ["\t\t" + json.dumps(a) + ": " + inline(b) for a, b in v.items()]
            parts.append("\t" + json.dumps(k) + ": {\n" + (",\n".join(inner) + "\n" if inner else "") + "\t}")
        else:
            parts.append("\t" + json.dumps(k) + ": " + inline(v))
    # The editor closes the file right after "elements" when that is the last key, on a new line otherwise.
    end = "}" if keys[-1] == "elements" else "\n}"
    return "{\n" + ",\n".join(parts) + end
