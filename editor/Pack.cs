// ZorEdit - level editor for VAULTS OF ZOR (TRS-80 Model I, TRSDOS)
// Pack.cs: level pack model (format 5: user-defined objects), .ZLP text format,
//          binary pack compiler, /CMD reader/writer, checks and maze generator.
// Builds with the .NET Framework 4 compiler (C# 5).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZorEdit
{
    /// A kind of object, shared by every level of the pack. In its script, "this"
    /// is the particular placed object the rule is about.
    public class ObjType
    {
        public string Id = "thing";         // script name (one word)
        public string Name = "A THING";     // shown in messages and the inventory
        public int Look = 19;               // engine picture, see Looks
        public bool Solid, Carry, Keep;
        public string Nouns = "";           // words the parser accepts, e.g. "KEY,KEYS"
        public int Value;                   // starting value of each placed object
        public string Desc = "";            // LOOK / EXAMINE text
        public char Symbol = '\0';          // map symbol in the editor ('\0' = from the look)
        public string Script = "";          // behaviours

        public ObjType Clone() { return (ObjType)MemberwiseClone(); }

        /// The engine's pictures: name, code, default map symbol.
        public static readonly string[] LookNames = { "none", "wall", "door", "ladder", "guardian", "key", "sword", "potion", "gold",
            "amulet", "sign", "trap", "teleporter", "fountain", "plate", "hermit", "merchant", "ghost", "thing" };
        public static readonly int[] LookCodes = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19 };
        public const string LookSymbols = " #DHGKSP$A?^@W=&&&*";

        public static int LookCode(string s)
        {
            int n;
            if (int.TryParse(s, out n)) return n;
            int i = Array.IndexOf(LookNames, (s ?? "").Trim().ToLowerInvariant());
            return i < 0 ? 19 : LookCodes[i];
        }
        public static string LookName(int code)
        {
            int i = Array.IndexOf(LookCodes, code);
            return i < 0 ? code.ToString() : LookNames[i];
        }
        public char MapSymbol
        {
            get
            {
                if (Symbol != '\0' && Symbol != ' ') return Symbol;
                int i = Array.IndexOf(LookCodes, Look);
                char c = i < 0 ? '*' : LookSymbols[i];
                return c == ' ' ? '~' : c;
            }
        }
        public List<string> NounList()
        {
            return Nouns.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToUpperInvariant()).ToList();
        }
    }

    /// An object placed on a level.
    public class Inst
    {
        public int X, Y;                    // 0,0 = nowhere; 0,1 = carried from the start
        public string Type = "";
        public int? Value;                  // null = the type's starting value
        public int P1, P2;                  // two numbers its behaviours can use ("target" = P1,P2)
        public string Name = "";            // script name, so level rules can refer to it
        public List<string> Texts = new List<string>();  // [0] description, [1].. said with "say this 2" etc.
        public string Script = "";          // behaviours of this one object

        public bool Nowhere { get { return X == 0 && Y == 0; } }
        public bool Carried { get { return X == 0 && Y == 1; } }
        public bool OnMap { get { return X > 0 && Y > 0; } }
        public string Text(int part) { return part >= 1 && part <= Texts.Count ? Texts[part - 1] ?? "" : ""; }
        public void SetText(int part, string t)
        {
            while (Texts.Count < part) Texts.Add("");
            Texts[part - 1] = t ?? "";
            while (Texts.Count > 0 && string.IsNullOrEmpty(Texts[Texts.Count - 1])) Texts.RemoveAt(Texts.Count - 1);
        }
        public Inst Clone() { Inst i = (Inst)MemberwiseClone(); i.Texts = new List<string>(Texts); return i; }
    }

    public class Level
    {
        public const int W = 32, H = 24;
        public const int MaxObjects = 64;
        public string Name = "NEW LEVEL";
        public string Intro = "";
        public int StartX = 1, StartY = 1, Facing = 1;      // 0 N, 1 E, 2 S, 3 W
        public bool[,] Wall = new bool[H, W];
        public List<Inst> Objs = new List<Inst>();
        public string Script = "";

        public Level()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Wall[y, x] = true;
            Wall[1, 1] = false;
        }

        public Level Clone()
        {
            Level l = (Level)MemberwiseClone();
            l.Wall = (bool[,])Wall.Clone();
            l.Objs = Objs.Select(o => o.Clone()).ToList();
            return l;
        }

        public static bool IsBorder(int x, int y) { return x == 0 || y == 0 || x >= W - 1 || y >= H - 1; }
        public void ForceBorder()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (IsBorder(x, y)) Wall[y, x] = true;
        }
        public IEnumerable<Inst> At(int x, int y) { return Objs.Where(o => o.X == x && o.Y == y); }
        public Inst Named(string n) { return Objs.FirstOrDefault(o => o.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && n.Length > 0); }
    }

    public class Pack
    {
        public string Banner1 = "VAULTS", Banner2 = "OF ZOR";
        public string Subtitle = "A MAZE ADVENTURE FOR THE TRS-80 MODEL I";
        public string Line1 = "", Line2 = "", Line3 = "";
        public string Victory = "YOU HAVE COME THROUGH EVERY LEVEL. WELL DONE!";
        public int StartHp = 12, MaxHp = 15;
        public string StatusCounter = "";   // counter shown on the status line ("" = none)
        public string StatusLabel = "$:";
        public string Script = "";          // rules for the whole pack (no particular object)
        public List<ObjType> Types = new List<ObjType>();
        public List<Level> Levels = new List<Level>();
        public const int MaxLevels = 9, MaxTypes = 128;

        public ObjType Type(string id) { return Types.FirstOrDefault(t => t.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase)); }
        public int TypeIndex(string id) { return Types.FindIndex(t => t.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase)); }
    }

    public static class Util
    {
        public static readonly int[] DX = { 0, 1, 0, -1 };
        public static readonly int[] DY = { -1, 0, 1, 0 };

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
        public static bool IsBlank(string t) { return string.IsNullOrEmpty(Clean(t ?? "").Replace("|", "").Trim()); }
        public static bool IsWord(string s) { return Regex.IsMatch(s ?? "", @"^[A-Za-z][A-Za-z0-9_]*$"); }
    }

    // ------------------------------------------------------------------ .ZLP text files
    public static class Zlp
    {
        public const string Header =
            "; Vaults of Zor level pack, format 5 - edit with ZorEdit.exe or any text editor.\n" +
            "; [object] sections define the kinds of object; a level's map is # wall and . floor,\n" +
            "; and obj=X,Y,TYPE places an object (0,0 or 'nowhere' = not on the map yet,\n" +
            "; 'carried' = the player has it). The o... lines after it set that object's\n" +
            "; value, P1,P2, name, texts and own script. Scripts run until endscript.";

        public static Pack Load(string path)
        {
            string[] lines = File.ReadAllLines(path);
            if (!lines.Any(l => Regex.IsMatch(l.Trim(), @"^format\s*=\s*5\b", RegexOptions.IgnoreCase))) return V4.Load(lines);
            return Parse(lines);
        }

        public static Pack Parse(string[] lines)
        {
            Pack p = new Pack();
            Level cur = null;
            ObjType typ = null;
            Inst obj = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                string low = line.ToLowerInvariant();
                if (low == "[pack]") { cur = null; typ = null; obj = null; continue; }
                if (low == "[object]") { cur = null; obj = null; typ = new ObjType(); p.Types.Add(typ); continue; }
                if (low == "[level]") { typ = null; obj = null; cur = new Level(); p.Levels.Add(cur); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                if (k == "script" || k == "oscript")
                {
                    List<string> sl = new List<string>();
                    while (i + 1 < lines.Length && lines[i + 1].Trim().ToLowerInvariant() != "endscript") sl.Add(lines[++i]);
                    i++;
                    v = string.Join("\n", sl);
                }
                if (typ != null) SetType(typ, k, v);
                else if (cur != null)
                {
                    if (k == "map")
                    {
                        for (int y = 0; y < Level.H && i + 1 < lines.Length; y++)
                        {
                            string m = lines[++i];
                            for (int x = 0; x < Level.W; x++) cur.Wall[y, x] = x >= m.Length || m[x] == '#';
                        }
                        cur.ForceBorder();
                    }
                    else if (k == "obj") { obj = ParseObj(v); if (obj != null) cur.Objs.Add(obj); }
                    else if (k.StartsWith("o") && obj != null && k != "obj") SetObj(obj, k.Substring(1), v);
                    else SetLevel(cur, k, v);
                }
                else SetPack(p, k, v);
            }
            if (p.Levels.Count == 0) p.Levels.Add(new Level());
            return p;
        }

        static void SetPack(Pack p, string k, string v)
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
                case "status":
                    {
                        string[] f = v.Split(new[] { ',' }, 2);
                        p.StatusCounter = f[0].Trim();
                        p.StatusLabel = f.Length > 1 ? f[1] : "";
                        break;
                    }
                case "script": p.Script = v; break;
            }
        }

        static void SetType(ObjType t, string k, string v)
        {
            switch (k)
            {
                case "id": t.Id = v; break;
                case "name": t.Name = v; break;
                case "look": t.Look = ObjType.LookCode(v); break;
                case "symbol": t.Symbol = v.Length > 0 ? v[0] : '\0'; break;
                case "flags":
                    {
                        string f = "," + v.ToLowerInvariant().Replace(" ", "") + ",";
                        t.Solid = f.Contains(",solid,"); t.Carry = f.Contains(",carry,"); t.Keep = f.Contains(",keep,");
                        break;
                    }
                case "nouns": t.Nouns = v; break;
                case "value": t.Value = Int(v, 0); break;
                case "desc": t.Desc = v; break;
                case "script": t.Script = v; break;
            }
        }

        static Inst ParseObj(string v)       // X,Y,TYPE  |  nowhere,TYPE  |  carried,TYPE
        {
            string[] f = v.Split(',').Select(s => s.Trim()).ToArray();
            if (f.Length == 2 && f[0].Equals("nowhere", StringComparison.OrdinalIgnoreCase)) return new Inst { Type = f[1] };
            if (f.Length == 2 && f[0].Equals("carried", StringComparison.OrdinalIgnoreCase)) return new Inst { Y = 1, Type = f[1] };
            if (f.Length < 3) return null;
            return new Inst { X = Int(f[0], 0), Y = Int(f[1], 0), Type = f[2] };
        }

        static void SetObj(Inst o, string k, string v)
        {
            switch (k)
            {
                case "value": o.Value = Int(v, 0); break;
                case "p":
                    {
                        string[] f = v.Split(',');
                        o.P1 = Int(f[0], 0); o.P2 = f.Length > 1 ? Int(f[1], 0) : 0;
                        break;
                    }
                case "name": o.Name = v; break;
                case "text": o.SetText(1, v); break;
                case "script": o.Script = v; break;
                default:
                    if (k.StartsWith("text") && Int(k.Substring(4), 0) >= 2) o.SetText(Int(k.Substring(4), 0), v);
                    break;
            }
        }

        static void SetLevel(Level l, string k, string v)
        {
            switch (k)
            {
                case "name": l.Name = v; break;
                case "intro": l.Intro = v; break;
                case "start":
                    {
                        string[] f = v.Split(',');
                        if (f.Length >= 2) { l.StartX = Int(f[0], 1); l.StartY = Int(f[1], 1); }
                        if (f.Length >= 3) { int d = "NESW".IndexOf(f[2].Trim().ToUpperInvariant().FirstOrDefault()); if (d >= 0) l.Facing = d; }
                        break;
                    }
                case "script": l.Script = v; break;
            }
        }

        public static void Save(Pack p, string path) { File.WriteAllLines(path, Lines(p).ToArray()); }

        public static List<string> Lines(Pack p)
        {
            List<string> o = new List<string>(Header.Split('\n'));
            o.Add("");
            o.Add("[pack]");
            o.Add("format=5");
            o.Add("banner1=" + p.Banner1);
            o.Add("banner2=" + p.Banner2);
            o.Add("subtitle=" + p.Subtitle);
            o.Add("line1=" + p.Line1);
            o.Add("line2=" + p.Line2);
            o.Add("line3=" + p.Line3);
            o.Add("victory=" + One(p.Victory));
            o.Add("starthp=" + p.StartHp);
            o.Add("maxhp=" + p.MaxHp);
            if (!string.IsNullOrWhiteSpace(p.StatusCounter)) o.Add("status=" + p.StatusCounter + "," + p.StatusLabel);
            ScriptLines(o, "script", p.Script);
            foreach (ObjType t in p.Types)
            {
                o.Add("");
                o.Add("[object]");
                o.Add("id=" + t.Id);
                o.Add("name=" + t.Name);
                o.Add("look=" + ObjType.LookName(t.Look));
                if (t.Symbol != '\0') o.Add("symbol=" + t.Symbol);
                List<string> fl = new List<string>();
                if (t.Solid) fl.Add("solid");
                if (t.Carry) fl.Add("carry");
                if (t.Keep) fl.Add("keep");
                if (fl.Count > 0) o.Add("flags=" + string.Join(",", fl));
                if (t.Nouns.Length > 0) o.Add("nouns=" + t.Nouns);
                if (t.Value != 0) o.Add("value=" + t.Value);
                if (t.Desc.Length > 0) o.Add("desc=" + One(t.Desc));
                ScriptLines(o, "script", t.Script);
            }
            foreach (Level l in p.Levels)
            {
                o.Add("");
                o.Add("[level]");
                o.Add("name=" + l.Name);
                o.Add("intro=" + One(l.Intro));
                o.Add("start=" + l.StartX + "," + l.StartY + "," + "NESW"[l.Facing & 3]);
                o.Add("map=");
                for (int y = 0; y < Level.H; y++)
                {
                    StringBuilder b = new StringBuilder();
                    for (int x = 0; x < Level.W; x++) b.Append(l.Wall[y, x] ? '#' : '.');
                    o.Add(b.ToString());
                }
                foreach (Inst i in l.Objs)
                {
                    o.Add("obj=" + (i.Nowhere ? "nowhere," : i.Carried ? "carried," : i.X + "," + i.Y + ",") + i.Type);
                    if (i.Name.Length > 0) o.Add("oname=" + i.Name);
                    if (i.Value.HasValue) o.Add("ovalue=" + i.Value.Value);
                    if (i.P1 != 0 || i.P2 != 0) o.Add("op=" + i.P1 + "," + i.P2);
                    for (int t = 1; t <= i.Texts.Count; t++)
                        if (!string.IsNullOrEmpty(i.Texts[t - 1])) o.Add("otext" + (t == 1 ? "" : t.ToString()) + "=" + One(i.Texts[t - 1]));
                    ScriptLines(o, "oscript", i.Script);
                }
                ScriptLines(o, "script", l.Script);
            }
            return o;
        }

        static string One(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "|"); }
        static void ScriptLines(List<string> o, string key, string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return;
            o.Add(key + "=");
            o.AddRange(script.Replace("\r", "").TrimEnd('\n').Split('\n'));
            o.Add("endscript");
        }

        public static int Int(string s, int def)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), out v) ? v : def;
        }
    }

    // ------------------------------------------------------------------ binary pack
    public static class Compiler
    {
        public const int TitleSize = 1024;
        public const byte Version = 5;      // level file format (5 = user-defined objects)

        /// Builds the binary level pack read by ZOR/CMD (DESIGN-OBJECTS.md section 7).
        public static byte[] Build(Pack p)
        {
            ScriptCompiler sc;
            byte[] b = Build(p, out sc);
            if (sc.Errors.Count > 0) throw new Exception(string.Join("\n", sc.Errors.Take(8)));
            return b;
        }

        /// As Build, but leaves the script errors in sc.Errors (the result is then incomplete).
        public static byte[] Build(Pack p, out ScriptCompiler sc)
        {
            int n = p.Levels.Count;
            sc = new ScriptCompiler(p);
            if (n < 1 || n > Pack.MaxLevels) sc.Errors.Add("A pack needs 1 to " + Pack.MaxLevels + " levels.");
            if (p.Types.Count > Pack.MaxTypes) sc.Errors.Add("A pack can have at most " + Pack.MaxTypes + " kinds of object.");

            // words: type nouns first, so rule nouns join their groups; then every script's words
            int statusCtr = 255;
            if (!string.IsNullOrWhiteSpace(p.StatusCounter)) statusCtr = sc.CounterId(p.StatusCounter.Trim());
            List<int> typeNoun = new List<int>();
            foreach (ObjType t in p.Types)
            {
                List<string> w = t.NounList();
                if (w.Count == 0) w.Add(t.Id.ToUpperInvariant());
                typeNoun.Add(sc.SafeNounGroup(w, "object " + t.Id));
            }
            List<KeyValuePair<string, string>> all = AllScripts(p);
            foreach (KeyValuePair<string, string> s in all) sc.RegisterWords(s.Value, s.Key);
            foreach (KeyValuePair<string, string> s in all) sc.Synonyms(s.Value, s.Key);

            // pack rules: the pack script, then every type's behaviours
            RuleSet prs = new RuleSet();
            List<int[]> typeText = p.Types.Select(t => new[] { prs.Text(Util.Fit(t.Name, 24), 30), Util.IsBlank(t.Desc) ? 0 : prs.Text(t.Desc, 30) }).ToList();
            sc.Compile(p.Script, "Pack script", prs, 0, -1, null);
            for (int t = 0; t < p.Types.Count; t++) sc.Compile(p.Types[t].Script, "Object " + p.Types[t].Id, prs, t + 1, -1, null);
            if (prs.Rules.Count > 255) sc.Errors.Add("The pack has more than 255 object and pack rules.");

            List<byte> b = new List<byte>();
            b.AddRange(Encoding.ASCII.GetBytes("ZORPACK"));
            b.Add(Version);
            b.Add(0); b.Add(0);                     // total length, patched below
            b.Add((byte)n);
            int maxhp = Clamp(p.MaxHp, 1, 99);
            b.Add((byte)Clamp(p.StartHp, 1, maxhp));
            b.Add((byte)maxhp);
            b.Add((byte)p.Types.Count);
            b.Add((byte)statusCtr);
            string lab = (Util.Fit(p.StatusLabel, 2) + "  ").Substring(0, 2);
            b.AddRange(Encoding.ASCII.GetBytes(lab));
            b.Add(0);
            for (int i = 0; i < 12 + 2 * n; i++) b.Add(0);     // pointers, patched below

            Patch(b, 18, b.Count);
            b.AddRange(RenderTitle(p));
            Patch(b, 20, b.Count);
            AddText(b, Util.Wrap(p.Victory, 30));
            b.Add(0);

            for (int li = 0; li < n; li++)
            {
                Level l = p.Levels[li].Clone();
                l.ForceBorder();
                string pre = "Level " + (li + 1);
                if (l.Objs.Count > Level.MaxObjects) sc.Errors.Add(pre + " has " + l.Objs.Count + " objects; the limit is " + Level.MaxObjects + ".");
                Dictionary<string, int> names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < l.Objs.Count; k++)
                {
                    Inst o = l.Objs[k];
                    if (p.TypeIndex(o.Type) < 0) sc.Errors.Add(pre + ": the object at " + o.X + "," + o.Y + " is of an unknown kind '" + o.Type + "'.");
                    if (o.Name.Length == 0) continue;
                    if (!Util.IsWord(o.Name)) sc.Errors.Add(pre + ": object name '" + o.Name + "' should be one word.");
                    else if (names.ContainsKey(o.Name)) sc.Errors.Add(pre + ": two objects are called '" + o.Name + "'.");
                    else names[o.Name] = k;
                }
                RuleSet lrs = new RuleSet();
                for (int k = 0; k < l.Objs.Count; k++)
                {
                    Inst o = l.Objs[k];
                    int t = p.TypeIndex(o.Type);
                    if (t >= 0 && !string.IsNullOrWhiteSpace(o.Script))
                        sc.Compile(o.Script, pre + " object " + (o.Name.Length > 0 ? o.Name : o.Type + " at " + o.X + "," + o.Y), lrs, t + 1, k, names);
                }
                sc.Compile(l.Script, pre + " script", lrs, 0, -1, names);
                if (lrs.Rules.Count > 255) sc.Errors.Add(pre + " has more than 255 rules.");
                if (lrs.Texts.Count > 255) sc.Errors.Add(pre + " has more than 255 texts.");

                int at = b.Count;
                Patch(b, 30 + li * 2, at);
                b.Add((byte)Clamp(l.StartX, 1, Level.W - 2)); b.Add((byte)Clamp(l.StartY, 1, Level.H - 2));
                b.Add((byte)(l.Facing & 3)); b.Add(0);
                for (int i = 0; i < 10; i++) b.Add(0);
                for (int y = 0; y < Level.H; y++)          // terrain, one bit per cell
                    for (int xb = 0; xb < 4; xb++)
                    {
                        int v = 0;
                        for (int k = 0; k < 8; k++) if (l.Wall[y, xb * 8 + k]) v |= 1 << k;
                        b.Add((byte)v);
                    }
                Patch(b, at + 4, b.Count);
                AddText(b, new List<string> { Util.Fit(l.Name, 20) }); b.Add(0);
                Patch(b, at + 6, b.Count);
                AddText(b, Util.Wrap(l.Intro, 30)); b.Add(0);

                Patch(b, at + 8, b.Count);
                b.Add((byte)Math.Min(l.Objs.Count, Level.MaxObjects));
                List<int> textAt = new List<int>();
                foreach (Inst o in l.Objs.Take(Level.MaxObjects))
                {
                    int t = Math.Max(0, p.TypeIndex(o.Type));
                    int v = o.Value ?? (t < p.Types.Count ? p.Types[t].Value : 0);
                    b.Add((byte)Clamp(o.X, 0, Level.W - 1)); b.Add((byte)Clamp(o.Y, 0, Level.H - 1));
                    b.Add((byte)t); b.Add((byte)Clamp(v, 0, 255));
                    b.Add((byte)Clamp(o.P1, 0, 255)); b.Add((byte)Clamp(o.P2, 0, 255));
                    textAt.Add(b.Count); b.Add(0); b.Add(0);
                }
                for (int k = 0; k < textAt.Count; k++)
                {
                    byte[] tx = InstText(l.Objs[k]);
                    if (tx == null) continue;
                    Patch(b, textAt[k], b.Count);
                    b.AddRange(tx);
                }
                if (lrs.Rules.Count > 0) { Patch(b, at + 10, b.Count); lrs.WriteRules(b); }
                if (lrs.Texts.Count > 0) { Patch(b, at + 12, b.Count); lrs.WriteTexts(b, Patch); }
            }
            Patch(b, 22, b.Count);
            b.AddRange(sc.VocabTable());
            Patch(b, 24, b.Count);
            for (int t = 0; t < p.Types.Count; t++)
            {
                ObjType ty = p.Types[t];
                b.Add((byte)Clamp(ty.Look, 0, 31));
                b.Add((byte)((ty.Solid ? 1 : 0) | (ty.Carry ? 2 : 0) | (ty.Keep ? 4 : 0)));
                b.Add((byte)typeNoun[t]);
                b.Add((byte)Clamp(ty.Value, 0, 255));
                b.Add((byte)typeText[t][0]); b.Add((byte)typeText[t][1]);
            }
            Patch(b, 26, b.Count);
            prs.WriteRules(b);
            Patch(b, 28, b.Count);
            prs.WriteTexts(b, Patch);
            if (prs.Texts.Count > 255) sc.Errors.Add("The pack has more than 255 texts (object names, descriptions and pack rule texts).");
            Patch(b, 8, b.Count);
            return b.ToArray();
        }

        /// Every script of the pack with a label for error messages.
        public static List<KeyValuePair<string, string>> AllScripts(Pack p)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            r.Add(new KeyValuePair<string, string>("Pack script", p.Script));
            foreach (ObjType t in p.Types) r.Add(new KeyValuePair<string, string>("Object " + t.Id, t.Script));
            for (int i = 0; i < p.Levels.Count; i++)
            {
                foreach (Inst o in p.Levels[i].Objs)
                    if (!string.IsNullOrWhiteSpace(o.Script)) r.Add(new KeyValuePair<string, string>("Level " + (i + 1) + " object", o.Script));
                r.Add(new KeyValuePair<string, string>("Level " + (i + 1) + " script", p.Levels[i].Script));
            }
            return r;
        }

        /// A placed object's text: description, then speeches, separated by 02.
        static byte[] InstText(Inst o)
        {
            int last = o.Texts.Count;
            while (last > 0 && Util.IsBlank(o.Texts[last - 1])) last--;
            if (last == 0) return null;
            List<byte> b = new List<byte>();
            for (int i = 0; i < last; i++)
            {
                if (i > 0) b.Add(2);
                string t = o.Texts[i] ?? "";
                if (Util.IsBlank(t)) continue;
                if (i == 0) AddText(b, Util.Wrap(t, 30));
                else { List<byte> s = new List<byte>(); AddSpeech(s, t); s.RemoveAt(s.Count - 1); b.AddRange(s); }
            }
            b.Add(0);
            return b.ToArray();
        }

        /// Lines separated by 13 (no terminator).
        public static void AddText(List<byte> b, List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) b.Add(13);
                b.AddRange(Encoding.ASCII.GetBytes(lines[i]));
            }
        }

        public static void Patch(List<byte> b, int at, int v) { b[at] = (byte)(v & 255); b[at + 1] = (byte)(v >> 8); }

        /// Speech: short sentences separated by 01, each wrapped into lines of up to
        /// 28 characters separated by 13, at most 4 lines per piece; ends with 0.
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
            throw new Exception("The game engine has no built-in pack marker for format " + Compiler.Version + ".");
        }

        /// Replace the built-in pack. Returns the last address used.
        public int SetPack(byte[] pack)
        {
            int at = FindBuiltin();
            if (at + pack.Length > 0xFF00) throw new Exception("Pack too large to fit in 48K (" + pack.Length + " bytes, " + (0xFF00 - at) + " free).");
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
        public static List<string> Check(Pack p)
        {
            List<string> r = new List<string>();
            ScriptCompiler sc;
            byte[] bin = null;
            try { bin = Compiler.Build(p, out sc); r.AddRange(sc.Errors.Select(e => "ERROR " + e)); }
            catch (Exception ex) { r.Add("ERROR " + ex.Message); }
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjType t in p.Types)
            {
                if (!Util.IsWord(t.Id)) r.Add("ERROR object id '" + t.Id + "' should be one word (letters, digits, _).");
                else if (!ids.Add(t.Id)) r.Add("ERROR two kinds of object have the id '" + t.Id + "'.");
            }
            for (int i = 0; i < p.Levels.Count; i++) r.AddRange(CheckLevel(p, p.Levels[i], i));
            if (bin != null)
                try
                {
                    CmdImage im = Engine.Load();
                    int free = 0xFF00 - im.FindBuiltin();
                    if (bin.Length > free) r.Add("ERROR the level file is " + bin.Length + " bytes; only " + free + " fit in memory with the engine.");
                }
                catch { }
            return r;
        }

        static List<string> CheckLevel(Pack p, Level l, int index)
        {
            List<string> r = new List<string>();
            string pre = "Level " + (index + 1) + ": ";
            if (Level.IsBorder(l.StartX, l.StartY) || l.Wall[l.StartY, l.StartX]) r.Add("ERROR " + pre + "the start is inside a wall.");
            foreach (Inst o in l.At(l.StartX, l.StartY))
            {
                ObjType t = p.Type(o.Type);
                if (t != null && t.Solid) r.Add("WARNING " + pre + "the player starts on " + t.Id + ", which is solid.");
            }
            foreach (Inst o in l.Objs)
            {
                if (!o.OnMap) continue;
                if (o.X >= Level.W - 1 || o.Y >= Level.H - 1) r.Add("ERROR " + pre + o.Type + " at " + o.X + "," + o.Y + " is outside the map.");
                else if (l.Wall[o.Y, o.X]) r.Add("WARNING " + pre + o.Type + " at " + o.X + "," + o.Y + " is inside a wall, so the player can't reach it.");
            }
            // is there a way on? some rule of this level, its objects or the pack must say next or win
            Regex exit = new Regex(@"\b(next|win)\b", RegexOptions.IgnoreCase);
            bool way = exit.IsMatch(Strip(l.Script)) || exit.IsMatch(Strip(p.Script)) || l.Objs.Any(o => exit.IsMatch(Strip(o.Script)));
            if (!way)
                foreach (string id in l.Objs.Select(o => o.Type).Distinct())
                {
                    ObjType t = p.Type(id);
                    if (t != null && exit.IsMatch(Strip(t.Script))) way = true;
                }
            if (!way) r.Add("WARNING " + pre + "no rule of this level or its objects says next or win, so the level can't be finished.");
            int intro = Util.Wrap(l.Intro, 30).Count;
            if (intro > 6) r.Add("NOTE " + pre + "intro is " + intro + " lines; the text window shows 7, so it will scroll.");
            return r;
        }

        /// Script text without comments and quoted texts.
        static string Strip(string s)
        {
            return Regex.Replace(Regex.Replace(s ?? "", "\"[^\"\\n]*\"?", " "), "#.*", " ");
        }
    }

    // ------------------------------------------------------------------ maze generator
    public static class MazeGen
    {
        /// Carves a maze with loops into the template (walls only; objects left
        /// inside walls are removed). The start moves to 1,1.
        public static Level Generate(Random rng, Level template)
        {
            Level l = template.Clone();
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++) l.Wall[y, x] = true;
            Stack<int> st = new Stack<int>();
            l.Wall[1, 1] = false;
            st.Push(1 * 32 + 1);
            int[] dxs = { 2, -2, 0, 0 }, dys = { 0, 0, 2, -2 };
            while (st.Count > 0)
            {
                int c = st.Peek(), x = c % 32, y = c / 32;
                List<int> opts = new List<int>();
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dxs[d], ny = y + dys[d];
                    if (nx > 0 && nx < 31 && ny > 0 && ny < 23 && l.Wall[ny, nx]) opts.Add(d);
                }
                if (opts.Count == 0) { st.Pop(); continue; }
                int k = opts[rng.Next(opts.Count)];
                l.Wall[y + dys[k] / 2, x + dxs[k] / 2] = false;
                l.Wall[y + dys[k], x + dxs[k]] = false;
                st.Push((y + dys[k]) * 32 + x + dxs[k]);
            }
            for (int i = 0; i < 40; i++)            // a few loops
            {
                int x = rng.Next(1, 30), y = rng.Next(1, 22);
                if (!l.Wall[y, x] || (x + y) % 2 == 0) continue;
                if ((!l.Wall[y, x - 1] && !l.Wall[y, x + 1]) || (!l.Wall[y - 1, x] && !l.Wall[y + 1, x])) l.Wall[y, x] = false;
            }
            l.StartX = 1; l.StartY = 1;
            l.Facing = !l.Wall[1, 2] ? 1 : 2;
            l.Objs.RemoveAll(o => o.OnMap && l.Wall[o.Y, o.X]);
            return l;
        }
    }
}
