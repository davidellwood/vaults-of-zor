"""Build a hand-made level pack in format 5 and put it into the engine (test helper).

usage: python v5pack.py ENGINE.CMD OUT.CMD [OUT.LEV]

Level 1 is one corridor (row 1, start at 1,1 facing east) with an object of
every kind in a row, so a test can walk east and meet them one after another:
  2 sign   3 key   4 gold (25)   5 door   6 hermit (gives a potion, steps aside)
  7 hidden trap   8 sword   9 guardian (2 hp)   10 fountain (2 uses)
  11 plate (opens 13,1)   12 floor   13 wall (opened by the plate)
  14 secret wall   15 teleporter -> 18,3, a room with the amulet at 19,3 and
  the ladder at 20,3.
Level 2: a ladder at 3,1 that wins the game if the sword came along.
"""
import sys

# ---------------------------------------------------------------- encoding
def wrap(text, width):
    lines = []
    for para in text.upper().split("|"):
        line = ""
        for w in para.split():
            if not line:
                line = w
            elif len(line) + 1 + len(w) <= width:
                line += " " + w
            else:
                lines.append(line); line = w
        lines.append(line)
    while lines and not lines[-1]:
        lines.pop()
    return lines

def txt(text, width=30):
    return bytes("\r".join(wrap(text, width)), "ascii")

def speech(text):
    out = []
    cur = ""
    sents = []
    for w in text.upper().split():
        cur = w if not cur else cur + " " + w
        if w[-1] in ".!?:;":
            sents.append(cur); cur = ""
    if cur:
        sents.append(cur)
    return b"\x01".join(bytes("\r".join(wrap(s, 28)[:4]), "ascii") for s in sents)

class Texts:
    def __init__(self):
        self.t = []
    def add(self, b):
        if b in self.t:
            return self.t.index(b) + 1
        self.t.append(b)
        return len(self.t)

THIS = 0xFF
# conditions
CARRIED, HERE, PRESENT, EXISTS, AT, FLAG, CNTGE, CNTLT, IAHEAD, CHANCE, HAS, AHEAD, NEAR, \
    VALGE, VALLT, HPGE, FACING, TYPEHERE, IS, MATCH, SELF = range(1, 22)
# actions
SAY, SPEAK, SET, CLEAR, GET, DROP, DESTROY, PUT, SWAP, CELL, GOTO, CSET, CADD, CSUB, SCORE, \
    HURT, HEAL, GIVE, TAKE, WIN, DIE, INV, LOOK, CONT, NEXT, BECOME, SETV, ADDV, SUBV, SAYIT, \
    SPEAKIT, CREATE, USEM, STOP = range(1, 35)
EVERY, START, BUMP, ARRIVE = 0, 253, 254, 255
V_TAKE, V_LOOK, V_TALK = 7, 12, 17

def c(op, a=0, b=0, cc=0, neg=False):
    return bytes([op | (0x80 if neg else 0), a, b, cc])
a = c

def rule(verb, noun, scope, conds, acts):
    return bytes([verb, noun, 0 if scope is None else scope + 1, len(conds), len(acts)]) + \
        b"".join(conds) + b"".join(acts)

