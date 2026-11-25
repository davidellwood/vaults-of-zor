// ZorEdit - Script.cs: the rule language (Scott Adams style, format 5).
//
// A script is a list of rules, one per line (a line starting with a space
// continues the one before; '#' starts a comment):
//
//   on OPEN if ahead this and has key then take key; remove this; say "IT OPENS."
//   on TAKE if here this then add gold value; score value; remove this
//   on arrive then say this 2; goto target
//   every turn if near this and chance 30 then say "IT BITES!"; hurt 2 random
//   synonym UNLOCK = OPEN
//
// Rules can live in four places: the pack script (no particular object), an
// object type's behaviours ("this" = the placed object of that type the rule is
// about), a level script, or one placed object's own behaviours. Level rules can
// name the level's objects by their object names. Verbs and nouns are created
// by using them, flags and counters by naming them; all are shared by the pack.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZorEdit
{
    /// Rules and texts of the pack, or of one level.
    public class RuleSet
    {
        public List<byte[]> Rules = new List<byte[]>();
        public List<byte[]> Texts = new List<byte[]>();     // encoded, without the 0

        public int Text(string t, int width)
        {
            List<byte> b = new List<byte>();
            Compiler.AddText(b, Util.Wrap(t, width));
            return Intern(b.ToArray());
        }
        public int Speech(string t)
        {
            List<byte> b = new List<byte>();
            Compiler.AddSpeech(b, t);
            b.RemoveAt(b.Count - 1);
            return Intern(b.ToArray());
        }
        int Intern(byte[] t)
        {
            for (int i = 0; i < Texts.Count; i++) if (Texts[i].SequenceEqual(t)) return i + 1;
            Texts.Add(t);
            return Texts.Count;
        }
        public void WriteRules(List<byte> b)
        {
            b.Add((byte)Math.Min(255, Rules.Count));
            foreach (byte[] r in Rules.Take(255)) b.AddRange(r);
        }
        public void WriteTexts(List<byte> b, Action<List<byte>, int, int> patch)
        {
            int n = Math.Min(255, Texts.Count);
            b.Add((byte)n);
            int tab = b.Count;
            for (int t = 0; t < n; t++) { b.Add(0); b.Add(0); }
            for (int t = 0; t < n; t++) { patch(b, tab + t * 2, b.Count); b.AddRange(Texts[t]); b.Add(0); }
        }
    }

    public class ScriptCompiler
    {
        // the engine's own commands (rules may use them to change what they do)
        public static readonly Dictionary<string, int> BuiltinVerbs = new Dictionary<string, int>
        {
            {"F",1},{"FORWARD",1},{"GO",1},{"WALK",1},{"B",2},{"BACK",2},{"L",3},{"LEFT",3},{"R",4},{"RIGHT",4},
            {"T",5},{"TURN",5},{"AROUND",6},{"G",7},{"GET",7},{"TAKE",7},
            {"X",12},{"LOOK",12},{"EXAMINE",12},{"I",13},{"INV",13},{"INVENTORY",13},{"H",14},{"HELP",14},{"?",14},
            {"S",15},{"SCORE",15},{"Q",16},{"QUIT",16},{"TALK",17},{"SPEAK",17},{"ASK",17},{"GREET",17},{"HELLO",17},
            {"DROP",18},
        };
        public const int Every = 0, Start = 253, Bump = 254, Arrive = 255, This = 255;
        static readonly string[] CellKinds = { "FLOOR", "WALL", "OPEN" };

        // pack-wide state
        public Dictionary<string, int> Verbs = new Dictionary<string, int>();    // script words -> id (incl. synonyms of built-ins)
        public Dictionary<string, int> Nouns = new Dictionary<string, int>();
        public Dictionary<string, int> Flags = new Dictionary<string, int>();
        public Dictionary<string, int> Counters = new Dictionary<string, int>();
        public List<string> Errors = new List<string>();
        readonly Pack pack;
        int nextVerb = 32, nextNoun = 1;

        // the script being compiled
        string where;
        RuleSet cur;
        int scope, self;                    // scope: 0 or type + 1; self: placed object index or -1
        Dictionary<string, int> insts;      // object names of the level (null in pack rules)

        class Stmt { public int Line; public List<string> T; }

        public ScriptCompiler(Pack p) { pack = p; }

        // ------------------------------------------------------------ passes
        /// Pass 1: the verbs and nouns rules are written for.
        public void RegisterWords(string script, string label)
        {
            where = label;
            foreach (Stmt s in Split(script))
                if (Is(s, 0, "ON") && s.T.Count > 1 && !IsTrigger(s.T[1]))
                    Try(s, () =>
                    {
                        VerbId(Up(s.T[1]));
                        if (s.T.Count > 2 && !IsKey(s.T[2]) && !Is(s, 2, "ANY")) NounId(Up(s.T[2]));
                    });
        }

        /// Pass 2: synonyms.
        public void Synonyms(string script, string label)
        {
            where = label;
            foreach (Stmt s in Split(script)) if (Is(s, 0, "SYNONYM")) Try(s, () => Synonym(s));
        }

        /// Pass 3: the rules, into rs. scopeType = 0 or type index + 1; selfInst =
        /// the placed object the rules belong to (-1 = none); names = the level's objects.
        public void Compile(string script, string label, RuleSet rs, int scopeType, int selfInst, Dictionary<string, int> names)
        {
            where = label;
            cur = rs;
            scope = scopeType;
            self = selfInst;
            insts = names;
            foreach (Stmt s in Split(script))
            {
                if (Is(s, 0, "SYNONYM")) continue;
                if (Is(s, 0, "ON") || Is(s, 0, "EVERY")) Try(s, () => Rule(s));
                else Err(s, "a rule starts with on, every or synonym, not '" + s.T[0] + "'.");
            }
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

        // ------------------------------------------------------------ statements
        public int SafeNounGroup(List<string> words, string label)
        {
            try { return NounGroup(words); }
            catch (Exception ex) { Errors.Add(label + ": " + ex.Message); return 0; }
        }

        int NounGroup(List<string> words)
        {
            int id = 0;
            foreach (string w in words) { CheckWord(w); if (Nouns.ContainsKey(w)) { id = Nouns[w]; break; } }
            if (id == 0) { if (nextNoun > 254) throw new Exception("too many nouns."); id = nextNoun++; }
            foreach (string w in words) if (!Nouns.ContainsKey(w)) Nouns[w] = id;
            return id;
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
            else throw new Exception("'" + old + "' is not a verb or noun used anywhere in the pack.");
        }

        void Rule(Stmt s)
        {
            int verb, noun = 0, i;
            if (Is(s, 0, "EVERY")) { if (!Is(s, 1, "TURN")) throw new Exception("write: every turn ..."); verb = Every; i = 2; }
            else
            {
                if (s.T.Count < 2) throw new Exception("on needs a verb or arrive, bump or start.");
                i = 2;
                string w = Up(s.T[1]);
                if (w == "ARRIVE") verb = Arrive;
                else if (w == "BUMP") verb = Bump;
                else if (w == "START") verb = Start;
                else
                {
                    verb = VerbId(w);
                    if (i < s.T.Count && !IsKey(s.T[i]))
                    {
                        if (!Is(s, i, "ANY")) noun = NounId(Up(s.T[i]));
                        i++;
                    }
                }
            }
            List<byte[]> conds = new List<byte[]>(), acts = new List<byte[]>();
            if (self >= 0) conds.Add(new byte[] { 21, (byte)self, 0, 0 });
            if (i < s.T.Count && Is(s, i, "IF"))
            {
                i++;
                while (true)
                {
                    conds.AddRange(Cond(s, ref i));
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
            List<byte> r = new List<byte> { (byte)verb, (byte)noun, (byte)scope, (byte)conds.Count, (byte)acts.Count };
            foreach (byte[] c in conds) r.AddRange(c);
            foreach (byte[] a in acts) r.AddRange(a);
            cur.Rules.Add(r.ToArray());
        }

        static byte[] Op(int op, int a, int b, int c, bool not = false)
        {
            return new[] { (byte)(op | (not ? 0x80 : 0)), (byte)a, (byte)b, (byte)c };
        }

        List<byte[]> Cond(Stmt s, ref int i)
        {
            bool not = false;
            if (Is(s, i, "NOT")) { not = true; i++; }
            string k = Word(s, i++);
            List<byte[]> r = new List<byte[]>();
            int x, y;
            switch (k)
            {
                case "CARRIED":
                case "HAS":
                    if (IsInst(s, i)) { r.Add(Op(1, Item(s, i++), 0, 0, not)); break; }
                    {
                        int t = TypeArg(s, i++), n = 0;
                        if (IsNum(s, i)) n = Num(s, i++, 1, 255);
                        r.Add(Op(11, t, n, 0, not));
                        break;
                    }
                case "HERE":
                    if (IsInst(s, i)) r.Add(Op(2, Item(s, i++), 0, 0, not));
                    else r.Add(Op(18, TypeArg(s, i++), 0, 0, not));
                    break;
                case "PRESENT": r.Add(Op(3, Item(s, i++), 0, 0, not)); break;
                case "EXISTS": r.Add(Op(4, Item(s, i++), 0, 0, not)); break;
                case "NEAR": r.Add(Op(13, Item(s, i++), 0, 0, not)); break;
                case "AT": Xy(s, ref i, false, out x, out y); r.Add(Op(5, x, y, 0, not)); break;
                case "AHEAD":
                case "FACING":
                    if (IsNum(s, i)) { Xy(s, ref i, false, out x, out y); r.Add(Op(12, x, y, 0, not)); }
                    else if (IsInst(s, i)) r.Add(Op(9, Item(s, i++), 0, 0, not));
                    else r.Add(Op(17, TypeArg(s, i++), 0, 0, not));
                    break;
                case "FLAG": r.Add(Op(6, Flag(s, i++), 0, 0, not)); break;
                case "COUNTER":
                    {
                        int c = Counter(s, i++);
                        Compare(s, ref i, not, 7, 8, c, r, 65535);
                        break;
                    }
                case "VALUE":
                    {
                        int it = IsRel(s, i) ? This : Item(s, i++);
                        if (it == This) NeedScope();
                        Compare(s, ref i, not, 14, 15, it, r);
                        break;
                    }
                case "HP": Compare(s, ref i, not, 16, -16, 0, r); break;
                case "CHANCE": r.Add(Op(10, Math.Min(255, (Num(s, i++, 0, 100) * 256 + 50) / 100), 0, 0, not)); break;
                case "IS":
                    if (IsInst(s, i)) r.Add(Op(21, Item(s, i++), 0, 0, not));
                    else { NeedScope(); r.Add(Op(19, TypeArg(s, i++), 0, 0, not)); }
                    break;
                case "MATCHES": NeedScope(); r.Add(Op(20, TypeArg(s, i++), 0, 0, not)); break;
                default: throw new Exception("unknown condition '" + k + "'.");
            }
            return r;
        }

        /// REL N for a value compared with op ge (a >= N) or lt (a < N); lt < 0 means "not ge".
        void Compare(Stmt s, ref int i, bool not, int ge, int lt, int a, List<byte[]> r, int max = 255)
        {
            string rel = Tok(s, i++);
            int n = Num(s, i++, 0, max);
            List<KeyValuePair<bool, int>> parts = new List<KeyValuePair<bool, int>>();   // (is ge, N)
            if (rel == ">=") parts.Add(new KeyValuePair<bool, int>(true, n));
            else if (rel == ">") parts.Add(new KeyValuePair<bool, int>(true, Math.Min(max, n + 1)));
            else if (rel == "<") parts.Add(new KeyValuePair<bool, int>(false, n));
            else if (rel == "<=") parts.Add(new KeyValuePair<bool, int>(false, Math.Min(max, n + 1)));
            else if (rel == "=")
            {
                if (not) throw new Exception("'not ... = N' can't be tested; use < and > in two rules.");
                parts.Add(new KeyValuePair<bool, int>(true, n));
                if (n < max) parts.Add(new KeyValuePair<bool, int>(false, n + 1));
            }
            else throw new Exception("compare with >=, >, =, < or <=, not '" + rel + "'.");
            foreach (KeyValuePair<bool, int> p in parts)
            {
                bool g = p.Key, neg = not;
                int op = g ? ge : lt;
                if (op < 0) { op = -op; neg = !neg; }
                // counter ops: a = counter, b,c = N (16-bit); value ops: a = object, b = N; hp: a = N
                r.Add(op == 16 ? Op(op, p.Value, 0, 0, neg) : Op(op, a, p.Value & 255, p.Value >> 8, neg));
            }
        }

        byte[] Act(Stmt s, ref int i)
        {
            string k = Word(s, i++);
            int x, y, n, c;
            switch (k)
            {
                case "SAY":
                case "SPEAK":
                    if (!Tok(s, i).StartsWith("\""))
                    {
                        int it = Item(s, i++);
                        return Op(k == "SAY" ? 30 : 31, it, Num(s, i++, 1, 20), 0);
                    }
                    return k == "SAY" ? Op(1, cur.Text(Quoted(s, i++), 30), 0, 0) : Op(2, cur.Speech(Quoted(s, i++)), 0, 0);
                case "DIE": return Op(21, cur.Text(Quoted(s, i++), 30), 0, 0);
                case "SET": return Op(3, Flag(s, i++), 0, 0);
                case "CLEAR": return Op(4, Flag(s, i++), 0, 0);
                case "GET": return Op(5, Item(s, i++), 0, 0);
                case "DROP": return Op(6, Item(s, i++), 0, 0);
                case "DESTROY":
                case "REMOVE": return Op(7, Item(s, i++), 0, 0);
                case "PUT":
                    {
                        int it = Item(s, i++);
                        if (!Is(s, i, "AT")) throw new Exception("write: put OBJECT at X,Y");
                        i++;
                        Xy(s, ref i, true, out x, out y);
                        return Op(8, it, x, y);
                    }
                case "SWAP": { int a = Item(s, i++); return Op(9, a, Item(s, i++), 0); }
                case "CELL":
                    {
                        Xy(s, ref i, true, out x, out y);
                        int kind = Array.IndexOf(CellKinds, Word(s, i));
                        if (kind < 0) throw new Exception("a cell becomes floor, wall or open (floor, and solid objects there are removed), not '" + s.T[i] + "'.");
                        i++;
                        return Op(10, x, y, kind);
                    }
                case "GOTO": Xy(s, ref i, true, out x, out y); return Op(11, x, y, 0);
                case "COUNTER":
                    {
                        int ctr = Counter(s, i++);
                        string rel = Tok(s, i++);
                        if (rel == "=") { n = Num(s, i++, 0, 65535); return Op(12, ctr, n & 255, n >> 8); }
                        if (rel != "+" && rel != "-") throw new Exception("write: counter NAME = N, counter NAME + N or counter NAME - N");
                        NumVal(s, ref i, 255, out n, out c);
                        return Op(rel == "+" ? 13 : 14, ctr, n, c);
                    }
                case "ADD":
                case "SUB":
                    {
                        int ctr = Counter(s, i++);
                        NumVal(s, ref i, 255, out n, out c);
                        return Op(k == "ADD" ? 13 : 14, ctr, n, c);
                    }
                case "SCORE": NumVal(s, ref i, 65535, out n, out c); return Op(15, n & 255, n >> 8, c);
                case "HURT":
                    {
                        NumVal(s, ref i, 255, out n, out c);
                        int rnd = 0;
                        if (Is(s, i, "RANDOM")) { rnd = 1; i++; }
                        return Op(16, n, rnd, c);
                    }
                case "HEAL": NumVal(s, ref i, 255, out n, out c); return Op(17, n, 0, c);
                case "GIVE":
                case "TAKE":
                    {
                        int t = TypeArg(s, i++);
                        n = IsNum(s, i) ? Num(s, i++, 1, 255) : 0;
                        return Op(k == "GIVE" ? 18 : 19, t, n, 0);
                    }
                case "WIN": return Op(20, 0, 0, 0);
                case "INVENTORY": return Op(22, 0, 0, 0);
                case "LOOK": return Op(23, 0, 0, 0);
                case "CONTINUE": return Op(24, 0, 0, 0);
                case "NEXT": return Op(25, 0, 0, 0);
                case "STOP": return Op(34, 0, 0, 0);
                case "BECOME":
                    {
                        // become TYPE (this object) | become OBJECT TYPE
                        int it = This;
                        if (i + 1 < s.T.Count && s.T[i + 1] != ";") it = Item(s, i++);
                        else NeedScope();
                        return Op(26, it, TypeArg(s, i++), 0);
                    }
                case "VALUE":
                    {
                        // value [OBJECT] = N | + N | - N
                        int it = This;
                        if (Tok(s, i) != "=" && Tok(s, i) != "+" && Tok(s, i) != "-") it = Item(s, i++);
                        else NeedScope();
                        string rel = Tok(s, i++);
                        int v = Num(s, i++, 0, 255);
                        if (rel == "=") return Op(27, it, v, 0);
                        if (rel == "+") return Op(28, it, v, 0);
                        if (rel == "-") return Op(29, it, v, 0);
                        throw new Exception("write: value = N, value + N or value - N");
                    }
                case "CREATE":
                    {
                        int t = TypeArg(s, i++);
                        if (!Is(s, i, "AT")) throw new Exception("write: create TYPE at X,Y");
                        i++;
                        Xy(s, ref i, true, out x, out y);
                        return Op(32, t, x, y);
                    }
                case "USEUP": NeedScope(); return Op(33, TypeArg(s, i++), 0, 0);
                default: throw new Exception("unknown action '" + k + "'.");
            }
        }

        // ------------------------------------------------------------ arguments
        /// A number, or "value" (this object's value) or "p1" (its P1).
        void NumVal(Stmt s, ref int i, int max, out int n, out int c)
        {
            string w = Word(s, i);
            if (w == "VALUE") { NeedScope(); n = 0; c = 1; i++; return; }
            if (w == "P1") { NeedScope(); n = 0; c = 2; i++; return; }
            n = Num(s, i++, 0, max);
            c = 0;
        }

        /// X,Y, or "target" (this object's P1,P2) where allowed.
        void Xy(Stmt s, ref int i, bool target, out int x, out int y)
        {
            if (Is(s, i, "TARGET"))
            {
                if (!target) throw new Exception("'target' can't be used here; give X,Y.");
                NeedScope();
                x = 255; y = 0; i++;
                return;
            }
            x = Num(s, i, 0, Level.W - 1);
            Expect(s, i + 1, ",");
            y = Num(s, i + 2, 0, Level.H - 1);
            i += 3;
        }

        void NeedScope()
        {
            if (scope == 0) throw new Exception("'this', 'value', 'p1' and 'target' only make sense in an object's behaviours.");
        }

        bool IsInst(Stmt s, int i)
        {
            if (i >= s.T.Count) return false;
            if (Is(s, i, "THIS")) return true;
            return insts != null && insts.ContainsKey(s.T[i]);
        }

        int Item(Stmt s, int i)
        {
            string n = Tok(s, i);
            if (n.Equals("THIS", StringComparison.OrdinalIgnoreCase)) { NeedScope(); return This; }
            int id;
            if (insts != null && insts.TryGetValue(n, out id)) return id;
            if (insts == null) throw new Exception("'" + n + "': pack and object rules can only refer to 'this' (or to kinds of object with has, here, give...).");
            throw new Exception("no object called '" + n + "' on this level.");
        }

        int TypeArg(Stmt s, int i)
        {
            string n = Tok(s, i);
            int t = pack.TypeIndex(n);
            if (t < 0) throw new Exception("no kind of object called '" + n + "'.");
            return t;
        }

        int VerbId(string w)
        {
            int id;
            if (BuiltinVerbs.TryGetValue(w, out id)) return id;
            if (Verbs.TryGetValue(w, out id)) return id;
            CheckWord(w);
            if (nextVerb > 252) throw new Exception("too many verbs.");
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

        int Flag(Stmt s, int i)
        {
            string n = Up(Tok(s, i));
            int id;
            if (!Flags.TryGetValue(n, out id)) { if (Flags.Count >= 32) throw new Exception("more than 32 flags."); Flags[n] = id = Flags.Count; }
            return id;
        }

        public int CounterId(string name)
        {
            string n = Up(name);
            int id;
            if (!Counters.TryGetValue(n, out id)) { if (Counters.Count >= 16) throw new Exception("more than 16 counters."); Counters[n] = id = Counters.Count; }
            return id;
        }
        int Counter(Stmt s, int i) { return CounterId(Tok(s, i)); }

        static string Up(string s) { return (s ?? "").ToUpperInvariant(); }
        static void CheckWord(string w)
        {
            if (!Regex.IsMatch(w, @"^[A-Z0-9?][A-Z0-9]*$") || w.Length > 12) throw new Exception("'" + w + "' is not a usable word (letters and digits, up to 12).");
        }
        static readonly string[] Keys = { "IF", "THEN", "AND" };
        static bool IsKey(string t) { return Keys.Contains(Up(t)); }
        static bool IsTrigger(string t) { string u = Up(t); return u == "ARRIVE" || u == "BUMP" || u == "START"; }
        static bool Is(Stmt s, int i, string w) { return i < s.T.Count && s.T[i].Equals(w, StringComparison.OrdinalIgnoreCase); }
        static bool IsNum(Stmt s, int i) { int v; return i < s.T.Count && int.TryParse(s.T[i], out v); }
        static bool IsRel(Stmt s, int i) { return i < s.T.Count && (s.T[i] == "=" || s.T[i] == "<" || s.T[i] == ">" || s.T[i] == "<=" || s.T[i] == ">="); }
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
            string[] lines = (script ?? "").Replace("\r", "").Split('\n');
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

        public static List<string> Tokens(string line)
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
                if (ch == ',' || ch == ';' || ch == '=' || ch == '+' || ch == '-') { t.Add(ch.ToString()); i++; continue; }
                if (ch == '<' || ch == '>')
                {
                    if (i + 1 < line.Length && line[i + 1] == '=') { t.Add(ch + "="); i += 2; }
                    else { t.Add(ch.ToString()); i++; }
                    continue;
                }
                int k = i;
                while (k < line.Length && !char.IsWhiteSpace(line[k]) && ",;=<>\"#+-".IndexOf(line[k]) < 0) k++;
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
