#!/usr/bin/env python3
"""
gen.py - asset generator for VAULTS OF ZOR (TRS-80 Model I, Z80).

Produces gfx.inc, included by zor.asm:
  * 3D view wall segments, pre-rasterised into TRS-80 semigraphic characters
  * sprites (guardian, items, exit ladder) with per-pixel transparency masks
Also writes levels/default.zlp (the default two-level pack) if it is
missing, or always with --levels. Its maze generator places items so that
the door and guardian really gate the amulet and key.

TRS-80 semigraphics: chars 128-191, each a 2x3 block of pixels:
    bit0 bit1      (top row)
    bit2 bit3      (middle row)
    bit4 bit5      (bottom row)
The 3D window is 32 chars x 16 rows = 64 x 48 pixels.

Run:  python gen.py            (writes gfx.inc)
      python gen.py --preview  (also prints ASCII previews)
"""
import os
import random
import sys
from collections import deque

VW, VH = 64, 48            # 3D view size in pixels
CW, CH = VW // 2, VH // 3  # ... in characters (32 x 16)

# ---------------------------------------------------------------- geometry
# Perspective boundaries. Boundary k sits at the near edge of the cell k steps
# ahead (k=0 is the player's own cell). xL = left x of boundary in pixels
# (must be even so segments are character aligned); half height scales with it.
#
# For smooth movement the view is also drawn part way through a step: at
# phase f the player has moved f of a cell forward, so boundary k sits at
# distance k - f. Each phase gets its own pre-rendered set of segments.
PHASES = 4                              # 0, 1/4, 1/2, 3/4 of a step
SCALE = [1, 0.6875, 0.4375, 0.25, 0.125]  # size at distance 0..4 (phase 0: xL 0,10,18,24,28)
CY = 24.0
COURSES = 4  # brick courses per wall


def scale_at(z):
    i = min(int(z), 3)
    return SCALE[i] + (SCALE[i + 1] - SCALE[i]) * (z - i)


def set_phase(f):
    """Set the boundary globals for a view f (0 <= f < 1) of a cell forward."""
    global XL, XR, HH, TOP, BOT
    XL = [0]
    for k in range(1, 5):
        x = 2 * round(16 * (1 - scale_at(k - f)))
        XL.append(min(max(x, XL[-1] + 2), 30))
    XR = [VW - x for x in XL]
    HH = [24 * (32 - x) / 32 for x in XL]
    TOP = [CY - h for h in HH]
    BOT = [CY + h for h in HH]


set_phase(0)


def mortar_rows(top, bot):
    """Pixel rows that hold horizontal mortar lines for a wall span."""
    h = bot - top
    if h / COURSES < 2.5:
        return set()
    return {int(top + h * f / COURSES) for f in range(1, COURSES)}


def course_of(yc, top, bot):
    return int((yc - top) / ((bot - top) / COURSES))


def new_canvas(fill=0):
    return [[fill] * VW for _ in range(VH)]


def side_wall(cv, k):
    """Left side wall of depth k (trapezoid between boundaries k and k+1)."""
    x0, x1 = XL[k], XL[k + 1]
    for x in range(x0, x1):
        t = (x + 0.5 - x0) / (x1 - x0)
        top = TOP[k] + (TOP[k + 1] - TOP[k]) * t
        bot = BOT[k] + (BOT[k + 1] - BOT[k]) * t
        mrows = mortar_rows(top, bot)
        for y in range(VH):
            yc = y + 0.5
            if not (top <= yc < bot):
                continue
            on = 1
            if x == x1 - 1:          # dark joint at the far edge
                on = 0
            if y in mrows:
                on = 0
            if k <= 1 and mrows and course_of(yc, top, bot) % 2 == 1 \
                    and x == (x0 + x1) // 2:
                on = 0               # staggered vertical joint
            if k >= 3 and (x + y) % 2:
                on = 0               # distance dither
            cv[y][x] = on


def flat_face(cv, x0, x1, d, door=False, far_joint=False):
    """Front-facing wall at depth boundary d spanning pixels x0..x1-1."""
    top, bot = TOP[d], BOT[d]
    w, h = x1 - x0, bot - top
    mrows = mortar_rows(top, bot)
    # door rectangle (in face coordinates)
    dx0, dx1 = XL[d] + int((XR[d] - XL[d]) * 0.30), XL[d] + int((XR[d] - XL[d]) * 0.70)
    dy0 = int(top + h * 0.22)
    for x in range(x0, x1):
        for y in range(VH):
            yc = y + 0.5
            if not (top <= yc < bot):
                continue
            on = 1
            if y in mrows:
                on = 0
            if mrows:
                c = course_of(yc, top, bot)
                fw = XR[d] - XL[d]
                joints = [0.5] if c % 2 == 0 else [0.25, 0.75]
                for f in joints:
                    if x == XL[d] + int(fw * f):
                        on = 0
            if far_joint and x == x1 - 1:
                on = 0
            if d >= 4 and (x + y) % 2:
                on = 0
            if door and dx0 <= x < dx1 and y >= dy0:
                # door: dark frame, vertical planks inside, lock plate
                if x in (dx0, dx1 - 1) or y == dy0:
                    on = 0
                else:
                    on = 0 if (x - dx0) % 3 == 0 else 1
                    if d <= 2 and x == dx1 - 3 and abs(y - (dy0 + bot) / 2) < 1:
                        on = 0
            cv[y][x] = on


