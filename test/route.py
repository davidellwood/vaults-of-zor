"""Build a key sequence that walks a route through one level (test helper).

usage: python route.py pack.zlp level  step step ...
  steps:  x,y        walk to cell x,y (doors and guardians block, secret walls don't)
          face:N     turn to face N/E/S/W
          clear:x,y  treat cell x,y as floor from now on (guardian killed, door opened)
          any other  passed through as keys (e.g. LOOK/ OPEN/ TAKE/)
"""
import sys
from collections import deque
from plan import load, DX, DY

levels = load(sys.argv[1])
lv = levels[int(sys.argv[2]) - 1]
g = lv["map"]
start = next((x, y) for y in range(24) for x in range(32) if g[y][x] == "H")
f = lv["facing"]
dr = "NESW".index(f) if f and f in "NESW" else next(
    d for d in (1, 2, 3, 0) if g[start[1] + DY[d]][start[0] + DX[d]] not in "#D%")
pos, keys = start, ["ENTER"]


def face(d):
    global dr
    keys.extend({0: [], 1: ["RIGHT"], 2: ["RIGHT", "RIGHT"], 3: ["LEFT"]}[(d - dr) % 4])
    dr = d


def walk(t):
    global pos
    q = deque([pos]); prev = {pos: None}
    while q:
        c = q.popleft()
        if c == t:
            break
        for d in range(4):
            n = (c[0] + DX[d], c[1] + DY[d])
            if n not in prev and g[n[1]][n[0]] not in "#DG&":
                prev[n] = c; q.append(n)
    p = [t]
    while prev[p[-1]] is not None:
        p.append(prev[p[-1]])
    for c in p[::-1][1:]:
        face([i for i in range(4) if (pos[0] + DX[i], pos[1] + DY[i]) == c][0])
        keys.append("UP")
        pos = c


for step in sys.argv[3:]:
    if step.startswith("face:"):
        face("NESW".index(step[5]))
    elif step.startswith("clear:"):           # e.g. a guardian killed or a door opened
        x, y = map(int, step[6:].split(","))
        g[y][x] = "."
    elif "," in step and step.replace(",", "").isdigit():
        x, y = map(int, step.split(","))
        walk((x, y))
    else:
        keys.append(step)
print(" ".join(keys))
