// ZorEdit - Script.cs: the level script language (Scott Adams style rules).
//
// A level script declares items and rules:
//
//   item lamp "A DUSTY LANTERN" at 5,7 nouns LANTERN,LAMP desc "AN OLD LANTERN."
//   item idol "A STONE IDOL" at 9,3 fixed nouns IDOL
//   synonym IGNITE = LIGHT
//   on LIGHT LAMP if carried lamp then swap lamp litlamp; say "IT GLOWS."
//   every turn if flag cursed and chance 20 then hurt 1
//   on arrive if at 12,5 and not flag seen then say "AN ALTAR."; set seen
//
// '#' starts a comment; a line starting with a space continues the one before.
// Verbs and nouns are created by using them; flags and counters by naming them.
// Flags, counters and the vocabulary are shared by all levels of a pack.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZorEdit
{
    public class LevelScript
    {
        public List<byte[]> Items = new List<byte[]>();     // 6 bytes each
        public List<byte[]> Rules = new List<byte[]>();
        public List<byte[]> Texts = new List<byte[]>();     // encoded, 0-terminated
        public bool Empty { get { return Items.Count == 0 && Rules.Count == 0; } }
    }

    public class ScriptCompiler
    {
        public static readonly Dictionary<string, int> BuiltinVerbs = new Dictionary<string, int>
        {
            {"F",1},{"FORWARD",1},{"GO",1},{"WALK",1},{"B",2},{"BACK",2},{"L",3},{"LEFT",3},{"R",4},{"RIGHT",4},
            {"T",5},{"TURN",5},{"AROUND",6},{"G",7},{"GET",7},{"TAKE",7},{"O",8},{"OPEN",8},{"UNLOCK",8},
            {"U",9},{"USE",9},{"A",10},{"ATTACK",10},{"FIGHT",10},{"KILL",10},{"HIT",10},{"D",11},{"DRINK",11},
            {"X",12},{"LOOK",12},{"EXAMINE",12},{"I",13},{"INV",13},{"INVENTORY",13},{"H",14},{"HELP",14},{"?",14},
            {"S",15},{"SCORE",15},{"Q",16},{"QUIT",16},{"TALK",17},{"SPEAK",17},{"ASK",17},{"GREET",17},{"HELLO",17},
            {"DROP",18},
        };
        public static readonly string[] CellWords = { "FLOOR", "WALL", "DOOR", "LADDER", "GUARDIAN", "KEY", "SWORD", "POTION", "GOLD",
            "AMULET", "SIGN", "TRAP", "TELEPORTER", "FOUNTAIN", "PLATE", "SECRET", "HERMIT", "MERCHANT", "GHOST" };
        static readonly string[] HaveWords = { "SWORD", "AMULET", "KEY0", "KEY1", "KEY2", "KEY3", "POTION", "GOLD" };

        // pack-wide state
        public Dictionary<string, int> Verbs = new Dictionary<string, int>();    // script words -> id (incl. synonyms of built-ins)
        public Dictionary<string, int> Nouns = new Dictionary<string, int>();
        public Dictionary<string, int> Flags = new Dictionary<string, int>();
        public Dictionary<string, int> Counters = new Dictionary<string, int>();
        int nextVerb = 32, nextNoun = 1;
        public List<string> Errors = new List<string>();

        string where;
        Dictionary<string, int> items;
        LevelScript cur;

        class Stmt { public int Line; public List<string> T; }

        // ------------------------------------------------------------ public
        public LevelScript CompileLevel(string script, int levelIndex)
        {
            where = "Level " + (levelIndex + 1) + " script";
            cur = new LevelScript();
            items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<Stmt> stmts = Split(script ?? "");
            // pass 1: items (their nouns join the vocabulary)
            foreach (Stmt s in stmts) if (Is(s, 0, "ITEM")) Try(s, () => ItemDecl(s));
            // pass 2: words used by rules, then synonyms
            foreach (Stmt s in stmts) if (Is(s, 0, "ON") && s.T.Count > 1 && !Is(s, 1, "ARRIVE")) Try(s, () => RegisterRuleWords(s));
            foreach (Stmt s in stmts) if (Is(s, 0, "SYNONYM")) Try(s, () => Synonym(s));
            // pass 3: rules
            foreach (Stmt s in stmts)
            {
                if (Is(s, 0, "ITEM") || Is(s, 0, "SYNONYM")) continue;
                if (Is(s, 0, "ON") || Is(s, 0, "EVERY")) Try(s, () => Rule(s));
                else Err(s, "a statement starts with item, synonym, on or every, not '" + s.T[0] + "'.");
            }
            if (cur.Items.Count > 32) Errors.Add(where + ": more than 32 items.");
            if (cur.Rules.Count > 255) Errors.Add(where + ": more than 255 rules.");
            if (cur.Texts.Count > 255) Errors.Add(where + ": more than 255 texts.");
            return cur;
        }

        /// The pack vocabulary table read by the engine.
        public byte[] VocabTable()
        {
            List<byte> b = new List<byte>();
            foreach (KeyValuePair<string, int> kv in Verbs) Word(b, 1, kv.Value, kv.Key);
            foreach (KeyValuePair<string, int> kv in Nouns) Word(b, 2, kv.Value, kv.Key);
            b.Add(0);
            return b.ToArray();
        }

        static void Word(List<byte> b, int type, int id, string w)
        {
            b.Add((byte)type); b.Add((byte)id);
            b.AddRange(Encoding.ASCII.GetBytes(w)); b.Add(0);
        }

        /// Item positions declared with "at x,y" (for the editor's map), no checking.
        public static List<KeyValuePair<Point2, string>> ItemPositions(string script)
        {
            List<KeyValuePair<Point2, string>> r = new List<KeyValuePair<Point2, string>>();
            foreach (Match m in Regex.Matches(script ?? "", @"^\s*item\s+(\S+)\s+""[^""]*"".*?\bat\s+(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline))
                r.Add(new KeyValuePair<Point2, string>(new Point2(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), m.Groups[1].Value));
            return r;
        }

        // ------------------------------------------------------------ statements
        void ItemDecl(Stmt s)
        {
            // item NAME "TITLE" (at X,Y | carried | nowhere) [fixed] [nouns A,B] [desc "TEXT"]
            if (s.T.Count < 3) throw new Exception("item needs a name and a \"title\".");
            string name = s.T[1];
            if (!IsWord(name)) throw new Exception("item name '" + name + "' should be a single word.");
            if (items.ContainsKey(name)) throw new Exception("item '" + name + "' is declared twice.");
            if (cur.Items.Count >= 32) throw new Exception("a level can have at most 32 items.");
            string title = Quoted(s, 2);
            int x = 0, y = 0, noun = 0, flags = 1, desc = 0;
            int i = 3;
            while (i < s.T.Count)
            {
                string k = s.T[i].ToUpperInvariant();
                if (k == "AT") { x = Num(s, i + 1, 1, 30); Expect(s, i + 2, ","); y = Num(s, i + 3, 1, 22); i += 4; }
                else if (k == "CARRIED") { x = 0; y = 1; i++; }
                else if (k == "NOWHERE") { x = 0; y = 0; i++; }
                else if (k == "FIXED") { flags = 0; i++; }
                else if (k == "NOUNS" || k == "NOUN")
                {
                    i++;
                    List<string> words = new List<string>();
                    while (i < s.T.Count) { words.Add(Up(s.T[i])); i++; if (i < s.T.Count && s.T[i] == ",") i++; else break; }
                    noun = NounGroup(words);
                }
                else if (k == "DESC") { desc = AddText(Quoted(s, i + 1), 30); i += 2; }
                else throw new Exception("unexpected '" + s.T[i] + "' in item " + name + ".");
            }
            if (noun == 0) noun = NounGroup(new List<string> { Up(name) });
            items[name] = cur.Items.Count;
            int t = AddText(Util.Fit(title, 24), 30);
            cur.Items.Add(new[] { (byte)x, (byte)y, (byte)noun, (byte)flags, (byte)t, (byte)desc });
        }

        int NounGroup(List<string> words)
        {
            int id = 0;
            foreach (string w in words) { CheckWord(w); if (Nouns.ContainsKey(w)) { id = Nouns[w]; break; } }
            if (id == 0) { if (nextNoun > 254) throw new Exception("too many nouns."); id = nextNoun++; }
            foreach (string w in words) if (!Nouns.ContainsKey(w)) Nouns[w] = id;
            return id;
        }

        void RegisterRuleWords(Stmt s)
        {
            VerbId(Up(s.T[1]));
            if (s.T.Count > 2 && !IsKey(s.T[2]) && !s.T[2].Equals("ANY", StringComparison.OrdinalIgnoreCase)) NounId(Up(s.T[2]));
        }

        void Synonym(Stmt s)
        {
            // synonym NEW = EXISTING
            if (s.T.Count != 4 || s.T[2] != "=") throw new Exception("write: synonym NEWWORD = EXISTINGWORD");
            string nw = Up(s.T[1]), old = Up(s.T[3]);
            CheckWord(nw);
            int id;
            if (BuiltinVerbs.TryGetValue(old, out id) || Verbs.TryGetValue(old, out id))
            {
                if (BuiltinVerbs.ContainsKey(nw)) throw new Exception(nw + " is already a built-in command.");
                Verbs[nw] = id;
            }
            else if (Nouns.TryGetValue(old, out id)) Nouns[nw] = id;
            else throw new Exception("'" + old + "' is not a verb or noun used in the scripts so far.");
        }

        void Rule(Stmt s)
        {
            int verb, noun = 0, i;
            if (Is(s, 0, "EVERY")) { if (!Is(s, 1, "TURN")) throw new Exception("write: every turn ..."); verb = 0; i = 2; }
            else if (Is(s, 1, "ARRIVE")) { verb = 255; i = 2; }
            else
            {
                if (s.T.Count < 2) throw new Exception("on needs a verb.");
                verb = VerbId(Up(s.T[1]));
                i = 2;
                if (i < s.T.Count && !IsKey(s.T[i]))
                {
                    if (!s.T[i].Equals("ANY", StringComparison.OrdinalIgnoreCase)) noun = NounId(Up(s.T[i]));
                    i++;
                }
            }
            List<byte[]> conds = new List<byte[]>(), acts = new List<byte[]>();
            if (i < s.T.Count && Is(s, i, "IF"))
            {
                i++;
                while (true)
                {
                    conds.Add(Cond(s, ref i));
                    if (i < s.T.Count && Is(s, i, "AND")) { i++; continue; }
                    break;
                }
            }
            if (i >= s.T.Count || !Is(s, i, "THEN")) throw new Exception("expected 'then' after the conditions.");
            i++;
            while (true)
            {
                acts.Add(Act(s, ref i));
                if (i < s.T.Count && s.T[i] == ";") { i++; if (i >= s.T.Count) break; continue; }
                if (i < s.T.Count) throw new Exception("expected ';' between actions, found '" + s.T[i] + "'.");
                break;
            }
            if (conds.Count + acts.Count > 60) throw new Exception("too many conditions and actions in one rule (60 at most).");
            List<byte> r = new List<byte> { (byte)verb, (byte)noun, (byte)conds.Count, (byte)acts.Count };
            foreach (byte[] c in conds) r.AddRange(c);
            foreach (byte[] a in acts) r.AddRange(a);
            cur.Rules.Add(r.ToArray());
        }

        byte[] Cond(Stmt s, ref int i)
        {
            bool not = false;
            if (Is(s, i, "NOT")) { not = true; i++; }
            string k = Word(s, i++);
            int op, a = 0, b = 0, c = 0;
            switch (k)
            {
                case "CARRIED": op = 1; a = Item(s, i++); break;
                case "HERE": op = 2; a = Item(s, i++); break;
                case "PRESENT": op = 3; a = Item(s, i++); break;
                case "EXISTS": op = 4; a = Item(s, i++); break;
                case "AT": op = 5; a = Num(s, i, 0, 31); Expect(s, i + 1, ","); b = Num(s, i + 2, 0, 23); i += 3; break;
                case "AHEAD": op = 12; a = Num(s, i, 0, 31); Expect(s, i + 1, ","); b = Num(s, i + 2, 0, 23); i += 3; break;
                case "FLAG": op = 6; a = Flag(s, i++); break;
                case "COUNTER":
                    {
                        a = Counter(s, i++);
                        string rel = Tok(s, i++);
                        int n = Num(s, i++, 0, 255);
                        if (rel == ">=") { op = 7; b = n; }
                        else if (rel == ">") { op = 7; b = Math.Min(255, n + 1); }
                        else if (rel == "<") { op = 8; b = n; }
                        else if (rel == "<=") { op = 8; b = Math.Min(255, n + 1); }
                        else if (rel == "=")
                        {
                            throw new Exception("use 'counter " + s.T[i - 3] + " >= " + n + " and counter " + s.T[i - 3] + " < " + (n + 1) + "' to test for an exact value.");
                        }
                        else throw new Exception("compare a counter with >=, >, < or <=.");
                        break;
                    }
                case "FACING": op = 9; a = Cell(s, i++); break;
                case "CHANCE": op = 10; a = Math.Min(255, (Num(s, i++, 0, 100) * 256 + 50) / 100); break;
                case "HAS":
                    {
                        op = 11; a = Have(s, i++);
                        if (a == 7) { int n = Num(s, i++, 0, 65535); b = n & 255; c = n >> 8; }
                        break;
                    }
                default: throw new Exception("unknown condition '" + k + "'.");
            }
            return new[] { (byte)(op | (not ? 0x80 : 0)), (byte)a, (byte)b, (byte)c };
        }

        byte[] Act(Stmt s, ref int i)
        {
            string k = Word(s, i++);
            int op, a = 0, b = 0, c = 0;
            switch (k)
            {
                case "SAY": op = 1; a = AddText(Quoted(s, i++), 30); break;
                case "SPEAK": op = 2; a = AddSpeech(Quoted(s, i++)); break;
                case "SET": op = 3; a = Flag(s, i++); break;
                case "CLEAR": op = 4; a = Flag(s, i++); break;
                case "GET": op = 5; a = Item(s, i++); break;
                case "DROP": op = 6; a = Item(s, i++); break;
                case "DESTROY": op = 7; a = Item(s, i++); break;
                case "PUT": op = 8; a = Item(s, i++); if (!Is(s, i, "AT")) throw new Exception("write: put ITEM at X,Y"); b = Num(s, i + 1, 1, 30); Expect(s, i + 2, ","); c = Num(s, i + 3, 1, 22); i += 4; break;
                case "SWAP": op = 9; a = Item(s, i++); b = Item(s, i++); break;
                case "CELL": op = 10; a = Num(s, i, 1, 30); Expect(s, i + 1, ","); b = Num(s, i + 2, 1, 22); i += 3; c = Cell(s, i++); break;
                case "GOTO": op = 11; a = Num(s, i, 1, 30); Expect(s, i + 1, ","); b = Num(s, i + 2, 1, 22); i += 3; break;
                case "COUNTER": op = 12; a = Counter(s, i++); Expect(s, i++, "="); b = Num(s, i++, 0, 255); break;
                case "ADD": op = 13; a = Counter(s, i++); b = Num(s, i++, 0, 255); break;
                case "SUB": op = 14; a = Counter(s, i++); b = Num(s, i++, 0, 255); break;
                case "SCORE": { op = 15; int n = Num(s, i++, 0, 65535); a = n & 255; b = n >> 8; break; }
                case "HURT": op = 16; a = Num(s, i++, 0, 255); break;
                case "HEAL": op = 17; a = Num(s, i++, 0, 255); break;
                case "GIVE":
                case "TAKE":
                    {
                        op = k == "GIVE" ? 18 : 19; a = Have(s, i++);
                        if (a == 7) { int n = Num(s, i++, 0, 65535); b = n & 255; c = n >> 8; }
                        break;
                    }
                case "WIN": op = 20; break;
                case "DIE": op = 21; a = AddText(Quoted(s, i++), 30); break;
                case "INVENTORY": op = 22; break;
                case "LOOK": op = 23; break;
                case "CONTINUE": op = 24; break;
                default: throw new Exception("unknown action '" + k + "'.");
            }
            return new[] { (byte)op, (byte)a, (byte)b, (byte)c };
        }

        // ------------------------------------------------------------ words and values
        int VerbId(string w)
        {
            int id;
            if (BuiltinVerbs.TryGetValue(w, out id)) return id;
            if (Verbs.TryGetValue(w, out id)) return id;
            CheckWord(w);
            if (nextVerb > 254) throw new Exception("too many verbs.");
            Verbs[w] = id = nextVerb++;
            return id;
        }

        int NounId(string w)
        {
            int id;
            if (Nouns.TryGetValue(w, out id)) return id;
            CheckWord(w);
            if (nextNoun > 254) throw new Exception("too many nouns.");
            Nouns[w] = id = nextNoun++;
            return id;
        }

        int Item(Stmt s, int i)
        {
            string n = Tok(s, i);
            int id;
            if (!items.TryGetValue(n, out id)) throw new Exception("no item called '" + n + "' in this level.");
            return id;
        }

        int Flag(Stmt s, int i)
        {
            string n = Up(Tok(s, i));
            int id;
            if (!Flags.TryGetValue(n, out id)) { if (Flags.Count >= 32) throw new Exception("more than 32 flags."); Flags[n] = id = Flags.Count; }
            return id;
        }

        int Counter(Stmt s, int i)
        {
            string n = Up(Tok(s, i));
            int id;
            if (!Counters.TryGetValue(n, out id)) { if (Counters.Count >= 8) throw new Exception("more than 8 counters."); Counters[n] = id = Counters.Count; }
            return id;
        }

        int Cell(Stmt s, int i)
        {
            int c = Array.IndexOf(CellWords, Up(Tok(s, i)));
            if (c < 0) throw new Exception("'" + s.T[i] + "' is not a kind of cell (" + string.Join(" ", CellWords).ToLowerInvariant() + ").");
            return c;
        }

        int Have(Stmt s, int i)
        {
            string w = Up(Tok(s, i));
            if (w == "KEY") w = "KEY0";
            int c = Array.IndexOf(HaveWords, w);
            if (c < 0) throw new Exception("'" + s.T[i] + "' should be sword, amulet, key0-key3, potion or gold.");
            return c;
        }

        int AddText(string t, int width)
        {
            List<byte> b = new List<byte>();
            List<string> lines = Util.Wrap(t, width);
            for (int i = 0; i < lines.Count; i++) { if (i > 0) b.Add(13); b.AddRange(Encoding.ASCII.GetBytes(lines[i])); }
            b.Add(0);
            return Intern(b.ToArray());
        }

        int AddSpeech(string t)
        {
            List<byte> b = new List<byte>();
            Compiler.AddSpeech(b, t);
            return Intern(b.ToArray());
        }

        int Intern(byte[] t)
        {
            for (int i = 0; i < cur.Texts.Count; i++) if (cur.Texts[i].SequenceEqual(t)) return i + 1;
            cur.Texts.Add(t);
            return cur.Texts.Count;
        }

        static string Up(string s) { return (s ?? "").ToUpperInvariant(); }
        static bool IsWord(string s) { return Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9_]*$"); }
        static void CheckWord(string w)
        {
            if (!Regex.IsMatch(w, @"^[A-Z0-9?][A-Z0-9]*$") || w.Length > 12) throw new Exception("'" + w + "' is not a usable word (letters and digits, up to 12).");
        }
        static readonly string[] Keys = { "IF", "THEN", "AND" };
        static bool IsKey(string t) { return Keys.Contains(Up(t)); }
        static bool Is(Stmt s, int i, string w) { return i < s.T.Count && s.T[i].Equals(w, StringComparison.OrdinalIgnoreCase); }
        static string Tok(Stmt s, int i) { if (i >= s.T.Count) throw new Exception("the line ends too soon."); return s.T[i]; }
        static string Word(Stmt s, int i) { return Up(Tok(s, i)); }
        static void Expect(Stmt s, int i, string t) { if (Tok(s, i) != t) throw new Exception("expected '" + t + "' but found '" + s.T[i] + "'."); }
        static int Num(Stmt s, int i, int lo, int hi)
        {
            int v;
            if (!int.TryParse(Tok(s, i), out v)) throw new Exception("expected a number but found '" + s.T[i] + "'.");
            if (v < lo || v > hi) throw new Exception(v + " is out of range (" + lo + "-" + hi + ").");
            return v;
        }
        static string Quoted(Stmt s, int i)
        {
            string t = Tok(s, i);
            if (!t.StartsWith("\"")) throw new Exception("expected a \"quoted text\" but found '" + t + "'.");
            return t.Substring(1);
        }

        void Try(Stmt s, Action a)
        {
            try { a(); }
            catch (Exception ex) { Err(s, ex.Message); }
        }
        void Err(Stmt s, string m) { Errors.Add(where + " line " + s.Line + ": " + m); }

        // ------------------------------------------------------------ tokenizer
        static List<Stmt> Split(string script)
        {
            List<Stmt> r = new List<Stmt>();
            string[] lines = script.Replace("\r", "").Split('\n');
            Stmt cur = null;
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                List<string> toks = Tokens(raw);
                if (toks.Count == 0) continue;
                bool cont = raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && cur != null;
                if (cont) cur.T.AddRange(toks);
                else { cur = new Stmt { Line = n + 1, T = toks }; r.Add(cur); }
            }
            return r;
        }

        static List<string> Tokens(string line)
        {
            List<string> t = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                char ch = line[i];
                if (ch == '#') break;
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '"')
                {
                    int j = line.IndexOf('"', i + 1);
                    if (j < 0) j = line.Length;
                    t.Add("\"" + line.Substring(i + 1, j - i - 1));       // quoted text keeps a leading "
                    i = j + 1;
                    continue;
                }
                if (ch == ',' || ch == ';' || ch == '=') { t.Add(ch.ToString()); i++; continue; }
                if (ch == '<' || ch == '>')
                {
                    if (i + 1 < line.Length && line[i + 1] == '=') { t.Add(ch + "="); i += 2; }
                    else { t.Add(ch.ToString()); i++; }
                    continue;
                }
                int k = i;
                while (k < line.Length && !char.IsWhiteSpace(line[k]) && ",;=<>\"#".IndexOf(line[k]) < 0) k++;
                t.Add(line.Substring(i, k - i));
                i = k;
            }
            return t;
        }
    }

    public struct Point2
    {
        public int X, Y;
        public Point2(int x, int y) { X = x; Y = y; }
    }
}
