"""Plan winning key sequences for the levels in a .ZLP pack (test helper).

usage: python plan.py pack.zlp [levels...]   e.g.  plan.py ../levels/default.zlp 1 2
Assumes each level has one sword, guardian, key, door and amulet (like the
generated levels). Prints the key tokens for test/play.ps1.
"""
import sys
from collections import deque

DX = [0, 1, 0, -1]; DY = [-1, 0, 1, 0]


def load(path):
    levels, cur, lines = [], None, open(path).read().splitlines()
    i = 0
    while i < len(lines):
        l = lines[i].strip()
        if l.lower() == "[level]":
            cur = {"facing": "AUTO"}; levels.append(cur)
        elif cur is not None and "=" in l:
            k, v = l.split("=", 1)
            if k == "map":
                cur["map"] = [list(lines[i + 1 + y].ljust(32, "#")) for y in range(24)]
                i += 24
            else:
                cur[k] = v
        i += 1
    return levels


def plan_level(lv):
    g = lv["map"]
    find = lambda ch: next(((x, y) for y in range(24) for x in range(32) if g[y][x] == ch), None)
    start = find("H")
    f = lv["facing"]
    dr = "NESW".index(f) if f in "NESW" and f else next(
        d for d in (1, 2, 3, 0) if g[start[1] + DY[d]][start[0] + DX[d]] not in "#D")
    keys, pos = [], start

    def path(a, b, block):
        q = deque([a]); prev = {a: None}
        while q:
            c = q.popleft()
            if c == b: break
            for d in range(4):
                n = (c[0] + DX[d], c[1] + DY[d])
                if n not in prev and g[n[1]][n[0]] not in "#&" and n not in block:
                    prev[n] = c; q.append(n)
        p = [b]
        while prev[p[-1]] is not None: p.append(prev[p[-1]])
        return p[::-1]

    def face(d):
        nonlocal dr
        keys.extend({0: [], 1: ["RIGHT"], 2: ["RIGHT", "RIGHT"], 3: ["LEFT"]}[(d - dr) % 4]); dr = d

    def step_dir(a, b):
        return [i for i in range(4) if (a[0] + DX[i], a[1] + DY[i]) == b][0]

    def walk(t, stop=False, block=()):
        nonlocal pos
        p = path(pos, t, set(block))
        if stop: p = p[:-1]
        for c in p[1:]:
            face(step_dir(pos, c)); keys.append("UP"); pos = c

    door, guard = find("D"), find("G")
    walk(find("S"), block=(door, guard)); keys.append("TAKE/")
    walk(guard, True, (door,)); face(step_dir(pos, guard)); keys += ["A/"] * 9
    g[guard[1]][guard[0]] = "."
    walk(find("K"), block=(door,)); keys.append("TAKE/")
    walk(door, True); face(step_dir(pos, door)); keys.append("OPEN/")
    g[door[1]][door[0]] = "."
    walk(find("A")); keys.append("TAKE/")
    walk(start)
    return keys


if __name__ == "__main__":
    levels = load(sys.argv[1])
    which = [int(a) for a in sys.argv[2:]] or range(1, len(levels) + 1)
    out = ["ENTER"]
    for i, n in enumerate(which):
        if i: out.append("ENTER")          # "level complete - press ENTER"
        out += plan_level(levels[n - 1])
    print(" ".join(out))
