// ZorEdit - MainForm.cs: the editor window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ZorEdit
{
    public class MainForm : Form
    {
        Pack pack;
        string path;
        bool dirty, loading;
        Cell tool = Cell.Wall;
        bool selecting, picking;            // Select tool active / choosing a target cell
        int selX = -1, selY = -1;           // selected cell
        readonly Random rng = new Random();

        ListBox levelList;
        MapCanvas canvas;
        TextBox tName, tIntro, tBanner1, tBanner2, tSub, tLine1, tLine2, tLine3, tVictory;
        TextBox[] tNames = new TextBox[11];
        NumericUpDown nGuardHp, nStartHp, nMaxHp, nHeal, nHit, nGuardHit, nGoldMin, nGoldMax;
        ComboBox cFacing;
        Label introPreview, toolLabel;
        PictureBox titlePic;
        ToolStripStatusLabel sCell, sInfo;
        ToolStripButton selectButton;
        Dictionary<Cell, ToolStripButton> toolButtons = new Dictionary<Cell, ToolStripButton>();
        // selected-cell settings panel
        GroupBox gCell;
        Label lCellTitle, lCellHint, lPText;
        Label[] lP = new Label[4];
        NumericUpDown[] nP = new NumericUpDown[4];
        TextBox tPText, tPText2;
        Label lPText2;
        Button bTarget;
        TextBox tScript;
        Label lScriptResult;

        static readonly Cell[] Palette = { Cell.Wall, Cell.Floor, Cell.Door, Cell.Ladder, Cell.Guard,
                                           Cell.Key, Cell.Sword, Cell.Potion, Cell.Gold, Cell.Amulet,
                                           Cell.Sign, Cell.Trap, Cell.Teleport, Cell.Fountain, Cell.Plate, Cell.Secret, Cell.Npc };
        static readonly string[] PaletteNames = { "Wall", "Floor", "Door", "Ladder (start/exit)", "Guardian",
                                                  "Key", "Sword", "Potion", "Gold", "Amulet",
                                                  "Sign", "Trap", "Teleporter", "Fountain", "Pressure plate", "Secret wall", "Character (NPC)" };
        static readonly string[] PaletteKeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
                                                 "Shift+1", "Shift+2", "Shift+3", "Shift+4", "Shift+5", "Shift+6", "Shift+7" };

        public MainForm(string open)
        {
            Text = "ZorEdit";
            ClientSize = new Size(1290, 720);
            MinimumSize = new Size(1100, 700);
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            BuildUi();
            if (open != null && File.Exists(open)) OpenFile(open);
            else NewPack(true);
        }

        Level Cur { get { int i = levelList.SelectedIndex; return i >= 0 && i < pack.Levels.Count ? pack.Levels[i] : pack.Levels[0]; } }

        // ================================================================ UI construction
        void BuildUi()
        {
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New Pack", Keys.Control | Keys.N, (s, e) => { if (ConfirmLose()) NewPack(false); }));
            file.DropDownItems.Add(Item("&Open Pack...", Keys.Control | Keys.O, (s, e) => OpenDialog()));
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (s, e) => Save(false)));
            file.DropDownItems.Add(Item("Save &As...", Keys.None, (s, e) => Save(true)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Export &Level File (.LEV)...", Keys.Control | Keys.L, (s, e) => ExportLev()));
            file.DropDownItems.Add(Item("Export &Game (.CMD with levels built in)...", Keys.Control | Keys.E, (s, e) => ExportCmd()));
            file.DropDownItems.Add(Item("Export Game &Engine only (ZOR.CMD, loads ZOR/LEV)...", Keys.None, (s, e) => ExportEngine()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.None, (s, e) => Close()));
            ToolStripMenuItem lev = new ToolStripMenuItem("&Level");
            lev.DropDownItems.Add(Item("&Add Level", Keys.None, (s, e) => AddLevel(new Level())));
            lev.DropDownItems.Add(Item("&Duplicate Level", Keys.None, (s, e) => AddLevel(Cur.Clone())));
            lev.DropDownItems.Add(Item("De&lete Level", Keys.None, (s, e) => DeleteLevel()));
            lev.DropDownItems.Add(Item("Move &Up", Keys.None, (s, e) => MoveLevel(-1)));
            lev.DropDownItems.Add(Item("Move Do&wn", Keys.None, (s, e) => MoveLevel(1)));
            lev.DropDownItems.Add(new ToolStripSeparator());
            lev.DropDownItems.Add(Item("&Generate Random Maze", Keys.Control | Keys.G, (s, e) => GenerateMaze()));
            lev.DropDownItems.Add(Item("&Clear Map", Keys.None, (s, e) => ClearMap()));
            ToolStripMenuItem test = new ToolStripMenuItem("&Test");
            test.DropDownItems.Add(Item("&Check Pack", Keys.F7, (s, e) => CheckPack(true)));
            test.DropDownItems.Add(Item("&Play in Emulator", Keys.F5, (s, e) => Play(false)));
            test.DropDownItems.Add(Item("Play from TRSDOS &Disk in Emulator", Keys.Shift | Keys.F5, (s, e) => Play(true)));
            test.DropDownItems.Add(new ToolStripSeparator());
            test.DropDownItems.Add(Item("Set &Emulator Path...", Keys.None, (s, e) => PickEmulator()));
            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&Quick Guide", Keys.F1, (s, e) => ShowHelp()));
            help.DropDownItems.Add(Item("&Script Reference", Keys.Shift | Keys.F1, (s, e) => ShowScriptHelp()));
            menu.Items.AddRange(new ToolStripItem[] { file, lev, test, help });

            ToolStrip tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.ImageScalingSize = new Size(24, 24);
            selectButton = new ToolStripButton("Select", MapCanvas.SelectIcon());
            selectButton.TextImageRelation = TextImageRelation.ImageBeforeText;
            selectButton.ToolTipText = "Select a cell to edit its settings  (Esc)";
            selectButton.Click += (s, e) => SetSelect();
            tools.Items.Add(selectButton);
            tools.Items.Add(new ToolStripSeparator());
            for (int i = 0; i < Palette.Length; i++)
            {
                Cell c = Palette[i];
                ToolStripButton b = new ToolStripButton("", MapCanvas.Swatch(c));
                b.DisplayStyle = ToolStripItemDisplayStyle.Image;
                b.ToolTipText = PaletteNames[i] + "  (key " + PaletteKeys[i] + ")";
                b.Click += (s, e) => SetTool(c);
                toolButtons[c] = b;
                tools.Items.Add(b);
                if (i == 9) tools.Items.Add(new ToolStripSeparator());
            }
            tools.Items.Add(new ToolStripSeparator());
            toolLabel = new Label { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };
            tools.Items.Add(new ToolStripControlHost(toolLabel));

            StatusStrip status = new StatusStrip();
            sCell = new ToolStripStatusLabel("") { Width = 260, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            sInfo = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(sCell);
            status.Items.Add(sInfo);

            // left: level list
            Panel left = new Panel { Dock = DockStyle.Left, Width = 200, Padding = new Padding(6) };
            Label ll = new Label { Text = "Levels (played in order)", Dock = DockStyle.Top, Height = 20 };
            levelList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            levelList.SelectedIndexChanged += (s, e) => ShowLevel();
            FlowLayoutPanel lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 100 };
            lb.Controls.Add(Btn("Add", 58, (s, e) => AddLevel(new Level())));
            lb.Controls.Add(Btn("Copy", 58, (s, e) => AddLevel(Cur.Clone())));
            lb.Controls.Add(Btn("Delete", 58, (s, e) => DeleteLevel()));
            lb.Controls.Add(Btn("Up", 58, (s, e) => MoveLevel(-1)));
            lb.Controls.Add(Btn("Down", 58, (s, e) => MoveLevel(1)));
            lb.Controls.Add(Btn("Maze", 58, (s, e) => GenerateMaze()));
            left.Controls.Add(levelList);
            left.Controls.Add(lb);
            left.Controls.Add(ll);

            // right: properties
            TabControl tabs = new TabControl { Dock = DockStyle.Right, Width = 380 };
            tabs.TabPages.Add(LevelTab());
            tabs.TabPages.Add(GameTab());
            tabs.TabPages.Add(TitleTab());
            tabs.TabPages.Add(ScriptTab());
            tabs.SelectedIndexChanged += (s, e) => RefreshTitle();

            // centre: map
            Panel mid = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(24, 24, 28) };
            canvas = new MapCanvas { Location = new Point(10, 10) };
            canvas.Painted += (x, y, erase) => PaintCell(x, y, erase);
            canvas.Hover += (x, y) => sCell.Text = x < 0 ? "" : "Cell " + x + "," + y + "  " + CellName(Cur.Map[y, x]) +
                string.Concat(canvas.ScriptItems.Where(it => it.Key.X == x && it.Key.Y == y).Select(it => "  + " + it.Value));
            mid.Controls.Add(canvas);

            Controls.Add(mid);
            Controls.Add(tabs);
            Controls.Add(left);
            Controls.Add(tools);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
            SetTool(Cell.Wall);
        }

        TabPage LevelTab()
        {
            TabPage p = new TabPage("Level");
            TableLayoutPanel t = Grid(p);
            tName = Box(20); Row(t, "Name", tName);
            nGuardHp = Num(1, 50); Row(t, "Guardian hits to kill", nGuardHp);
            cFacing = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cFacing.Items.AddRange(new object[] { "Auto", "North", "East", "South", "West" });
            Row(t, "Start facing", cFacing);
            tIntro = new TextBox { Multiline = true, Height = 110, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, CharacterCasing = CharacterCasing.Upper };
            Row(t, "Intro text (| = new line)", tIntro);
            introPreview = new Label { AutoSize = true, Font = new Font("Consolas", 9f), BackColor = Color.Black, ForeColor = Color.FromArgb(210, 215, 255), Padding = new Padding(4), MinimumSize = new Size(250, 20) };
            Row(t, "As shown in the game", introPreview);
            Button chk = Btn("Check pack (F7)", 140, (s, e) => CheckPack(true));
            Row(t, "", chk);
            tName.TextChanged += Changed; tIntro.TextChanged += Changed;
            nGuardHp.ValueChanged += Changed; cFacing.SelectedIndexChanged += Changed;
            p.AutoScroll = true;

            // settings of the selected cell
            gCell = new GroupBox { Text = "Selected cell", Width = 340, Height = 420 };
            TableLayoutPanel c = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(4) };
            lCellTitle = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            c.Controls.Add(lCellTitle);
            for (int i = 0; i < 4; i++)
            {
                lP[i] = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
                nP[i] = Num(0, 255);
                nP[i].ValueChanged += ParamChanged;
                c.Controls.Add(lP[i]);
                c.Controls.Add(nP[i]);
            }
            bTarget = Btn("Pick target on map", 150, (s, e) => { picking = true; lCellHint.Text = "Click the target cell on the map (Esc cancels)."; });
            c.Controls.Add(bTarget);
            lPText = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            tPText = new TextBox { Multiline = true, Width = 320, Height = 54, ScrollBars = ScrollBars.Vertical, CharacterCasing = CharacterCasing.Upper };
            tPText.TextChanged += ParamChanged;
            c.Controls.Add(lPText);
            c.Controls.Add(tPText);
            lPText2 = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            tPText2 = new TextBox { Multiline = true, Width = 320, Height = 54, ScrollBars = ScrollBars.Vertical, CharacterCasing = CharacterCasing.Upper };
            tPText2.TextChanged += ParamChanged;
            c.Controls.Add(lPText2);
            c.Controls.Add(tPText2);
            lCellHint = new Label { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 0) };
            c.Controls.Add(lCellHint);
            gCell.Controls.Add(c);
            Row(t, "", gCell);
            ShowCellPanel();
            return p;
        }

        TabPage GameTab()
        {
            TabPage p = new TabPage("Game");
            p.AutoScroll = true;
            TableLayoutPanel t = Grid(p);
            tVictory = new TextBox { Multiline = true, Height = 60, Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper };
            Row(t, "Victory text (after the last level)", tVictory);
            nStartHp = Num(1, 99); Row(t, "Starting HP", nStartHp);
            nMaxHp = Num(1, 99); Row(t, "Maximum HP", nMaxHp);
            nHeal = Num(0, 99); Row(t, "Potion heals", nHeal);
            nHit = Num(0, 100); Row(t, "Sword hit chance %", nHit);
            nGuardHit = Num(0, 100); Row(t, "Guardian attack chance %", nGuardHit);
            nGoldMin = Num(0, 200); Row(t, "Gold per pile, min", nGoldMin);
            nGoldMax = Num(0, 255); Row(t, "Gold per pile, max", nGoldMax);
            for (int i = 0; i < 11; i++) { tNames[i] = Box(18); Row(t, "Name: " + Pack.NameLabels[i].ToLowerInvariant(), tNames[i]); tNames[i].TextChanged += Changed; }
            tVictory.TextChanged += Changed;
            foreach (NumericUpDown n in new[] { nStartHp, nMaxHp, nHeal, nHit, nGuardHit, nGoldMin, nGoldMax }) n.ValueChanged += Changed;
            return p;
        }

        TabPage ScriptTab()
        {
            TabPage p = new TabPage("Script");
            Label head = new Label
            {
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(4),
                Text = "Rules for this level, Scott Adams style: items, verbs, flags, counters. Help > Script Reference."
            };
            tScript = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsTab = true, Font = new Font("Consolas", 9f), CharacterCasing = CharacterCasing.Normal
            };
            tScript.TextChanged += (s, e) =>
            {
                if (loading) return;
                Cur.Script = tScript.Text.Replace("\r\n", "\n");
                canvas.ScriptItems = ScriptCompiler.ItemPositions(Cur.Script);
                MarkDirty();
            };
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 110, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Height = 30, Width = 360 };
            buttons.Controls.Add(Btn("Check script", 100, (s, e) => CheckScript()));
            buttons.Controls.Add(Btn("Reference", 90, (s, e) => ShowScriptHelp()));
            buttons.Controls.Add(Btn("Example", 80, (s, e) => InsertExample()));
            lScriptResult = new Label { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DarkGreen };
            bottom.Controls.Add(buttons);
            bottom.Controls.Add(lScriptResult);
            p.Controls.Add(tScript);
            p.Controls.Add(bottom);
            p.Controls.Add(head);
            return p;
        }

        void CheckScript()
        {
            List<string> errs = Checker.CheckScripts(pack);
            lScriptResult.ForeColor = errs.Count == 0 ? Color.DarkGreen : Color.DarkRed;
            lScriptResult.Text = errs.Count == 0 ? "All scripts compile." : string.Join("\n", errs.Take(6).Select(e => e.Substring(6)));
        }

        void InsertExample()
        {
            if (tScript.Text.Trim().Length > 0 && MessageBox.Show(this, "Add the example to the end of this level's script?", "Script", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            int lx, ly;
            Cur.FindLadder(out lx, out ly);
            string ex = "# Example: a lantern that can be lit, and a hidden reward\r\n" +
                "item lamp \"A DUSTY LANTERN\" at " + lx + "," + ly + " nouns LANTERN,LAMP desc \"AN OLD LANTERN. IT IS NOT LIT.\"\r\n" +
                "item litlamp \"A LIT LANTERN\" nowhere nouns LANTERN,LAMP desc \"IT BURNS BRIGHTLY.\"\r\n" +
                "synonym IGNITE = LIGHT\r\n" +
                "on LIGHT LAMP if carried lamp then swap lamp litlamp; say \"THE LANTERN GLOWS.\"; score 10\r\n" +
                "on LIGHT LAMP if present litlamp then say \"IT IS ALREADY LIT.\"\r\n" +
                "on LIGHT any then say \"YOU HAVE NOTHING TO LIGHT.\"\r\n";
            tScript.AppendText((tScript.Text.Length > 0 && !tScript.Text.EndsWith("\n") ? "\r\n" : "") + ex);
        }

        void ShowScriptHelp()
        {
            Info("LEVEL SCRIPTS  (one statement per line; # comment; a line starting with a space continues the last)\n\n" +
                 "item NAME \"TITLE\" at X,Y | carried | nowhere  [fixed]  [nouns WORD,WORD]  [desc \"TEXT\"]\n" +
                 "synonym NEWWORD = WORD\n" +
                 "on VERB [NOUN | any] [if CONDITION and ...] then ACTION; ACTION...\n" +
                 "every turn [if ...] then ...        on arrive [if ...] then ...\n\n" +
                 "The first 'on' rule whose conditions hold acts, and the command stops there (unless it says continue). " +
                 "Rules may use built-in verbs (TAKE, OPEN, LOOK...) to change what they do. All 'every turn' and 'on arrive' rules that hold act.\n\n" +
                 "CONDITIONS (put 'not' in front to reverse):\n" +
                 "  carried ITEM   here ITEM   present ITEM   exists ITEM   at X,Y   ahead X,Y\n" +
                 "  flag NAME   counter NAME >= N  (> < <=)   chance PERCENT   facing CELL\n" +
                 "  has sword | amulet | key0-key3 | potion | gold N\n\n" +
                 "ACTIONS:\n" +
                 "  say \"TEXT\"   speak \"TEXT\" (words fly from the character in front)   die \"TEXT\"\n" +
                 "  set FLAG   clear FLAG   counter NAME = N   add NAME N   sub NAME N\n" +
                 "  get ITEM   drop ITEM   destroy ITEM   put ITEM at X,Y   swap ITEM ITEM\n" +
                 "  cell X,Y CELL   goto X,Y   score N   hurt N   heal N (0 = fully)\n" +
                 "  give WHAT [N]   take WHAT [N]   win   inventory   look   continue\n\n" +
                 "CELL: floor wall door ladder guardian key sword potion gold amulet sign trap teleporter fountain plate secret hermit merchant ghost\n\n" +
                 "Items can be picked up with TAKE/GET, put down with DROP and looked at with LOOK/EXAMINE. " +
                 "Flags (32), counters (8) and words are shared by all levels of the pack.");
        }

        TabPage TitleTab()
        {
            TabPage p = new TabPage("Title Screen");
            TableLayoutPanel t = Grid(p);
            tBanner1 = Box(10); Row(t, "Banner line 1 (big)", tBanner1);
            tBanner2 = Box(10); Row(t, "Banner line 2 (big)", tBanner2);
            tSub = Box(63); Row(t, "Subtitle", tSub);
            tLine1 = Box(63); Row(t, "Text line 1", tLine1);
            tLine2 = Box(63); Row(t, "Text line 2", tLine2);
            tLine3 = Box(63); Row(t, "Text line 3", tLine3);
            titlePic = new PictureBox { Width = 352, Height = 200, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            Row(t, "Preview", titlePic);
            foreach (TextBox b in new[] { tBanner1, tBanner2, tSub, tLine1, tLine2, tLine3 }) b.TextChanged += Changed;
            return p;
        }

        static TableLayoutPanel Grid(TabPage p)
        {
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.Controls.Add(t);
            return t;
        }

        static void Row(TableLayoutPanel t, string label, Control c)
        {
            if (label.Length > 0) t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            if (c is TextBox && !((TextBox)c).Multiline) c.Width = 330;
            t.Controls.Add(c);
        }

        static TextBox Box(int max) { return new TextBox { MaxLength = max, CharacterCasing = CharacterCasing.Upper }; }
        static NumericUpDown Num(int lo, int hi) { return new NumericUpDown { Minimum = lo, Maximum = hi, Width = 80 }; }
        static Button Btn(string text, int w, EventHandler h) { Button b = new Button { Text = text, Width = w }; b.Click += h; return b; }
        static ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text, null, h);
            if (keys != Keys.None) m.ShortcutKeys = keys;
            return m;
        }

        // ================================================================ model <-> UI
        void NewPack(bool sample)
        {
            string def = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "levels", "default.zlp");
            if (sample && File.Exists(def)) { OpenFile(def); path = null; dirty = false; UpdateTitle(); return; }
            pack = new Pack();
            pack.Line1 = "RECOVER THE AMULET AND CLIMB BACK OUT";
            pack.Levels.Add(MazeGen.Generate(rng, new Level { Name = "LEVEL ONE" }));
            path = null;
            LoadUi();
            dirty = false;
            UpdateTitle();
        }

        void OpenFile(string f)
        {
            try
            {
                pack = Zlp.Load(f);
                path = f;
                LoadUi();
                dirty = false;
                UpdateTitle();
                Settings.Set("last", f);
            }
            catch (Exception ex) { Error("Could not open " + f + "\n\n" + ex.Message); }
        }

        void LoadUi()
        {
            loading = true;
            tBanner1.Text = pack.Banner1; tBanner2.Text = pack.Banner2; tSub.Text = pack.Subtitle;
            tLine1.Text = pack.Line1; tLine2.Text = pack.Line2; tLine3.Text = pack.Line3;
            tVictory.Text = pack.Victory;
            SetNum(nStartHp, pack.StartHp); SetNum(nMaxHp, pack.MaxHp); SetNum(nHeal, pack.Heal);
            SetNum(nHit, pack.Hit); SetNum(nGuardHit, pack.GuardHit); SetNum(nGoldMin, pack.GoldMin); SetNum(nGoldMax, pack.GoldMax);
            for (int i = 0; i < 11; i++) tNames[i].Text = pack.Names[i];
            RefreshList(0);
            loading = false;
            ShowLevel();
            RefreshTitle();
        }

        static void SetNum(NumericUpDown n, int v) { n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        void RefreshList(int select)
        {
            bool was = loading;
            loading = true;
            levelList.Items.Clear();
            for (int i = 0; i < pack.Levels.Count; i++) levelList.Items.Add((i + 1) + ". " + pack.Levels[i].Name);
            levelList.SelectedIndex = Math.Max(0, Math.Min(select, pack.Levels.Count - 1));
            loading = was;
        }

        void ShowLevel()
        {
            if (loading || pack == null) return;
            loading = true;
            Level l = Cur;
            tName.Text = l.Name;
            SetNum(nGuardHp, l.GuardHp);
            cFacing.SelectedIndex = l.Facing + 1;
            tIntro.Text = l.Intro.Replace("|", "|");
            if (tScript != null) { tScript.Text = (l.Script ?? "").Replace("\n", "\r\n"); lScriptResult.Text = ""; }
            loading = false;
            canvas.Level = l;
            canvas.ScriptItems = ScriptCompiler.ItemPositions(l.Script);
            Select(-1, -1);
            UpdatePreview();
            UpdateInfo();
        }

        void Changed(object sender, EventArgs e)
        {
            if (loading) return;
            Level l = Cur;
            l.Name = tName.Text;
            l.GuardHp = (int)nGuardHp.Value;
            l.Facing = cFacing.SelectedIndex - 1;
            l.Intro = tIntro.Text.Replace("\r\n", "|");
            pack.Banner1 = tBanner1.Text; pack.Banner2 = tBanner2.Text; pack.Subtitle = tSub.Text;
            pack.Line1 = tLine1.Text; pack.Line2 = tLine2.Text; pack.Line3 = tLine3.Text;
            pack.Victory = tVictory.Text.Replace("\r\n", "|");
            pack.StartHp = (int)nStartHp.Value; pack.MaxHp = (int)nMaxHp.Value; pack.Heal = (int)nHeal.Value;
            pack.Hit = (int)nHit.Value; pack.GuardHit = (int)nGuardHit.Value;
            pack.GoldMin = (int)nGoldMin.Value; pack.GoldMax = (int)nGoldMax.Value;
            for (int i = 0; i < 11; i++) pack.Names[i] = tNames[i].Text;
            if (Array.IndexOf(tNames, sender) >= 0) ShowCellPanel();
            if (sender == tName) { int i = levelList.SelectedIndex; levelList.Items[i] = (i + 1) + ". " + l.Name; }
            if (sender == cFacing) canvas.Invalidate();
            UpdatePreview();
            RefreshTitle();
            MarkDirty();
        }

        void UpdatePreview()
        {
            List<string> lines = new List<string> { "LEVEL " + (levelList.SelectedIndex + 1) + ": " + Util.Fit(Cur.Name, 20) };
            lines.AddRange(Util.Wrap(Cur.Intro, 30));
            introPreview.Text = string.Join("\n", lines.Select(s => s.PadRight(30)));
        }

        void RefreshTitle()
        {
            if (pack == null || titlePic == null) return;
            byte[] scr = Compiler.RenderTitle(pack);
            Bitmap bmp = Trs80Screen.Render(scr, 64, 16);
            Image old = titlePic.Image;
            titlePic.Image = bmp;
            if (old != null) old.Dispose();
        }

        void UpdateInfo()
        {
            try
            {
                int size = Compiler.Build(pack).Length;
                sInfo.Text = pack.Levels.Count + " level(s), level file " + size + " bytes.  Hover over a toolbar button to see what it paints.";
            }
            catch (Exception ex) { sInfo.Text = ex.Message; }
        }

        void MarkDirty() { if (!dirty) { dirty = true; UpdateTitle(); } }
        void UpdateTitle() { Text = "ZorEdit - " + (path == null ? "untitled" : Path.GetFileName(path)) + (dirty ? " *" : "") + "   (Vaults of Zor level editor)"; }

        // ================================================================ editing
        void SetTool(Cell c)
        {
            tool = c;
            selecting = picking = false;
            selectButton.Checked = false;
            foreach (KeyValuePair<Cell, ToolStripButton> kv in toolButtons) kv.Value.Checked = kv.Key == c;
            toolLabel.Text = "Painting: " + CellName(c) + ".  Left button paints, right button erases.";
            canvas.Cursor = Cursors.Cross;
        }

        void SetSelect()
        {
            selecting = true;
            picking = false;
            selectButton.Checked = true;
            foreach (ToolStripButton b in toolButtons.Values) b.Checked = false;
            toolLabel.Text = "Select: click a cell to edit its settings.";
            canvas.Cursor = Cursors.Arrow;
        }

        void PaintCell(int x, int y, bool erase)
        {
            Level l = Cur;
            if (picking)                    // choosing the target of a teleporter or plate
            {
                picking = false;
                ObjParams p = selX < 0 ? null : l.Params(selX, selY, true);
                if (p != null && !Level.IsBorder(x, y)) { p.A = x; p.B = y; MarkDirty(); }
                canvas.Hold = true;         // don't paint with the rest of this click
                ShowCellPanel();
                canvas.Invalidate();
                return;
            }
            if (selecting) { Select(x, y); return; }
            if (Level.IsBorder(x, y)) return;
            Cell c = erase ? Cell.Floor : tool;
            if (l.Map[y, x] != c)
            {
                if (c == Cell.Ladder)
                    for (int yy = 0; yy < Level.H; yy++)
                        for (int xx = 0; xx < Level.W; xx++)
                            if (l.Map[yy, xx] == Cell.Ladder) l.Map[yy, xx] = Cell.Floor;
                l.Map[y, x] = c;
                l.Objs.Remove(y * Level.W + x);     // a new kind of cell starts with default settings
                MarkDirty();
                UpdateInfo();
            }
            // painting something with settings selects it, so they can be edited straight away
            if (ParamSpec.For(c) != null) Select(x, y);
            else if (x == selX && y == selY) Select(-1, -1);
            canvas.Invalidate();
        }

        void Select(int x, int y)
        {
            selX = x; selY = y;
            canvas.Selected = new Point(x, y);
            ShowCellPanel();
        }

        /// Fill the selected-cell panel with the fields the cell type has.
        void ShowCellPanel()
        {
            if (gCell == null) return;
            bool was = loading;
            loading = true;
            Level l = pack == null ? null : Cur;
            ParamSpec spec = l == null || selX < 0 ? null : ParamSpec.For(l.Map[selY, selX]);
            ObjParams p = spec == null ? null : (l.Params(selX, selY, false) ?? new ObjParams());
            lCellTitle.Text = selX < 0 ? "No cell selected" : CellName(l.Map[selY, selX]) + " at " + selX + "," + selY;
            for (int i = 0; i < 4; i++)
            {
                bool on = spec != null && spec.Labels[i] != null;
                lP[i].Visible = nP[i].Visible = on;
                if (!on) continue;
                lP[i].Text = spec.Labels[i];
                nP[i].Maximum = spec.Max[i];
                SetNum(nP[i], i == 0 ? p.A : i == 1 ? p.B : i == 2 ? p.C : p.D);
            }
            if (spec != null && l.Map[selY, selX] == Cell.Key || spec != null && l.Map[selY, selX] == Cell.Door)
                lP[0].Text = spec.Labels[0] + ":  " + pack.Names[Math.Max(0, Math.Min(3, p.A))];
            bTarget.Visible = spec != null && spec.Target;
            lPText.Visible = tPText.Visible = spec != null && spec.TextLabel != null;
            if (tPText.Visible) { lPText.Text = spec.TextLabel; tPText.Text = (p.Text ?? "").Replace("|", "\r\n"); }
            lPText2.Visible = tPText2.Visible = spec != null && spec.Text2Label != null;
            if (tPText2.Visible) { lPText2.Text = spec.Text2Label; tPText2.Text = (p.Text2 ?? "").Replace("|", "\r\n"); }
            if (spec != null && l.Map[selY, selX] == Cell.Npc)
            {
                lP[0].Text = "Looks like:  " + ParamSpec.NpcLooks[Math.Min(2, p.A)] + "  (0 hermit, 1 merchant, 2 ghost)";
                lP[1].Text = "Does:  " + ParamSpec.NpcActions[Math.Min(4, p.B)] + "  (0-4)";
                lP[3].Text = "Item:  " + ParamSpec.NpcItems[Math.Min(8, p.D)] + "  (0 none, 1-4 key colour 0-3, 5 sword, 6 potion, 7 gold, 8 amulet)";
                lP[3].MaximumSize = new Size(320, 0);
            }
            lCellHint.Text = selX < 0 ? "Use the Select tool (Esc) or paint a character, sign, trap, teleporter, fountain, plate, secret wall, key, door, guardian, potion or gold to edit its settings."
                           : spec == null ? "This kind of cell has no settings."
                           : "0 means use the default. Text is shown in the game's 30-column window.";
            loading = was;
        }

        void ParamChanged(object sender, EventArgs e)
        {
            if (loading || selX < 0) return;
            ObjParams p = Cur.Params(selX, selY, true);
            if (p == null) return;
            p.A = (int)nP[0].Value; p.B = (int)nP[1].Value; p.C = (int)nP[2].Value; p.D = (int)nP[3].Value;
            p.Text = tPText.Text.Replace("\r\n", "|");
            p.Text2 = tPText2.Text.Replace("\r\n", "|");
            if (Cur.Map[selY, selX] == Cell.Npc && sender != tPText && sender != tPText2) ShowCellPanel();
            if (sender == nP[0] && (Cur.Map[selY, selX] == Cell.Key || Cur.Map[selY, selX] == Cell.Door)) ShowCellPanel();
            canvas.Invalidate();
            MarkDirty();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown) && (keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.Escape) { if (picking) { picking = false; ShowCellPanel(); } else SetSelect(); return true; }
                int k = (int)code - (int)Keys.D0;
                bool shift = (keyData & Keys.Shift) != 0;
                if (k >= 0 && k <= 9 && !shift) { SetTool(Palette[(k + 9) % 10]); return true; }
                if (k >= 1 && k <= 7 && shift) { SetTool(Palette[9 + k]); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void AddLevel(Level l)
        {
            if (pack.Levels.Count >= Pack.MaxLevels) { Error("A pack can hold at most " + Pack.MaxLevels + " levels."); return; }
            int at = levelList.SelectedIndex + 1;
            pack.Levels.Insert(at, l);
            RefreshList(at);
            ShowLevel();
            MarkDirty();
        }

        void DeleteLevel()
        {
            if (pack.Levels.Count <= 1) { Error("A pack needs at least one level."); return; }
            if (MessageBox.Show(this, "Delete level " + (levelList.SelectedIndex + 1) + " \"" + Cur.Name + "\"?", "ZorEdit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int i = levelList.SelectedIndex;
            pack.Levels.RemoveAt(i);
            RefreshList(i);
            ShowLevel();
            MarkDirty();
        }

        void MoveLevel(int d)
        {
            int i = levelList.SelectedIndex, j = i + d;
            if (j < 0 || j >= pack.Levels.Count) return;
            Level l = pack.Levels[i];
            pack.Levels[i] = pack.Levels[j];
            pack.Levels[j] = l;
            RefreshList(j);
            ShowLevel();
            MarkDirty();
        }

        void GenerateMaze()
        {
            if (MessageBox.Show(this, "Replace the map of level " + (levelList.SelectedIndex + 1) + " with a random maze?\nThe ladder, a sword, a guardian guarding a key, a locked door before the amulet, potions and gold are placed for you.",
                "Generate maze", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Level g = MazeGen.Generate(rng, Cur);
            Cur.Map = g.Map;
            canvas.Invalidate();
            MarkDirty();
            UpdateInfo();
        }

        void ClearMap()
        {
            if (MessageBox.Show(this, "Clear the map of this level to open floor?", "Clear map", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++)
                    Cur.Map[y, x] = Level.IsBorder(x, y) ? Cell.Wall : Cell.Floor;
            Cur.Map[1, 1] = Cell.Ladder;
            canvas.Invalidate();
            MarkDirty();
        }

        static string CellName(Cell c) { return PaletteNames[Array.IndexOf(Palette, c)]; }

        // ================================================================ files
        bool ConfirmLose()
        {
            if (!dirty) return true;
            DialogResult r = MessageBox.Show(this, "Save changes to " + (path == null ? "this pack" : Path.GetFileName(path)) + "?", "ZorEdit", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save(false);
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmLose()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        void OpenDialog()
        {
            if (!ConfirmLose()) return;
            using (OpenFileDialog d = new OpenFileDialog { Filter = "Zor level packs (*.zlp)|*.zlp|All files|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenFile(d.FileName);
        }

        bool Save(bool ask)
        {
            if (path == null || ask)
            {
                using (SaveFileDialog d = new SaveFileDialog { Filter = "Zor level packs (*.zlp)|*.zlp", DefaultExt = "zlp", FileName = path == null ? "mylevels.zlp" : Path.GetFileName(path) })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    path = d.FileName;
                }
            }
            try
            {
                Zlp.Save(pack, path);
                dirty = false;
                UpdateTitle();
                Settings.Set("last", path);
                return true;
            }
            catch (Exception ex) { Error("Could not save:\n" + ex.Message); return false; }
        }

        bool CheckPack(bool showOk)
        {
            List<string> all = new List<string>();
            for (int i = 0; i < pack.Levels.Count; i++) all.AddRange(Checker.Check(pack.Levels[i], i));
            all.AddRange(Checker.CheckScripts(pack));
            bool errors = all.Any(s => s.StartsWith("ERROR"));
            if (all.Count == 0) { if (showOk) MessageBox.Show(this, "All " + pack.Levels.Count + " level(s) look playable.", "Check pack", MessageBoxButtons.OK, MessageBoxIcon.Information); return true; }
            if (showOk || errors)
                MessageBox.Show(this, string.Join("\n\n", all), errors ? "Check pack - fix these before exporting" : "Check pack", MessageBoxButtons.OK, errors ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
            return !errors;
        }

        string AskSave(string filter, string ext, string name)
        {
            using (SaveFileDialog d = new SaveFileDialog { Filter = filter, DefaultExt = ext, FileName = name })
            {
                string dir = Settings.Get("exportdir");
                if (dir != null && Directory.Exists(dir)) d.InitialDirectory = dir;
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                Settings.Set("exportdir", Path.GetDirectoryName(d.FileName));
                return d.FileName;
            }
        }

        void ExportLev()
        {
            if (!CheckPack(false)) return;
            string f = AskSave("TRS-80 level file (*.lev)|*.lev", "lev", path == null ? "ZOR.LEV" : Path.GetFileNameWithoutExtension(path).ToUpperInvariant() + ".LEV");
            if (f == null) return;
            try
            {
                byte[] b = Compiler.Build(pack);
                File.WriteAllBytes(f, b);
                Info("Wrote " + Path.GetFileName(f) + " (" + b.Length + " bytes).\n\nCopy it to your TRSDOS disk (for example IMPORT2 " + Path.GetFileName(f).ToUpperInvariant() + " " + DosName(f) + ") and run:\n\n    ZOR " + DosName(f) + "\n\nA file called ZOR/LEV is loaded when ZOR is run on its own.");
            }
            catch (Exception ex) { Error(ex.Message); }
        }

        void ExportCmd()
        {
            if (!CheckPack(false)) return;
            string f = AskSave("TRS-80 program (*.cmd)|*.cmd", "cmd", "ZOR.CMD");
            if (f == null) return;
            try
            {
                CmdImage im = Engine.Load();
                int top = im.SetPack(Compiler.Build(pack));
                File.WriteAllBytes(f, im.Write());
                Info("Wrote " + Path.GetFileName(f) + " with " + pack.Levels.Count + " level(s) built in.\nIt loads at 5200H and uses memory up to " + top.ToString("X4") + "H (TRSDOS, 48K).\n\nRun it from TRSDOS as " + Path.GetFileNameWithoutExtension(f).ToUpperInvariant() + ", or in trs80gp with:\n    trs80gp -m1 " + Path.GetFileName(f));
            }
            catch (Exception ex) { Error(ex.Message); }
        }

        void ExportEngine()
        {
            string f = AskSave("TRS-80 program (*.cmd)|*.cmd", "cmd", "ZOR.CMD");
            if (f == null) return;
            try { File.WriteAllBytes(f, Engine.Load().Write()); Info("Wrote the game engine with no levels built in.\nWhen run as ZOR it loads ZOR/LEV, or ZOR NAME/LEV loads another level file."); }
            catch (Exception ex) { Error(ex.Message); }
        }

        static string DosName(string f)
        {
            string n = new string(Path.GetFileNameWithoutExtension(f).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (n.Length == 0 || !char.IsLetter(n[0])) n = "Z" + n;
            if (n.Length > 8) n = n.Substring(0, 8);
            return n + "/LEV";
        }

        // ================================================================ emulator
        void PickEmulator()
        {
            using (OpenFileDialog d = new OpenFileDialog { Filter = "trs80gp.exe|trs80gp.exe|Programs|*.exe", Title = "Where is trs80gp.exe?" })
                if (d.ShowDialog(this) == DialogResult.OK) Settings.Set("emulator", d.FileName);
        }

        void Play(bool fromDisk)
        {
            if (!CheckPack(false)) return;
            string emu = Settings.Get("emulator");
            if (emu == null || !File.Exists(emu))
            {
                Info("Please show ZorEdit where trs80gp.exe is (download it from 48k.ca).");
                PickEmulator();
                emu = Settings.Get("emulator");
                if (emu == null || !File.Exists(emu)) return;
            }
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "ZorEdit");
                Directory.CreateDirectory(dir);
                string args;
                if (!fromDisk)
                {
                    CmdImage im = Engine.Load();
                    im.SetPack(Compiler.Build(pack));
                    string f = Path.Combine(dir, "ZOR.CMD");
                    File.WriteAllBytes(f, im.Write());
                    args = "-m1 \"" + f + "\"";
                }
                else
                {
                    // Boot TRSDOS, copy the engine and level file onto a blank disk
                    // in drive 2 with IMPORT2 (utilities disk in drive 1), then run
                    // ZOR so it loads ZOR/LEV from disk. Typed input waits until
                    // TRSDOS asks for a command, so the lines can be queued.
                    File.WriteAllBytes(Path.Combine(dir, "zor.cmd"), Engine.Load().Write());
                    File.WriteAllBytes(Path.Combine(dir, "zor.lev"), Compiler.Build(pack));
                    args = "-m1 -frehd_dir \"" + dir + "\" -d1 :tu1 -d2 :tb1 -id 200 " +
                           "-i \"IMPORT2 zor.cmd ZOR/CMD:2\\r\" " +
                           "-i \"IMPORT2 zor.lev ZOR/LEV:2\\r\" -i \"ZOR\\r\"";
                }
                Process.Start(new ProcessStartInfo(emu, args) { UseShellExecute = false, WorkingDirectory = dir });
            }
            catch (Exception ex) { Error("Could not start the emulator:\n" + ex.Message); }
        }

        void ShowHelp()
        {
            Info("VAULTS OF ZOR level editor\n\n" +
                 "Map: pick a tile on the toolbar (or keys 1-0) and paint with the left mouse button. The right button erases to floor. The outer ring is always wall.\n\n" +
                 "Every level needs one ladder (H): the player starts there, and returns to it with an amulet (A) to finish the level.\n" +
                 "Doors need a key (K) of the same colour; each key opens one door. Guardians (G) block the way and can only be killed with a sword (S). Potions (P) heal, gold ($) scores.\n\n" +
                 "Spaces with game logic: Sign (shows its text), Trap (damage, hidden or visible, once or every time), Teleporter (moves the player to its target), Fountain (heals, can run dry), Pressure plate (opens its target cell), Secret wall (looks like wall, walk straight through).\n\n" +
                 "Characters (&): a hermit, merchant or ghost that talks in a speech bubble when the player walks into it or types TALK. It can just talk, give an item, sell an item for gold, heal, or step aside; set what it says first and what it says once its deed is done.\n\n" +
                 "Settings: use the Select tool (Esc) or just paint the object, then edit it under Selected cell on the Level tab. 0 means use the default. Teleporters and plates need a target: press Pick target on map, then click the cell.\n\n" +
                 "Levels are played in list order; HP, gold, potions and the sword carry over to the next level.\n\n" +
                 "Test > Check Pack (F7) does a play-through to make sure every amulet can be reached.\n" +
                 "Test > Play in Emulator (F5) runs the pack in trs80gp. Shift+F5 boots TRSDOS and loads the level file from disk the way a real machine would.\n\n" +
                 "File > Export Level File writes a .LEV file for a TRSDOS disk: run it with  ZOR NAME/LEV  (or name it ZOR/LEV).\n" +
                 "File > Export Game writes a single ZOR.CMD with the levels built in.\n\n" +
                 "Packs are saved as .ZLP text files that you can also edit by hand.");
        }

        void Info(string s) { MessageBox.Show(this, s, "ZorEdit", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void Error(string s) { MessageBox.Show(this, s, "ZorEdit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // ==================================================================== map canvas
    public class MapCanvas : Control
    {
        public const int CS = 20;
        Level level;
        int hx = -1, hy = -1;
        public event Action<int, int, bool> Painted;
        public event Action<int, int> Hover;

        static readonly Color Bg = Color.FromArgb(10, 10, 14), WallC = Color.FromArgb(205, 210, 255), BorderC = Color.FromArgb(120, 125, 165);

        public MapCanvas()
        {
            DoubleBuffered = true;
            Size = new Size(Level.W * CS + 1, Level.H * CS + 1);
            Cursor = Cursors.Cross;
        }

        public Level Level { get { return level; } set { level = value; Invalidate(); } }
        Point selected = new Point(-1, -1);
        public Point Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public bool Hold;                   // ignore the rest of this mouse drag
        List<KeyValuePair<Point2, string>> scriptItems = new List<KeyValuePair<Point2, string>>();
        public List<KeyValuePair<Point2, string>> ScriptItems { get { return scriptItems; } set { scriptItems = value; Invalidate(); } }

        /// Colours used for keys and doors of colour 0-3 (brass, iron, silver, gold).
        /// Colours for the three character looks (hermit, merchant, ghost).
        public static readonly Color[] NpcColours = { Color.LightGreen, Color.Goldenrod, Color.WhiteSmoke };
        public static readonly Color[] KeyColours = { Color.FromArgb(205, 127, 50), Color.FromArgb(130, 140, 150), Color.Silver, Color.Gold };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Bg);
            if (level == null) return;
            using (Pen grid = new Pen(Color.FromArgb(32, 32, 40)))
            {
                for (int x = 0; x <= Level.W; x++) g.DrawLine(grid, x * CS, 0, x * CS, Level.H * CS);
                for (int y = 0; y <= Level.H; y++) g.DrawLine(grid, 0, y * CS, Level.W * CS, y * CS);
            }
            int face = level.StartFacing();
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++)
                    DrawCell(g, new Rectangle(x * CS, y * CS, CS, CS), level.Map[y, x], Level.IsBorder(x, y), face, level.Params(x, y, false));
            // teleporter and plate targets
            foreach (KeyValuePair<int, ObjParams> kv in level.Objs)
            {
                int x = kv.Key % Level.W, y = kv.Key / Level.W;
                ParamSpec spec = ParamSpec.For(level.Map[y, x]);
                if (spec == null || !spec.Target || kv.Value.A <= 0 || kv.Value.B <= 0) continue;
                bool sel = selected.X == x && selected.Y == y;
                Color col = level.Map[y, x] == Cell.Teleport ? Color.Cyan : Color.Silver;
                using (Pen p = new Pen(Color.FromArgb(sel ? 255 : 110, col), sel ? 2.5f : 1.5f) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor })
                {
                    p.DashStyle = sel ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(p, x * CS + CS / 2, y * CS + CS / 2, kv.Value.A * CS + CS / 2, kv.Value.B * CS + CS / 2);
                }
                using (Pen p = new Pen(Color.FromArgb(sel ? 255 : 110, col), 1.5f))
                    g.DrawRectangle(p, kv.Value.A * CS + 2, kv.Value.B * CS + 2, CS - 4, CS - 4);
            }
            // script items placed with "at x,y": a small magenta + in the corner of the cell
            using (Pen p = new Pen(Color.Magenta, 2))
                foreach (KeyValuePair<Point2, string> it in scriptItems)
                {
                    int cx = it.Key.X * CS + CS - 6, cy = it.Key.Y * CS + 6;
                    g.DrawLine(p, cx - 4, cy, cx + 4, cy);
                    g.DrawLine(p, cx, cy - 4, cx, cy + 4);
                }
            if (selected.X >= 0)
                using (Pen p = new Pen(Color.Cyan, 2)) g.DrawRectangle(p, selected.X * CS, selected.Y * CS, CS, CS);
            if (hx >= 0)
                using (Pen p = new Pen(Color.Yellow, 2)) g.DrawRectangle(p, hx * CS + 1, hy * CS + 1, CS - 2, CS - 2);
        }

        static void DrawCell(Graphics g, Rectangle r, Cell c, bool border, int face, ObjParams prm)
        {
            int colour = prm == null ? 0 : prm.A & 3;
            switch (c)
            {
                case Cell.Wall:
                    using (Brush b = new SolidBrush(border ? BorderC : WallC)) g.FillRectangle(b, r);
                    return;
                case Cell.Secret:           // a wall you can walk through: hatched
                    using (Brush b = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.WideDownwardDiagonal, Color.FromArgb(120, 125, 165), WallC))
                        g.FillRectangle(b, r);
                    return;
                case Cell.Floor:
                    g.FillRectangle(Brushes.DimGray, r.X + CS / 2 - 1, r.Y + CS / 2 - 1, 2, 2);
                    return;
                case Cell.Door:
                    using (Brush b = new SolidBrush(Color.FromArgb(150, 95, 40))) g.FillRectangle(b, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
                    using (Pen p = new Pen(Color.FromArgb(70, 40, 10))) { g.DrawLine(p, r.X + 7, r.Y + 2, r.X + 7, r.Bottom - 3); g.DrawLine(p, r.X + 13, r.Y + 2, r.X + 13, r.Bottom - 3); }
                    using (Brush b = new SolidBrush(KeyColours[colour])) g.FillEllipse(b, r.Right - 8, r.Y + r.Height / 2 - 2, 5, 5);
                    return;
                case Cell.Plate:
                    using (Brush b = new SolidBrush(Color.FromArgb(110, 110, 120))) g.FillRectangle(b, r.X + 3, r.Y + 5, r.Width - 6, r.Height - 10);
                    using (Pen p = new Pen(Color.Silver)) g.DrawRectangle(p, r.X + 3, r.Y + 5, r.Width - 7, r.Height - 11);
                    return;
                case Cell.Ladder:
                    using (Pen p = new Pen(Color.LightGreen, 2))
                    {
                        g.DrawLine(p, r.X + 6, r.Y + 3, r.X + 6, r.Bottom - 3);
                        g.DrawLine(p, r.Right - 6, r.Y + 3, r.Right - 6, r.Bottom - 3);
                        for (int k = 5; k < CS - 3; k += 4) g.DrawLine(p, r.X + 6, r.Y + k, r.Right - 6, r.Y + k);
                    }
                    // facing arrow
                    Point m = new Point(r.X + CS / 2, r.Y + CS / 2);
                    Point tip = new Point(m.X + Util.DX[face] * 9, m.Y + Util.DY[face] * 9);
                    using (Pen p = new Pen(Color.Yellow, 3) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor }) g.DrawLine(p, m, tip);
                    return;
            }
            string t = Util.MapChars[(int)c].ToString();
            Color col = c == Cell.Guard ? Color.OrangeRed : c == Cell.Amulet ? Color.Gold : c == Cell.Gold ? Color.Khaki :
                        c == Cell.Potion ? Color.Violet : c == Cell.Sword ? Color.LightSkyBlue :
                        c == Cell.Key ? KeyColours[colour] : c == Cell.Sign ? Color.Wheat :
                        c == Cell.Trap ? (prm != null && prm.B != 0 ? Color.Red : Color.Gray) :
                        c == Cell.Teleport ? Color.Cyan : c == Cell.Fountain ? Color.DeepSkyBlue :
                        c == Cell.Npc ? NpcColours[prm == null ? 0 : Math.Min(2, prm.A)] : Color.Orange;
            using (Brush b = new SolidBrush(Color.FromArgb(50, col))) g.FillEllipse(b, r.X + 2, r.Y + 2, CS - 4, CS - 4);
            using (Font f = new Font("Consolas", 11f, FontStyle.Bold))
            using (Brush b = new SolidBrush(col))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(t, f, b, r, sf);
            }
        }

        public static Bitmap Swatch(Cell c)
        {
            Bitmap b = new Bitmap(CS, CS);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Bg);
                DrawCell(g, new Rectangle(0, 0, CS, CS), c, false, 1, null);
            }
            return b;
        }

        public static Bitmap SelectIcon()
        {
            Bitmap b = new Bitmap(CS, CS);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                Point[] arrow = { new Point(5, 2), new Point(5, 16), new Point(9, 12), new Point(12, 18), new Point(14, 17), new Point(11, 11), new Point(16, 11) };
                g.FillPolygon(Brushes.White, arrow);
                g.DrawPolygon(Pens.Black, arrow);
            }
            return b;
        }

        bool HitCell(MouseEventArgs e, out int x, out int y)
        {
            x = e.X / CS; y = e.Y / CS;
            return e.X >= 0 && e.Y >= 0 && x < Level.W && y < Level.H;
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); Apply(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int x, y;
            if (HitCell(e, out x, out y)) { if (x != hx || y != hy) { hx = x; hy = y; Invalidate(); if (Hover != null) Hover(x, y); } }
            Apply(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hx = hy = -1; Invalidate(); if (Hover != null) Hover(-1, -1); }
        protected override void OnMouseUp(MouseEventArgs e) { Hold = false; }

        void Apply(MouseEventArgs e)
        {
            int x, y;
            if (Hold || level == null || !HitCell(e, out x, out y) || Painted == null) return;
            if (e.Button == MouseButtons.Left) Painted(x, y, false);
            else if (e.Button == MouseButtons.Right) Painted(x, y, true);
        }
    }

    // ==================================================================== TRS-80 screen preview
    public static class Trs80Screen
    {
        public static Bitmap Render(byte[] scr, int cols, int rows)
        {
            const int cw = 6, ch = 12, s = 1;
            Bitmap bmp = new Bitmap(cols * cw * s, rows * ch * s);
            Color fg = Color.FromArgb(210, 215, 255);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Brush b = new SolidBrush(fg))
            using (Font f = new Font("Consolas", 8f, FontStyle.Bold))
            {
                g.Clear(Color.Black);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int v = scr[r * cols + c];
                        int x = c * cw, y = r * ch;
                        if (v >= 128)
                        {
                            for (int py = 0; py < 3; py++)
                                for (int px = 0; px < 2; px++)
                                    if ((v & (1 << (py * 2 + px))) != 0) g.FillRectangle(b, x + px * 3, y + py * 4, 3, 4);
                        }
                        else if (v > 32) g.DrawString(((char)v).ToString(), f, b, x - 2, y);
                    }
            }
            return bmp;
        }
    }

    // ==================================================================== engine + settings
    public static class Engine
    {
        /// The game engine: engine.cmd beside ZorEdit.exe if present, else the built-in copy.
        public static CmdImage Load()
        {
            string side = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "engine.cmd");
            if (File.Exists(side)) return CmdImage.Read(File.ReadAllBytes(side));
            using (Stream s = typeof(Engine).Assembly.GetManifestResourceStream("engine.cmd"))
            {
                if (s == null) throw new Exception("ZorEdit was built without the game engine.");
                MemoryStream m = new MemoryStream();
                s.CopyTo(m);
                return CmdImage.Read(m.ToArray());
            }
        }
    }

    public static class Settings
    {
        static string File_ { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZorEdit", "settings.txt"); } }

        public static string Get(string key)
        {
            try
            {
                if (!File.Exists(File_)) return null;
                foreach (string l in File.ReadAllLines(File_))
                    if (l.StartsWith(key + "=")) return l.Substring(key.Length + 1);
            }
            catch { }
            return null;
        }

        public static void Set(string key, string value)
        {
            try
            {
                List<string> lines = File.Exists(File_) ? File.ReadAllLines(File_).Where(l => !l.StartsWith(key + "=")).ToList() : new List<string>();
                lines.Add(key + "=" + value);
                Directory.CreateDirectory(Path.GetDirectoryName(File_));
                File.WriteAllLines(File_, lines.ToArray());
            }
            catch { }
        }
    }
}
