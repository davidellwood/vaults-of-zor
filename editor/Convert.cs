// ZorEdit - Convert.cs: opens old (format 4) .ZLP packs and turns them into
// format 5. Every fixed kind of cell of the old engine (key, door, guardian,
// sword, potion, gold, amulet, ladder, sign, trap, teleporter, fountain, plate,
// secret wall, the three characters) becomes an ordinary object type with
// behaviours written in the rule language, so it can be changed or deleted.
// Script items become object types with a named placed object.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZorEdit
{
    public static class V4
    {
        // old cell codes
        const int Floor = 0, Wall = 1, Door = 2, Ladder = 3, Guard = 4, Key = 5, Sword = 6, Potion = 7, Gold = 8, Amulet = 9,
                  Sign = 10, Trap = 11, Teleport = 12, Fountain = 13, Plate = 14, Secret = 15, Npc = 16;
        const string MapChars = ".#DHGKSP$A?^@W=%&";

        class OldObj { public int A, B, C, D; public string Text = "", Text2 = ""; }
        class OldLevel
        {
            public string Name = "NEW LEVEL", Intro = "", Script = "";
            public int GuardHp = 3, Facing = -1;
            public int[,] Map = new int[Level.H, Level.W];
            public Dictionary<int, OldObj> Objs = new Dictionary<int, OldObj>();
            public OldObj Obj(int x, int y) { OldObj o; return Objs.TryGetValue(y * Level.W + x, out o) ? o : new OldObj(); }
        }

        // ------------------------------------------------------------ reading
        public static Pack Load(string[] lines)
        {
            Pack p = new Pack();
            p.Victory = "YOU CLIMB INTO THE SUNLIGHT!";
            Dictionary<string, string> set = new Dictionary<string, string>();
            List<OldLevel> levels = new List<OldLevel>();
            OldLevel cur = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.Equals("[pack]", StringComparison.OrdinalIgnoreCase)) { cur = null; continue; }
                if (line.Equals("[level]", StringComparison.OrdinalIgnoreCase)) { cur = new OldLevel(); levels.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                if (cur == null) { set[k] = v; continue; }
                switch (k)
                {
                    case "name": cur.Name = v; break;
                    case "guardhp": cur.GuardHp = Zlp.Int(v, 3); break;
                    case "facing": cur.Facing = "NESW".IndexOf(v.ToUpperInvariant().FirstOrDefault()); break;
                    case "intro": cur.Intro = v; break;
                    case "map":
                        for (int y = 0; y < Level.H && i + 1 < lines.Length; y++)
                        {
                            string m = lines[++i];
                            for (int x = 0; x < Level.W; x++)
                            {
                                int cc = x < m.Length ? MapChars.IndexOf(char.ToUpperInvariant(m[x])) : 1;
                                if (x < m.Length && m[x] == ' ') cc = 0;
                                cur.Map[y, x] = Level.IsBorder(x, y) ? Wall : cc < 0 ? Floor : cc;
                            }
                        }
                        break;
                    case "obj":
                        {
                            string[] f = v.Split(new[] { ',' }, 7);
                            if (f.Length < 6) break;
                            int key = Zlp.Int(f[1], 0) * Level.W + Zlp.Int(f[0], 0);
                            OldObj o;
                            if (!cur.Objs.TryGetValue(key, out o)) cur.Objs[key] = o = new OldObj();
                            o.A = Zlp.Int(f[2], 0); o.B = Zlp.Int(f[3], 0); o.C = Zlp.Int(f[4], 0); o.D = Zlp.Int(f[5], 0);
                            o.Text = f.Length > 6 ? f[6] : "";
                            break;
                        }
                    case "obj2":
                        {
                            string[] f = v.Split(new[] { ',' }, 3);
                            if (f.Length < 3) break;
                            int key = Zlp.Int(f[1], 0) * Level.W + Zlp.Int(f[0], 0);
                            OldObj o;
                            if (!cur.Objs.TryGetValue(key, out o)) cur.Objs[key] = o = new OldObj();
                            o.Text2 = f[2];
                            break;
                        }
                    case "script":
                        {
                            List<string> sl = new List<string>();
                            while (i + 1 < lines.Length && lines[i + 1].Trim().ToLowerInvariant() != "endscript") sl.Add(lines[++i]);
                            i++;
                            cur.Script = string.Join("\n", sl);
                            break;
                        }
                }
            }
            Func<string, string, string> S = (k, d) => { string v; return set.TryGetValue(k, out v) ? v : d; };
            Func<string, int, int> N = (k, d) => Zlp.Int(S(k, null), d);
            p.Banner1 = S("banner1", p.Banner1); p.Banner2 = S("banner2", p.Banner2); p.Subtitle = S("subtitle", p.Subtitle);
            p.Line1 = S("line1", ""); p.Line2 = S("line2", ""); p.Line3 = S("line3", "");
            p.Victory = S("victory", p.Victory);
            p.StartHp = N("starthp", 12); p.MaxHp = N("maxhp", 15);
            Conv c = new Conv(p)
            {
                Heal = N("heal", 6), Hit = N("hit", 70), GuardHit = N("guardhit", 37),
                GoldMin = N("goldmin", 5), GoldMax = N("goldmax", 20),
            };
            string[] keys = { "key", "key1", "key2", "key3", "sword", "potion", "gold", "amulet", "hermit", "merchant", "ghost" };
            for (int i = 0; i < keys.Length; i++) c.Names[i] = S(keys[i], c.Names[i]);
            if (levels.Count == 0) levels.Add(new OldLevel());
            for (int i = 0; i < levels.Count; i++) p.Levels.Add(c.MakeLevel(levels[i], i));
            c.Finish();
            return p;
        }

        // ------------------------------------------------------------ converting
        class Conv
        {
            readonly Pack p;
            public int Heal, Hit, GuardHit, GoldMin, GoldMax;
            public string[] Names = { "A BRASS KEY", "AN IRON KEY", "A SILVER KEY", "A GOLD KEY",
                                      "A SHORT SWORD", "A RED POTION", "SOME GOLD", "THE AMULET OF ZOR",
                                      "AN OLD HERMIT", "A MERCHANT", "A GHOST" };
            bool usesGold;

            public Conv(Pack pk) { p = pk; }

            // ---- library types (made the first time they are needed)
            string Need(string id)
            {
                if (p.Type(id) != null) return id;
                ObjType t = Make(id);
                p.Types.Add(t);
                foreach (string other in Refs(t.Script)) Need(other);
                return id;
            }

            /// Library ids a script refers to (has/give/take/become/create/useup).
            IEnumerable<string> Refs(string script)
            {
                foreach (Match m in Regex.Matches(script ?? "", @"\b(?:has|give|take|become|create|useup|ahead|carried)\s+(key[0-3]|door[0-3]|sword|potion|amulet|trapv|ladder)\b", RegexOptions.IgnoreCase))
                    yield return m.Groups[1].Value.ToLowerInvariant();
            }

            ObjType T(string id, string name, string look, string nouns, string desc = "", bool solid = false, bool carry = false, bool keep = false, int value = 0, string script = "")
            {
                return new ObjType
                {
                    Id = id, Name = name, Look = ObjType.LookCode(look), Nouns = nouns, Desc = desc,
                    Solid = solid, Carry = carry, Keep = keep, Value = value, Script = script.Replace("\r", "").Trim('\n')
                };
            }

            ObjType Make(string id)
            {
                Match g = Regex.Match(id, @"^guardian(?:_(\d+)_(\d+))?$");
                if (g.Success)
                {
                    int dmg = g.Groups[1].Success ? int.Parse(g.Groups[1].Value) : 3;
                    int ch = g.Groups[2].Success ? int.Parse(g.Groups[2].Value) : GuardHit;
                    return T(id, "THE GUARDIAN", "guardian", "GUARDIAN,MONSTER", "THE HORNED GUARDIAN GLARES AT YOU AND SNORTS.", solid: true, value: 3, script:
                        "# value = its hit points\n" +
                        "on bump then say \"THE GUARDIAN BARS THE WAY!\"\n" +
                        "on ATTACK if ahead this and has sword and chance " + Hit + " then value - 1; say \"YOUR SWORD BITES DEEP!\"\n" +
                        "on ATTACK if ahead this and has sword then say \"THE GUARDIAN DODGES.\"\n" +
                        "on ATTACK if ahead this then say \"YOUR FISTS DO NOTHING! YOU NEED A WEAPON.\"\n" +
                        "every turn if value < 1 then remove this; say \"THE GUARDIAN CRASHES DOWN, DEAD!\"\n" +
                        "every turn if near this and chance " + ch + " then say \"THE GUARDIAN GORES YOU!\"; hurt " + dmg + " random");
                }
                Match k = Regex.Match(id, @"^(key|door)([0-3])$");
                if (k.Success)
                {
                    int c = int.Parse(k.Groups[2].Value);
                    if (k.Groups[1].Value == "key") return T(id, Names[c], "key", "KEY", carry: true);
                    string key = "key" + c, open = "take " + key + "; remove this; say \"THE KEY TURNS. THE DOOR GROANS OPEN.\"";
                    return T(id, "A DOOR", "door", "DOOR", "A STOUT IRON-BOUND DOOR. IT TAKES " + Names[c] + ".", solid: true, script:
                        "on OPEN if ahead this and has " + key + " then " + open + "\n" +
                        "on USE if ahead this and has " + key + " then " + open + "\n" +
                        "on OPEN if ahead this then say \"LOCKED. YOU NEED " + Names[c] + ".\"\n" +
                        "on bump then say \"THE DOOR IS LOCKED.\"");
                }
                switch (id)
                {
                    case "sword": return T(id, Names[4], "sword", "SWORD", carry: true, keep: true);
                    case "potion":
                        return T(id, Names[5], "potion", "POTION", "", carry: true, keep: true, value: Heal, script:
                            "# value = how many HP it heals\n" +
                            "on DRINK if carried this then say \"YOU FEEL STRONGER.\"; heal value; remove this\n" +
                            "on USE if carried this then say \"YOU FEEL STRONGER.\"; heal value; remove this");
                    case "gold":
                        usesGold = true;
                        return T(id, Names[6], "gold", "GOLD,COINS", value: 10, script:
                            "# value = how many gold pieces (counted on the status line, and scored)\n" +
                            "on TAKE if here this then add gold value; score value; say \"YOU GATHER THE GOLD.\"; remove this");
                    case "amulet":
                        return T(id, Names[7], "amulet", "AMULET", carry: true, script:
                            "on TAKE if here this then get this; say \"YOU TAKE " + Names[7] + ".\"; say \"NOW BACK TO THE LADDER!\"");
                    case "ladder":
                        return T(id, "A LADDER", "ladder", "LADDER", "THE LADDER UP TO DAYLIGHT.", script:
                            "# the way out: climb it with the amulet\n" +
                            "on arrive if has amulet then take amulet; score 100; say \"YOU ESCAPE WITH THE AMULET!\"; next; stop\n" +
                            "on arrive then say \"THE LADDER LEADS UP TO DAYLIGHT - BUT NOT WITHOUT THE AMULET!\"");
                    case "sign":
                        return T(id, "A SIGN", "sign", "SIGN", script:
                            "# its description is what the sign says\n" +
                            "on arrive then say \"A SIGN READS:\"; say this 1");
                    case "trap":
                        return T(id, "A TRAP", "none", "TRAP", value: 3, script:
                            "# hidden until it springs; value = damage, text 2 = message\n" +
                            "on arrive then say this 2; hurt value; become trapv");
                    case "trapv":
                        return T(id, "A TRAP", "trap", "TRAP", "THE JAWS OF A TRAP.", value: 3, script:
                            "# value = damage, text 2 = message\n" +
                            "on arrive then say this 2; hurt value");
                    case "trapo":
                        return T(id, "A TRAP", "none", "TRAP", value: 3, script:
                            "# hidden, springs once; value = damage, text 2 = message\n" +
                            "on arrive then say this 2; hurt value; remove this");
                    case "trapvo":
                        return T(id, "A TRAP", "trap", "TRAP", "THE JAWS OF A TRAP.", value: 3, script:
                            "# springs once; value = damage, text 2 = message\n" +
                            "on arrive then say this 2; hurt value; remove this");
                    case "teleporter":
                        return T(id, "A GLOWING CIRCLE", "teleporter", "CIRCLE", "A CIRCLE OF LIGHT ON THE FLOOR.", script:
                            "# P1,P2 = where it sends the player, text 2 = message\n" +
                            "on arrive then say this 2; goto target");
                    case "fountain":
                        return T(id, "A FOUNTAIN", "fountain", "FOUNTAIN,WATER", "A FOUNTAIN BUBBLES HERE.", script:
                            "# value = drinks left (0 = never runs dry), P1 = HP healed (0 = fully), text 2 = message\n" +
                            "on arrive if value >= 2 then say this 2; heal p1; value - 1; stop\n" +
                            "on arrive if value >= 1 then say this 2; heal p1; remove this; say \"THE FOUNTAIN RUNS DRY.\"; stop\n" +
                            "on arrive then say this 2; heal p1");
                    case "plate":
                        return T(id, "A STONE PLATE", "plate", "PLATE", "A STONE PLATE IN THE FLOOR.", script:
                            "# P1,P2 = the cell it opens, text 2 = message\n" +
                            "on arrive if value < 1 then value = 1; cell target open; say this 2");
                    case "secret":
                        return T(id, "A WALL", "wall", "WALL", "A WALL OF COLD, DAMP STONE.", script:
                            "# looks like a wall, but isn't; text 2 = message\n" +
                            "on arrive then say this 2; remove this");
                    case "hermit": return T(id, Names[8], "hermit", "HERMIT", solid: true, script: "on TALK then speak this 2");
                    case "merchant": return T(id, Names[9], "merchant", "MERCHANT,TRADER", solid: true, script: "on TALK then speak this 2");
                    case "ghost": return T(id, Names[10], "ghost", "GHOST,SPIRIT", solid: true, script: "on TALK then speak this 2");
                }
                throw new Exception("no library object '" + id + "'");
            }

            // ---- one level
            public Level MakeLevel(OldLevel o, int index)
            {
                Level l = new Level { Name = o.Name, Intro = o.Intro };
                for (int y = 0; y < ZorEdit.Level.H; y++)
                    for (int x = 0; x < ZorEdit.Level.W; x++)
                        l.Wall[y, x] = o.Map[y, x] == Wall;
                bool found = false;
                for (int y = 0; y < ZorEdit.Level.H && !found; y++)
                    for (int x = 0; x < ZorEdit.Level.W && !found; x++)
                        if (o.Map[y, x] == Ladder) { l.StartX = x; l.StartY = y; found = true; }
                l.Facing = o.Facing;
                if (l.Facing < 0)
                {
                    l.Facing = 1;
                    foreach (int d in new[] { 1, 2, 3, 0 })
                    {
                        int c = o.Map[l.StartY + Util.DY[d], l.StartX + Util.DX[d]];
                        if (c != Wall && c != Door && c != Secret) { l.Facing = d; break; }
                    }
                }
                Random rng = new Random(1000 + index);
                for (int y = 0; y < ZorEdit.Level.H; y++)
                    for (int x = 0; x < ZorEdit.Level.W; x++)
                    {
                        int c = o.Map[y, x];
                        if (c == Floor || c == Wall) continue;
                        OldObj q = o.Obj(x, y);
                        Inst i = new Inst { X = x, Y = y };
                        switch (c)
                        {
                            case Ladder: i.Type = Need("ladder"); break;
                            case Door: i.Type = Need("door" + (q.A & 3)); break;
                            case Key: i.Type = Need("key" + (q.A & 3)); break;
                            case Sword: i.Type = Need("sword"); break;
                            case Amulet: i.Type = Need("amulet"); break;
                            case Potion:
                                i.Type = Need("potion");
                                if (q.A > 0 && q.A != Heal) i.Value = q.A;
                                break;
                            case Gold:
                                i.Type = Need("gold");
                                i.Value = q.A > 0 ? q.A : rng.Next(GoldMin, Math.Max(GoldMin, GoldMax) + 1);
                                break;
                            case Guard:
                                {
                                    int dmg = q.B > 0 ? q.B : 3, ch = q.C > 0 ? q.C : GuardHit;
                                    i.Type = Need(dmg == 3 && ch == GuardHit ? "guardian" : "guardian_" + dmg + "_" + ch);
                                    Need("sword");
                                    i.Value = q.A > 0 ? q.A : Math.Max(1, o.GuardHp);
                                    if (!Util.IsBlank(q.Text)) i.SetText(1, q.Text);
                                    break;
                                }
                            case Sign:
                                i.Type = Need("sign");
                                i.SetText(1, Util.IsBlank(q.Text) ? "THE WORDS ARE WORN AWAY." : q.Text);
                                break;
                            case Trap:
                                i.Type = Need((q.B != 0 ? "trapv" : "trap") + (q.C != 0 ? "o" : ""));
                                i.Value = q.A > 0 ? q.A : 3;
                                i.SetText(2, Util.IsBlank(q.Text) ? "CLICK! A TRAP SPRINGS!" : q.Text);
                                break;
                            case Teleport:
                                i.Type = Need("teleporter");
                                i.P1 = q.A; i.P2 = q.B;
                                i.SetText(2, Util.IsBlank(q.Text) ? "THE FLOOR GLOWS AND THE VAULT SPINS AROUND YOU..." : q.Text);
                                break;
                            case Fountain:
                                i.Type = Need("fountain");
                                i.Value = q.B; i.P1 = q.A;
                                i.SetText(2, Util.IsBlank(q.Text) ? "YOU DRINK FROM THE FOUNTAIN." : q.Text);
                                break;
                            case Plate:
                                i.Type = Need("plate");
                                i.P1 = q.A; i.P2 = q.B;
                                i.SetText(2, Util.IsBlank(q.Text) ? "CLICK! STONE GRINDS NEARBY." : q.Text);
                                break;
                            case Secret:
                                i.Type = Need("secret");
                                i.SetText(2, Util.IsBlank(q.Text) ? "THE WALL WAS AN ILLUSION!" : q.Text);
                                break;
                            case Npc: Character(i, q); break;
                            default: continue;
                        }
                        l.Objs.Add(i);
                    }
                l.Script = Script(o.Script, l, index);
                return l;
            }

            /// A character: its look is the type, what it does is its own script.
            void Character(Inst i, OldObj q)
            {
                i.Type = Need(new[] { "hermit", "merchant", "ghost" }[Math.Min(2, Math.Max(0, q.A))]);
                string first = Util.IsBlank(q.Text) ? "HELLO, TRAVELLER." : q.Text;
                i.SetText(2, first);
                i.SetText(3, Util.IsBlank(q.Text2) ? first : q.Text2);
                int item = q.D & 0x7F, amount = q.C;
                string give = "", got = "";
                if (item >= 1 && item <= 4) { give = "give " + Need("key" + (item - 1)); got = Names[item - 1]; }
                else if (item == 5) { give = "give " + Need("sword"); got = Names[4]; }
                else if (item == 6) { give = "give " + Need("potion"); got = Names[5]; }
                else if (item == 7) { usesGold = true; give = "add gold " + amount; got = Names[6]; }
                else if (item == 8) { give = "give " + Need("amulet"); got = Names[7]; }
                string recv = give.Length == 0 ? "" : "; " + give + "; say \"YOU RECEIVE " + got + ".\"" + (item == 8 ? "; say \"NOW BACK TO THE LADDER!\"" : "");
                switch (q.B)
                {
                    case 1:
                        if (give.Length == 0) return;
                        i.Script = "# gives " + got + " once\n" +
                                   "on TALK if value < 1 then speak this 2; value = 1" + recv + "\n" +
                                   "on TALK then speak this 3";
                        break;
                    case 2:
                        usesGold = true;
                        if (give.Length == 0) return;
                        i.Script = "# sells " + got + " for " + amount + " gold\n" +
                                   "on TALK if value < 1 and counter gold >= " + amount + " then speak this 2; sub gold " + amount +
                                   "; say \"YOU PAY " + amount + " GOLD.\"; value = 1" + recv + "\n" +
                                   "on TALK if value < 1 then speak this 2; say \"YOU NEED " + amount + " GOLD.\"\n" +
                                   "on TALK then speak this 3";
                        break;
                    case 3:
                        i.Script = "# heals the player once\n" +
                                   "on TALK if value < 1 then speak this 2; value = 1; heal " + amount + "\n" +
                                   "on TALK then speak this 3";
                        break;
                    case 4:
                        i.Script = "# steps aside after talking\n" +
                                   "on TALK then speak this 2; remove this; say \"AND WITH THAT, THEY ARE GONE.\"";
                        break;
                }
            }

            // ---- a level script: items become objects, old words are rewritten
            static readonly string[] CellWords = { "FLOOR", "WALL", "DOOR", "LADDER", "GUARDIAN", "KEY", "SWORD", "POTION", "GOLD",
                "AMULET", "SIGN", "TRAP", "TELEPORTER", "FOUNTAIN", "PLATE", "SECRET", "HERMIT", "MERCHANT", "GHOST" };

            string Script(string script, Level l, int index)
            {
                List<string> outl = new List<string>();
                string[] lines = (script ?? "").Replace("\r", "").Split('\n');
                for (int n = 0; n < lines.Length; n++)
                {
                    // gather one statement (a line and the lines that continue it)
                    int start = n;
                    while (n + 1 < lines.Length && lines[n + 1].Length > 0 && (lines[n + 1][0] == ' ' || lines[n + 1][0] == '\t')) n++;
                    string stmt = string.Join("\n", lines, start, n - start + 1);
                    if (Regex.IsMatch(stmt, @"^\s*item\s", RegexOptions.IgnoreCase) && Item(stmt.Replace("\n", " "), l)) continue;
                    outl.Add(Rewrite(stmt));
                }
                while (outl.Count > 0 && outl[outl.Count - 1].Trim().Length == 0) outl.RemoveAt(outl.Count - 1);
                return string.Join("\n", outl);
            }

            /// item NAME "TITLE" at X,Y | carried | nowhere [fixed] [nouns A,B] [desc "TEXT"]
            bool Item(string stmt, Level l)
            {
                List<string> t = ScriptCompiler.Tokens(stmt);
                if (t.Count < 3 || !t[2].StartsWith("\"")) return false;
                string name = t[1];
                ObjType ty = new ObjType { Id = UniqueId(name.ToLowerInvariant()), Name = Util.Fit(t[2].Substring(1), 24), Look = 19, Carry = true };
                Inst i = new Inst { Type = ty.Id, Name = name };
                List<string> nouns = new List<string>();
                for (int k = 3; k < t.Count; k++)
                {
                    string w = t[k].ToUpperInvariant();
                    if (w == "AT" && k + 3 < t.Count) { i.X = Zlp.Int(t[k + 1], 0); i.Y = Zlp.Int(t[k + 3], 0); k += 3; }
                    else if (w == "CARRIED") { i.X = 0; i.Y = 1; }
                    else if (w == "NOWHERE") { i.X = 0; i.Y = 0; }
                    else if (w == "FIXED") ty.Carry = false;
                    else if (w == "NOUNS" || w == "NOUN")
                    {
                        while (k + 1 < t.Count) { nouns.Add(t[++k].ToUpperInvariant()); if (k + 1 < t.Count && t[k + 1] == ",") k++; else break; }
                    }
                    else if (w == "DESC" && k + 1 < t.Count) ty.Desc = t[++k].TrimStart('"');
                }
                ty.Nouns = string.Join(",", nouns.Count > 0 ? nouns : new List<string> { name.ToUpperInvariant() });
                p.Types.Add(ty);
                l.Objs.Add(i);
                return true;
            }

            string UniqueId(string id)
            {
                string r = id;
                for (int k = 2; p.Type(r) != null; k++) r = id + k;
                return r;
            }

            string Rewrite(string s)
            {
                const RegexOptions I = RegexOptions.IgnoreCase;
                if (Regex.IsMatch(s, @"\bgold\b", I)) usesGold = true;
                s = Regex.Replace(s, @"\bhas\s+gold\s+(\d+)", "counter gold >= $1", I);
                s = Regex.Replace(s, @"(?<!\bon\s+)\bgive\s+gold\s+(\d+)", "add gold $1", I);
                s = Regex.Replace(s, @"(?<!\bon\s+)\btake\s+gold\s+(\d+)", "sub gold $1", I);
                s = Regex.Replace(s, @"(?<!\bon\s+)\b(has|give|take)\s+key\b(?![0-9])", "$1 key0", I);
                s = Regex.Replace(s, @"(?<!\bon\s+)\b(has|give|take)\s+(key[0-3]|sword|potion|amulet)\b", m => m.Groups[1].Value + " " + Need(m.Groups[2].Value.ToLowerInvariant()), I);
                s = Regex.Replace(s, @"\bcell\s+(\d+)\s*,\s*(\d+)\s+([a-z]+)\b", m =>
                {
                    string w = m.Groups[3].Value.ToUpperInvariant(), xy = m.Groups[1].Value + "," + m.Groups[2].Value;
                    if (w == "FLOOR") return "cell " + xy + " open";
                    if (w == "WALL") return "cell " + xy + " wall";
                    string id = CellType(w);
                    return id == null ? m.Value : "create " + id + " at " + xy;
                }, I);
                s = Regex.Replace(s, @"\bfacing\s+([a-z]+)\b", m =>
                {
                    string id = CellType(m.Groups[1].Value.ToUpperInvariant());
                    return id == null ? m.Value : "facing " + id;
                }, I);
                return s;
            }

            string CellType(string w)
            {
                switch (w)
                {
                    case "DOOR": return Need("door0");
                    case "LADDER": return Need("ladder");
                    case "GUARDIAN": Need("sword"); return Need("guardian");
                    case "KEY": return Need("key0");
                    case "SWORD": return Need("sword");
                    case "POTION": return Need("potion");
                    case "GOLD": return Need("gold");
                    case "AMULET": return Need("amulet");
                    case "SIGN": return Need("sign");
                    case "TRAP": return Need("trapv");
                    case "TELEPORTER": return Need("teleporter");
                    case "FOUNTAIN": return Need("fountain");
                    case "PLATE": return Need("plate");
                    case "SECRET": return Need("secret");
                    case "HERMIT": case "MERCHANT": case "GHOST": return Need(w.ToLowerInvariant());
                }
                return null;
            }

            /// Pack-wide words of the old engine, and the gold counter.
            public void Finish()
            {
                List<string> s = new List<string> { "# words the old engine knew" };
                if (p.Type("door0") != null || p.Type("door1") != null || p.Type("door2") != null || p.Type("door3") != null)
                    s.Add("synonym UNLOCK = OPEN\nsynonym O = OPEN");
                if (p.Types.Any(t => t.Id.StartsWith("guardian"))) s.Add("synonym A = ATTACK\nsynonym KILL = ATTACK\nsynonym FIGHT = ATTACK\nsynonym HIT = ATTACK");
                if (p.Type("potion") != null) s.Add("synonym D = DRINK\nsynonym U = USE");
                p.Script = s.Count > 1 ? string.Join("\n", s) : "";
                if (usesGold || p.Type("gold") != null) { p.StatusCounter = "gold"; p.StatusLabel = "$:"; }
            }
        }
    }
}