def mirror(cv):
    return [list(reversed(r)) for r in cv]


def char_at(cv, cx, cy, transparent=False):
    """Return (mask, bits) for one character cell."""
    mask = bits = 0
    for py in range(3):
        for px in range(2):
            v = cv[cy * 3 + py][cx * 2 + px]
            b = 1 << (py * 2 + px)
            if v is None:
                continue
            mask |= b
            if v:
                bits |= b
    return mask, bits


def region_bytes(cv, c0, c1):
    """Characters for columns c0..c1-1, all 16 rows, row major."""
    out = []
    for cy in range(CH):
        for cx in range(c0, c1):
            out.append(0x80 | char_at(cv, cx, cy)[1])
    return out


# ---------------------------------------------------------------- segments
def build_segments():
    """Returns dict name -> (col, width, bytes)."""
    segs = {}
    for k in range(4):
        for state in range(3):          # 0 empty, 1 open with face, 2 wall
            cv = new_canvas()
            if state == 1:
                flat_face(cv, XL[k], XL[k + 1], k + 1, far_joint=True)
            elif state == 2:
                side_wall(cv, k)
            c0, c1 = XL[k] // 2, XL[k + 1] // 2
            m = mirror(cv)
            r0, r1 = XR[k + 1] // 2, XR[k] // 2
            if state == 0:              # all blank: a 2-byte fill record
                segs[f"L{k}0"] = (c0, c1 - c0, None)
                segs[f"R{k}0"] = (r0, r1 - r0, None)
                continue
            segs[f"L{k}{state}"] = (c0, c1 - c0, region_bytes(cv, c0, c1))
            segs[f"R{k}{state}"] = (r0, r1 - r0, region_bytes(m, r0, r1))
    for d in range(1, 5):
        for door in (0, 1):
            cv = new_canvas()
            flat_face(cv, XL[d], XR[d], d, door=bool(door))
            c0, c1 = XL[d] // 2, XR[d] // 2
            segs[f"F{d}{door}"] = (c0, c1 - c0, region_bytes(cv, c0, c1))
    c0, c1 = XL[4] // 2, XR[4] // 2
    segs["F5"] = (c0, c1 - c0, None)
    return segs


# ---------------------------------------------------------------- sprites
# non-player characters (cell codes 16-18), drawn full height like the guardian
NPCS = {
    "HERMIT": """
......###.......
.....#####......
....##ooo##.....
....#o#o#o#.....
....##ooo##..#..
.....#####...#..
....#######..#..
...#########.#..
...##.###.####..
...##.###.##.#..
...##.###....#..
...##.###....#..
....#######..#..
....#######..#..
....#######..#..
....#######..#..
...#########.#..
...#########.#..
...#########.#..
..###########...
..###########...
..###########...
................
................
""",
    "MERCHANT": """
................
.....######.....
.....######.....
...##########...
......#oo#......
......####......
......#oo#......
.....######.....
...##########...
..####.##.####..
..###..##..###..
..###.####.###..
.###..####..##..
.##..######.....
.##..######.###.
.....######.####
.....######.####
.....###.###.##.
.....##...##....
.....##...##....
.....##...##....
....###...###...
................
................
""",
    "GHOST": """
.....######.....
...##########...
..############..
..###oo##oo###..
.####oo##oo####.
.##############.
.######oo######.
.#####oooo#####.
.######oo######.
.##############.
.##############.
.##############.
.##############.
.##############.
.##############.
.##############.
.##############.
.##############.
.###.####.####..
.##...##...###..
.#.....#....##..
................
................
................
""",
}

GUARDIAN = """
.##..........##.
.##..........##.
..##........##..
...##.####.##...
....########....
....#oo##oo#....
....########....
.....######.....
.....#o##o#.....
......####......
...##########...
..############..
.##############.
.###.######.###.
.###.######.###.
.###..####..###.
.##...####...##.
.##..######..##.
.....######.....
.....##..##.....
.....##..##.....
....###..###....
...####..####...
................
"""

ITEMS = {
    "KEY": """
.##.....
#oo#####
#oo#.#.#
.##.....
""",
    "SWORD": """
...#........
..##........
############
..##........
...#........
""",
    "POTION": """
.###.
..#..
.###.
##o##
#####
.###.
""",
    "GOLD": """
...##...
..#o##..
.######.
##o###o#
""",
    "AMULET": """
.#####.
#.....#
.#...#.
..###..
.#ooo#.
..###..
""",
}

# floor features ("spaces") drawn like items
ITEMS.update({
    "SIGN": """
########
#oooooo#
#o####o#
#oooooo#
#o###oo#
#oooooo#
########
...##...
...##...
...##...
""",
    "TRAP": """
#..#..#..#
##.##.##.#
##########
""",
    "TELE": """
...######...
.##oooooo##.
#oooooooooo#
.##oooooo##.
...######...
""",
    "FOUNT": """
....##....
...#..#...
....##....
....##....
##########
#oooooooo#
.########.
""",
    "PLATE": """
.########.
#oooooooo#
##########
""",
    "THING": """
.########.
#oooooooo#
##########
#oooo#ooo#
#oooooooo#
##########
""",
})