def pct(p):
    return min(255, (p * 256 + 50) // 100)

# ---------------------------------------------------------------- the pack
verbs = {"OPEN": 32, "UNLOCK": 32, "O": 32, "ATTACK": 33, "KILL": 33, "FIGHT": 33, "A": 33,
         "DRINK": 34, "D": 34}
nouns = {}
def noun(*words):
    nid = len(set(nouns.values())) + 1
    for w in words:
        nouns[w] = nid
    return nid

pt = Texts()
types = []          # (look, flags, noun, value, name, desc)
def T(name, look, flags, n, value=0, desc=""):
    types.append((look, flags, n, value, pt.add(txt(name, 24)), pt.add(txt(desc)) if desc else 0))
    return len(types) - 1
SOLID, CARRY, KEEP = 1, 2, 4

SIGN = T("A SIGN", 10, 0, noun("SIGN"))
KEY = T("A BRASS KEY", 5, CARRY, noun("KEY"))
GOLD = T("SOME GOLD", 8, CARRY, noun("GOLD", "COINS"), 25)
DOOR = T("A DOOR", 2, SOLID, noun("DOOR"), 0, "A STOUT IRON-BOUND DOOR.")
HERMIT = T("AN OLD HERMIT", 16, SOLID, noun("HERMIT", "MAN"))
POTION = T("A RED POTION", 7, CARRY, noun("POTION"), 6)
HTRAP = T("A HIDDEN TRAP", 0, 0, noun("TRAP"), 2)
VTRAP = T("A SPRUNG TRAP", 11, 0, nouns["TRAP"], 0, "THE JAWS OF A SPRUNG TRAP.")
SWORD = T("A SHORT SWORD", 6, CARRY | KEEP, noun("SWORD"))
GUARD = T("THE GUARDIAN", 4, SOLID, noun("GUARDIAN", "MONSTER"), 2, "THE HORNED GUARDIAN GLARES.")
FOUNT = T("A FOUNTAIN", 13, 0, noun("FOUNTAIN"), 2)
PLATE = T("A STONE PLATE", 14, 0, noun("PLATE"))
SECRET = T("A WALL", 1, 0, noun("WALL"), 0, "A WALL OF COLD, DAMP STONE.")
TELE = T("A GLOWING CIRCLE", 12, 0, noun("CIRCLE"))
AMULET = T("THE AMULET OF ZOR", 9, CARRY, noun("AMULET"))
LADDER = T("A LADDER", 3, 0, noun("LADDER"), 0, "THE LADDER UP TO DAYLIGHT.")

P = lambda s: pt.add(txt(s))
S = lambda s: pt.add(speech(s))
prules = [
    # gold: counted on the status line (counter 0) and scored
    rule(V_TAKE, 0, GOLD, [c(HERE, THIS)], [a(CADD, 0, 0, 1), a(SCORE, 0, 0, 1), a(SAY, P("YOU GATHER THE GOLD.")), a(DESTROY, THIS)]),
    # door: a carried key with the same P1 opens it and is used up
    rule(32, 0, DOOR, [c(MATCH, KEY)], [a(USEM, KEY), a(DESTROY, THIS), a(SAY, P("THE KEY TURNS. THE DOOR GROANS OPEN."))]),
    rule(32, 0, DOOR, [], [a(SAY, P("LOCKED. YOU NEED THE RIGHT KEY."))]),
    rule(BUMP, 0, DOOR, [], [a(SAY, P("THE DOOR IS LOCKED."))]),
    # hermit: talking (or walking into him) gives a potion once, then he steps aside
    rule(V_TALK, 0, HERMIT, [c(VALLT, THIS, 1)], [a(SPEAKIT, THIS, 2), a(GIVE, POTION), a(SAY, P("YOU RECEIVE A RED POTION.")), a(SETV, THIS, 1)]),
    rule(V_TALK, 0, HERMIT, [], [a(SPEAKIT, THIS, 3), a(DESTROY, THIS), a(SAY, P("AND WITH THAT, HE IS GONE."))]),
    # potion: DRINK heals by its value
    rule(34, 0, POTION, [c(CARRIED, THIS)], [a(HEAL, 0, 0, 1), a(DESTROY, THIS)]),
    # hidden trap: hurts by its value once, then shows
    rule(ARRIVE, 0, HTRAP, [], [a(SAY, P("CLICK! A TRAP SPRINGS!")), a(HURT, 0, 0, 1), a(BECOME, THIS, VTRAP)]),
    # guardian: blocks, fights back, dies at value 0
    rule(BUMP, 0, GUARD, [], [a(SAY, P("THE GUARDIAN BARS THE WAY!"))]),
    rule(33, 0, GUARD, [c(HAS, SWORD), c(CHANCE, pct(100))], [a(SUBV, THIS, 1), a(SAY, P("YOUR SWORD BITES DEEP!"))]),
    rule(33, 0, GUARD, [], [a(SAY, P("YOUR FISTS DO NOTHING!"))]),
    rule(EVERY, 0, GUARD, [c(VALLT, THIS, 1)], [a(DESTROY, THIS), a(SAY, P("THE GUARDIAN CRASHES DOWN, DEAD!"))]),
    rule(EVERY, 0, GUARD, [c(NEAR, THIS), c(VALGE, THIS, 1), c(CHANCE, pct(50))], [a(SAY, P("THE GUARDIAN GORES YOU!")), a(HURT, 2, 1)]),
    # fountain: heals fully, value = uses left
    rule(ARRIVE, 0, FOUNT, [c(VALGE, THIS, 2)], [a(SAY, P("YOU DRINK FROM THE FOUNTAIN.")), a(HEAL, 0), a(SUBV, THIS, 1), a(STOP)]),
    rule(ARRIVE, 0, FOUNT, [], [a(SAY, P("YOU DRINK FROM THE FOUNTAIN.")), a(HEAL, 0), a(DESTROY, THIS), a(SAY, P("THE FOUNTAIN RUNS DRY."))]),
    # plate: opens the cell P1,P2 the first time
    rule(ARRIVE, 0, PLATE, [c(VALLT, THIS, 1)], [a(CELL, THIS, 0, 2), a(SETV, THIS, 1), a(SAY, P("CLICK! STONE GRINDS NEARBY."))]),
    # secret wall
    rule(ARRIVE, 0, SECRET, [], [a(SAY, P("THE WALL WAS AN ILLUSION!")), a(DESTROY, THIS)]),
    # teleporter to P1,P2
    rule(ARRIVE, 0, TELE, [], [a(SAY, P("THE VAULT SPINS AROUND YOU...")), a(GOTO, THIS)]),
    # ladder: out with the amulet
    rule(ARRIVE, 0, LADDER, [c(HAS, AMULET)], [a(SCORE, 100), a(NEXT)]),
    rule(ARRIVE, 0, LADDER, [c(HAS, SWORD)], [a(WIN)]),
    rule(ARRIVE, 0, LADDER, [], [a(SAY, P("THE LADDER LEADS UP TO DAYLIGHT.")), a(LOOK)]),
]

def parts(*ps, speech_from=2):
    out = []
    for i, p in enumerate(ps, 1):
        out.append(speech(p) if i >= speech_from else txt(p))
    return b"\x02".join(out)

def walls(open_cells):
    g = [[1] * 32 for _ in range(24)]
    for (x, y) in open_cells:
        g[y][x] = 0
    return g

lev1_open = [(x, 1) for x in range(1, 16) if x != 13] + [(x, 3) for x in range(17, 21)]
lev1 = dict(
    start=(1, 1, 1), name="THE TEST CORRIDOR", intro="WALK EAST. EVERY KIND OF OBJECT IS ON THE WAY.",
    terrain=walls(lev1_open),
    inst=[  # x, y, type, value, p1, p2, text (bytes or None)
        (2, 1, SIGN, 0, 0, 0, txt("THE SIGN READS: TAKE THE KEY AND OPEN THE DOOR.")),
        (3, 1, KEY, 0, 0, 0, None),
        (4, 1, GOLD, 25, 0, 0, None),
        (5, 1, DOOR, 0, 0, 0, None),
        (6, 1, HERMIT, 0, 0, 0, parts("A HERMIT IN RAGS.", "TAKE THIS POTION, TRAVELLER. YOU WILL NEED IT.", "GOOD LUCK. I MUST GO.")),
        (7, 1, HTRAP, 2, 0, 0, None),
        (8, 1, SWORD, 0, 0, 0, None),
        (9, 1, GUARD, 2, 0, 0, None),
        (10, 1, FOUNT, 2, 0, 0, None),
        (11, 1, PLATE, 0, 13, 1, None),
        (14, 1, SECRET, 0, 0, 0, None),
        (15, 1, TELE, 0, 18, 3, None),
        (19, 3, AMULET, 0, 0, 0, None),
        (20, 3, LADDER, 0, 0, 0, None),
    ],
    rules=[], texts=[])
lt = Texts()
lev1["rules"] = [rule(ARRIVE, 0, None, [c(AT, 12, 1)], [a(SAY, lt.add(txt("A LEVEL RULE: YOU ARE AT 12,1.")))])]
lev1["texts"] = lt.t

lev2 = dict(
    start=(1, 1, 1), name="THE WAY OUT", intro="THE SWORD CAME WITH YOU.",
    terrain=walls([(1, 1), (2, 1), (3, 1)]),
    inst=[(3, 1, LADDER, 0, 0, 0, None)], rules=[], texts=[])

def title():
    scr = bytearray(b" " * 1024)
    for row, s in ((4, "VAULTS OF ZOR - FORMAT 5 TEST"), (15, "PRESS ENTER TO BEGIN")):
        c0 = (64 - len(s)) // 2
        scr[row * 64 + c0: row * 64 + c0 + len(s)] = s.encode()
    return bytes(scr)

def build():
    levels = [lev1, lev2]
    b = bytearray(b"ZORPACK\x05")
    b += b"\0\0" + bytes([len(levels), 12, 15, len(types), 0, ord("$"), ord(":"), 0])
    assert len(b) == 18
    hdr = len(b)
    b += bytes(12 + 2 * len(levels))
    def patch(at, v):
        b[at] = v & 255; b[at + 1] = v >> 8
    def table(texts):
        at = len(b)
        b.append(len(texts))
        b.extend(bytes(2 * len(texts)))
        for i, t in enumerate(texts):
            patch(at + 1 + 2 * i, len(b)); b.extend(t + b"\0")
        return at
    patch(18, len(b)); b += title()
    patch(20, len(b)); b += txt("YOU CLIMB OUT WITH THE AMULET!") + b"\0"
    patch(22, len(b))
    for w, i in verbs.items():
        b += bytes([1, i]) + w.encode() + b"\0"
    for w, i in nouns.items():
        b += bytes([2, i]) + w.encode() + b"\0"
    b.append(0)
    patch(24, len(b))
    for t in types:
        b += bytes(t)
    patch(26, len(b)); b.append(len(prules))
    for r in prules:
        b += r
    patch(28, table(pt.t))
    for li, lv in enumerate(levels):
        patch(30 + 2 * li, len(b))
        at = len(b)
        x, y, d = lv["start"]
        b += bytes([x, y, d, 0]) + bytes(10)
        for yy in range(24):            # the terrain follows the 14-byte header
            for xb in range(4):
                v = 0
                for k in range(8):
                    if lv["terrain"][yy][xb * 8 + k]:
                        v |= 1 << k
                b.append(v)
        patch(at + 4, len(b)); b += txt(lv["name"], 20) + b"\0"
        patch(at + 6, len(b)); b += txt(lv["intro"]) + b"\0"
        patch(at + 8, len(b)); b.append(len(lv["inst"]))
        tpos = []
        for (ix, iy, t, v, p1, p2, text) in lv["inst"]:
            b += bytes([ix, iy, t, v, p1, p2]); tpos.append((len(b), text)); b += b"\0\0"
        for pos, text in tpos:
            if text:
                patch(pos, len(b)); b += text + b"\0"
        if lv["rules"]:
            patch(at + 10, len(b)); b.append(len(lv["rules"]))
            for r in lv["rules"]:
                b += r
        if lv["texts"]:
            patch(at + 12, table(lv["texts"]))
    patch(8, len(b))
    return bytes(b)

# ---------------------------------------------------------------- /CMD
def read_cmd(f):
    mem = bytearray(65536); lo, hi, entry = 65536, -1, None
    i = 0
    while i + 1 < len(f):
        t, n = f[i], f[i + 1]
        if t == 1:
            L = n + 256 if n < 3 else n
            ad = f[i + 2] | f[i + 3] << 8
            mem[ad:ad + L - 2] = f[i + 4:i + 2 + L]
            lo, hi = min(lo, ad), max(hi, ad + L - 3)
            i += 2 + L
        elif t == 2:
            entry = f[i + 2] | f[i + 3] << 8; break
        else:
            i += 2 + n
    return mem, lo, hi, entry

def write_cmd(mem, lo, hi, entry):
    o = bytearray()
    for ad in range(lo, hi + 1, 256):
        n = min(256, hi - ad + 1)
        o += bytes([1, (n + 2) & 255, ad & 255, ad >> 8]) + mem[ad:ad + n]
    return bytes(o + bytes([2, 2, entry & 255, entry >> 8]))

if __name__ == "__main__":
    pack = build()
    mem, lo, hi, entry = read_cmd(open(sys.argv[1], "rb").read())
    at = bytes(mem).rfind(b"ZORPACK\x05", 0, hi + 1)
    assert at > 0, "engine has no format 5 pack marker"
    mem[at:] = bytes(65536 - at)
    mem[at:at + len(pack)] = pack
    hi = at + len(pack) - 1
    assert hi < 0xFF00, "pack too big"
    open(sys.argv[2], "wb").write(write_cmd(mem, lo, hi, entry))
    if len(sys.argv) > 3:
        open(sys.argv[3], "wb").write(pack)
    print(f"pack {len(pack)} bytes at {at:04X}H-{hi:04X}H, {len(types)} types, {len(prules)} pack rules")
