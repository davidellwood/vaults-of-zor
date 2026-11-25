// ZorEdit - Behaviour.cs: the When / If / Do behaviour builder, the behaviour
// list that shows a script as rules, and the object library.
//
// A rule is parsed into rows (one per condition or action) by matching it
// against templates such as "counter {C} {R} {N}"; each row gets a drop-down
// for its kind and a field per placeholder, and is written back as script text.
// Anything the templates don't cover stays as an editable text row, so no rule
// is ever lost by opening it in the builder.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZorEdit
{
    /// Where a script lives, so the builder knows what "this" and object names mean.
    public class BehaviourContext
    {
        public Pack Pack;
        public Level Level;                 // null for pack and object-type scripts
        public int Scope;                   // 0 = no particular object, else type index + 1
        public int Self = -1;               // placed object index, or -1
        public string Title = "";
        public bool HasThis { get { return Scope > 0; } }
        public Action Changed;              // called when the builder changes the level (e.g. names an object)
    }

    // ==================================================================== map picker
    /// Shows the level map; clicking a cell picks it (or an object on it).
    public class MapPicker : Form
    {
        readonly BehaviourContext ctx;
        readonly bool wantObj;
        readonly Label hint;
        public string Result;

        public MapPicker(BehaviourContext c, bool obj)
        {
            ctx = c; wantObj = obj;
            Text = obj ? "Pick an object on the map" : "Pick a cell on the map";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            MapCanvas m = new MapCanvas { Pack = c.Pack, Level = c.Level, Location = new Point(8, 8), Selected = new Point(-1, -1) };
            hint = new Label { Location = new Point(8, m.Bottom + 6), AutoSize = true,
                               Text = obj ? "Click a cell with an object on it. Esc cancels." : "Click a cell. Esc cancels." };
            m.Hover += (x, y) => hint.Text = x < 0 ? "" : x + "," + y + (c.Level.Wall[y, x] ? "  wall" : "") +
                                     string.Concat(c.Level.At(x, y).Select(o => "  " + o.Type + (o.Name.Length > 0 ? " (" + o.Name + ")" : "")));
            m.Painted += (x, y, right, first) => { if (first) { m.Hold = true; BeginInvoke(new Action(() => Pick(x, y, m))); } };
            Controls.Add(m);
            Controls.Add(hint);
            ClientSize = new Size(m.Right + 8, hint.Bottom + 10);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        void Pick(int x, int y, MapCanvas m)
        {
            if (!wantObj) { Result = x + "," + y; DialogResult = DialogResult.OK; Close(); return; }
            List<Inst> here = ctx.Level.At(x, y).ToList();
            if (here.Count == 0) { hint.Text = "Nothing on " + x + "," + y + ". Click a cell with an object on it."; return; }
            if (here.Count == 1) { Choose(here[0]); return; }
            ContextMenuStrip menu = new ContextMenuStrip();
            foreach (Inst o in here)
            {
                Inst oo = o;
                menu.Items.Add(o.Type + (o.Name.Length > 0 ? " (" + o.Name + ")" : ""), null, (s, e) => Choose(oo));
            }
            menu.Show(m, m.PointToClient(Cursor.Position));
        }

        void Choose(Inst o)
        {
            if (ctx.HasThis && ctx.Self >= 0 && ctx.Self < ctx.Level.Objs.Count && ctx.Level.Objs[ctx.Self] == o)
            {
                Result = "this"; DialogResult = DialogResult.OK; Close(); return;
            }
            if (o.Name.Length == 0)
            {
                // a rule can only name an object that has a name: give it one
                string id = Regex.Replace(o.Type.ToLowerInvariant(), "[^a-z0-9]", "");
                if (id.Length == 0) id = "obj";
                int n = 1;
                while (ctx.Level.Named(id + n) != null || ctx.Pack.Type(id + n) != null) n++;
                o.Name = id + n;
                if (ctx.Changed != null) ctx.Changed();
            }
            Result = o.Name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ==================================================================== rule model
    public class RowDef
    {
        public string Label, Tpl;
        public bool Cond;
        public RowDef(bool cond, string label, string tpl) { Cond = cond; Label = label; Tpl = tpl; }
        public string[] Parts { get { return Tpl.Split(' '); } }
        public override string ToString() { return Label; }
    }

    public class Row
    {
        public RowDef D;
        public bool Not;
        public List<string> Args = new List<string>();
    }

    public class RuleModel
    {
        public string Trigger = "verb";     // verb, arrive, bump, every, start
        public string Verb = "", Noun = "";
        public string Where = "";           // for object rules on a command: "", here, ahead, carried
        public List<Row> Conds = new List<Row>(), Acts = new List<Row>();

        public static readonly RowDef[] CondDefs =
        {
            new RowDef(true, "player carries something of the kind", "has {T}"),
            new RowDef(true, "player carries some of the kind (at least N)", "has {T} {N}"),
            new RowDef(true, "player carries the object", "carried {O}"),
            new RowDef(true, "object / kind is here (player's cell)", "here {OT}"),
            new RowDef(true, "cell in front of the player is", "ahead {XY}"),
            new RowDef(true, "object / kind is in front of the player", "ahead {OT}"),
            new RowDef(true, "object is here or next to the player", "near {O}"),
            new RowDef(true, "object is here or carried", "present {O}"),
            new RowDef(true, "object is somewhere on the level or carried", "exists {O}"),
            new RowDef(true, "player is at cell", "at {XY}"),
            new RowDef(true, "flag is set", "flag {F}"),
            new RowDef(true, "counter compares", "counter {C} {R} {N}"),
            new RowDef(true, "this object's value compares", "value {R} {N}"),
            new RowDef(true, "an object's value compares", "value {O} {R} {N}"),
            new RowDef(true, "player's HP compares", "hp {R} {N}"),
            new RowDef(true, "chance (percent)", "chance {N}"),
            new RowDef(true, "this object is of the kind", "is {T}"),
            new RowDef(true, "player carries a kind with the same P1 as this", "matches {T}"),
        };
        public static readonly RowDef[] ActDefs =
        {
            new RowDef(false, "say text", "say {S}"),
            new RowDef(false, "say a text of an object (2, 3...)", "say {O} {N}"),
            new RowDef(false, "speak text (speech bubble)", "speak {S}"),
            new RowDef(false, "speak a text of an object (\"<NAME> SAYS:\")", "speak {O} {N}"),
            new RowDef(false, "give the player a new one of the kind", "give {T}"),
            new RowDef(false, "give the player N of the kind", "give {T} {N}"),
            new RowDef(false, "take one of the kind from the player", "take {T}"),
            new RowDef(false, "take N of the kind from the player", "take {T} {N}"),
            new RowDef(false, "use up a carried kind with the same P1 as this", "useup {T}"),
            new RowDef(false, "remove an object", "remove {O}"),
            new RowDef(false, "the player picks up an object", "get {O}"),
            new RowDef(false, "drop an object here", "drop {O}"),
            new RowDef(false, "put an object at a cell", "put {O} at {XY}"),
            new RowDef(false, "swap two objects' places", "swap {O} {O}"),
            new RowDef(false, "this object becomes another kind", "become {T}"),
            new RowDef(false, "create an object of the kind at a cell", "create {T} at {XY}"),
            new RowDef(false, "set this object's value", "value = {N}"),
            new RowDef(false, "add to this object's value", "value + {N}"),
            new RowDef(false, "subtract from this object's value", "value - {N}"),
            new RowDef(false, "change a cell", "cell {XY} {K}"),
            new RowDef(false, "move the player to a cell", "goto {XY}"),
            new RowDef(false, "set a flag", "set {F}"),
            new RowDef(false, "clear a flag", "clear {F}"),
            new RowDef(false, "set a counter", "counter {C} = {N}"),
            new RowDef(false, "add to a counter", "add {C} {V}"),
            new RowDef(false, "subtract from a counter", "sub {C} {V}"),
            new RowDef(false, "add to the score", "score {V}"),
            new RowDef(false, "hurt the player", "hurt {V}"),
            new RowDef(false, "hurt the player 1 to N at random", "hurt {V} random"),
            new RowDef(false, "heal the player (0 = fully)", "heal {V}"),
            new RowDef(false, "go on to the next level", "next"),
            new RowDef(false, "win the game", "win"),
            new RowDef(false, "the player dies", "die {S}"),
            new RowDef(false, "show the inventory", "inventory"),
            new RowDef(false, "look around", "look"),
            new RowDef(false, "carry on with the built-in command too", "continue"),
            new RowDef(false, "stop (no more rules this time)", "stop"),
        };
        public static readonly RowDef RawCond = new RowDef(true, "other (written as text)", "{RAW}");
        public static readonly RowDef RawAct = new RowDef(false, "other (written as text)", "{RAW}");

        /// Parse one rule; null if it is not an on/every rule at all.
        public static RuleModel Parse(string text, bool hasThis)
        {
            List<string> t = ScriptCompiler.Tokens((text ?? "").Replace("\r", "").Replace("\n", " "));
            if (t.Count == 0) return null;
            RuleModel m = new RuleModel();
            int i;
            string w0 = t[0].ToUpperInvariant();
            if (w0 == "EVERY") { m.Trigger = "every"; i = t.Count > 1 && t[1].Equals("turn", StringComparison.OrdinalIgnoreCase) ? 2 : 1; }
            else if (w0 == "ON" && t.Count > 1)
            {
                string w = t[1].ToUpperInvariant();
                i = 2;
                if (w == "ARRIVE" || w == "BUMP" || w == "START") m.Trigger = w.ToLowerInvariant();
                else
                {
                    m.Verb = w;
                    if (i < t.Count && !Kw(t[i], "IF") && !Kw(t[i], "THEN")) { if (!Kw(t[i], "ANY")) m.Noun = t[i].ToUpperInvariant(); i++; }
                }
            }
            else return null;
            List<List<string>> conds = new List<List<string>>(), acts = new List<List<string>>();
            if (i < t.Count && Kw(t[i], "IF"))
            {
                i++;
                List<string> cur = new List<string>();
                while (i < t.Count && !Kw(t[i], "THEN"))
                {
                    if (Kw(t[i], "AND")) { conds.Add(cur); cur = new List<string>(); }
                    else cur.Add(t[i]);
                    i++;
                }
                conds.Add(cur);
            }
            if (i < t.Count && Kw(t[i], "THEN"))
            {
                i++;
                List<string> cur = new List<string>();
                for (; i < t.Count; i++)
                {
                    if (t[i] == ";") { acts.Add(cur); cur = new List<string>(); }
                    else cur.Add(t[i]);
                }
                acts.Add(cur);
            }
            foreach (List<string> c in conds.Where(c => c.Count > 0)) m.Conds.Add(Match(c, true));
            foreach (List<string> a in acts.Where(a => a.Count > 0)) m.Acts.Add(Match(a, false));
            // "only when this object is here / in front / carried"
            if (hasThis && m.Trigger == "verb" && m.Conds.Count > 0)
            {
                Row f = m.Conds[0];
                if (!f.Not && f.Args.Count == 1 && f.Args[0].Equals("this", StringComparison.OrdinalIgnoreCase) &&
                    (f.D.Tpl == "here {OT}" || f.D.Tpl == "ahead {OT}" || f.D.Tpl == "carried {O}"))
                {
                    m.Where = f.D.Tpl.Split(' ')[0];
                    m.Conds.RemoveAt(0);
                }
            }
            return m;
        }

        static bool Kw(string t, string w) { return t.Equals(w, StringComparison.OrdinalIgnoreCase); }

        static Row Match(List<string> tok, bool cond)
        {
            bool not = false;
            List<string> t = tok;
            if (cond && t.Count > 0 && Kw(t[0], "NOT")) { not = true; t = t.Skip(1).ToList(); }
            if (!cond && t.Count > 0 && Kw(t[0], "DESTROY")) { t = new List<string>(t); t[0] = "remove"; }
            foreach (RowDef d in cond ? CondDefs : ActDefs)
            {
                List<string> args = TryMatch(d, t);
                if (args != null) return new Row { D = d, Not = not, Args = args };
            }
            return new Row { D = cond ? RawCond : RawAct, Args = new List<string> { Detok(tok) } };
        }

        static List<string> TryMatch(RowDef d, List<string> t)
        {
            List<string> args = new List<string>();
            int i = 0;
            int n;
            foreach (string p in d.Parts)
            {
                if (p.StartsWith("{"))
                {
                    if (i >= t.Count) return null;
                    switch (p)
                    {
                        case "{S}": if (!t[i].StartsWith("\"")) return null; args.Add(t[i].Substring(1)); i++; break;
                        case "{N}": if (!int.TryParse(t[i], out n)) return null; args.Add(t[i]); i++; break;
                        case "{V}": if (!int.TryParse(t[i], out n) && !Kw(t[i], "value") && !Kw(t[i], "p1")) return null; args.Add(t[i].ToLowerInvariant() == "value" || t[i].ToLowerInvariant() == "p1" ? t[i].ToLowerInvariant() : t[i]); i++; break;
                        case "{R}": if (!new[] { ">=", ">", "=", "<", "<=" }.Contains(t[i])) return null; args.Add(t[i]); i++; break;
                        case "{K}": if (!new[] { "floor", "wall", "open" }.Contains(t[i].ToLowerInvariant())) return null; args.Add(t[i].ToLowerInvariant()); i++; break;
                        case "{XY}":
                            if (Kw(t[i], "target")) { args.Add("target"); i++; break; }
                            if (i + 2 >= t.Count || !int.TryParse(t[i], out n) || t[i + 1] != "," || !int.TryParse(t[i + 2], out n)) return null;
                            args.Add(t[i] + "," + t[i + 2]); i += 3; break;
                        default:            // {O} {OT} {T} {F} {C}: one word
                            if (!Regex.IsMatch(t[i], @"^[A-Za-z_][A-Za-z0-9_]*$")) return null;
                            if (Kw(t[i], "random") || Kw(t[i], "at")) return null;
                            args.Add(t[i]); i++; break;
                    }
                }
                else
                {
                    if (i >= t.Count || !Kw(t[i], p)) return null;
                    i++;
                }
            }
            return i == t.Count ? args : null;
        }

        /// Tokens back to text.
        public static string Detok(List<string> t)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < t.Count; i++)
            {
                string s = t[i].StartsWith("\"") ? t[i] + "\"" : t[i];
                if (i > 0 && s != "," && s != ";" && t[i - 1] != ",") b.Append(' ');
                b.Append(s);
            }
            return b.ToString();
        }

        public static string RowText(Row r)
        {
            List<string> o = new List<string>();
            if (r.Not) o.Add("not");
            int a = 0;
            foreach (string p in r.D.Parts)
            {
                if (!p.StartsWith("{")) { o.Add(p); continue; }
                string v = a < r.Args.Count ? r.Args[a] : "";
                a++;
                if (p == "{S}") v = "\"" + (v ?? "").Replace("\"", "'") + "\"";
                else if (p == "{XY}") v = Regex.Replace(v ?? "", @"\s+", "");
                else if (p != "{RAW}") v = (v ?? "").Trim();
                o.Add(v.Length == 0 ? "?" : v);
            }
            return string.Join(" ", o);
        }

        public string Text()
        {
            string head = Trigger == "every" ? "every turn" : Trigger == "verb" ? "on " + (Verb.Length > 0 ? Verb : "?") + (Noun.Length > 0 ? " " + Noun : "") : "on " + Trigger;
            List<string> c = new List<string>();
            if (Where.Length > 0) c.Add(Where + " this");
            c.AddRange(Conds.Select(RowText));
            string s = head + (c.Count > 0 ? " if " + string.Join(" and ", c) : "") + " then ";
            s += Acts.Count == 0 ? "?" : string.Join("; ", Acts.Select(RowText));
            return s;
        }
    }

    // ==================================================================== builder dialog
    public class BehaviourForm : Form
    {
        readonly BehaviourContext ctx;
        ComboBox cWhen, cVerb, cNoun, cWhere;
        Label lVerb, lNoun, lWhere, lErr;
        FlowLayoutPanel pConds, pActs;
        TextBox tPreview, tRaw;
        CheckBox chRaw;
        Panel builder;
        bool building;
        public string Result;
        static readonly string[] WhenKeys = { "verb", "arrive", "bump", "every", "start" };

        public BehaviourForm(BehaviourContext c, string rule)
        {
            ctx = c;
            Text = "Behaviour - " + c.Title;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;

            TableLayoutPanel head = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 4, Padding = new Padding(8, 8, 8, 0) };
            cWhen = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            cWhen.Items.AddRange(c.HasThis
                ? new object[] { "the player types a command", "the player steps onto it", "the player walks into it", "every turn", "the level starts" }
                : new object[] { "the player types a command", "the player arrives anywhere", "the player walks into something", "every turn", "the level starts" });
            cVerb = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
            cNoun = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
            cWhere = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            cWhere.Items.AddRange(new object[] { "wherever it is", "it is here (player's cell)", "it is in front of the player", "the player carries it" });
            Words w = Words.Gather(c.Pack);
            cVerb.Items.AddRange(w.Verbs.ToArray());
            cNoun.Items.Add("(any)");
            cNoun.Items.AddRange(w.Nouns.ToArray());
            head.Controls.Add(new Label { Text = "When", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) }, 0, 0);
            head.Controls.Add(cWhen, 1, 0);
            lVerb = new Label { Text = "Verb", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            FlowLayoutPanel vn = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            lNoun = new Label { Text = "Noun", AutoSize = true, Padding = new Padding(8, 4, 0, 0) };
            vn.Controls.Add(lVerb); vn.Controls.Add(cVerb); vn.Controls.Add(lNoun); vn.Controls.Add(cNoun);
            head.Controls.Add(vn, 2, 0);
            lWhere = new Label { Text = "only if", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            head.Controls.Add(lWhere, 1, 1);
            head.Controls.Add(cWhere, 2, 1);

            pConds = RowPanel();
            pActs = RowPanel();
            Button addC = new Button { Text = "+ Add condition", Width = 130 };
            addC.Click += (s, e) => { AddRow(pConds, new Row { D = RuleModel.CondDefs[0], Args = new List<string>() }); Changed(); };
            Button addA = new Button { Text = "+ Add action", Width = 130 };
            addA.Click += (s, e) => { AddRow(pActs, new Row { D = RuleModel.ActDefs[0], Args = new List<string>() }); Changed(); };

            builder = new Panel { Dock = DockStyle.Fill };
            SplitContainer sp = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
            sp.Panel1.Controls.Add(pConds);
            sp.Panel1.Controls.Add(Bar("If (all of these must hold; none = always)", addC));
            sp.Panel2.Controls.Add(pActs);
            sp.Panel2.Controls.Add(Bar("Do (in this order)", addA));
            builder.Controls.Add(sp);
            tRaw = new TextBox { Dock = DockStyle.Fill, Multiline = true, Font = new Font("Consolas", 9.5f), ScrollBars = ScrollBars.Both, WordWrap = true, Visible = false };
            tRaw.TextChanged += (s, e) => { if (!building) Check(tRaw.Text); };

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 118, Padding = new Padding(8) };
            tPreview = new TextBox { Dock = DockStyle.Top, Height = 44, Multiline = true, ReadOnly = true, Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(245, 245, 235) };
            lErr = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Color.DarkRed };
            FlowLayoutPanel btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.RightToLeft };
            Button cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            Button ok = new Button { Text = "OK", Width = 90 };
            ok.Click += (s, e) => Accept();
            chRaw = new CheckBox { Text = "Edit as text", AutoSize = true, Padding = new Padding(0, 6, 20, 0) };
            chRaw.CheckedChanged += (s, e) => SetRaw(chRaw.Checked);
            btns.Controls.Add(cancel); btns.Controls.Add(ok); btns.Controls.Add(chRaw);
            bottom.Controls.Add(lErr);
            bottom.Controls.Add(tPreview);
            bottom.Controls.Add(btns);
            AcceptButton = null;
            CancelButton = cancel;

            Controls.Add(builder);
            Controls.Add(tRaw);
            Controls.Add(bottom);
            Controls.Add(head);

            cWhen.SelectedIndexChanged += (s, e) => { UpdateHead(); Changed(); };
            cVerb.TextChanged += (s, e) => Changed();
            cNoun.TextChanged += (s, e) => Changed();
            cWhere.SelectedIndexChanged += (s, e) => Changed();
            LoadRule(rule);
        }

        static FlowLayoutPanel RowPanel()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 0, 0, 0) };
        }

        static Panel Bar(string text, Button b)
        {
            Panel p = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 4, 8, 0) };
            Label l = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Location = new Point(8, 7) };
            b.Location = new Point(360, 2);
            p.Controls.Add(l); p.Controls.Add(b);
            return p;
        }

        void LoadRule(string rule)
        {
            building = true;
            RuleModel m = RuleModel.Parse(rule, ctx.HasThis);
            if (m == null && !string.IsNullOrWhiteSpace(rule)) { chRaw.Checked = true; tRaw.Text = rule; building = false; SetRaw(true); return; }
            if (m == null) m = new RuleModel { Trigger = ctx.HasThis ? "verb" : "arrive", Where = ctx.HasThis ? "ahead" : "" };
            cWhen.SelectedIndex = Math.Max(0, Array.IndexOf(WhenKeys, m.Trigger));
            cVerb.Text = m.Verb;
            cNoun.Text = m.Noun.Length == 0 ? "(any)" : m.Noun;
            cWhere.SelectedIndex = new[] { "", "here", "ahead", "carried" }.ToList().IndexOf(m.Where);
            pConds.Controls.Clear(); pActs.Controls.Clear();
            foreach (Row r in m.Conds) AddRow(pConds, r);
            foreach (Row r in m.Acts) AddRow(pActs, r);
            if (m.Acts.Count == 0) AddRow(pActs, new Row { D = RuleModel.ActDefs[0], Args = new List<string> { "" } });
            building = false;
            UpdateHead();
            Changed();
        }

        void UpdateHead()
        {
            bool verb = cWhen.SelectedIndex == 0;
            lVerb.Visible = cVerb.Visible = lNoun.Visible = cNoun.Visible = verb;
            lWhere.Visible = cWhere.Visible = verb && ctx.HasThis;
            if (cWhere.SelectedIndex < 0) cWhere.SelectedIndex = 0;
        }

        // ---- one row: [not] kind [fields] [up] [x]
        class RowUi
        {
            public Row R;
            public CheckBox Not;
            public ComboBox Kind;
            public FlowLayoutPanel Fields;
            public List<Control> Args = new List<Control>();
        }

        void AddRow(FlowLayoutPanel panel, Row r)
        {
            bool cond = panel == pConds;
            FlowLayoutPanel line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            RowUi u = new RowUi { R = r };
            line.Tag = u;
            if (cond)
            {
                u.Not = new CheckBox { Text = "not", AutoSize = true, Checked = r.Not, Padding = new Padding(0, 3, 0, 0) };
                u.Not.CheckedChanged += (s, e) => Changed();
                line.Controls.Add(u.Not);
            }
            u.Kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, MaxDropDownItems = 30 };
            u.Kind.Items.AddRange(cond ? RuleModel.CondDefs : RuleModel.ActDefs);
            u.Kind.Items.Add(cond ? RuleModel.RawCond : RuleModel.RawAct);
            u.Kind.SelectedItem = r.D;
            line.Controls.Add(u.Kind);
            u.Fields = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            line.Controls.Add(u.Fields);
            Button up = new Button { Text = "↑", Width = 26 };
            up.Click += (s, e) => { int i = panel.Controls.GetChildIndex(line); if (i > 0) { panel.Controls.SetChildIndex(line, i - 1); Changed(); } };
            Button del = new Button { Text = "✕", Width = 26 };
            del.Click += (s, e) => { panel.Controls.Remove(line); Changed(); };
            line.Controls.Add(up); line.Controls.Add(del);
            BuildFields(u);
            u.Kind.SelectedIndexChanged += (s, e) =>
            {
                RowDef d = (RowDef)u.Kind.SelectedItem;
                if (d == u.R.D) return;
                // keep the text when switching to "other"
                List<string> keep = d.Tpl == "{RAW}" ? new List<string> { RuleModel.RowText(Current(u)) } : u.R.Args;
                u.R = new Row { D = d, Not = u.R.Not, Args = keep };
                BuildFields(u);
                Changed();
            };
            panel.Controls.Add(line);
        }

        void BuildFields(RowUi u)
        {
            u.Fields.Controls.Clear();
            u.Args.Clear();
            Words w = Words.Gather(ctx.Pack);
            int a = 0;
            foreach (string p in u.R.D.Parts)
            {
                if (!p.StartsWith("{"))
                {
                    if (p != u.R.D.Parts[0]) u.Fields.Controls.Add(new Label { Text = p, AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
                    continue;
                }
                string v = a < u.R.Args.Count ? u.R.Args[a] : "";
                a++;
                Control c;
                switch (p)
                {
                    case "{S}": c = new TextBox { Width = 330, Text = v, CharacterCasing = CharacterCasing.Upper }; break;
                    case "{RAW}": c = new TextBox { Width = 400, Text = v }; break;
                    case "{N}": c = new TextBox { Width = 45, Text = v.Length > 0 ? v : "1" }; break;
                    case "{XY}": c = Combo(v.Length > 0 ? v : "1,1", 70, ctx.HasThis ? new[] { "target" } : new string[0]); break;
                    case "{V}": c = Combo(v.Length > 0 ? v : "1", 60, ctx.HasThis ? new[] { "value", "p1" } : new string[0]); break;
                    case "{R}": c = List(v.Length > 0 ? v : ">=", new[] { ">=", ">", "=", "<", "<=" }, 48); break;
                    case "{K}": c = List(v.Length > 0 ? v : "open", new[] { "floor", "wall", "open" }, 70); break;
                    case "{T}": c = Combo(v, 120, ctx.Pack.Types.Select(t => t.Id)); break;
                    case "{O}": c = Combo(v.Length > 0 ? v : ctx.HasThis ? "this" : "", 120, Objects(false)); break;
                    case "{OT}": c = Combo(v.Length > 0 ? v : ctx.HasThis ? "this" : "", 120, Objects(true)); break;
                    case "{F}": c = Combo(v, 110, w.Flags); break;
                    case "{C}": c = Combo(v, 110, w.Counters); break;
                    default: c = new TextBox { Width = 100, Text = v }; break;
                }
                c.TextChanged += (s, e) => Changed();
                if (c is ComboBox) ((ComboBox)c).SelectedIndexChanged += (s, e) => Changed();
                u.Args.Add(c);
                u.Fields.Controls.Add(c);
                if (ctx.Level != null && (p == "{XY}" || p == "{O}" || p == "{OT}"))
                {
                    bool obj = p != "{XY}";
                    Control field = c;
                    Button pick = new Button { Text = "Map", Width = 40, Height = c.Height + 1, Margin = new Padding(0, 2, 6, 0) };
                    tips.SetToolTip(pick, obj ? "Pick the object on the map (an unnamed object is given a name)" : "Pick the cell on the map");
                    pick.Click += (s, e) =>
                    {
                        using (MapPicker mp = new MapPicker(ctx, obj))
                            if (mp.ShowDialog(this) == DialogResult.OK) { field.Text = mp.Result; RefreshObjectLists(); }
                    };
                    u.Fields.Controls.Add(pick);
                }
            }
        }

        readonly ToolTip tips = new ToolTip();

        /// After the picker names an object, offer the new name in every object drop-down.
        void RefreshObjectLists()
        {
            foreach (FlowLayoutPanel panel in new[] { pConds, pActs })
                foreach (Control line in panel.Controls)
                {
                    RowUi u = line.Tag as RowUi;
                    if (u == null) continue;
                    for (int i = 0, a = 0; i < u.R.D.Parts.Length && a < u.Args.Count; i++)
                    {
                        string p = u.R.D.Parts[i];
                        if (!p.StartsWith("{")) continue;
                        ComboBox cb = u.Args[a++] as ComboBox;
                        if (cb == null || (p != "{O}" && p != "{OT}")) continue;
                        string t = cb.Text;
                        cb.Items.Clear();
                        cb.Items.AddRange(Objects(p == "{OT}").Distinct().Cast<object>().ToArray());
                        cb.Text = t;
                    }
                }
        }

        IEnumerable<string> Objects(bool types)
        {
            List<string> r = new List<string>();
            if (ctx.HasThis) r.Add("this");
            if (ctx.Level != null) r.AddRange(ctx.Level.Objs.Where(o => o.Name.Length > 0).Select(o => o.Name));
            if (types) r.AddRange(ctx.Pack.Types.Select(t => t.Id));
            return r;
        }

        static ComboBox Combo(string v, int w, IEnumerable<string> items)
        {
            ComboBox c = new ComboBox { Width = w, DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(items.Distinct().Cast<object>().ToArray());
            c.Text = v;
            return c;
        }
        static ComboBox List(string v, string[] items, int w)
        {
            ComboBox c = new ComboBox { Width = w, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            c.SelectedItem = items.Contains(v) ? v : items[0];
            return c;
        }

        static Row Current(RowUi u)
        {
            return new Row { D = u.R.D, Not = u.Not != null && u.Not.Checked, Args = u.Args.Select(c => c.Text).ToList() };
        }

        RuleModel Model()
        {
            RuleModel m = new RuleModel { Trigger = WhenKeys[Math.Max(0, cWhen.SelectedIndex)] };
            m.Verb = cVerb.Text.Trim().ToUpperInvariant();
            m.Noun = cNoun.Text.Trim() == "(any)" ? "" : cNoun.Text.Trim().ToUpperInvariant();
            if (m.Trigger == "verb" && ctx.HasThis) m.Where = new[] { "", "here", "ahead", "carried" }[Math.Max(0, cWhere.SelectedIndex)];
            foreach (Control c in pConds.Controls) m.Conds.Add(Current((RowUi)c.Tag));
            foreach (Control c in pActs.Controls) m.Acts.Add(Current((RowUi)c.Tag));
            return m;
        }

        void Changed()
        {
            if (building) return;
            string s = Model().Text();
            tPreview.Text = s;
            Check(s);
        }

        bool Check(string rule)
        {
            List<string> errs = CompileErrors(ctx, rule);
            lErr.ForeColor = errs.Count == 0 ? Color.DarkGreen : Color.DarkRed;
            lErr.Text = errs.Count == 0 ? "OK - the rule compiles." : errs[0];
            return errs.Count == 0;
        }

        /// Compile one rule in its context, with every word of the pack known.
        public static List<string> CompileErrors(BehaviourContext ctx, string rule)
        {
            ScriptCompiler sc = new ScriptCompiler(ctx.Pack);
            foreach (ObjType t in ctx.Pack.Types)
            {
                List<string> w = t.NounList();
                if (w.Count == 0) w.Add(t.Id.ToUpperInvariant());
                sc.SafeNounGroup(w, "x");
            }
            foreach (KeyValuePair<string, string> s in Compiler.AllScripts(ctx.Pack)) sc.RegisterWords(s.Value, s.Key);
            sc.RegisterWords(rule, "Rule");
            foreach (KeyValuePair<string, string> s in Compiler.AllScripts(ctx.Pack)) sc.Synonyms(s.Value, s.Key);
            sc.Errors.Clear();
            Dictionary<string, int> names = null;
            if (ctx.Level != null)
            {
                names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int k = 0; k < ctx.Level.Objs.Count; k++)
                    if (ctx.Level.Objs[k].Name.Length > 0 && !names.ContainsKey(ctx.Level.Objs[k].Name)) names[ctx.Level.Objs[k].Name] = k;
            }
            sc.Compile(rule, "Rule", new RuleSet(), ctx.Scope, ctx.Self, names);
            return sc.Errors.Select(e => Regex.Replace(e, @"^Rule line \d+: ", "")).ToList();
        }

        void SetRaw(bool raw)
        {
            if (raw)
            {
                if (!building && builder.Visible) { building = true; tRaw.Text = Model().Text(); building = false; }
                builder.Visible = false; tRaw.Visible = true; tPreview.Visible = false;
                Check(tRaw.Text);
            }
            else
            {
                if (RuleModel.Parse(tRaw.Text, ctx.HasThis) == null && tRaw.Text.Trim().Length > 0)
                {
                    MessageBox.Show(this, "This text is not an on / every rule, so it can only be edited as text.", "Behaviour");
                    building = true; chRaw.Checked = true; building = false;
                    return;
                }
                string t = tRaw.Text;
                builder.Visible = true; tRaw.Visible = false; tPreview.Visible = true;
                LoadRule(t);
            }
        }

        void Accept()
        {
            string s = chRaw.Checked ? tRaw.Text.Replace("\r\n", "\n").Trim() : Model().Text();
            if (!Check(s) && MessageBox.Show(this, lErr.Text + "\n\nKeep the rule anyway? (The pack can't be exported until it is fixed.)", "Behaviour", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Result = s;
            DialogResult = DialogResult.OK;
        }
    }

    /// Flags, counters, verbs and nouns used anywhere in the pack (for drop-downs).
    public class Words
    {
        public List<string> Flags = new List<string>(), Counters = new List<string>(), Verbs = new List<string>(), Nouns = new List<string>();

        public static Words Gather(Pack p)
        {
            Words w = new Words();
            string all = string.Join("\n", Compiler.AllScripts(p).Select(k => Regex.Replace(k.Value ?? "", "\"[^\"\\n]*\"?", " ")));
            foreach (Match m in Regex.Matches(all, @"\b(?:flag|set|clear)\s+([A-Za-z]\w*)", RegexOptions.IgnoreCase)) w.Flags.Add(m.Groups[1].Value.ToLowerInvariant());
            foreach (Match m in Regex.Matches(all, @"\b(?:counter|add|sub)\s+([A-Za-z]\w*)", RegexOptions.IgnoreCase)) w.Counters.Add(m.Groups[1].Value.ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(p.StatusCounter)) w.Counters.Add(p.StatusCounter.ToLowerInvariant());
            foreach (Match m in Regex.Matches(all, @"^\s*on\s+([A-Za-z?]\w*)(?:\s+([A-Za-z]\w*))?", RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                string v = m.Groups[1].Value.ToUpperInvariant();
                if (v == "ARRIVE" || v == "BUMP" || v == "START") continue;
                w.Verbs.Add(v);
                string n = m.Groups[2].Value.ToUpperInvariant();
                if (n.Length > 0 && n != "IF" && n != "THEN" && n != "ANY") w.Nouns.Add(n);
            }
            foreach (string v in new[] { "TAKE", "DROP", "LOOK", "TALK", "OPEN", "USE", "ATTACK", "DRINK", "PUSH", "PULL", "READ" }) w.Verbs.Add(v);
            foreach (ObjType t in p.Types) w.Nouns.AddRange(t.NounList());
            w.Flags = w.Flags.Distinct().OrderBy(s => s).ToList();
            w.Counters = w.Counters.Distinct().OrderBy(s => s).ToList();
            w.Verbs = w.Verbs.Distinct().OrderBy(s => s).ToList();
            w.Nouns = w.Nouns.Distinct().OrderBy(s => s).ToList();
            return w;
        }
    }

    // ==================================================================== behaviour list
    /// Shows a script as a list of rules with Add / Edit / Delete, or as text.
    public class BehaviourList : UserControl
    {
        ListBox list;
        TextBox text;
        CheckBox chText;
        Button bAdd, bEdit, bDel, bUp, bDown;
        List<string> stmts = new List<string>();
        string script = "";
        bool quiet;
        public Func<BehaviourContext> Context;
        public event EventHandler ScriptChanged;
        public string NewRule;              // starting text for Add (null = an empty rule)

        public BehaviourList()
        {
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, Font = new Font("Consolas", 8.5f) };
            list.DoubleClick += (s, e) => Edit();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) Delete(); if (e.KeyCode == Keys.Enter) Edit(); };
            text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsTab = true, Font = new Font("Consolas", 8.5f), Visible = false };
            text.TextChanged += (s, e) => { if (quiet) return; script = text.Text.Replace("\r\n", "\n"); Fire(); };
            FlowLayoutPanel b = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, WrapContents = false };
            bAdd = Btn("Add...", 62, (s, e) => Add());
            bEdit = Btn("Edit...", 58, (s, e) => Edit());
            bDel = Btn("Delete", 58, (s, e) => Delete());
            bUp = Btn("Up", 40, (s, e) => MoveRow(-1));
            bDown = Btn("Down", 48, (s, e) => MoveRow(1));
            chText = new CheckBox { Text = "As text", AutoSize = true, Padding = new Padding(4, 5, 0, 0) };
            chText.CheckedChanged += (s, e) => SetText(chText.Checked);
            b.Controls.AddRange(new Control[] { bAdd, bEdit, bDel, bUp, bDown, chText });
            Controls.Add(list);
            Controls.Add(text);
            Controls.Add(b);
        }

        static Button Btn(string t, int w, EventHandler h) { Button b = new Button { Text = t, Width = w, Height = 24 }; b.Click += h; return b; }

        public string Script
        {
            get { return script; }
            set { script = (value ?? "").Replace("\r", ""); quiet = true; text.Text = script.Replace("\n", "\r\n"); quiet = false; Split(); }
        }

        public new bool Enabled
        {
            get { return list.Enabled; }
            set { foreach (Control c in new Control[] { list, text, bAdd, bEdit, bDel, bUp, bDown, chText }) c.Enabled = value; }
        }

        void Split()
        {
            stmts.Clear();
            string[] lines = script.Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                if (lines[n].Trim().Length == 0) continue;
                string s = lines[n];
                while (n + 1 < lines.Length && lines[n + 1].Length > 0 && (lines[n + 1][0] == ' ' || lines[n + 1][0] == '\t')) s += "\n" + lines[++n];
                stmts.Add(s);
            }
            int sel = list.SelectedIndex;
            list.Items.Clear();
            foreach (string s in stmts) list.Items.Add(Regex.Replace(s, @"\n\s*", " "));
            if (sel >= 0 && sel < list.Items.Count) list.SelectedIndex = sel;
        }

        void Join()
        {
            script = string.Join("\n", stmts);
            quiet = true; text.Text = script.Replace("\n", "\r\n"); quiet = false;
            Split();
            Fire();
        }

        void Fire() { if (ScriptChanged != null) ScriptChanged(this, EventArgs.Empty); }

        void SetText(bool on)
        {
            text.Visible = on; list.Visible = !on;
            foreach (Control c in new Control[] { bAdd, bEdit, bDel, bUp, bDown }) c.Visible = !on;
            if (!on) Split();
        }

        public void Add() { Add(NewRule); }

        public void Add(string start)
        {
            if (Context == null) return;
            string r = Ask(start);
            if (r == null) return;
            Split();
            stmts.Add(r);
            Join();
            list.SelectedIndex = list.Items.Count - 1;
        }

        public int Count { get { return stmts.Count; } }
        public string Statement(int i) { return Regex.Replace(stmts[i], @"\n\s*", " "); }
        public void EditAt(int i) { if (i >= 0 && i < stmts.Count) { if (chText.Checked) chText.Checked = false; list.SelectedIndex = i; Edit(); } }

        void Edit()
        {
            int i = list.SelectedIndex;
            if (i < 0 || Context == null) return;
            string r = Ask(stmts[i]);
            if (r == null) return;
            stmts[i] = r;
            Join();
            list.SelectedIndex = i;
        }

        /// Edit one statement: a rule in the builder, anything else as a line of text.
        string Ask(string s)
        {
            if (s != null && RuleModel.Parse(s, true) == null)
                return TextPrompt.Ask(FindForm(), "Edit line", "A comment (# ...) or synonym NEWWORD = WORD:", s);
            using (BehaviourForm f = new BehaviourForm(Context(), s))
                return f.ShowDialog(FindForm()) == DialogResult.OK ? f.Result : null;
        }

        void Delete()
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            stmts.RemoveAt(i);
            Join();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
        }

        void MoveRow(int d)
        {
            int i = list.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= stmts.Count) return;
            string t = stmts[i]; stmts[i] = stmts[j]; stmts[j] = t;
            Join();
            list.SelectedIndex = j;
        }
    }

    public static class TextPrompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (Form f = new Form { Text = title, ClientSize = new Size(520, 110), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = new Font("Segoe UI", 9f) })
            {
                Label l = new Label { Text = label, AutoSize = true, Location = new Point(10, 10) };
                TextBox t = new TextBox { Text = value ?? "", Location = new Point(10, 34), Width = 500, Font = new Font("Consolas", 9f) };
                Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(334, 72), Width = 85 };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(425, 72), Width = 85 };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }
    }

    // ==================================================================== library
    public static class Library
    {
        /// library.zlp from the levels folder beside ZorEdit.exe, else the built-in copy.
        public static Pack Load()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (string f in new[] { Path.Combine(dir, "levels", "library.zlp"), Path.Combine(dir, "library.zlp") })
                if (File.Exists(f)) return Zlp.Parse(File.ReadAllLines(f));
            using (Stream s = typeof(Library).Assembly.GetManifestResourceStream("library.zlp"))
            {
                if (s == null) return new Pack();
                using (StreamReader r = new StreamReader(s))
                    return Zlp.Parse(r.ReadToEnd().Replace("\r", "").Split('\n'));
            }
        }

        /// The first comment line of a type's script.
        public static string Note(ObjType t)
        {
            Match m = Regex.Match(t.Script ?? "", @"^\s*#\s*(.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// Copy types (and the library types their scripts mention) into the pack.
        /// Returns the ids added; types whose id is already in the pack are skipped.
        public static List<string> Import(Pack lib, Pack into, IEnumerable<string> ids)
        {
            List<string> added = new List<string>();
            Queue<string> q = new Queue<string>(ids);
            while (q.Count > 0)
            {
                string id = q.Dequeue();
                ObjType t = lib.Type(id);
                if (t == null || into.Type(id) != null) continue;
                into.Types.Add(t.Clone());
                added.Add(t.Id);
                string code = Regex.Replace(t.Script ?? "", "\"[^\"\\n]*\"?|#.*", " ");
                foreach (ObjType other in lib.Types)
                    if (into.Type(other.Id) == null && Regex.IsMatch(code, @"\b" + Regex.Escape(other.Id) + @"\b", RegexOptions.IgnoreCase))
                        q.Enqueue(other.Id);
                if (Regex.IsMatch(code, @"\b(add|sub)\s+gold\b|\bcounter\s+gold\b", RegexOptions.IgnoreCase) && string.IsNullOrWhiteSpace(into.StatusCounter))
                {
                    into.StatusCounter = "gold";
                    into.StatusLabel = "$:";
                }
            }
            return added;
        }
    }

    public class LibraryForm : Form
    {
        readonly CheckedListBox box;
        readonly Pack lib;
        public List<string> Chosen = new List<string>();

        public LibraryForm(Pack library, Pack pack)
        {
            lib = library;
            Text = "Add objects from the library";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterParent;
            Label head = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(8),
                Text = "Tick the objects to copy into this pack. Objects they need come too (a door brings a key). Greyed ones are already in the pack."
            };
            box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Font = new Font("Consolas", 9f) };
            foreach (ObjType t in lib.Types)
            {
                bool have = pack.Type(t.Id) != null;
                box.Items.Add(new Entry { T = t, Have = have });
            }
            box.ItemCheck += (s, e) => { if (((Entry)box.Items[e.Index]).Have) e.NewValue = CheckState.Unchecked; };
            FlowLayoutPanel b = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            Button cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            Button ok = new Button { Text = "Add", Width = 90 };
            ok.Click += (s, e) => { Chosen = box.CheckedItems.Cast<Entry>().Select(x => x.T.Id).ToList(); DialogResult = DialogResult.OK; };
            b.Controls.Add(cancel); b.Controls.Add(ok);
            Controls.Add(box); Controls.Add(b); Controls.Add(head);
            CancelButton = cancel;
        }

        class Entry
        {
            public ObjType T; public bool Have;
            public override string ToString() { return (Have ? "(in pack) " : "") + T.Id.PadRight(11) + T.Name.PadRight(20) + " " + Library.Note(T); }
        }
    }
}
