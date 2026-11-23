// ZorEdit - level editor for VAULTS OF ZOR (TRS-80 Model I, TRSDOS)
// Pack.cs: level pack model, .ZLP text format, binary pack compiler,
//          /CMD reader/writer, validation and maze generator.
// Builds with the .NET Framework 4 compiler (C# 5).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZorEdit
{
    public enum Cell : byte
    {
        Floor = 0, Wall, Door, Ladder, Guard, Key, Sword, Potion, Gold, Amulet,
        Sign, Trap, Teleport, Fountain, Plate, Secret,         // spaces with game logic
        Npc                                                    // a character (game codes 16-18 by look)
    }

    /// Parameters of one cell. Their meaning depends on the cell type (see ParamSpec).
    public class ObjParams
    {
        public int A, B, C, D;
        public string Text = "", Text2 = "";
        public ObjParams Clone() { return (ObjParams)MemberwiseClone(); }
        public bool IsDefault { get { return A == 0 && B == 0 && C == 0 && D == 0 && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(Text2); } }
    }

    /// Describes the parameters a cell type has, for the editor UI, files and compiler.
    public class ParamSpec
    {
        public string[] Labels = new string[4];   // null = field not used
        public int[] Max = new int[4];
        public string TextLabel;                  // null = no text
        public string Text2Label;                 // null = no second text
        public bool Target;                       // A,B are a target cell
        public bool Percent;                      // C is a percentage (stored as 0-255)

        public static readonly string[] NpcLooks = { "hermit", "merchant", "ghost" };
        public static readonly string[] NpcActions = { "just talks", "gives an item", "sells an item for gold", "heals the player", "steps aside after talking" };
        public static readonly string[] NpcItems = { "nothing", "key colour 0", "key colour 1", "key colour 2", "key colour 3", "sword", "potion", "gold", "amulet" };

        static readonly Dictionary<Cell, ParamSpec> specs = new Dictionary<Cell, ParamSpec>();
        static void Add(Cell c, string a, int ma, string b, int mb, string cc, int mc, string text, bool target = false, bool pct = false)
        {
            ParamSpec s = new ParamSpec { TextLabel = text, Target = target, Percent = pct };
            s.Labels[0] = a; s.Max[0] = ma; s.Labels[1] = b; s.Max[1] = mb; s.Labels[2] = cc; s.Max[2] = mc;
            specs[c] = s;
        }
        static ParamSpec()
        {
            Add(Cell.Gold, "Amount (0 = random)", 255, null, 0, null, 0, null);
            Add(Cell.Potion, "Heals HP (0 = game setting)", 99, null, 0, null, 0, null);
            Add(Cell.Key, "Colour (0-3, see Game tab)", 3, null, 0, null, 0, null);
            Add(Cell.Door, "Key colour needed (0-3)", 3, null, 0, null, 0, null);
            Add(Cell.Guard, "Hit points (0 = level setting)", 50, "Max damage per blow (0 = 3)", 20, "Attack chance % (0 = game setting)", 100, "Description shown by LOOK", false, true);
            Add(Cell.Sign, null, 0, null, 0, null, 0, "Sign text");
            Add(Cell.Trap, "Damage (0 = 3)", 50, "Visible (1) or hidden (0)", 1, "Works once (1) or every time (0)", 1, "Message (blank = CLICK! A TRAP SPRINGS!)");
            Add(Cell.Teleport, "Destination X", 31, "Destination Y", 23, null, 0, "Message (blank = default)", true);
            Add(Cell.Fountain, "Heals HP (0 = fully)", 99, "Uses (0 = never runs dry)", 99, null, 0, "Message (blank = default)");
            Add(Cell.Plate, "Opens cell X", 31, "Opens cell Y", 23, null, 0, "Message (blank = CLICK! STONE GRINDS NEARBY.)", true);
            Add(Cell.Secret, null, 0, null, 0, null, 0, "Message when found (blank = default)");
            Add(Cell.Npc, "Looks like (0 hermit, 1 merchant, 2 ghost)", 2,
                "Does (0 talk, 1 give, 2 sell, 3 heal, 4 step aside)", 4,
                "Gold price / gold given / HP healed (0 = full)", 255,
                "First speech (in a speech bubble)");
            ParamSpec n = specs[Cell.Npc];
            n.Labels[3] = "Item (0 none, 1-4 key colour 0-3, 5 sword, 6 potion, 7 gold, 8 amulet)";
            n.Max[3] = 8;
            n.Text2Label = "Speech once the deed is done (blank = first speech)";
        }
        public static ParamSpec For(Cell c) { ParamSpec s; return specs.TryGetValue(c, out s) ? s : null; }
    }

    public class Level
    {
        public const int W = 32, H = 24;
        public const int MaxObjects = 64;
        public string Name = "NEW LEVEL";
        public int GuardHp = 3;
        public int Facing = -1;             // -1 auto, 0 N, 1 E, 2 S, 3 W
        public string Intro = "";
        public Cell[,] Map = new Cell[H, W];
        public Dictionary<int, ObjParams> Objs = new Dictionary<int, ObjParams>();   // key y*W+x
        public string Script = "";          // Scott Adams style rules (see Script.cs)

        /// Parameters of a cell (null if it has none set). create = make default ones.
        public ObjParams Params(int x, int y, bool create)
        {
            ObjParams p;
            if (Objs.TryGetValue(y * W + x, out p)) return p;
            if (!create || ParamSpec.For(Map[y, x]) == null) return null;
            p = new ObjParams();
            Objs[y * W + x] = p;
            return p;
        }

        /// Drop parameters of cells whose type no longer takes them.
        public void PruneParams()
        {
            foreach (int k in Objs.Keys.ToList())
                if (ParamSpec.For(Map[k / W, k % W]) == null) Objs.Remove(k);
        }

        public Level()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Map[y, x] = Cell.Wall;
            Map[1, 1] = Cell.Ladder;
        }

        public Level Clone()
        {
            Level l = (Level)MemberwiseClone();
            l.Map = (Cell[,])Map.Clone();
            l.Objs = Objs.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
            return l;
        }

        public static bool IsBorder(int x, int y)
        {
            return x == 0 || y == 0 || x >= W - 1 || y >= H - 1;
        }

        public bool FindLadder(out int lx, out int ly)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Map[y, x] == Cell.Ladder) { lx = x; ly = y; return true; }
            lx = ly = 0;
            return false;
        }

        public int Count(Cell c)
        {
            int n = 0;
            foreach (Cell v in Map) if (v == c) n++;
            return n;
        }

        public void ForceBorder()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (IsBorder(x, y)) Map[y, x] = Cell.Wall;
        }

        public static bool Solid(Cell c) { return c == Cell.Wall || c == Cell.Door || c == Cell.Secret; }

        /// Facing used by the game: explicit, else the first open direction.
        public int StartFacing()
        {
            if (Facing >= 0) return Facing;
            int lx, ly;
            if (!FindLadder(out lx, out ly)) return 1;
            int[] order = { 1, 2, 3, 0 };
            foreach (int d in order)
            {
                int nx = lx + Util.DX[d], ny = ly + Util.DY[d];
                if (!Solid(Map[ny, nx])) return d;
            }
            return 1;
        }
    }

    public class Pack
    {
        public string Banner1 = "VAULTS", Banner2 = "OF ZOR";
        public string Subtitle = "A MAZE ADVENTURE FOR THE TRS-80 MODEL I";
        public string Line1 = "", Line2 = "", Line3 = "";
        public string Victory = "YOU CLIMB INTO THE SUNLIGHT!";
        public int StartHp = 12, MaxHp = 15, Heal = 6, Hit = 70, GuardHit = 37, GoldMin = 5, GoldMax = 20;
        public string[] Names = { "A BRASS KEY", "AN IRON KEY", "A SILVER KEY", "A GOLD KEY",
                                  "A SHORT SWORD", "A RED POTION", "SOME GOLD", "THE AMULET OF ZOR",
                                  "AN OLD HERMIT", "A MERCHANT", "A GHOST" };
        public List<Level> Levels = new List<Level>();

        /// Keys 0-3 are the four key colours (key = colour 0, as in older packs).
        public static readonly string[] NameKeys = { "key", "key1", "key2", "key3", "sword", "potion", "gold", "amulet",
                                                     "hermit", "merchant", "ghost" };
        public static readonly string[] NameLabels = { "Key colour 0", "Key colour 1", "Key colour 2", "Key colour 3",
                                                       "Sword", "Potion", "Gold", "Amulet",
                                                       "Hermit (character)", "Merchant (character)", "Ghost (character)" };
        public const int MaxLevels = 9;
    }

    public static class Util
    {
        public static readonly int[] DX = { 0, 1, 0, -1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };
        public const string MapChars = ".#DHGKSP$A?^@W=%&";

        /// Upper-case and keep only characters the Model I can show.
        public static string Clean(string s)
        {
            if (s == null) return "";
            StringBuilder b = new StringBuilder();
            foreach (char ch in s.ToUpperInvariant())
            {
                if (ch == '|' || ch == '\n') { b.Append('|'); continue; }
                if (ch >= 32 && ch <= 90) b.Append(ch);
            }
            return b.ToString();
        }

        /// Word-wrap to the 30-column text window. '|' forces a new line.
        public static List<string> Wrap(string text, int width)
        {
            List<string> lines = new List<string>();
            foreach (string para in Clean(text).Replace("\r", "").Split('|'))
            {
                string line = "";
                foreach (string w0 in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string w = w0;
                    while (w.Length > width) { if (line.Length > 0) { lines.Add(line); line = ""; } lines.Add(w.Substring(0, width)); w = w.Substring(width); }
                    if (line.Length == 0) line = w;
                    else if (line.Length + 1 + w.Length <= width) line += " " + w;
                    else { lines.Add(line); line = w; }
                }
                lines.Add(line);
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        public static string Fit(string s, int max)
        {
            s = Clean(s).Replace("|", " ");
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }

    // ------------------------------------------------------------------ .ZLP text files
    public static class Zlp
    {
        public static Pack Load(string path)
        {
            Pack p = new Pack();
            Level cur = null;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.Equals("[pack]", StringComparison.OrdinalIgnoreCase)) { cur = null; continue; }
                if (line.Equals("[level]", StringComparison.OrdinalIgnoreCase)) { cur = new Level(); p.Levels.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                if (cur != null)
                {
                    switch (k)
                    {
                        case "name": cur.Name = v; break;
                        case "guardhp": cur.GuardHp = Int(v, 3); break;
                        case "facing": cur.Facing = "NESW".IndexOf(v.ToUpperInvariant().Length > 0 ? v.ToUpperInvariant()[0] : 'A'); break;
                        case "intro": cur.Intro = v; break;
                        case "map":
                            for (int y = 0; y < Level.H && i + 1 < lines.Length; y++)
                            {
                                string m = lines[++i];
                                for (int x = 0; x < Level.W; x++)
                                {
                                    int c = x < m.Length ? Util.MapChars.IndexOf(char.ToUpperInvariant(m[x])) : 1;
                                    if (x < m.Length && m[x] == ' ') c = 0;
                                    cur.Map[y, x] = c < 0 ? Cell.Floor : (Cell)c;
                                }
                            }
                            cur.ForceBorder();
                            break;
                        case "obj":         // obj=x,y,a,b,c,d,text
                            string[] f = v.Split(new[] { ',' }, 7);
                            if (f.Length >= 6)
                            {
                                int ox = Int(f[0], -1), oy = Int(f[1], -1);
                                if (ox > 0 && oy > 0 && ox < Level.W - 1 && oy < Level.H - 1)
                                    cur.Objs[oy * Level.W + ox] = new ObjParams
                                    {
                                        A = Int(f[2], 0), B = Int(f[3], 0), C = Int(f[4], 0), D = Int(f[5], 0),
                                        Text = f.Length > 6 ? f[6] : "",
                                        Text2 = cur.Objs.ContainsKey(oy * Level.W + ox) ? cur.Objs[oy * Level.W + ox].Text2 : ""
                                    };
                            }
                            break;
                        case "script":      // script= ... endscript
                            {
                                List<string> sl = new List<string>();
                                while (i + 1 < lines.Length && lines[i + 1].Trim().ToLowerInvariant() != "endscript") sl.Add(lines[++i]);
                                i++;
                                cur.Script = string.Join("\n", sl);
                                break;
                            }
                        case "obj2":        // obj2=x,y,text  (second text, e.g. a character's later speech)
                            string[] f2 = v.Split(new[] { ',' }, 3);
                            if (f2.Length == 3)
                            {
                                int ox = Int(f2[0], -1), oy = Int(f2[1], -1);
                                if (ox > 0 && oy > 0 && ox < Level.W - 1 && oy < Level.H - 1)
                                {
                                    ObjParams q;
                                    if (!cur.Objs.TryGetValue(oy * Level.W + ox, out q)) cur.Objs[oy * Level.W + ox] = q = new ObjParams();
                                    q.Text2 = f2[2];
                                }
                            }
                            break;
                    }
                }
                else
                {
                    switch (k)
                    {
                        case "banner1": p.Banner1 = v; break;
                        case "banner2": p.Banner2 = v; break;
                        case "subtitle": p.Subtitle = v; break;
                        case "line1": p.Line1 = v; break;
                        case "line2": p.Line2 = v; break;
                        case "line3": p.Line3 = v; break;
                        case "victory": p.Victory = v; break;
                        case "starthp": p.StartHp = Int(v, 12); break;
                        case "maxhp": p.MaxHp = Int(v, 15); break;
                        case "heal": p.Heal = Int(v, 6); break;
                        case "hit": p.Hit = Int(v, 70); break;
                        case "guardhit": p.GuardHit = Int(v, 37); break;
                        case "goldmin": p.GoldMin = Int(v, 5); break;
                        case "goldmax": p.GoldMax = Int(v, 20); break;
                        default:
                            int n = Array.IndexOf(Pack.NameKeys, k);
                            if (n >= 0) p.Names[n] = v;
                            break;
                    }
                }
            }
            if (p.Levels.Count == 0) p.Levels.Add(new Level());
            foreach (Level l in p.Levels) l.PruneParams();
            return p;
        }

        public static void Save(Pack p, string path)
        {
            List<string> o = new List<string>();
            o.Add("; Vaults of Zor level pack - edit with ZorEdit.exe or any text editor");
            o.Add("; map: # wall  . floor  D door  H ladder (start)  G guardian");
            o.Add(";      K key  S sword  P potion  $ gold  A amulet");
            o.Add(";      ? sign  ^ trap  @ teleporter  W fountain  = pressure plate  % secret wall  & character");
            o.Add("; obj=x,y,a,b,c,d,text  sets the parameters of the cell at x,y (see README)");
            o.Add("; obj2=x,y,text         a character's speech once its deed is done");
            o.Add("");
            o.Add("[pack]");
            o.Add("banner1=" + p.Banner1);
            o.Add("banner2=" + p.Banner2);
            o.Add("subtitle=" + p.Subtitle);
            o.Add("line1=" + p.Line1);
            o.Add("line2=" + p.Line2);
            o.Add("line3=" + p.Line3);
            o.Add("victory=" + p.Victory.Replace("\r", "").Replace("\n", "|"));
            o.Add("starthp=" + p.StartHp);
            o.Add("maxhp=" + p.MaxHp);
            o.Add("heal=" + p.Heal);
            o.Add("hit=" + p.Hit);
            o.Add("guardhit=" + p.GuardHit);
            o.Add("goldmin=" + p.GoldMin);
            o.Add("goldmax=" + p.GoldMax);
            for (int i = 0; i < Pack.NameKeys.Length; i++) o.Add(Pack.NameKeys[i] + "=" + p.Names[i]);
            o.Add("");
            foreach (Level l in p.Levels)
            {
                o.Add("[level]");
                o.Add("name=" + l.Name);
                o.Add("guardhp=" + l.GuardHp);
                o.Add("facing=" + (l.Facing < 0 ? "AUTO" : "NESW"[l.Facing].ToString()));
                o.Add("intro=" + l.Intro.Replace("\r", "").Replace("\n", "|"));
                o.Add("map=");
                for (int y = 0; y < Level.H; y++)
                {
                    StringBuilder b = new StringBuilder();
                    for (int x = 0; x < Level.W; x++) b.Append(Util.MapChars[(int)l.Map[y, x]]);
                    o.Add(b.ToString());
                }
                foreach (KeyValuePair<int, ObjParams> kv in l.Objs.OrderBy(k => k.Key))
                {
                    ObjParams q = kv.Value;
                    if (q.IsDefault) continue;
                    o.Add("obj=" + (kv.Key % Level.W) + "," + (kv.Key / Level.W) + "," + q.A + "," + q.B + "," + q.C + "," + q.D + "," +
                          (q.Text ?? "").Replace("\r", "").Replace("\n", "|"));
                    if (!string.IsNullOrEmpty(q.Text2))
                        o.Add("obj2=" + (kv.Key % Level.W) + "," + (kv.Key / Level.W) + "," + q.Text2.Replace("\r", "").Replace("\n", "|"));
                }
                if (!string.IsNullOrWhiteSpace(l.Script))
                {
                    o.Add("script=");
                    o.AddRange(l.Script.Replace("\r", "").TrimEnd('\n').Split('\n'));
                    o.Add("endscript");
                }
                o.Add("");
            }
            File.WriteAllLines(path, o.ToArray());
        }

        static int Int(string s, int def)
        {
            int v;
            return int.TryParse(s, out v) ? v : def;
        }
    }

    // ------------------------------------------------------------------ binary pack
    public static class Compiler
    {
        public const int TitleSize = 1024;
        public const byte Version = 4;      // level file format (4 = level scripts and vocabulary)

        /// Builds the binary level pack read by ZOR/CMD (see zor.asm for the layout).
        public static byte[] Build(Pack p)
        {
            int n = p.Levels.Count;
            if (n < 1 || n > Pack.MaxLevels) throw new Exception("A pack needs 1 to " + Pack.MaxLevels + " levels.");
            List<byte> b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("ZORPACK"));
            b.Add(Version);
            b.Add(0); b.Add(0);                     // total length, patched below
            b.Add((byte)n);
            int maxhp = Clamp(p.MaxHp, 1, 99);
            b.Add((byte)Clamp(p.StartHp, 1, maxhp));
            b.Add((byte)maxhp);
            b.Add((byte)Clamp(p.Heal, 0, 99));
            b.Add((byte)Clamp((p.Hit * 256 + 50) / 100, 0, 255));
            b.Add((byte)Clamp((p.GuardHit * 256 + 50) / 100, 0, 255));
            int gmin = Clamp(p.GoldMin, 0, 200);
            int gmax = Clamp(p.GoldMax, gmin, gmin + 127);
            b.Add((byte)gmin);
            b.Add((byte)(gmax - gmin + 1));
            int offTitle = b.Count; b.Add(0); b.Add(0);
            int offVict = b.Count; b.Add(0); b.Add(0);
            int offNames = b.Count; for (int i = 0; i < 22; i++) b.Add(0);
            int offVocab = b.Count; b.Add(0); b.Add(0);
            int offLevs = b.Count; for (int i = 0; i < n * 2; i++) b.Add(0);
            ScriptCompiler sc = new ScriptCompiler();
            List<LevelScript> scripts = p.Levels.Select((lv, k) => sc.CompileLevel(lv.Script, k)).ToList();
            if (sc.Errors.Count > 0) throw new Exception(string.Join("\n", sc.Errors.Take(8)));

            Patch(b, offTitle, b.Count);
            b.AddRange(RenderTitle(p));
            Patch(b, offVict, b.Count);
            AddText(b, Util.Wrap(p.Victory, 30));
            for (int i = 0; i < 11; i++)
            {
                Patch(b, offNames + i * 2, b.Count);
                AddText(b, new List<string> { Util.Fit(p.Names[i], 18) });
            }
            for (int li = 0; li < n; li++)
            {
                Level l = p.Levels[li].Clone();
                l.ForceBorder();
                int lx, ly;
                if (!l.FindLadder(out lx, out ly)) throw new Exception("Level " + (li + 1) + " has no ladder (start).");
                Patch(b, offLevs + li * 2, b.Count);
                b.Add((byte)lx); b.Add((byte)ly); b.Add((byte)l.StartFacing());
                b.Add((byte)Clamp(l.GuardHp, 1, 50));
                int offName = b.Count; b.Add(0); b.Add(0);
                int offIntro = b.Count; b.Add(0); b.Add(0);
                int offObjs = b.Count; b.Add(0); b.Add(0);
                int offItems = b.Count; b.Add(0); b.Add(0);
                int offRules = b.Count; b.Add(0); b.Add(0);
                int offTexts = b.Count; b.Add(0); b.Add(0);
                for (int y = 0; y < Level.H; y++)          // the map, one byte per cell
                    for (int x = 0; x < Level.W; x++)
                    {
                        Cell c = l.Map[y, x];
                        if (c == Cell.Npc)                  // characters: code 16 + look
                        {
                            ObjParams q = l.Params(x, y, false);
                            b.Add((byte)(16 + (q == null ? 0 : Clamp(q.A, 0, 2))));
                        }
                        else b.Add((byte)c);
                    }
                Patch(b, offName, b.Count);
                AddText(b, new List<string> { Util.Fit(l.Name, 20) });
                Patch(b, offIntro, b.Count);
                AddText(b, Util.Wrap(l.Intro, 30));

                // object records: x, y, a, b, c, d, text offset (patched once texts are placed)
                l.PruneParams();
                List<KeyValuePair<int, ObjParams>> objs = l.Objs.Where(kv => !kv.Value.IsDefault).OrderBy(kv => kv.Key).ToList();
                if (objs.Count > Level.MaxObjects)
                    throw new Exception("Level " + (li + 1) + " has " + objs.Count + " cells with settings; the limit is " + Level.MaxObjects + ".");
                Patch(b, offObjs, b.Count);
                b.Add((byte)objs.Count);
                List<int> textAt = new List<int>();
                foreach (KeyValuePair<int, ObjParams> kv in objs)
                {
                    ObjParams q = kv.Value;
                    ParamSpec spec = ParamSpec.For(l.Map[kv.Key / Level.W, kv.Key % Level.W]);
                    int[] v = { q.A, q.B, q.C, q.D };
                    for (int k = 0; k < 4; k++) v[k] = spec.Labels[k] == null ? 0 : Clamp(v[k], 0, spec.Max[k]);
                    if (spec.Percent && v[2] > 0) v[2] = Clamp((v[2] * 256 + 50) / 100, 1, 255);
                    b.Add((byte)(kv.Key % Level.W)); b.Add((byte)(kv.Key / Level.W));
                    b.Add((byte)v[0]); b.Add((byte)v[1]); b.Add((byte)v[2]); b.Add((byte)v[3]);
                    textAt.Add(b.Count); b.Add(0); b.Add(0);
                }
                for (int k = 0; k < objs.Count; k++)
                {
                    ObjParams q = objs[k].Value;
                    bool npc = l.Map[objs[k].Key / Level.W, objs[k].Key % Level.W] == Cell.Npc;
                    if (npc)
                    {
                        // a character: first speech, 0, later speech, 0 - each split into
                        // short sentences that appear one at a time over the 3D view
                        if (IsBlank(q.Text) && IsBlank(q.Text2)) continue;
                        Patch(b, textAt[k], b.Count);
                        AddSpeech(b, IsBlank(q.Text) ? "HELLO, TRAVELLER." : q.Text);
                        if (IsBlank(q.Text2)) b.Add(0); else AddSpeech(b, q.Text2);
                        continue;
                    }
                    if (IsBlank(q.Text)) continue;
                    Patch(b, textAt[k], b.Count);
                    AddText(b, Util.Wrap(q.Text, 30));
                }

                // the level's script: items, rules and texts (see zor.asm "Adventure scripting")
                LevelScript ls = scripts[li];
                if (!ls.Empty)
                {
                    Patch(b, offItems, b.Count);
                    b.Add((byte)ls.Items.Count);
                    foreach (byte[] it in ls.Items) b.AddRange(it);
                    Patch(b, offRules, b.Count);
                    b.Add((byte)ls.Rules.Count);
                    foreach (byte[] r in ls.Rules) b.AddRange(r);
                    Patch(b, offTexts, b.Count);
                    b.Add((byte)ls.Texts.Count);
                    int tab = b.Count;
                    for (int t = 0; t < ls.Texts.Count; t++) { b.Add(0); b.Add(0); }
                    for (int t = 0; t < ls.Texts.Count; t++) { Patch(b, tab + t * 2, b.Count); b.AddRange(ls.Texts[t]); }
                }
            }
            Patch(b, offVocab, b.Count);
            b.AddRange(sc.VocabTable());
            Patch(b, 8, b.Count);
            return b.ToArray();
        }

        static void AddText(List<byte> b, List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) b.Add(13);
                b.AddRange(Encoding.ASCII.GetBytes(lines[i]));
            }
            b.Add(0);
        }

        static void Patch(List<byte> b, int at, int v) { b[at] = (byte)(v & 255); b[at + 1] = (byte)(v >> 8); }
        static bool IsBlank(string t) { return string.IsNullOrEmpty(Util.Clean(t ?? "").Replace("|", "").Trim()); }

        /// A character's speech: short sentences separated by 01, each wrapped into
        /// lines of up to 28 characters separated by 13, at most 4 lines per piece.
        public static void AddSpeech(List<byte> b, string text)
        {
            List<List<string>> pieces = new List<List<string>>();
            foreach (string sentence in Sentences(text))
            {
                List<string> lines = Util.Wrap(sentence, 28);
                for (int i = 0; i < lines.Count; i += 4) pieces.Add(lines.Skip(i).Take(4).ToList());
            }
            for (int p = 0; p < pieces.Count; p++)
            {
                if (p > 0) b.Add(1);
                for (int i = 0; i < pieces[p].Count; i++)
                {
                    if (i > 0) b.Add(13);
                    b.AddRange(Encoding.ASCII.GetBytes(pieces[p][i]));
                }
            }
            b.Add(0);
        }

        /// Split text into sentences at . ! ? : ; and at '|'.
        public static List<string> Sentences(string text)
        {
            List<string> r = new List<string>();
            foreach (string para in Util.Clean(text).Split('|'))
            {
                string cur = "";
                foreach (string w in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    cur = cur.Length == 0 ? w : cur + " " + w;
                    char last = w[w.Length - 1];
                    if (last == '.' || last == '!' || last == '?' || last == ':' || last == ';') { r.Add(cur); cur = ""; }
                }
                if (cur.Length > 0) r.Add(cur);
            }
            return r;
        }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // ---- title screen: big banner in semigraphics plus centred text lines
        public static byte[] RenderTitle(Pack p)
        {
            byte[] scr = new byte[TitleSize];
            for (int i = 0; i < scr.Length; i++) scr[i] = 32;
            bool[,] pix = new bool[48, 128];
            Big(pix, Util.Fit(p.Banner1, 10), 3);
            Big(pix, Util.Fit(p.Banner2, 10), 15);
            for (int cy = 0; cy < 9; cy++)
                for (int cx = 0; cx < 64; cx++)
                {
                    int bits = 0;
                    for (int py = 0; py < 3; py++)
                        for (int px = 0; px < 2; px++)
                            if (pix[cy * 3 + py, cx * 2 + px]) bits |= 1 << (py * 2 + px);
                    if (bits != 0) scr[cy * 64 + cx] = (byte)(0x80 | bits);
                    else if (cy == 0) scr[cx] = 0x83;
                    else if (cy == 8) scr[8 * 64 + cx] = 0xB0;
                }
            Text(scr, 9, p.Subtitle);
            Text(scr, 11, p.Line1);
            Text(scr, 12, p.Line2);
            Text(scr, 13, p.Line3);
            Text(scr, 15, "PRESS ENTER TO BEGIN  -  BREAK FOR TRSDOS");
            return scr;
        }

        static void Text(byte[] scr, int row, string s)
        {
            s = Util.Fit(s, 63);
            int c0 = (64 - s.Length) / 2;
            for (int i = 0; i < s.Length; i++) scr[row * 64 + c0 + i] = (byte)s[i];
        }

        static void Big(bool[,] pix, string text, int y0)
        {
            const int sx = 2;
            int w = text.Length * 6 * sx - sx;
            int x0 = (128 - w) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                string g = BannerFont.Glyph(text[i]);
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                        if (g[r * 5 + c] == '#')
                            for (int k = 0; k < sx; k++)
                            {
                                int x = x0 + (i * 6 + c) * sx + k;
                                if (x >= 0 && x < 128) pix[y0 + r, x] = true;
                            }
            }
        }
    }

    public static class BannerFont
    {
        static readonly Dictionary<char, string> G = new Dictionary<char, string>
        {
            {'A',".###.#...##...#######...##...##...#"},{'B',"####.#...##...#####.#...##...#####."},
            {'C',".###.#...##....#....#....#...#.###."},{'D',"####.#...##...##...##...##...#####."},
            {'E',"######....#....####.#....#....#####"},{'F',"######....#....####.#....#....#...."},
            {'G',".###.#...##....#.####...##...#.####"},{'H',"#...##...##...#######...##...##...#"},
            {'I',".###...#....#....#....#....#...###."},{'J',"..###...#....#....#....#.#..#..##.."},
            {'K',"#...##..#.#.#..##...#.#..#..#.#...#"},{'L',"#....#....#....#....#....#....#####"},
            {'M',"#...###.###.#.##.#.##...##...##...#"},{'N',"#...##...###..##.#.##..###...##...#"},
            {'O',".###.#...##...##...##...##...#.###."},{'P',"####.#...##...#####.#....#....#...."},
            {'Q',".###.#...##...##...##.#.##..#..##.#"},{'R',"####.#...##...#####.#.#..#..#.#...#"},
            {'S',".#####....#.....###.....#....#####."},{'T',"#####..#....#....#....#....#....#.."},
            {'U',"#...##...##...##...##...##...#.###."},{'V',"#...##...##...##...#.#.#..#.#...#.."},
            {'W',"#...##...##...##.#.##.#.###.###...#"},{'X',"#...##...#.#.#...#...#.#.#...##...#"},
            {'Y',"#...##...#.#.#...#....#....#....#.."},{'Z',"#####....#...#...#...#...#....#####"},
            {'0',".###.#...##..###.#.###..##...#.###."},{'1',"..#...##....#....#....#....#...###."},
            {'2',".###.#...#....#...#...#...#...#####"},{'3',"####.....#....#.###.....#....#####."},
            {'4',"...#...##..#.#.#..#.#####...#....#."},{'5',"######....####.....#....##...#.###."},
            {'6',".###.#....#....####.#...##...#.###."},{'7',"#####....#...#...#...#....#....#..."},
            {'8',".###.#...##...#.###.#...##...#.###."},{'9',".###.#...##...#.####....#....#.###."},
            {'!',"..#....#....#....#....#.........#.."},{'?',".###.#...#....#...#...#.........#.."},
            {'.',"................................#.."},{',',"...........................#...#..."},
            {'-',"...............#####..............."},{'\'',"..#....#..........................."},
        };

        public static string Glyph(char c)
        {
            string g;
            if (c == ':') return ".......#...............#..........";
            if (G.TryGetValue(c, out g) && g.Length == 35) return g;
            return new string('.', 35);
        }
    }

    // ------------------------------------------------------------------ /CMD files
    public class CmdImage
    {
        public byte[] Mem = new byte[65536];
        public int Lo = 65536, Hi = -1, Entry = -1;

        public static CmdImage Read(byte[] f)
        {
            CmdImage im = new CmdImage();
            int i = 0;
            while (i + 1 < f.Length)
            {
                int t = f[i], len = f[i + 1];
                if (t == 1)
                {
                    int L = len < 3 ? len + 256 : len;
                    int addr = f[i + 2] | (f[i + 3] << 8);
                    for (int k = 0; k < L - 2; k++) im.Put(addr + k, f[i + 4 + k]);
                    i += 2 + L;
                }
                else if (t == 2)
                {
                    im.Entry = f[i + 2] | (f[i + 3] << 8);
                    break;
                }
                else i += 2 + len;
            }
            if (im.Entry < 0 || im.Hi < 0) throw new Exception("Not a valid TRS-80 /CMD file.");
            return im;
        }

        void Put(int a, byte v) { Mem[a] = v; if (a < Lo) Lo = a; if (a > Hi) Hi = a; }

        public byte[] Write()
        {
            List<byte> o = new List<byte>();
            for (int a = Lo; a <= Hi; a += 256)
            {
                int n = Math.Min(256, Hi - a + 1);
                o.Add(1); o.Add((byte)((n + 2) & 255)); o.Add((byte)(a & 255)); o.Add((byte)(a >> 8));
                for (int k = 0; k < n; k++) o.Add(Mem[a + k]);
            }
            o.Add(2); o.Add(2); o.Add((byte)(Entry & 255)); o.Add((byte)(Entry >> 8));
            return o.ToArray();
        }

        /// Address of the built-in pack (the last "ZORPACK",version signature).
        public int FindBuiltin()
        {
            byte[] sig = Encoding.ASCII.GetBytes("ZORPACK" + (char)Compiler.Version);
            for (int a = Hi - sig.Length + 1; a >= Lo; a--)
            {
                int k = 0;
                while (k < sig.Length && Mem[a + k] == sig[k]) k++;
                if (k == sig.Length) return a;
            }
            throw new Exception("The game engine has no built-in pack marker.");
        }

        /// Replace the built-in pack. Returns the last address used.
        public int SetPack(byte[] pack)
        {
            int at = FindBuiltin();
            if (at + pack.Length > 0xFF00) throw new Exception("Pack too large to fit in 48K (" + pack.Length + " bytes).");
            Array.Clear(Mem, at, Mem.Length - at);
            Array.Copy(pack, 0, Mem, at, pack.Length);
            Hi = at + pack.Length - 1;
            return Hi;
        }
    }

    // ------------------------------------------------------------------ validation
    public static class Checker
    {
        /// Returns a list of problems; lines starting with "ERROR" stop export.
        /// Compile every level's script (they share words, flags and counters).
        public static List<string> CheckScripts(Pack p)
        {
            ScriptCompiler sc = new ScriptCompiler();
            for (int i = 0; i < p.Levels.Count; i++) sc.CompileLevel(p.Levels[i].Script, i);
            return sc.Errors.Select(e => "ERROR " + e).ToList();
        }

        public static List<string> Check(Level l, int index)
        {
            List<string> r = new List<string>();
            string pre = "Level " + (index + 1) + ": ";
            int ladders = l.Count(Cell.Ladder);
            if (ladders == 0) r.Add("ERROR " + pre + "no ladder (H). The ladder is the start and the exit.");
            if (ladders > 1) r.Add("ERROR " + pre + "more than one ladder.");
            bool npcAmulet = l.Objs.Any(kv => l.Map[kv.Key / Level.W, kv.Key % Level.W] == Cell.Npc
                                              && (kv.Value.B == 1 || kv.Value.B == 2) && kv.Value.D == 8);
            if (l.Count(Cell.Amulet) == 0 && !npcAmulet) r.Add("ERROR " + pre + "no amulet (A) to collect, and no character gives one.");
            foreach (KeyValuePair<int, ObjParams> kv in l.Objs)
            {
                ObjParams q = kv.Value;
                if (l.Map[kv.Key / Level.W, kv.Key % Level.W] != Cell.Npc) continue;
                string who = "character at " + (kv.Key % Level.W) + "," + (kv.Key / Level.W);
                if ((q.B == 1 || q.B == 2) && q.D == 0) r.Add("WARNING " + pre + who + " gives or sells an item but has none set.");
                if (q.B == 2 && q.C == 0) r.Add("NOTE " + pre + who + " sells for 0 gold, so it is a gift.");
            }
            if (r.Count > 0) return r;

            // per-cell settings
            int settings = l.Objs.Count(kv => !kv.Value.IsDefault);
            if (settings > Level.MaxObjects) r.Add("ERROR " + pre + settings + " cells have settings; the limit is " + Level.MaxObjects + ".");
            foreach (KeyValuePair<int, ObjParams> kv in l.Objs)
            {
                int x = kv.Key % Level.W, y = kv.Key / Level.W;
                Cell c = l.Map[y, x];
                ParamSpec spec = ParamSpec.For(c);
                if (spec == null || !spec.Target) continue;
                int tx = kv.Value.A, ty = kv.Value.B;
                string what = (c == Cell.Teleport ? "teleporter" : "pressure plate") + " at " + x + "," + y;
                if (tx <= 0 || ty <= 0 || tx >= Level.W - 1 || ty >= Level.H - 1)
                    r.Add("ERROR " + pre + what + " has no target inside the map (pick one in the cell's settings).");
                else if (c == Cell.Teleport && (Level.Solid(l.Map[ty, tx]) || l.Map[ty, tx] == Cell.Guard))
                    r.Add("ERROR " + pre + what + " sends the player into a wall, door or guardian at " + tx + "," + ty + ".");
            }
            foreach (Cell c in new[] { Cell.Teleport, Cell.Plate })
                for (int y = 0; y < Level.H; y++)
                    for (int x = 0; x < Level.W; x++)
                        if (l.Map[y, x] == c && l.Params(x, y, false) == null)
                            r.Add("ERROR " + pre + (c == Cell.Teleport ? "teleporter" : "pressure plate") + " at " + x + "," + y + " has no target set.");
            if (r.Any(s => s.StartsWith("ERROR"))) return r;

            string why;
            if (!Solve(l, out why)) r.Add("WARNING " + pre + why);
            for (int colour = 0; colour < 4; colour++)
            {
                int doors = 0, keys = 0;
                for (int y = 0; y < Level.H; y++)
                    for (int x = 0; x < Level.W; x++)
                    {
                        if (l.Map[y, x] == Cell.Door && ColourOf(l, x, y) == colour) doors++;
                        if (l.Map[y, x] == Cell.Key && ColourOf(l, x, y) == colour) keys++;
                    }
                if (keys < doors) r.Add("NOTE " + pre + doors + " door(s) of key colour " + colour + " but only " + keys + " key(s); each key opens one door.");
            }
            if (l.Count(Cell.Guard) > 0 && l.Count(Cell.Sword) == 0) r.Add("WARNING " + pre + "guardians but no sword - they cannot be killed.");
            int intro = Util.Wrap(l.Intro, 30).Count;
            if (intro > 6) r.Add("NOTE " + pre + "intro is " + intro + " lines; the text window shows 7, so it will scroll.");
            return r;
        }

        static int ColourOf(Level l, int x, int y)
        {
            ObjParams p = l.Params(x, y, false);
            return p == null ? 0 : p.A & 3;
        }

        /// Greedy play-through: collect what is reachable, use keys on doors of their
        /// colour, step on pressure plates, take teleporters, walk through secret
        /// walls and kill guardians once a sword is held. True if an amulet is reached.
        public static bool Solve(Level lv, out string why)
        {
            Cell[,] m = (Cell[,])lv.Map.Clone();
            int sx, sy;
            lv.FindLadder(out sx, out sy);
            int[] keys = new int[4];
            bool sword = false;
            HashSet<int> talked = new HashSet<int>();
            while (true)
            {
                bool[,] seen = Flood(lv, m, sx, sy, sword);
                bool changed = false;
                // characters next to the reachable area: take what they give (assume gold is enough)
                for (int y = 1; y < Level.H - 1; y++)
                    for (int x = 1; x < Level.W - 1; x++)
                    {
                        if (m[y, x] != Cell.Npc || talked.Contains(y * Level.W + x)) continue;
                        bool near = false;
                        for (int d = 0; d < 4; d++) near |= seen[y + Util.DY[d], x + Util.DX[d]];
                        if (!near) continue;
                        talked.Add(y * Level.W + x);
                        changed = true;
                        ObjParams p = lv.Params(x, y, false);
                        if (p == null) continue;
                        if (p.B == 1 || p.B == 2)
                        {
                            if (p.D >= 1 && p.D <= 4) keys[p.D - 1]++;
                            if (p.D == 5) sword = true;
                            if (p.D == 8) { why = ""; return true; }
                        }
                        if (p.B == 4) m[y, x] = Cell.Floor;
                    }
                for (int y = 0; y < Level.H; y++)
                    for (int x = 0; x < Level.W; x++)
                    {
                        if (!seen[y, x]) continue;
                        Cell c = m[y, x];
                        if (c == Cell.Amulet) { why = ""; return true; }
                        if (c == Cell.Key) { keys[ColourOf(lv, x, y)]++; m[y, x] = Cell.Floor; changed = true; }
                        if (c == Cell.Sword) { sword = true; m[y, x] = Cell.Floor; changed = true; }
                        if (c == Cell.Plate)
                        {
                            ObjParams p = lv.Params(x, y, false);
                            m[y, x] = Cell.Floor;
                            if (p != null && p.A > 0 && p.B > 0 && p.A < Level.W - 1 && p.B < Level.H - 1) m[p.B, p.A] = Cell.Floor;
                            changed = true;
                        }
                    }
                if (changed) continue;
                bool opened = false;
                for (int y = 1; y < Level.H - 1 && !opened; y++)
                    for (int x = 1; x < Level.W - 1 && !opened; x++)
                        if (m[y, x] == Cell.Door && keys[ColourOf(lv, x, y)] > 0)
                            for (int d = 0; d < 4; d++)
                                if (seen[y + Util.DY[d], x + Util.DX[d]]) { m[y, x] = Cell.Floor; keys[ColourOf(lv, x, y)]--; opened = true; break; }
                if (opened) continue;
                why = "the amulet cannot be reached (check walls, doors, key colours, guardians and teleporters).";
                return false;
            }
        }

        static bool[,] Flood(Level lv, Cell[,] m, int sx, int sy, bool sword)
        {
            bool[,] seen = new bool[Level.H, Level.W];
            Queue<int> q = new Queue<int>();
            seen[sy, sx] = true;
            q.Enqueue(sy * Level.W + sx);
            while (q.Count > 0)
            {
                int c = q.Dequeue(), x = c % Level.W, y = c / Level.W;
                List<int> next = new List<int>();
                for (int d = 0; d < 4; d++) next.Add((y + Util.DY[d]) * Level.W + x + Util.DX[d]);
                if (m[y, x] == Cell.Teleport)
                {
                    ObjParams p = lv.Params(x, y, false);
                    if (p != null && p.A > 0 && p.B > 0 && p.A < Level.W - 1 && p.B < Level.H - 1) next.Add(p.B * Level.W + p.A);
                }
                foreach (int n in next)
                {
                    int nx = n % Level.W, ny = n / Level.W;
                    if (nx < 0 || ny < 0 || nx >= Level.W || ny >= Level.H || seen[ny, nx]) continue;
                    Cell v = m[ny, nx];
                    if (v == Cell.Wall || v == Cell.Door || v == Cell.Npc || (v == Cell.Guard && !sword)) continue;
                    seen[ny, nx] = true;
                    q.Enqueue(n);
                }
            }
            return seen;
        }
    }

    // ------------------------------------------------------------------ maze generator
    public static class MazeGen
    {
        /// Carves a maze with loops and places a gated set of items
        /// (sword, guardian before a key, door before the amulet).
        public static Level Generate(Random rng, Level template)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                Level l = template.Clone();
                for (int y = 0; y < Level.H; y++)
                    for (int x = 0; x < Level.W; x++) l.Map[y, x] = Cell.Wall;
                Carve(l, rng);
                for (int i = 0; i < 40; i++)
                {
                    int x = rng.Next(1, 30), y = rng.Next(1, 22);
                    if (l.Map[y, x] != Cell.Wall || (x + y) % 2 == 0) continue;
                    bool h = l.Map[y, x - 1] == Cell.Floor && l.Map[y, x + 1] == Cell.Floor;
                    bool v = l.Map[y - 1, x] == Cell.Floor && l.Map[y + 1, x] == Cell.Floor;
                    if (h || v) l.Map[y, x] = Cell.Floor;
                }
                if (Place(l, rng)) return l;
            }
            return template.Clone();
        }

        static void Carve(Level l, Random rng)
        {
            Stack<int> st = new Stack<int>();
            l.Map[1, 1] = Cell.Floor;
            st.Push(1 * 32 + 1);
            int[] dxs = { 2, -2, 0, 0 }, dys = { 0, 0, 2, -2 };
            while (st.Count > 0)
            {
                int c = st.Peek(), x = c % 32, y = c / 32;
                List<int> opts = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dxs[d], ny = y + dys[d];
                    if (nx > 0 && nx < 31 && ny > 0 && ny < 23 && l.Map[ny, nx] == Cell.Wall) opts.Add(d);
                }
                if (opts.Count == 0) { st.Pop(); continue; }
                int k = opts[rng.Next(opts.Count)];
                l.Map[y + dys[k] / 2, x + dxs[k] / 2] = Cell.Floor;
                l.Map[y + dys[k], x + dxs[k]] = Cell.Floor;
                st.Push((y + dys[k]) * 32 + x + dxs[k]);
            }
        }

        static bool Place(Level l, Random rng)
        {
            int[,] dist; int[,] prev;
            Bfs(l, 1, 1, out dist, out prev);
            List<int> dead = new List<int>();
            for (int y = 1; y < 23; y++)
                for (int x = 1; x < 31; x++)
                    if (l.Map[y, x] == Cell.Floor && dist[y, x] > 0 && Open(l, x, y) == 1) dead.Add(y * 32 + x);
            if (dead.Count < 6) return false;
            dead.Sort((a, b) => dist[a / 32, a % 32].CompareTo(dist[b / 32, b % 32]));
            int am = dead[dead.Count - 1];
            List<int> ap = Path(prev, am);
            if (ap.Count < 10) return false;
            int door = ap[ap.Count - 5];
            if (Open(l, door % 32, door / 32) != 2) return false;
            HashSet<int> aset = new HashSet<int>(ap);
            int key = -1; List<int> kp = null;
            for (int i = dead.Count - 2; i >= 0; i--)
            {
                List<int> p = Path(prev, dead[i]);
                if (p.Count(c => aset.Contains(c)) < ap.Count / 3 && p.Count > 12) { key = dead[i]; kp = p; break; }
            }
            if (key < 0) return false;
            int guard = kp[kp.Count - 4];
            if (aset.Contains(guard) || Open(l, guard % 32, guard / 32) != 2) return false;
            Set(l, door, Cell.Door); Set(l, guard, Cell.Guard);
            Set(l, key, Cell.Key); Set(l, am, Cell.Amulet);
            Bfs(l, 1, 1, out dist, out prev);       // reachable without passing door/guardian
            List<int> free = dead.Where(c => l.Map[c / 32, c % 32] == Cell.Floor && dist[c / 32, c % 32] > 8).ToList();
            if (free.Count == 0) return false;
            Set(l, free[rng.Next(free.Count)], Cell.Sword);
            List<int> floor = new List<int>();
            for (int y = 1; y < 23; y++)
                for (int x = 1; x < 31; x++)
                    if (l.Map[y, x] == Cell.Floor && (x > 3 || y > 3) && Math.Abs(x - guard % 32) + Math.Abs(y - guard / 32) > 2)
                        floor.Add(y * 32 + x);
            for (int i = 0; i < 10 && floor.Count > 0; i++)
            {
                int j = rng.Next(floor.Count);
                Set(l, floor[j], i < 2 ? Cell.Potion : Cell.Gold);
                floor.RemoveAt(j);
            }
            l.Map[1, 1] = Cell.Ladder;
            string why;
            return Checker.Solve(l, out why);
        }

        static void Set(Level l, int c, Cell v) { l.Map[c / 32, c % 32] = v; }

        static int Open(Level l, int x, int y)
        {
            int n = 0;
            for (int d = 0; d < 4; d++) if (!Level.Solid(l.Map[y + Util.DY[d], x + Util.DX[d]]) && l.Map[y + Util.DY[d], x + Util.DX[d]] != Cell.Guard) n++;
            return n;
        }

        static void Bfs(Level l, int sx, int sy, out int[,] dist, out int[,] prev)
        {
            dist = new int[24, 32]; prev = new int[24, 32];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++) { dist[y, x] = -1; prev[y, x] = -1; }
            Queue<int> q = new Queue<int>();
            dist[sy, sx] = 0; q.Enqueue(sy * 32 + sx);
            while (q.Count > 0)
            {
                int c = q.Dequeue(), x = c % 32, y = c / 32;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + Util.DX[d], ny = y + Util.DY[d];
                    Cell v = l.Map[ny, nx];
                    if (dist[ny, nx] >= 0 || Level.Solid(v) || v == Cell.Guard) continue;
                    dist[ny, nx] = dist[y, x] + 1; prev[ny, nx] = c; q.Enqueue(ny * 32 + nx);
                }
            }
        }

        static List<int> Path(int[,] prev, int goal)
        {
            List<int> p = new List<int>();
            for (int c = goal; c >= 0; c = prev[c / 32, c % 32]) p.Add(c);
            p.Reverse();
            return p;
        }
    }
}