GLINT = """
.#.
#o#
.#.
"""


def parse_art(s):
    rows = [r for r in s.strip("\n").split("\n")]
    w = max(len(r) for r in rows)
    out = []
    for r in rows:
        r = r.ljust(w, ".")
        out.append([None if c == "." else (1 if c == "#" else 0) for c in r])
    return out


def paste(cv, art, x0, y0, scale=1.0):
    h = len(art)
    w = len(art[0])
    nh, nw = max(1, round(h * scale)), max(1, round(w * scale))
    for y in range(nh):
        for x in range(nw):
            v = art[min(h - 1, int(y / scale))][min(w - 1, int(x / scale))]
            X, Y = x0 + x, y0 + y
            if v is not None and 0 <= X < VW and 0 <= Y < VH:
                cv[Y][X] = v


def outline(cv):
    """Give a sprite a 1-pixel opaque black halo so it reads against walls."""
    pts = [(x, y) for y in range(VH) for x in range(VW) if cv[y][x] == 1]
    for x, y in pts:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            X, Y = x + dx, y + dy
            if 0 <= X < VW and 0 <= Y < VH and cv[Y][X] is None:
                cv[Y][X] = 0


def sprite_bytes(cv):
    """Crop to the character bounding box -> [col,row,w,h, mask,bits,...]."""
    cells = [(cx, cy) for cy in range(CH) for cx in range(CW)
             if char_at(cv, cx, cy)[0]]
    if not cells:
        return None
    c0 = min(c[0] for c in cells); c1 = max(c[0] for c in cells) + 1
    r0 = min(c[1] for c in cells); r1 = max(c[1] for c in cells) + 1
    out = [c0, r0, c1 - c0, r1 - r0]
    for cy in range(r0, r1):
        for cx in range(c0, c1):
            m, b = char_at(cv, cx, cy)
            out += [m, b]
    return out


def floor_y(d):
    """Floor line (pixel y) halfway through the cell d steps ahead."""
    return CY + (HH[d] + HH[d + 1]) / 2


def wall_h(d):
    return HH[d] + HH[d + 1]


WALL_H1 = 27.0   # wall height of the cell 1 step ahead at phase 0 (item art scale 1)


