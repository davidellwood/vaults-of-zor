# User-defined objects: design proposal

Goal: no object is hardwired any more. Keys, doors, swords, guardians, potions, gold, the
amulet, traps, fountains, plates, teleporters, signs and characters stop being built into
`zor.asm` and ZorEdit. A level designer defines their own objects, gives each one a look and
a set of behaviours chosen from menus, and places them on the map. Behaviour can also be
attached to a single cell or a single placed object with a right-click.

## 1. What the engine keeps

The engine becomes a general first-person adventure engine:

* terrain: **floor** and **wall** only (everything else is an object)
* the 3D view, smooth movement and turning, map with fog of war, text window, speech bubbles
* the player: position, facing, **HP** (and death at 0), **score**
* the parser and the generic verbs: move/turn, `TAKE`/`GET`, `DROP`, `LOOK`/`EXAMINE`, `INV`,
  `TALK`, `SCORE`, `HELP`, `QUIT`
* the rules interpreter (today's script VM), extended as below
* flags (32) and counters (raised from 8 to 16), shared by all levels

Removed from the engine: the fixed handlers for key, door, sword, guardian, potion, gold,
amulet, sign, trap, teleporter, fountain, plate, secret wall and the three characters; the
built-in gold/potion/key/sword/amulet inventory; "amulet + ladder = level complete"; the
eleven pack-wide names; potion strength, hit chances and gold min/max on the Game tab. The
code freed roughly pays for the extensions.

## 2. Objects

An **object type** belongs to the pack (up to 32 types) and is shared by all levels:

| Property | Values |
|---|---|
| Name | shown in messages and inventory, e.g. `A RUSTY KEY` |
| Nouns | words the parser accepts for it, e.g. `KEY` |
| Look | one of the engine's pictures: guardian, hermit, merchant, ghost, key, sword, potion, gold, amulet, chest, sign, trap, teleporter, fountain, plate, ladder, **wall**, **door**, or invisible |
| Map symbol / colour | for ZorEdit and the in-game map |
| Solid | blocks movement (walking into it fires *bumped*) |
| Can be carried | `TAKE` picks it up |
| Starting value | a per-object number (hit points, uses left, price…) |
| Description | `LOOK`/`EXAMINE` text |
| Behaviours | list of rules, see 3 |

A **placed object** (instance) is a type at a cell. Each one may override the description and
carries its own **value** and two numbers **P1, P2** (a target cell, an amount, a key colour…)
that its type's behaviours can use. Up to 64 instances per level.

Looks of `wall` and `door` are drawn as wall faces, not sprites, so a secret wall is simply
"looks like wall, not solid", and a door is "looks like door, solid".

The ladder is no longer special: a level has a **start cell and facing** (set in the editor),
and the exit is whatever object you give a *next level* behaviour to.

## 3. Behaviours

A behaviour is **When → If → Do**, chosen from drop-down menus in ZorEdit. Inside a type's
behaviour, **THIS** means the particular placed object involved.

**When**
* the player **steps onto** it / **walks into** it (solid) / **is next to** it at the end of a turn
* the player types **VERB [NOUN]** while it is here, in front, or carried (any verb, including new ones)
* **every turn** (anywhere, or only while carried)
* the **level starts**
* for a cell rule: the player **arrives at** this cell, or types VERB while standing here / facing it

**If** (all must hold; each can be negated) — today's conditions, plus
* THIS value `= < > ≤ ≥` N
* carrying / here / in front: a **specific object** or **any object of a type**
* number of objects of a type carried ≥ N (keys, coins…)
* counter, flag, chance %, at cell, facing a type, HP ≥ N

**Do** — today's actions, plus
* THIS: remove, move to cell, swap to another type, change value (`= + −`)
* give / take an object (or N of a type) to/from the player
* change a cell to floor/wall, place an object at a cell
* teleport player to cell (P1,P2 or fixed), hurt / heal (random up to N, or exact)
* **next level**, **win game**, die with message, say / speak (speech bubble), score N
* stop / continue to the built-in verb

Rules from a type, from the level and from cells are compiled into the level's rule list, so
the engine has one interpreter. The editor shows the generated text and also lets you edit it
as text (today's script language, extended), for anyone who prefers typing.

## 4. ZorEdit

* **Objects tab:** list of the pack's object types. *New*, *Copy*, *Delete*, and *Add from
  library…*. Selecting one shows its properties and its behaviours; *Add behaviour* opens the
  When/If/Do builder.
* **Palette:** floor, wall, start position, then one button per object type (generated from
  the Objects tab, with its symbol and colour). Paint to place, right button erases.
* **Right-click a cell:** *Place object ▸*, *Edit this object* (value, P1/P2, description),
  *Add behaviour to this object…*, *Add behaviour to this cell…*, *Pick P1/P2 as target*,
  *Remove*. Cells and objects that carry their own behaviour get a small marker; P1/P2 targets
  draw as arrows as today.
* **Library:** a separate file, `library.zlp`, holds ready-made editable types (key + door,
  sword + guardian, potion, gold, amulet + exit ladder, trap, teleporter, fountain, plate, sign,
  secret wall, hermit/merchant/ghost). Nothing in it is built in: importing copies the type into
  your pack, and you can change or delete it. A **new pack starts empty**: no types, a walled
  map and a start cell.
* **Check Pack** can no longer know what your behaviours mean, so it checks what it can: a
  start cell, something with *next level* or *win* on every level reachable from the start
  (solid objects counted passable), rules that compile, targets inside the map, memory size.
* **Maze** generator carves corridors only.

## 5. Files

* Level file format **version 5**: object types table, per-level instance list (x, y, type,
  value, P1, P2, → text), rules with the new trigger kinds and ops. Version 4 files are no
  longer loaded by the engine.
* `.ZLP` gains `[object]` sections and `inst=` lines. ZorEdit **converts old v4 `.ZLP` packs**
  on opening, turning the old built-in cells into library types, so existing work is not lost.
* `levels/default.zlp` is regenerated in the new format (through the converter). It stays as
  the example and the test pack, but the editor no longer opens it on start.

## 6. Order of work

1. Engine: object tables, rendering by *look*, new triggers/conditions/actions, removal of
   the fixed handlers, v5 loader. Status line becomes `L1 HP:12 SC:150` + compass.
2. Pack compiler and `.ZLP` reader/writer for v5, plus the v4 converter.
3. ZorEdit UI: Objects tab, behaviour builder, object palette, right-click menu, library.
4. Convert the default pack and play it through in trs80gp (all four levels, including the
   pyramid) to prove nothing was lost; then test the new editor by building a level from an
   empty pack.

## 7. Level file format version 5 (as implemented)

All pointers are offsets from the pack start; 0 = none.

**Header**

| Offset | Contents |
|---|---|
| 0 | `"ZORPACK"`, 5 |
| 8 | dw total length |
| 10 | db number of levels |
| 11 | db start HP, max HP |
| 13 | db number of object types |
| 14 | db status counter (255 = none), 2 bytes status label (e.g. `$:`) |
| 17 | db spare |
| 18 | dw → 1024-byte title screen |
| 20 | dw → victory text |
| 22 | dw → vocabulary (as v4: type 1 verb / 2 noun, id, word, 0; ends with 0) |
| 24 | dw → object type table |
| 26 | dw → pack rules (type behaviours and pack-wide rules) |
| 28 | dw → pack texts |
| 30 | dw × n → level records |

**Object type** (6 bytes): look, flags, noun id, starting value, name (pack text no.),
description (pack text no.). Flags: bit 0 solid, bit 1 can be carried, bit 2 kept when the
player goes to the next level (if carried). Looks are the engine's picture codes: 0 none
(invisible), 1 wall face, 2 door face, 3 ladder, 4 guardian, 5 key, 6 sword, 7 potion, 8 gold,
9 amulet, 10 sign, 11 trap, 12 teleporter, 13 fountain, 14 plate, 16 hermit, 17 merchant,
18 ghost, 19 chest.

**Level record**: db start x, start y, facing, spare; dw → name; dw → intro; dw → instances;
dw → rules; dw → texts; then 96 bytes of terrain, one bit per cell (bit x&7 of byte
y·4 + x/8; 1 = wall).

**Instances**: db count (≤ 64), then 8 bytes each: x, y, type, value, P1, P2, dw → text.
x = 0, y = 0 is nowhere; x = 0, y = 1 is carried. The text is made of parts separated by
byte 2: part 1 is the description shown by LOOK (empty = the type's), later parts are for
`say THIS n` / `speak THIS n`. Up to 64 instances exist at once; `give` and `create` add new
ones.

**Rules** (a list: db count, then rules): db verb, noun, scope, number of conditions, number
of actions, then 4 bytes (op, a, b, c) per condition and action. Verb 1-31 built in, 32-252
script verbs, 253 level start, 254 walks into, 255 arrives, 0 every turn. Noun 0 = any.
Scope 0 = no particular object; t + 1 = object type t, and the rule is about THIS object of
that type. A condition op with bit 7 set is negated. An item argument of 255 means THIS.
Texts in pack rules and type names come from the pack texts; texts in level rules from the
level's texts (db count, dw offsets, 1-based).

**Which object is THIS.** For a typed command: every instance here, then in front, then
carried (only those whose type has the typed noun, if a noun was typed). If the noun matches
only carried objects (USE KEY), the objects in front come after them whatever their nouns,
so a door's `on USE` rule hears USE KEY. Rules without a
scope see the first of them. Then each candidate in turn tries the rules of its type; the
first rule that acts ends the command, and the built-in verb runs only if none did (or one
said `continue`). Because of this order, a behaviour should say where THIS has to be: the
editor adds `here THIS` to "when the player takes it", `ahead THIS` to "opens / attacks it",
`carried THIS` to "uses / drinks it". Walking into a solid object tries its *walks into*
rules, then its TALK rules. Arriving lists "YOU SEE ..." for the visible objects here
whose type has no arrival rules (those speak for themselves), then tries the level's
arrival rules, then those of every object here. Every turn runs the level's every-turn rules once, then the every-turn rules of
every object on the level or carried. In these automatic triggers *all* rules that hold act,
unless one does `stop`.

**Condition ops** (bit 7 = not): 1 carried I, 2 here I, 3 present I, 4 exists I, 5 at X,Y,
6 flag F, 7 counter C ≥ N, 8 counter C < N, 9 in front I, 10 chance (0-255), 11 has T [N],
12 ahead X,Y, 13 near I (here or next to the player), 14 value I ≥ N, 15 value I < N,
16 hp ≥ N, 17 facing type T, 18 type T here, 19 THIS is type T, 20 carrying a type T whose
P1 = THIS's P1, 21 THIS is object I (used for a behaviour attached to one placed object).
I = object (255 THIS); X = 255 means THIS's P1,P2.

**Action ops**: 1 say text, 2 speak text, 3 set F, 4 clear F, 5 get I, 6 drop I, 7 destroy
I, 8 put I at X,Y, 9 swap places I J, 10 cell X,Y K (0 floor, 1 wall, 2 open: floor and its
solid objects removed), 11 goto X,Y, 12 counter = N, 13 counter + N, 14 counter − N, 15 score
N (16-bit), 16 hurt N (b = 1: random 1..N), 17 heal N (0 = fully), 18 give T [N], 19 take
T [N], 20 win, 21 die text, 22 inventory, 23 look, 24 continue, 25 next level, 26 I becomes
type T, 27 value I = N, 28 value + N, 29 value − N, 30 say part N of I's text, 31 speak part
N of I's text ("<NAME> SAYS:"), 32 create T at X,Y, 33 use up a carried T matching THIS's
P1, 34 stop. In 13, 14, 15, 16 and 17, c = 1 uses THIS's value instead of N, c = 2 THIS's P1.

**Counters are 16-bit** (0-65535, stopping at either end). In conditions 7 and 8 and
action 12, N is b + 256 × c; in 13 and 14 it is b (0-255), THIS's value or THIS's P1.

## Risks

* **Memory.** Rules per object type are shared, but per-level rules for every door/guardian
  cost bytes. The default pack already reaches `FB4BH` of 48K. Type-level rules (written once
  per pack, not per level) should make it smaller, not larger, but this is measured in step 4.
* **Speed.** Running "next to it / every turn" rules for many objects each turn costs Z80 time;
  the engine will only scan instances within one cell for "next to".
* The guardian's fight (hit chance, damage) becomes behaviour, so its balance moves from the
  Game tab into the guardian type's rules.