def build_sprites():
    """Sprites for objects 1-3 cells ahead, sized for the current phase."""
    spr = {}
    figures = dict(GUARD=GUARDIAN, **NPCS)
    for name, art in figures.items():
        g = parse_art(art)
        for d in (1, 2, 3):
            cv = [[None] * VW for _ in range(VH)]
            tall = 0.92 if name == "GUARD" else 0.62    # the guardian is a giant
            sc = wall_h(d) * tall / len(g)
            w = round(len(g[0]) * sc)
            paste(cv, g, 32 - w // 2, round(floor_y(d) - len(g) * sc), sc)
            if wall_h(d) / WALL_H1 >= 0.85:
                outline(cv)
            spr[f"{name}{d}"] = sprite_bytes(cv)
    # exit ladder: rails and rungs from floor to ceiling, drawn procedurally
    for d in (1, 2, 3):
        cv = [[None] * VW for _ in range(VH)]
        top = int(CY - (HH[d] + HH[d + 1]) / 2)
        bot = int(floor_y(d))
        half = max(2, round(5 * wall_h(d) / WALL_H1))
        for y in range(top, bot):
            cv[y][32 - half] = 1
            cv[y][32 + half - 1] = 1
            if (y - top) % 3 == 1:
                for x in range(32 - half, 32 + half):
                    cv[y][x] = 1
            elif half >= 4:
                for x in range(32 - half + 1, 32 + half - 1):
                    cv[y][x] = 0
        spr[f"LADDER{d}"] = sprite_bytes(cv)
    gl = parse_art(GLINT)
    for d in (1, 2, 3):
        sc = wall_h(d) / WALL_H1
        for name, art in ITEMS.items():
            cv = [[None] * VW for _ in range(VH)]
            if sc >= 0.7:               # the item itself, scaled
                a = parse_art(art)
                w, h = round(len(a[0]) * sc), round(len(a) * sc)
                paste(cv, a, 32 - w // 2, int(floor_y(d)) - h, sc)
                if sc >= 0.85:
                    outline(cv)
            elif sc >= 0.35:            # too far to make out: a glint
                paste(cv, gl, 31, int(floor_y(d)) - 3)
            spr[f"{name}{d}"] = sprite_bytes(cv)
    return spr


# ---------------------------------------------------------------- maze
MW, MH = 32, 24          # storage size (power-of-two width)
FLOOR, WALL, DOOR, LADDER, GUARD, KEY, SWORD, POTION, GOLD, AMULET = range(10)
SIGN, TRAP, TELE, FOUNT, PLATE, SECRET = range(10, 16)   # spaces with game logic
HERMIT, MERCHANT, GHOST = range(16, 19)                   # non-player characters
THING = 19                # a scripted item lying on the floor (drawn, never stored in the map)
SOLID = (WALL, DOOR)


def carve_maze(rng):
    g = [[WALL] * MW for _ in range(MH)]
    W, H = 31, 23        # odd interior; column 31 and row 23 remain wall
    stack = [(1, 1)]
    g[1][1] = FLOOR
    while stack:
        x, y = stack[-1]
        nb = [(x + dx, y + dy, dx, dy) for dx, dy in ((2, 0), (-2, 0), (0, 2), (0, -2))
              if 0 < x + dx < W and 0 < y + dy < H and g[y + dy][x + dx] == WALL]
        if not nb:
            stack.pop()
            continue
        nx, ny, dx, dy = rng.choice(nb)
        g[y + dy // 2][x + dx // 2] = FLOOR
        g[ny][nx] = FLOOR
        stack.append((nx, ny))
    return g


def bfs(g, start, blocked=()):
    dist = {start: 0}
    prev = {}
    q = deque([start])
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + dx, y + dy)
            if g[n[1]][n[0]] not in SOLID and n not in dist and n not in blocked:
                dist[n] = dist[(x, y)] + 1
                prev[n] = (x, y)
                q.append(n)
    return dist, prev


def path_to(prev, start, goal):
    p = [goal]
    while p[-1] != start:
        p.append(prev[p[-1]])
    return p[::-1]


def open_neighbours(g, x, y):
    return sum(g[y + dy][x + dx] not in SOLID for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))


def build_maze(seed=1983):
    rng = random.Random(seed)
    while True:
        g = carve_maze(rng)
        start = (1, 1)
        dist, prev = bfs(g, start)
        dead = [c for c in dist if c != start and open_neighbours(g, *c) == 1]
        dead.sort(key=lambda c: dist[c])
        amulet = dead[-1]
        apath = path_to(prev, start, amulet)
        door = apath[-5]
        if open_neighbours(g, *door) != 2:
            continue
        aset = set(apath)
        # key: far dead end whose path leaves the amulet path early
        cands = []
        for d in dead:
            p = path_to(prev, start, d)
            shared = sum(1 for c in p if c in aset)
            if shared < len(apath) // 3 and dist[d] > 25:
                cands.append((dist[d], d, p))
        if not cands:
            continue
        cands.sort()
        _, key, kpath = cands[-1]
        guard = kpath[-4]
        if guard in aset or open_neighbours(g, *guard) != 2:
            continue
        # sword: reachable without passing door or guardian, not too close
        free, _ = bfs(g, start, blocked={door, guard})
        sw = [d for d in dead if d in free and 12 < dist[d] < 40 and d not in (key, amulet)]
        if not sw:
            continue
        sword = rng.choice(sw)
        g[door[1]][door[0]] = DOOR
        g[guard[1]][guard[0]] = GUARD
        # braid: knock out some dead-end walls to make loops, keeping gates intact
        key_set = {key, amulet}
        for _ in range(400):
            x, y = rng.randrange(1, 30), rng.randrange(1, 22)
            if g[y][x] != WALL or (x + y) % 2 == 0:
                continue
            # wall between two cells (horizontal or vertical)
            if not ((g[y][x - 1] not in SOLID and g[y][x + 1] not in SOLID) or
                    (g[y - 1][x] not in SOLID and g[y + 1][x] not in SOLID)):
                continue
            g[y][x] = FLOOR
            reach, _ = bfs(g, start, blocked={guard})
            if any(c in reach for c in key_set) or sword not in reach:
                g[y][x] = WALL
                continue
            if sum(1 for yy in range(23) for xx in range(31) if g[yy][xx] == FLOOR) > 420:
                break
        # gating sanity checks
        r1, _ = bfs(g, start, blocked={guard})
        assert key not in r1 and amulet not in r1 and sword in r1
        r2, _ = bfs(g, start, blocked={door})
        if amulet in r2:
            continue  # door must gate the amulet
        g[key[1]][key[0]] = KEY
        g[amulet[1]][amulet[0]] = AMULET
        g[sword[1]][sword[0]] = SWORD
        g[start[1]][start[0]] = LADDER
        empties = [c for c in r1 if g[c[1]][c[0]] == FLOOR and dist.get(c, 0) > 6
                   and open_neighbours(g, *c) == 2 and abs(c[0] - guard[0]) + abs(c[1] - guard[1]) > 2]
        rng.shuffle(empties)
        for c in empties[:2]:
            g[c[1]][c[0]] = POTION
        allfloor = [(x, y) for y in range(23) for x in range(31) if g[y][x] == FLOOR
                    and abs(x - guard[0]) + abs(y - guard[1]) > 2 and (x, y) != door]
        rng.shuffle(allfloor)
        for c in allfloor[:10]:
            g[c[1]][c[0]] = GOLD
        sdir = 1 if g[1][2] != WALL else 2      # face east if open, else south
        return g, start, sdir, dict(door=door, guard=guard, key=key, amulet=amulet, sword=sword)


# ---------------------------------------------------------------- default pack
MAPSYM = {FLOOR: ".", WALL: "#", DOOR: "D", LADDER: "H", GUARD: "G", KEY: "K",
          SWORD: "S", POTION: "P", GOLD: "$", AMULET: "A",
          SIGN: "?", TRAP: "^", TELE: "@", FOUNT: "W", PLATE: "=", SECRET: "%",
          HERMIT: "&", MERCHANT: "&", GHOST: "&"}


def add_features(g, start, info, seed):
    """Showcase level: put every kind of space and object setting into maze g.
    Everything goes in the area reachable before the guardian and door, so no
    shortcut can skip the puzzle. Returns the obj= lines."""
    rng = random.Random(seed)
    door, guard = info["door"], info["guard"]
    free, _ = bfs(g, start, blocked={door, guard})
    dist = free
    objs = []

    def floor_cells(lo=4, hi=999):
        return sorted(c for c in free if g[c[1]][c[0]] == FLOOR and lo <= dist[c] <= hi)

    def take(cells):
        c = rng.choice(cells)
        return c

    def obj(c, a=0, b=0, cc=0, text=""):
        objs.append(f"obj={c[0]},{c[1]},{a},{b},{cc},0,{text}")

    # sign right next to the ladder
    near = [c for c in floor_cells(1, 1)]
    if near:
        c = near[0]
        g[c[1]][c[0]] = SIGN
        obj(c, text="BEWARE: NOT EVERY STONE IS SOLID, NOT EVERY FLOOR IS SAFE.")
    # coloured key and door, a tougher guardian
    obj(info["key"], 1)                    # iron key
    obj(door, 1)                           # iron door
    obj(guard, 4, 3, 35, "A HUGE GUARDIAN IN BLACK IRON ARMOUR BLOCKS THE PASSAGE.")
    # hidden trap on the way to the sword, a visible one-shot trap elsewhere
    _, prev = bfs(g, start, blocked={door, guard})
    spath = path_to(prev, start, info["sword"])
    c = spath[len(spath) // 2]
    if g[c[1]][c[0]] == FLOOR:
        g[c[1]][c[0]] = TRAP
        obj(c, 2, 0, 0, "A HIDDEN SPIKE STABS YOUR FOOT!")
    c = take(floor_cells(6))
    g[c[1]][c[0]] = TRAP
    obj(c, 4, 1, 1, "THE TRAP SNAPS SHUT, THEN CRUMBLES TO RUST.")
    # fountain with two uses in a dead end
    dead = [c for c in floor_cells(5) if open_neighbours(g, *c) == 1]
    c = take(dead)
    g[c[1]][c[0]] = FOUNT
    obj(c, 0, 2, 0, "COOL WATER RESTORES YOU.")
    # teleporter in another dead end, landing far away (still before the gates)
    dead = [d for d in dead if d != c]
    t = take(dead)
    far = [f for f in floor_cells(dist[t] + 15) if abs(f[0] - t[0]) + abs(f[1] - t[1]) > 10]
    if far:
        dst = take(far)
        g[t[1]][t[0]] = TELE
        obj(t, dst[0], dst[1], 0, "")
    # a pressure plate that opens a wall between two parts of the free area
    walls = [(x, y) for y in range(1, 22) for x in range(1, 30)
             if g[y][x] == WALL and ((g[y][x - 1] == FLOOR and g[y][x + 1] == FLOOR and (x - 1, y) in free and (x + 1, y) in free)
                                     or (g[y - 1][x] == FLOOR and g[y + 1][x] == FLOOR and (x, y - 1) in free and (x, y + 1) in free))]
    rng.shuffle(walls)
    if len(walls) >= 2:
        w = walls[0]
        p = take(floor_cells(3, 12))
        g[p[1]][p[0]] = PLATE
        obj(p, w[0], w[1], 0, "")
        s2 = walls[1]                      # and a secret wall somewhere else
        g[s2[1]][s2[0]] = SECRET
        obj(s2, text="YOU STEP THROUGH THE WALL - IT WAS ONLY AN ILLUSION!")
    # a big pile of gold and a strong potion
    golds = [(x, y) for y in range(MH) for x in range(MW) if g[y][x] == GOLD]
    if golds:
        obj(golds[0], 50)
    pots = [(x, y) for y in range(MH) for x in range(MW) if g[y][x] == POTION]
    if pots:
        obj(pots[0], 12)
    return objs


def add_npcs(g, start, info, which):
    """Put a character into level `which` (0-2). Characters stand in empty dead
    ends (or, for the ghost, in front of the fountain), so they never block the
    way to the amulet. Returns the obj=/obj2= lines."""
    door, guard = info["door"], info["guard"]
    free, _ = bfs(g, start, blocked={door, guard})
    def quiet(c):                           # nothing but floor and wall around it
        return all(g[c[1] + dy][c[0] + dx] in (FLOOR, WALL) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
    dead = sorted((c for c in free if g[c[1]][c[0]] == FLOOR and open_neighbours(g, *c) == 1
                   and free[c] >= 3 and quiet(c)), key=lambda c: free[c])
    lines = []
    if which == 0 and dead:                  # a hermit near the start gives a potion
        c = dead[0]
        g[c[1]][c[0]] = HERMIT
        lines.append(f"obj={c[0]},{c[1]},0,1,0,6,WELCOME, YOUNG ONE. THE GUARDIAN "
                     "FEARS ONLY STEEL: FIND THE SWORD FIRST, THEN THE KEY. TAKE THIS POTION FOR THE ROAD.")
        lines.append(f"obj2={c[0]},{c[1]},GO NOW. THE AMULET WAITS BEYOND THE LOCKED DOOR.")
    elif which == 1 and dead:                # a merchant sells potions
        c = dead[len(dead) // 2]
        g[c[1]][c[0]] = MERCHANT
        lines.append(f"obj={c[0]},{c[1]},1,2,15,6,FINE POTIONS! A RED POTION FOR ONLY 15 GOLD PIECES.")
        lines.append(f"obj2={c[0]},{c[1]},A PLEASURE DOING BUSINESS. MIND THAT GUARDIAN!")
    elif which == 2:                         # a ghost guards the fountain
        fount = next(((x, y) for y in range(MH) for x in range(MW) if g[y][x] == FOUNT), None)
        if fount:
            n = next(((fount[0] + dx, fount[1] + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                      if g[fount[1] + dy][fount[0] + dx] == FLOOR), None)
            if n:
                g[n[1]][n[0]] = GHOST
                lines.append(f"obj={n[0]},{n[1]},2,4,0,0,THE WATERS BEHIND ME WERE MINE "
                             "ONCE... BUT YOU MAY DRINK. PASS, LIVING ONE, AND BEWARE THE HIDDEN SPIKES.")
    return lines


def add_script(g, start, info):
    """Level 1 side quest, Scott Adams style: light the lantern, search the dark
    corner, move the loose stone. Returns the script lines."""
    door, guard = info["door"], info["guard"]
    free, _ = bfs(g, start, blocked={door, guard})
    def quiet(c):
        return all(g[c[1] + dy][c[0] + dx] in (FLOOR, WALL) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
    dead = sorted((c for c in free if g[c[1]][c[0]] == FLOOR and open_neighbours(g, *c) == 1 and quiet(c)),
                  key=lambda c: free[c])
    lamp, nook = dead[0], dead[-1]          # lantern near the start, the dark corner far away
    L, N = f"{lamp[0]},{lamp[1]}", f"{nook[0]},{nook[1]}"
    return ["script=",
        "# A side quest in the style of the old two-word adventures.",
        "# Light the lantern, find the dark corner, move the loose stone.",
        f'item lamp "A DUSTY LANTERN" at {L} nouns LANTERN,LAMP desc "AN OLD BRASS LANTERN. IT IS NOT LIT, BUT THERE IS OIL IN IT."',
        'item litlamp "A LIT LANTERN" nowhere nouns LANTERN,LAMP desc "THE LANTERN BURNS WITH A STEADY YELLOW FLAME."',
        'item ruby "A GLITTERING RUBY" nowhere nouns RUBY,GEM desc "A RUBY AS BIG AS A PIGEON EGG."',
        "synonym IGNITE = LIGHT",
        "synonym PUSH = MOVE",
        "synonym PULL = MOVE",
        'on LIGHT LAMP if carried lamp and flag noil then say "THERE IS NO OIL LEFT."',
        'on LIGHT LAMP if carried lamp then swap lamp litlamp; counter oil = 40;',
        '  say "THE WICK CATCHES. THE LANTERN GLOWS."',
        'on LIGHT LAMP if present litlamp then say "IT IS ALREADY LIT."',
        'on LIGHT LAMP if here lamp then say "PICK IT UP FIRST."',
        'on LIGHT any then say "YOU HAVE NOTHING TO LIGHT."',
        'on EXTINGUISH LAMP if carried litlamp then swap litlamp lamp; say "YOU PUT THE LANTERN OUT."',
        "synonym DOUSE = EXTINGUISH",
        "every turn if carried litlamp then sub oil 1",
        "every turn if carried litlamp and counter oil < 1 then swap litlamp lamp;",
        '  say "THE LANTERN SPUTTERS AND GOES OUT. THE OIL IS GONE."; set noil',
        f'on arrive if at {N} and not present litlamp then say "THIS CORNER IS PITCH BLACK. YOU FEEL A COLD DRAUGHT."',
        f'on arrive if at {N} and present litlamp and not flag moved then',
        '  say "BY LANTERN LIGHT YOU SEE A LOOSE STONE IN THE WALL."; set spotted',
        f'on MOVE STONE if at {N} and flag spotted and not flag moved then put ruby at {N}; set moved;',
        '  score 50; say "THE STONE SLIDES OUT. SOMETHING GLITTERS IN THE HOLE BEHIND IT!"',
        f'on MOVE STONE if at {N} and not flag spotted then say "IT IS TOO DARK TO SEE ANY STONE."',
        'on MOVE STONE then say "WHAT STONE?"',
        'on EXAMINE STONE if flag spotted then say "IT IS LOOSE IN ITS SETTING."',
        'on RUB LAMP if present lamp then say "NOTHING HAPPENS. THIS IS NOT THAT KIND OF LANTERN."',
        'on RUB LAMP if present litlamp then say "OUCH! IT IS HOT."',
        "endscript"]


def write_default_pack():
    """Write levels/default.zlp, the editable level pack ZorEdit opens."""
    levels = [
        ("THE UPPER VAULTS", 1983, 3,
         "YOU CLIMB DOWN INTO THE DARK VAULTS OF ZOR. SOMEWHERE BELOW LIES "
         "THE AMULET. FIND IT AND RETURN HERE TO THE LADDER.|TYPE HELP FOR COMMANDS."),
        ("THE DEEP VAULTS", 2024, 4,
         "THE LADDER LEADS DEEPER STILL. THE AIR IS COLD AND A SECOND "
         "AMULET GLINTS SOMEWHERE IN THE DARK. A FIERCER GUARDIAN WAITS."),
        ("THE HALL OF TRIALS", 1977, 5,
         "THE LAST VAULT IS FULL OF TRICKS: SIGNS, TRAPS, A FOUNTAIN, "
         "A GLOWING CIRCLE AND WALLS THAT ARE NOT WHAT THEY SEEM. "
         "THE THIRD AMULET LIES BEHIND AN IRON DOOR."),
    ]
    out = ["; Vaults of Zor level pack - edit with ZorEdit.exe or any text editor",
           "; map: # wall  . floor  D door  H ladder (start)  G guardian",
           ";      K key  S sword  P potion  $ gold  A amulet",
           ";      ? sign  ^ trap  @ teleporter  W fountain  = pressure plate  % secret wall  & character",
           "; obj=x,y,a,b,c,d,text  sets the parameters of the cell at x,y (see README)",
           "; obj2=x,y,text         a character's speech once its deed is done",
           "",
           "[pack]",
           "banner1=VAULTS",
           "banner2=OF ZOR",
           "subtitle=A MAZE ADVENTURE FOR THE TRS-80 MODEL I",
           "line1=RECOVER THE AMULETS OF ZOR AND CLIMB BACK OUT",
           "line2=ARROWS: UP=FORWARD  DOWN=BACK  LEFT/RIGHT=TURN",
           "line3=OR TYPE COMMANDS: TAKE, OPEN, ATTACK, LOOK, HELP",
           "victory=YOU CLIMB INTO THE SUNLIGHT WITH THE AMULETS OF ZOR!",
           "starthp=12", "maxhp=15", "heal=6", "hit=70", "guardhit=37",
           "goldmin=5", "goldmax=20",
           "key=A BRASS KEY", "key1=AN IRON KEY", "key2=A SILVER KEY", "key3=A GOLD KEY",
           "sword=A SHORT SWORD", "potion=A RED POTION",
           "gold=SOME GOLD", "amulet=THE AMULET OF ZOR",
           "hermit=AN OLD HERMIT", "merchant=A MERCHANT", "ghost=A GHOST", ""]
    for name, seed, ghp, intro in levels:
        g, start, sdir, info = build_maze(seed)
        objs = add_features(g, start, info, seed) if name == "THE HALL OF TRIALS" else []
        which = len([l for l in out if l == "[level]"])
        objs += add_npcs(g, start, info, which)
        if which == 0:
            objs += add_script(g, start, info)
        out += ["[level]", f"name={name}", f"guardhp={ghp}", "facing=" + "NESW"[sdir],
                f"intro={intro}", "map="]
        out += ["".join(MAPSYM[g[y][x]] for x in range(MW)) for y in range(MH)]
        out += objs
        out.append("")
    os.makedirs("levels", exist_ok=True)
    with open(os.path.join("levels", "default.zlp"), "w", newline="\r\n") as f:
        f.write("\n".join(out))


# ---------------------------------------------------------------- output
def hexb(b):
    s = f"{b:02X}H"
    return "0" + s if s[0].isalpha() else s


def db(data, per=16):
    return ["\tdb\t" + ",".join(hexb(b) for b in data[i:i + per]) for i in range(0, len(data), per)]


def emit_phase(L, p, segs, spr):
    """Append the segment and sprite data for phase p to the output lines."""
    L.append(f"; ---- phase {p}: view {p}/{PHASES} of a step forward.  xL = {XL}")
    L.append(f"SEGL_{p}:\tdw\t" + ",".join(f"S_L{k}{s}_{p}" for k in range(4) for s in range(3)))
    L.append(f"SEGR_{p}:\tdw\t" + ",".join(f"S_R{k}{s}_{p}" for k in range(4) for s in range(3)))
    L.append(f"SEGF_{p}:\tdw\t" + ",".join(f"S_F{d}{t}_{p}" for d in range(1, 5) for t in (0, 1)))
    for name, (c, w, data) in segs.items():
        if data is None:                # blank: width with bit 7 set, no data
            L.append(f"S_{name}_{p}:\tdb\t{c},{w}+80H")
        else:
            L.append(f"S_{name}_{p}:\tdb\t{c},{w}")
            L += db(data, w if w <= 16 else 11)
    names = {LADDER: "LADDER", GUARD: "GUARD", KEY: "KEY", SWORD: "SWORD",
             POTION: "POTION", GOLD: "GOLD", AMULET: "AMULET", SIGN: "SIGN",
             TRAP: "TRAP", TELE: "TELE", FOUNT: "FOUNT", PLATE: "PLATE",
             HERMIT: "HERMIT", MERCHANT: "MERCHANT", GHOST: "GHOST", THING: "THING"}
    L.append(f"SPRTAB_{p}:")
    for code in range(20):
        ptrs = []
        for d in (1, 2, 3):
            n = f"{names[code]}{d}" if code in names else None
            ptrs.append(f"P_{n}_{p}" if n and spr.get(n) else "0")
        L.append("\tdw\t" + ",".join(ptrs))
    for name, data in spr.items():
        if data:
            L.append(f"P_{name}_{p}:")
            L += db(data)
    L.append("")


def main():
    L = ["; ---- generated by gen.py - do not edit ----", "",
         f"NPHASES\tequ\t{PHASES}",
         "; per phase: side tables L/R (depth*3+state), front table, blank centre, sprite table",
         "; segment format: db col, width ; then 16 rows of width bytes",
         ";                 (width + 80H = blank segment, no data follows)",
         "; sprite format:  db col,row,w,h ; then w*h pairs of (mask,bits)",
         "PHASETAB:"]
    for p in range(PHASES):
        L.append(f"\tdw\tSEGL_{p},SEGR_{p},SEGF_{p},S_F5_{p},SPRTAB_{p}")
    L.append("")
    for p in range(PHASES):
        set_phase(p / PHASES)
        emit_phase(L, p, build_segments(), build_sprites())
    set_phase(0)
    segs, spr = build_segments(), build_sprites()
    with open("gfx.inc", "w", newline="\n") as f:
        f.write("\n".join(L) + "\n")
    if "--levels" in sys.argv or not os.path.exists(os.path.join("levels", "default.zlp")):
        write_default_pack()

    if "--preview" in sys.argv:
        g, start, sdir, info = build_maze()
        for p in range(PHASES):         # a step forward down the first corridor
            set_phase(p / PHASES)
            print(f"--- phase {p}  xL={XL}")
            show(compose(build_segments(), build_sprites(), g, 1, 1, 1))


# ---------------------------------------------------------------- preview
def compose(segs, spr, g, x, y, d):
    """Python model of the Z80 renderer, returns 32x16 char screen."""
    DX = [0, 1, 0, -1]; DY = [-1, 0, 1, 0]
    fx, fy = DX[d], DY[d]
    lx, ly = DX[(d + 3) % 4], DY[(d + 3) % 4]

    def cell(dd, s):  # s: +1 left, 0 centre, -1 right
        X, Y = x + fx * dd + lx * s, y + fy * dd + ly * s
        if 0 <= X < MW and 0 <= Y < MH:
            return g[Y][X]
        return WALL
    buf = [[0x80] * CW for _ in range(CH)]

    def blit(name):
        c, w, data = segs[name]
        for r in range(CH):
            for i in range(w):
                buf[r][c + i] = 0x80 if data is None else data[r * w + i]
    front = 5
    for dd in range(1, 5):
        if cell(dd, 0) in SOLID:
            front = dd
            break
    for k in range(min(front, 4)):
        for side, s in (("L", 1), ("R", -1)):
            st = 2 if cell(k, s) in SOLID else (1 if cell(k + 1, s) in SOLID else 0)
            blit(f"{side}{k}{st}")
    if front <= 4:
        blit(f"F{front}{1 if cell(front, 0) == DOOR else 0}")
    else:
        blit("F5")
    for dd in range(1, min(front, 4)):
        code = cell(dd, 0)
        tbl = {LADDER: "LADDER", GUARD: "GUARD"}.get(code)
        name = None
        if tbl:
            name = f"{tbl}{dd}"
        elif code >= KEY:
            name = ["KEY", "SWORD", "POTION", "GOLD", "AMULET"][code - KEY] + str(dd)
        if name or code != FLOOR:
            if name and spr.get(name):
                s = spr[name]
                c0, r0, w, h = s[:4]
                for r in range(h):
                    for i in range(w):
                        m, b = s[4 + 2 * (r * w + i)], s[5 + 2 * (r * w + i)]
                        bg = buf[r0 + r][c0 + i]
                        buf[r0 + r][c0 + i] = 0x80 | (bg & ~m & 0x3F) | b
            break
    return buf


def show(buf):
    for cy in range(CH):
        for py in range(3):
            line = ""
            for cx in range(CW):
                v = buf[cy][cx]
                for px in range(2):
                    line += "#" if (v & 0x80) and v & (1 << (py * 2 + px)) else "."
            print(line)


def print_views(segs, spr, g):
    views = [(1, 1, 1), (1, 1, 2), (5, 5, 0), (9, 7, 1)]
    for x, y, d in views:
        print(f"--- view from {x},{y} dir {d}")
        show(compose(segs, spr, g, x, y, d))
    # sprites on an empty corridor
    for code in (GUARD, KEY, LADDER):
        gg = [[WALL] * MW for _ in range(MH)]
        for xx in range(1, 10):
            gg[5][xx] = FLOOR
        gg[5][2] = code
        print(f"--- sprite test {code}")
        show(compose(segs, spr, gg, 1, 5, 1))


if __name__ == "__main__":
    main()
