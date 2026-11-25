// ZorEdit - MainForm.cs: the editor window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZorEdit
{
    public class MainForm : Form
    {
        enum Tool { Select, Wall, Floor, Start, Object }

        Pack pack;
        string path;
        bool dirty, loading;
        Tool tool = Tool.Wall;
        string toolType;                    // object type painted by Tool.Object
        bool picking;                       // choosing the target (P1,P2) of the selected object
        int selX = -1, selY = -1;           // selected cell
        Inst selObj;                        // selected object
        readonly Random rng = new Random();

        ListBox levelList;
        MapCanvas canvas;
        ToolStrip tools;
        Label toolLabel;
        ToolStripStatusLabel sCell, sInfo;
        readonly List<ToolStripButton> fixedButtons = new List<ToolStripButton>();
        readonly List<ToolStripItem> typeButtons = new List<ToolStripItem>();
        // level tab
        TextBox tName, tIntro;
        ComboBox cFacing;
        Label introPreview;
        // selected object
        GroupBox gObj;
        Label lCell, lObjType;
        ComboBox cObjs;
        TextBox tOName, tOText1, tOText2, tOText3;
        BehaviourList oBeh, tBeh, lBeh, pBeh;   // behaviours: placed object, object type, level, pack
        CheckBox chOValue;
        NumericUpDown nOValue, nOP1, nOP2, nOX, nOY;
        Button bTarget, bRemove;
        // objects tab
        ListBox typeList;
        TextBox tTId, tTName, tTNouns, tTDesc, tTSymbol;
        ComboBox cTLook;
        CheckBox chSolid, chCarry, chKeep;
        NumericUpDown nTValue;
        Label lTypeUse;
        // game, title, scripts
        TextBox tVictory, tStatus, tStatusLabel, tBanner1, tBanner2, tSub, tLine1, tLine2, tLine3;
        NumericUpDown nStartHp, nMaxHp;
        PictureBox titlePic;
        Label lScriptResult;

        public MainForm(string open)
        {
            Text = "ZorEdit";
            ClientSize = new Size(1320, 760);
            MinimumSize = new Size(1150, 720);
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            BuildUi();
            if (open != null && File.Exists(open)) OpenFile(open);
            else NewPack();
        }

        Level Cur { get { int i = levelList.SelectedIndex; return i >= 0 && i < pack.Levels.Count ? pack.Levels[i] : pack.Levels[0]; } }

        // ================================================================ UI construction
        void BuildUi()
        {
            MenuStrip menu = new MenuStrip();
            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New Pack", Keys.Control | Keys.N, (s, e) => { if (ConfirmLose()) NewPack(); }));
            file.DropDownItems.Add(Item("&Open Pack...", Keys.Control | Keys.O, (s, e) => OpenDialog()));
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (s, e) => Save(false)));
            file.DropDownItems.Add(Item("Save &As...", Keys.None, (s, e) => Save(true)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Export &Level File (.LEV)...", Keys.Control | Keys.L, (s, e) => ExportLev()));
            file.DropDownItems.Add(Item("Export &Game (.CMD with levels built in)...", Keys.Control | Keys.E, (s, e) => ExportCmd()));
            file.DropDownItems.Add(Item("Export Game &Engine only (ZOR.CMD, loads ZOR/LEV)...", Keys.None, (s, e) => ExportEngine()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.None, (s, e) => Close()));
            ToolStripMenuItem edit = new ToolStripMenuItem("&Edit");
            // Ctrl+Z / Ctrl+Y are handled in ProcessCmdKey so text boxes keep their own undo.
            miUndo = Item("&Undo", Keys.None, (s, e) => Undo(true)); miUndo.ShortcutKeyDisplayString = "Ctrl+Z";
            miRedo = Item("&Redo", Keys.None, (s, e) => Undo(false)); miRedo.ShortcutKeyDisplayString = "Ctrl+Y";
            edit.DropDownItems.Add(miUndo);
            edit.DropDownItems.Add(miRedo);
            edit.DropDownOpening += (s, e) => { CommitUndo(); UpdateUndoMenu(); };
            undoTimer = new Timer { Interval = 500 };
            undoTimer.Tick += (s, e) => UndoTick();
            ToolStripMenuItem lev = new ToolStripMenuItem("&Level");
            lev.DropDownItems.Add(Item("&Add Level", Keys.None, (s, e) => AddLevel(BlankLevel())));
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
            test.DropDownItems.Add(Item("Play This &Level Only", Keys.F6, (s, e) => PlayLevel(false)));
            test.DropDownItems.Add(Item("Play This Level from the &Selected Cell", Keys.Shift | Keys.F6, (s, e) => PlayLevel(true)));
            test.DropDownItems.Add(Item("Play from TRSDOS &Disk in Emulator", Keys.Shift | Keys.F5, (s, e) => Play(true)));
            test.DropDownItems.Add(new ToolStripSeparator());
            test.DropDownItems.Add(Item("Set &Emulator Path...", Keys.None, (s, e) => PickEmulator()));
            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&Quick Guide", Keys.F1, (s, e) => ShowHelp()));
            help.DropDownItems.Add(Item("&Script Reference", Keys.Shift | Keys.F1, (s, e) => ShowScriptHelp()));
            menu.Items.AddRange(new ToolStripItem[] { file, edit, lev, test, help });

            tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(24, 24) };
            AddFixed("Select", MapCanvas.SelectIcon(), "Select a cell to edit its objects  (Esc)", Tool.Select);
            tools.Items.Add(new ToolStripSeparator());
            AddFixed("", MapCanvas.TerrainSwatch(true), "Wall  (key 1)", Tool.Wall);
            AddFixed("", MapCanvas.TerrainSwatch(false), "Floor  (key 2)", Tool.Floor);
            AddFixed("", MapCanvas.StartSwatch(), "Start position: click to place, click it again to turn  (key 3)", Tool.Start);
            tools.Items.Add(new ToolStripSeparator());
            toolLabel = new Label { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };

            StatusStrip status = new StatusStrip();
            sCell = new ToolStripStatusLabel("") { Width = 380, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            sInfo = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(sCell);
            status.Items.Add(sInfo);

            Panel left = new Panel { Dock = DockStyle.Left, Width = 190, Padding = new Padding(6) };
            Label ll = new Label { Text = "Levels (played in order)", Dock = DockStyle.Top, Height = 20 };
            levelList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            levelList.SelectedIndexChanged += (s, e) => ShowLevel();
            FlowLayoutPanel lb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 100 };
            lb.Controls.Add(Btn("Add", 54, (s, e) => AddLevel(BlankLevel())));
            lb.Controls.Add(Btn("Copy", 54, (s, e) => AddLevel(Cur.Clone())));
            lb.Controls.Add(Btn("Delete", 54, (s, e) => DeleteLevel()));
            lb.Controls.Add(Btn("Up", 54, (s, e) => MoveLevel(-1)));
            lb.Controls.Add(Btn("Down", 54, (s, e) => MoveLevel(1)));
            lb.Controls.Add(Btn("Maze", 54, (s, e) => GenerateMaze()));
            left.Controls.Add(levelList);
            left.Controls.Add(lb);
            left.Controls.Add(ll);

            TabControl tabs = new TabControl { Dock = DockStyle.Right, Width = 420 };
            tabs.TabPages.Add(LevelTab());
            tabs.TabPages.Add(ObjectsTab());
            tabs.TabPages.Add(ScriptTab());
            tabs.TabPages.Add(GameTab());
            tabs.TabPages.Add(TitleTab());
            tabs.SelectedIndexChanged += (s, e) => RefreshTitle();

            Panel mid = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(24, 24, 28) };
            canvas = new MapCanvas { Location = new Point(10, 10) };
            canvas.Painted += PaintCell;
            canvas.Hover += (x, y) => sCell.Text = x < 0 ? "" : CellText(x, y);
            mid.Controls.Add(canvas);

            Controls.Add(mid);
            Controls.Add(tabs);
            Controls.Add(left);
            Controls.Add(tools);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
        }

        void AddFixed(string text, Image img, string tip, Tool t)
        {
            ToolStripButton b = new ToolStripButton(text, img) { ToolTipText = tip, Tag = t };
            b.DisplayStyle = text.Length > 0 ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
            b.Click += (s, e) => SetTool(t, null);
            fixedButtons.Add(b);
            tools.Items.Add(b);
        }

        /// One palette button per object type.
        void RebuildPalette()
        {
            foreach (ToolStripItem b in typeButtons) tools.Items.Remove(b);
            typeButtons.Clear();
            int n = 0;
            foreach (ObjType t in pack.Types)
            {
                string id = t.Id;
                ToolStripButton b = new ToolStripButton("", MapCanvas.TypeSwatch(t)) { DisplayStyle = ToolStripItemDisplayStyle.Image, Tag = id };
                b.ToolTipText = t.Id + ": " + t.Name + (n < 7 ? "  (key " + "4567890"[n] + ")" : "");
                b.Click += (s, e) => SetTool(Tool.Object, id);
                typeButtons.Add(b);
                tools.Items.Add(b);
                n++;
            }
            ToolStripControlHost host = new ToolStripControlHost(toolLabel);
            typeButtons.Add(host);
            tools.Items.Add(host);
            if (tool == Tool.Object && pack.Type(toolType) == null) SetTool(Tool.Select, null);
            else SetTool(tool, toolType);
        }

        TabPage LevelTab()
        {
            TabPage p = new TabPage("Level") { AutoScroll = true };
            TableLayoutPanel t = Grid(p);
            tName = Box(20); Row(t, "Name", tName);
            cFacing = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cFacing.Items.AddRange(new object[] { "North", "East", "South", "West" });
            Row(t, "Start facing", cFacing);
            tIntro = new TextBox { Multiline = true, Height = 80, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, CharacterCasing = CharacterCasing.Upper };
            Row(t, "Intro text (| = new line)", tIntro);
            introPreview = new Label { AutoSize = true, Font = new Font("Consolas", 9f), BackColor = Color.Black, ForeColor = Color.FromArgb(210, 215, 255), Padding = new Padding(4), MinimumSize = new Size(250, 20) };
            Row(t, "As shown in the game", introPreview);
            tName.TextChanged += LevelChanged; tIntro.TextChanged += LevelChanged; cFacing.SelectedIndexChanged += LevelChanged;

            gObj = new GroupBox { Text = "Selected object", Width = 385, Height = 640 };
            TableLayoutPanel c = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(4) };
            lCell = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            c.Controls.Add(lCell);
            cObjs = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
            cObjs.SelectedIndexChanged += (s, e) => { if (!loading && cObjs.SelectedItem is ObjItem) SelectObj(((ObjItem)cObjs.SelectedItem).O); };
            c.Controls.Add(cObjs);
            lObjType = new Label { AutoSize = true, MaximumSize = new Size(360, 0), ForeColor = Color.DimGray };
            c.Controls.Add(lObjType);
            FlowLayoutPanel where = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            where.Controls.Add(new Label { Text = "X", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            nOX = Num(0, Level.W - 2); nOX.Width = 50; where.Controls.Add(nOX);
            where.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            nOY = Num(0, Level.H - 2); nOY.Width = 50; where.Controls.Add(nOY);
            where.Controls.Add(new Label { Text = "(0,0 nowhere yet, 0,1 carried)", AutoSize = true, Padding = new Padding(0, 6, 0, 0), ForeColor = Color.DimGray });
            c.Controls.Add(where);
            FlowLayoutPanel vals = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            chOValue = new CheckBox { Text = "Own value", AutoSize = true };
            vals.Controls.Add(chOValue);
            nOValue = Num(0, 255); nOValue.Width = 55; vals.Controls.Add(nOValue);
            vals.Controls.Add(new Label { Text = "P1", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
            nOP1 = Num(0, 255); nOP1.Width = 50; vals.Controls.Add(nOP1);
            vals.Controls.Add(new Label { Text = "P2", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            nOP2 = Num(0, 255); nOP2.Width = 50; vals.Controls.Add(nOP2);
            c.Controls.Add(vals);
            FlowLayoutPanel bt = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            bTarget = Btn("Pick P1,P2 as target on map", 180, (s, e) => { if (selObj != null) { picking = true; lObjType.Text = "Click the target cell on the map (Esc cancels)."; } });
            bRemove = Btn("Remove object", 110, (s, e) => RemoveSelected());
            bt.Controls.Add(bTarget); bt.Controls.Add(bRemove);
            c.Controls.Add(bt);
            c.Controls.Add(new Label { Text = "Object name (so level rules can refer to it)", AutoSize = true });
            tOName = new TextBox { Width = 160 };
            c.Controls.Add(tOName);
            c.Controls.Add(new Label { Text = "Description (blank = the object type's)", AutoSize = true });
            tOText1 = TextArea(360, 40); c.Controls.Add(tOText1);
            c.Controls.Add(new Label { Text = "Text 2 (say this 2 / speak this 2)", AutoSize = true });
            tOText2 = TextArea(360, 40); c.Controls.Add(tOText2);
            c.Controls.Add(new Label { Text = "Text 3", AutoSize = true });
            tOText3 = TextArea(360, 40); c.Controls.Add(tOText3);
            c.Controls.Add(new Label { Text = "This object's own behaviours (tried before its kind's)", AutoSize = true });
            oBeh = new BehaviourList { Width = 360, Height = 130 };
            oBeh.Context = ObjContext;
            oBeh.NewRule = "on arrive then say \"\"";
            oBeh.ScriptChanged += (s, e) => { if (loading || selObj == null) return; selObj.Script = oBeh.Script; canvas.Invalidate(); MarkDirty(); UpdateInfo(); };
            c.Controls.Add(oBeh);
            gObj.Controls.Add(c);
            Row(t, "", gObj);
            foreach (Control k in new Control[] { nOX, nOY, chOValue, nOValue, nOP1, nOP2, tOName, tOText1, tOText2, tOText3 })
            {
                if (k is NumericUpDown) ((NumericUpDown)k).ValueChanged += ObjChanged;
                else if (k is CheckBox) ((CheckBox)k).CheckedChanged += ObjChanged;
                else k.TextChanged += ObjChanged;
            }
            return p;
        }

        class ObjItem
        {
            public Inst O; public string S;
            public override string ToString() { return S; }
        }

        TabPage ObjectsTab()
        {
            TabPage p = new TabPage("Objects");
            Panel top = new Panel { Dock = DockStyle.Top, Height = 170, Padding = new Padding(6) };
            typeList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            typeList.SelectedIndexChanged += (s, e) => ShowType();
            FlowLayoutPanel tb = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 90, FlowDirection = FlowDirection.TopDown };
            tb.Controls.Add(Btn("New", 80, (s, e) => NewType(null)));
            tb.Controls.Add(Btn("Copy", 80, (s, e) => NewType(SelType())));
            tb.Controls.Add(Btn("Delete", 80, (s, e) => DeleteType()));
            tb.Controls.Add(Btn("Up", 80, (s, e) => MoveType(-1)));
            tb.Controls.Add(Btn("Down", 80, (s, e) => MoveType(1)));
            tb.Controls.Add(Btn("Library...", 80, (s, e) => AddFromLibrary()));
            top.Height = 200;
            top.Controls.Add(typeList);
            top.Controls.Add(tb);
            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(6) };
            body.Controls.Add(t);
            tTId = new TextBox { Width = 160 }; Row(t, "Id (one word, used in scripts)", tTId);
            tTName = Box(24); Row(t, "Name (shown in messages and the inventory)", tTName);
            FlowLayoutPanel lk = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            cTLook = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cTLook.Items.AddRange(ObjType.LookNames);
            lk.Controls.Add(cTLook);
            lk.Controls.Add(new Label { Text = "Map symbol", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            tTSymbol = new TextBox { Width = 24, MaxLength = 1 };
            lk.Controls.Add(tTSymbol);
            Row(t, "Looks like", lk);
            FlowLayoutPanel fl = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            chSolid = new CheckBox { Text = "Solid (blocks the way)", AutoSize = true };
            chCarry = new CheckBox { Text = "Can be carried", AutoSize = true };
            chKeep = new CheckBox { Text = "Kept on next level", AutoSize = true };
            fl.Controls.Add(chSolid); fl.Controls.Add(chCarry); fl.Controls.Add(chKeep);
            Row(t, "", fl);
            tTNouns = new TextBox { CharacterCasing = CharacterCasing.Upper }; Row(t, "Nouns the player can type (comma separated)", tTNouns);
            nTValue = Num(0, 255); Row(t, "Starting value (hit points, uses, amount...)", nTValue);
            tTDesc = TextArea(380, 44); Row(t, "Description (LOOK)", tTDesc);
            tBeh = new BehaviourList { Width = 380, Height = 220 };
            tBeh.Context = TypeContext;
            tBeh.NewRule = null;
            tBeh.ScriptChanged += (s, e) => { if (loading) return; ObjType ty = SelType(); if (ty == null) return; ty.Script = tBeh.Script; canvas.Invalidate(); MarkDirty(); UpdateInfo(); };
            Row(t, "Behaviours of every object of this kind (\"this\" = the placed object)", tBeh);
            lTypeUse = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.DimGray };
            Row(t, "", lTypeUse);
            foreach (Control k in new Control[] { tTId, tTName, tTSymbol, tTNouns, tTDesc }) k.TextChanged += TypeChanged;
            foreach (CheckBox k in new[] { chSolid, chCarry, chKeep }) k.CheckedChanged += TypeChanged;
            nTValue.ValueChanged += TypeChanged;
            cTLook.SelectedIndexChanged += TypeChanged;
            p.Controls.Add(body);
            p.Controls.Add(top);
            return p;
        }

        TabPage ScriptTab()
        {
            TabPage p = new TabPage("Scripts");
            SplitContainer sp = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 330 };
            lBeh = new BehaviourList { Dock = DockStyle.Fill };
            lBeh.Context = () => new BehaviourContext { Pack = pack, Level = Cur, Title = "level " + Cur.Name, Changed = BuilderChanged };
            lBeh.NewRule = "on arrive if at 1,1 then say \"\"";
            pBeh = new BehaviourList { Dock = DockStyle.Fill };
            pBeh.Context = () => new BehaviourContext { Pack = pack, Title = "every level" };
            sp.Panel1.Controls.Add(lBeh);
            sp.Panel1.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, Text = "This level's behaviours (cells, named objects; right-click a cell too):" });
            sp.Panel2.Controls.Add(pBeh);
            sp.Panel2.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, Text = "Behaviours on every level (synonyms go here):" });
            lBeh.ScriptChanged += (s, e) => { if (loading) return; Cur.Script = lBeh.Script; canvas.Invalidate(); MarkDirty(); UpdateInfo(); };
            pBeh.ScriptChanged += (s, e) => { if (loading) return; pack.Script = pBeh.Script; MarkDirty(); UpdateInfo(); };
            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 110, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Height = 30, Width = 400 };
            buttons.Controls.Add(Btn("Check scripts", 100, (s, e) => CheckScript()));
            buttons.Controls.Add(Btn("Reference", 90, (s, e) => ShowScriptHelp()));
            lScriptResult = new Label { AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = Color.DarkGreen };
            bottom.Controls.Add(buttons);
            bottom.Controls.Add(lScriptResult);
            p.Controls.Add(sp);
            p.Controls.Add(bottom);
            return p;
        }

        static TextBox ScriptBox()
        {
            return new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsTab = true, Font = new Font("Consolas", 9f) };
        }

        TabPage GameTab()
        {
            TabPage p = new TabPage("Game") { AutoScroll = true };
            TableLayoutPanel t = Grid(p);
            tVictory = new TextBox { Multiline = true, Height = 60, Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper };
            Row(t, "Victory text (after the last level)", tVictory);
            nStartHp = Num(1, 99); Row(t, "Starting HP", nStartHp);
            nMaxHp = Num(1, 99); Row(t, "Maximum HP", nMaxHp);
            tStatus = new TextBox { Width = 120 }; Row(t, "Counter shown on the status line (blank = none)", tStatus);
            tStatusLabel = new TextBox { Width = 40, MaxLength = 2, CharacterCasing = CharacterCasing.Upper }; Row(t, "Its label (2 characters)", tStatusLabel);
            tVictory.TextChanged += PackChanged; tStatus.TextChanged += PackChanged; tStatusLabel.TextChanged += PackChanged;
            nStartHp.ValueChanged += PackChanged; nMaxHp.ValueChanged += PackChanged;
            return p;
        }

        TabPage TitleTab()
        {
            TabPage p = new TabPage("Title");
            TableLayoutPanel t = Grid(p);
            tBanner1 = Box(10); Row(t, "Banner line 1 (big)", tBanner1);
            tBanner2 = Box(10); Row(t, "Banner line 2 (big)", tBanner2);
            tSub = Box(63); Row(t, "Subtitle", tSub);
            tLine1 = Box(63); Row(t, "Text line 1", tLine1);
            tLine2 = Box(63); Row(t, "Text line 2", tLine2);
            tLine3 = Box(63); Row(t, "Text line 3", tLine3);
            titlePic = new PictureBox { Width = 384, Height = 192, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            Row(t, "Preview", titlePic);
            foreach (TextBox b in new[] { tBanner1, tBanner2, tSub, tLine1, tLine2, tLine3 }) b.TextChanged += PackChanged;
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
            if (c is TextBox && !((TextBox)c).Multiline && c.Width == 100) c.Width = 370;
            t.Controls.Add(c);
        }

        static TextBox Box(int max) { return new TextBox { MaxLength = max, CharacterCasing = CharacterCasing.Upper }; }
        static TextBox TextArea(int w, int h) { return new TextBox { Multiline = true, Width = w, Height = h, ScrollBars = ScrollBars.Vertical, CharacterCasing = CharacterCasing.Upper }; }
        static NumericUpDown Num(int lo, int hi) { return new NumericUpDown { Minimum = lo, Maximum = hi, Width = 80 }; }
        static Button Btn(string text, int w, EventHandler h) { Button b = new Button { Text = text, Width = w }; b.Click += h; return b; }
        static ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text, null, h);
            if (keys != Keys.None) m.ShortcutKeys = keys;
            return m;
        }

        // ================================================================ model <-> UI
        static Level BlankLevel()
        {
            Level l = new Level { Name = "NEW LEVEL" };
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++)
                    l.Wall[y, x] = Level.IsBorder(x, y);
            return l;
        }

        void NewPack()
        {
            pack = new Pack();
            pack.Levels.Add(BlankLevel());
            path = null;
            LoadUi();
            dirty = false;
            UpdateTitle();
            ResetUndo();
        }

        void OpenFile(string f)
        {
            try
            {
                bool old = !File.ReadAllLines(f).Any(l => Regex.IsMatch(l.Trim(), @"^format\s*=\s*5\b", RegexOptions.IgnoreCase));
                pack = Zlp.Load(f);
                path = old ? null : f;
                LoadUi();
                dirty = old;
                UpdateTitle();
                ResetUndo();
                if (old) Info(Path.GetFileName(f) + " is an older pack. It has been converted: every kind of cell is now an object type you can change on the Objects tab, and script items are objects on the map.\n\nSave it under a new name to keep the converted version.");
                else Settings.Set("last", f);
            }
            catch (Exception ex) { Error("Could not open " + f + "\n\n" + ex.Message); }
        }

        void LoadUi()
        {
            loading = true;
            tBanner1.Text = pack.Banner1; tBanner2.Text = pack.Banner2; tSub.Text = pack.Subtitle;
            tLine1.Text = pack.Line1; tLine2.Text = pack.Line2; tLine3.Text = pack.Line3;
            tVictory.Text = pack.Victory.Replace("|", "\r\n");
            SetNum(nStartHp, pack.StartHp); SetNum(nMaxHp, pack.MaxHp);
            tStatus.Text = pack.StatusCounter; tStatusLabel.Text = pack.StatusLabel;
            pBeh.Script = pack.Script;
            RefreshList(0);
            RefreshTypes(0);
            loading = false;
            RebuildPalette();
            ShowLevel();
            ShowType();
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
            cFacing.SelectedIndex = l.Facing & 3;
            tIntro.Text = (l.Intro ?? "").Replace("|", "\r\n");
            lBeh.Script = l.Script;
            lScriptResult.Text = "";
            loading = false;
            canvas.Pack = pack;
            canvas.Level = l;
            Select(-1, -1);
            UpdatePreview();
            UpdateInfo();
        }

        void LevelChanged(object sender, EventArgs e)
        {
            if (loading) return;
            Level l = Cur;
            l.Name = tName.Text;
            l.Facing = Math.Max(0, cFacing.SelectedIndex);
            l.Intro = tIntro.Text.Replace("\r\n", "|");
            if (sender == tName) { int i = levelList.SelectedIndex; levelList.Items[i] = (i + 1) + ". " + l.Name; }
            canvas.Invalidate();
            UpdatePreview();
            MarkDirty();
        }

        void PackChanged(object sender, EventArgs e)
        {
            if (loading) return;
            pack.Banner1 = tBanner1.Text; pack.Banner2 = tBanner2.Text; pack.Subtitle = tSub.Text;
            pack.Line1 = tLine1.Text; pack.Line2 = tLine2.Text; pack.Line3 = tLine3.Text;
            pack.Victory = tVictory.Text.Replace("\r\n", "|");
            pack.StartHp = (int)nStartHp.Value; pack.MaxHp = (int)nMaxHp.Value;
            pack.StatusCounter = tStatus.Text.Trim(); pack.StatusLabel = tStatusLabel.Text;
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
            Bitmap bmp = Trs80Screen.Render(Compiler.RenderTitle(pack), 64, 16);
            Image old = titlePic.Image;
            titlePic.Image = bmp;
            if (old != null) old.Dispose();
        }

        void UpdateInfo()
        {
            ScriptCompiler sc;
            try
            {
                int size = Compiler.Build(pack, out sc).Length;
                sInfo.Text = pack.Levels.Count + " level(s), " + pack.Types.Count + " kinds of object, level file " + size + " bytes." +
                             (sc.Errors.Count > 0 ? "  Script problem: " + sc.Errors[0] : "");
            }
            catch (Exception ex) { sInfo.Text = ex.Message; }
        }

        void MarkDirty()
        {
            if (!dirty) { dirty = true; UpdateTitle(); }
            if (undoTimer != null) { undoTimer.Stop(); undoTimer.Start(); }
        }

        // ================================================================ undo / redo
        // The whole pack is kept as .ZLP text. Edits are grouped: a snapshot is taken
        // once editing has paused for a moment (and never while a mouse button is held).
        Stack<string> undo = new Stack<string>(), redo = new Stack<string>();
        string snapshot;
        Timer undoTimer;
        ToolStripMenuItem miUndo, miRedo;

        static string Snap(Pack p) { return string.Join("\n", Zlp.Lines(p)); }

        void ResetUndo()
        {
            undo.Clear(); redo.Clear();
            snapshot = Snap(pack);
            if (undoTimer != null) undoTimer.Stop();
            UpdateUndoMenu();
        }

        void CommitUndo()
        {
            if (pack == null || snapshot == null) return;
            if (undoTimer != null) undoTimer.Stop();
            string now = Snap(pack);
            if (now == snapshot) return;
            undo.Push(snapshot);
            snapshot = now;
            redo.Clear();
            UpdateUndoMenu();
        }

        void UndoTick()
        {
            if (Control.MouseButtons != MouseButtons.None) return;   // still dragging: try again next tick
            CommitUndo();
        }

        void UpdateUndoMenu()
        {
            if (miUndo != null) miUndo.Enabled = undo.Count > 0;
            if (miRedo != null) miRedo.Enabled = redo.Count > 0;
        }

        void Undo(bool back)
        {
            CommitUndo();
            Stack<string> from = back ? undo : redo, to = back ? redo : undo;
            if (from.Count == 0) return;
            to.Push(snapshot);
            snapshot = from.Pop();
            int li = levelList.SelectedIndex, ti = typeList.SelectedIndex, x = selX, y = selY;
            pack = Zlp.Parse(snapshot.Split('\n'));
            LoadUi();
            RefreshList(li);
            ShowLevel();
            RefreshTypes(ti);
            ShowType();
            if (x >= 0 && x < Level.W && y >= 0 && y < Level.H) Select(x, y);
            dirty = true;
            UpdateTitle();
            UpdateUndoMenu();
        }

        /// The control that really has the focus (inside any containers).
        Control FocusedControl()
        {
            Control c = ActiveControl;
            while (c is ContainerControl && ((ContainerControl)c).ActiveControl != null) c = ((ContainerControl)c).ActiveControl;
            return c;
        }

        bool TextFocused()
        {
            Control c = FocusedControl();
            return c is TextBoxBase || c is NumericUpDown || c is ComboBox || (c != null && c.Parent is NumericUpDown);
        }
        void UpdateTitle() { Text = "ZorEdit - " + (path == null ? "untitled" : Path.GetFileName(path)) + (dirty ? " *" : "") + "   (Vaults of Zor level editor)"; }

        string CellText(int x, int y)
        {
            Level l = Cur;
            string s = "Cell " + x + "," + y + "  " + (l.Wall[y, x] ? "wall" : "floor");
            if (x == l.StartX && y == l.StartY) s += "  start";
            foreach (Inst o in l.At(x, y)) s += "  + " + o.Type + (o.Name.Length > 0 ? " (" + o.Name + ")" : "");
            return s;
        }

        // ================================================================ painting
        void SetTool(Tool t, string type)
        {
            tool = t;
            toolType = type;
            picking = false;
            foreach (ToolStripButton b in fixedButtons) b.Checked = (Tool)b.Tag == t;
            foreach (ToolStripItem b in typeButtons) if (b is ToolStripButton) ((ToolStripButton)b).Checked = t == Tool.Object && (string)b.Tag == type;
            ObjType ty = pack == null ? null : pack.Type(type);
            toolLabel.Text = t == Tool.Select ? "Select: click a cell to edit its objects; right-click for its menu." :
                             t == Tool.Wall ? "Painting wall. Right button paints floor." :
                             t == Tool.Floor ? "Painting floor. Right button paints wall." :
                             t == Tool.Start ? "Start: click to place the player, click again to turn." :
                             "Placing " + (ty == null ? type : ty.Id + " (" + ty.Name + ")") + ". Right-click a cell for its menu.";
            if (canvas != null) canvas.Cursor = t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        }

        void PaintCell(int x, int y, bool right, bool first)
        {
            Level l = Cur;
            if (picking)
            {
                picking = false;
                if (selObj != null && !Level.IsBorder(x, y)) { selObj.P1 = x; selObj.P2 = y; MarkDirty(); }
                canvas.Hold = true;
                ShowObjPanel();
                canvas.Invalidate();
                return;
            }
            if (right && first && (tool == Tool.Select || tool == Tool.Object)) { canvas.Hold = true; Select(x, y); CellMenu(x, y); return; }
            if (tool == Tool.Select) { if (first) Select(x, y); return; }
            if (Level.IsBorder(x, y)) return;
            bool changed = false;
            switch (tool)
            {
                case Tool.Wall:
                case Tool.Floor:
                    {
                        bool wall = (tool == Tool.Wall) != right;
                        if (wall && x == l.StartX && y == l.StartY) break;
                        if (l.Wall[y, x] != wall) { l.Wall[y, x] = wall; changed = true; }
                        break;
                    }
                case Tool.Start:
                    if (!first) break;
                    if (x == l.StartX && y == l.StartY) l.Facing = (l.Facing + (right ? 3 : 1)) & 3;
                    else { l.StartX = x; l.StartY = y; l.Wall[y, x] = false; }
                    loading = true; cFacing.SelectedIndex = l.Facing; loading = false;
                    changed = true;
                    break;
                case Tool.Object:
                    if (!first) break;
                    if (right)
                    {
                        Inst top = l.At(x, y).LastOrDefault();
                        if (top != null) { l.Objs.Remove(top); changed = true; }
                        Select(x, y);
                    }
                    else
                    {
                        if (l.At(x, y).Any(o => o.Type == toolType)) { Select(x, y); break; }
                        if (l.Objs.Count >= Level.MaxObjects) { Error("A level can hold at most " + Level.MaxObjects + " objects."); break; }
                        ObjType ty = pack.Type(toolType);
                        if (ty != null && ty.Look != 1) l.Wall[y, x] = false;
                        Inst ni = new Inst { X = x, Y = y, Type = toolType };
                        l.Objs.Add(ni);
                        changed = true;
                        Select(x, y);
                        SelectObj(ni);
                    }
                    break;
            }
            if (changed) { MarkDirty(); UpdateInfo(); }
            canvas.Invalidate();
        }

        // ================================================================ behaviours
        BehaviourContext ObjContext()
        {
            Inst o = selObj;
            int ti = o == null ? -1 : pack.TypeIndex(o.Type);
            return new BehaviourContext
            {
                Pack = pack, Level = Cur, Scope = ti + 1, Self = o == null ? -1 : Cur.Objs.IndexOf(o), Changed = BuilderChanged,
                Title = o == null ? "" : "this " + o.Type + (o.Name.Length > 0 ? " (" + o.Name + ")" : "") + (o.OnMap ? " at " + o.X + "," + o.Y : "")
            };
        }

        /// The behaviour builder's map picker gave an object a name.
        void BuilderChanged()
        {
            if (selObj != null && tOName.Text != selObj.Name) { bool was = loading; loading = true; tOName.Text = selObj.Name; loading = was; }
            canvas.Invalidate();
            if (sCell != null && selX >= 0) sCell.Text = CellText(selX, selY);
            MarkDirty();
        }

        BehaviourContext TypeContext()
        {
            ObjType t = SelType();
            return new BehaviourContext { Pack = pack, Scope = t == null ? 0 : pack.TypeIndex(t.Id) + 1, Title = t == null ? "" : "every " + t.Id };
        }

        static string CellPattern(int x, int y) { return @"\bat\s+" + x + @"\s*,\s*" + y + @"\b"; }

        /// The right-click menu of a map cell.
        void CellMenu(int x, int y)
        {
            Level l = Cur;
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add(new ToolStripLabel("Cell " + x + "," + y + (l.Wall[y, x] ? " (wall)" : "")) { Font = new Font(Font, FontStyle.Bold) });
            bool border = Level.IsBorder(x, y);
            if (!border)
            {
                ToolStripMenuItem place = new ToolStripMenuItem("Place object");
                foreach (ObjType t in pack.Types)
                {
                    string id = t.Id;
                    place.DropDownItems.Add(new ToolStripMenuItem(t.Id + "  -  " + t.Name, MapCanvas.TypeSwatch(t), (s, e) => PlaceAt(x, y, id)));
                }
                if (pack.Types.Count == 0) place.DropDownItems.Add(new ToolStripMenuItem("(no kinds of object yet: Objects tab > New or Library)") { Enabled = false });
                place.DropDownItems.Add(new ToolStripSeparator());
                place.DropDownItems.Add(new ToolStripMenuItem("From the library...", null, (s, e) => { AddFromLibrary(); }));
                m.Items.Add(place);
            }
            foreach (Inst o in l.At(x, y).ToList())
            {
                Inst oo = o;
                ObjType t = pack.Type(o.Type);
                ToolStripMenuItem it = new ToolStripMenuItem(o.Type + (o.Name.Length > 0 ? " (" + o.Name + ")" : ""), t == null ? null : MapCanvas.TypeSwatch(t));
                it.DropDownItems.Add(new ToolStripMenuItem("Select / edit", null, (s, e) => SelectObj(oo)));
                it.DropDownItems.Add(new ToolStripMenuItem("Add a behaviour to this object only...", null, (s, e) => { SelectObj(oo); oBeh.Add(); }));
                it.DropDownItems.Add(new ToolStripMenuItem("Add a behaviour to every " + o.Type + "...", null, (s, e) => EditTypeBehaviour(oo.Type)));
                it.DropDownItems.Add(new ToolStripMenuItem("Pick its target (P1,P2) on the map", null, (s, e) => { SelectObj(oo); picking = true; lObjType.Text = "Click the target cell on the map (Esc cancels)."; }));
                it.DropDownItems.Add(new ToolStripMenuItem("Remove", null, (s, e) => { SelectObj(oo); RemoveSelected(); }));
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            if (!border)
            {
                m.Items.Add(new ToolStripMenuItem("Add a behaviour to this cell (when the player steps here)...", null, (s, e) =>
                    lBeh.Add("on arrive if at " + x + "," + y + " then say \"\"")));
                m.Items.Add(new ToolStripMenuItem("Add a behaviour for walking into this cell...", null, (s, e) =>
                    lBeh.Add("on bump if ahead " + x + "," + y + " then say \"\"")));
                List<int> rules = CellRules(x, y);
                if (rules.Count > 0)
                {
                    ToolStripMenuItem ed = new ToolStripMenuItem("Edit this cell's behaviours");
                    foreach (int k in rules)
                    {
                        int kk = k;
                        string line = lBeh.Statement(k);
                        ed.DropDownItems.Add(new ToolStripMenuItem(line.Length > 80 ? line.Substring(0, 77) + "..." : line, null, (s, e) => lBeh.EditAt(kk)));
                    }
                    m.Items.Add(ed);
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem("Play this level from here (Shift+F6)", null, (s, e) => PlayLevel(true)));
                m.Items.Add(new ToolStripMenuItem("Start here", null, (s, e) =>
                {
                    l.StartX = x; l.StartY = y; l.Wall[y, x] = false;
                    canvas.Invalidate(); MarkDirty(); UpdateInfo();
                }));
                m.Items.Add(new ToolStripMenuItem(l.Wall[y, x] ? "Make floor" : "Make wall", null, (s, e) =>
                {
                    if (!l.Wall[y, x] && x == l.StartX && y == l.StartY) return;
                    l.Wall[y, x] = !l.Wall[y, x];
                    canvas.Invalidate(); MarkDirty(); UpdateInfo();
                }));
            }
            m.Closed += (s, e) => canvas.Hold = false;
            m.Show(canvas, canvas.PointToClient(Cursor.Position));
        }

        /// Level statements that mention cell x,y.
        List<int> CellRules(int x, int y)
        {
            List<int> r = new List<int>();
            for (int k = 0; k < lBeh.Count; k++)
                if (Regex.IsMatch(lBeh.Statement(k), CellPattern(x, y), RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(lBeh.Statement(k), @"\bahead\s+" + x + @"\s*,\s*" + y + @"\b", RegexOptions.IgnoreCase)) r.Add(k);
            return r;
        }

        void PlaceAt(int x, int y, string type)
        {
            Level l = Cur;
            if (l.Objs.Count >= Level.MaxObjects) { Error("A level can hold at most " + Level.MaxObjects + " objects."); return; }
            ObjType ty = pack.Type(type);
            if (ty != null && ty.Look != 1) l.Wall[y, x] = false;
            Inst ni = new Inst { X = x, Y = y, Type = type };
            l.Objs.Add(ni);
            Select(x, y);
            SelectObj(ni);
            canvas.Invalidate();
            MarkDirty();
            UpdateInfo();
        }

        void EditTypeBehaviour(string id)
        {
            int i = pack.TypeIndex(id);
            if (i < 0) return;
            typeList.SelectedIndex = i;
            tBeh.Add();
        }

        void AddFromLibrary()
        {
            Pack lib = Library.Load();
            if (lib.Types.Count == 0) { Error("The library (levels\\library.zlp) could not be found or is empty."); return; }
            using (LibraryForm f = new LibraryForm(lib, pack))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Chosen.Count == 0) return;
                if (pack.Types.Count + f.Chosen.Count > Pack.MaxTypes) { Error("A pack can have at most " + Pack.MaxTypes + " kinds of object."); return; }
                string status = pack.StatusCounter;
                List<string> added = Library.Import(lib, pack, f.Chosen);
                if (pack.StatusCounter != status) { loading = true; tStatus.Text = pack.StatusCounter; tStatusLabel.Text = pack.StatusLabel; loading = false; }
                RefreshTypes(pack.Types.Count - 1);
                ShowType();
                RebuildPalette();
                canvas.Invalidate();
                MarkDirty();
                UpdateInfo();
                List<string> extra = added.Except(f.Chosen, StringComparer.OrdinalIgnoreCase).ToList();
                sInfo.Text = "Added " + string.Join(", ", added) + (extra.Count > 0 ? "  (" + string.Join(", ", extra) + " came along because they are needed)" : "") + ".";
            }
        }

        void Select(int x, int y)
        {
            selX = x; selY = y;
            canvas.Selected = new Point(x, y);
            selObj = x < 0 ? null : Cur.At(x, y).FirstOrDefault();
            ShowObjPanel();
        }

        void SelectObj(Inst o)
        {
            selObj = o;
            if (o != null && o.OnMap) { selX = o.X; selY = o.Y; canvas.Selected = new Point(o.X, o.Y); }
            ShowObjPanel();
        }

        /// Fill the selected-object panel.
        void ShowObjPanel()
        {
            if (gObj == null || pack == null) return;
            bool was = loading;
            loading = true;
            Level l = Cur;
            lCell.Text = selX < 0 ? "No cell selected" : CellText(selX, selY);
            cObjs.Items.Clear();
            foreach (Inst o in l.Objs.OrderBy(o => o.OnMap ? 1 : 0).ThenBy(o => o.Y).ThenBy(o => o.X))
            {
                string where = o.Nowhere ? "nowhere" : o.Carried ? "carried" : o.X + "," + o.Y;
                ObjItem it = new ObjItem { O = o, S = (o.Name.Length > 0 ? o.Name + " - " : "") + o.Type + "  at " + where };
                cObjs.Items.Add(it);
                if (o == selObj) cObjs.SelectedItem = it;
            }
            bool on = selObj != null;
            foreach (Control k in new Control[] { nOX, nOY, chOValue, nOValue, nOP1, nOP2, tOName, tOText1, tOText2, tOText3, oBeh, bTarget, bRemove }) k.Enabled = on;
            if (on)
            {
                ObjType t = pack.Type(selObj.Type);
                lObjType.Text = t == null ? "Unknown kind of object '" + selObj.Type + "'." :
                    t.Id + ": " + t.Name + (t.Solid ? ", solid" : "") + (t.Carry ? ", can be carried" : "") + ".  Its starting value is " + t.Value + ".";
                SetNum(nOX, selObj.X); SetNum(nOY, selObj.Y);
                chOValue.Checked = selObj.Value.HasValue;
                SetNum(nOValue, selObj.Value ?? (t == null ? 0 : t.Value));
                nOValue.Enabled = selObj.Value.HasValue;
                SetNum(nOP1, selObj.P1); SetNum(nOP2, selObj.P2);
                tOName.Text = selObj.Name;
                tOText1.Text = selObj.Text(1).Replace("|", "\r\n");
                tOText2.Text = selObj.Text(2).Replace("|", "\r\n");
                tOText3.Text = selObj.Text(3).Replace("|", "\r\n");
                oBeh.Script = selObj.Script;
            }
            else
            {
                lObjType.Text = selX < 0 ? "Use the Select tool (Esc) and click a cell, or pick an object above. Right-click a cell for its menu." : "Nothing here. Pick a kind of object on the toolbar to place one, or right-click the cell.";
                foreach (TextBox b in new[] { tOName, tOText1, tOText2, tOText3 }) b.Text = "";
                oBeh.Script = "";
            }
            canvas.SelectedObj = selObj;
            loading = was;
        }

        void ObjChanged(object sender, EventArgs e)
        {
            if (loading || selObj == null) return;
            Inst o = selObj;
            o.X = (int)nOX.Value; o.Y = (int)nOY.Value;
            if (o.X == 0 && o.Y > 1) o.Y = 1;
            o.Value = chOValue.Checked ? (int?)nOValue.Value : null;
            nOValue.Enabled = chOValue.Checked;
            o.P1 = (int)nOP1.Value; o.P2 = (int)nOP2.Value;
            o.Name = tOName.Text.Trim();
            o.SetText(1, tOText1.Text.Replace("\r\n", "|"));
            o.SetText(2, tOText2.Text.Replace("\r\n", "|"));
            o.SetText(3, tOText3.Text.Replace("\r\n", "|"));
            if (sender == nOX || sender == nOY || sender == tOName) { SelectObj(o); }
            canvas.Invalidate();
            MarkDirty();
        }

        void RemoveSelected()
        {
            if (selObj == null) return;
            Cur.Objs.Remove(selObj);
            selObj = null;
            Select(selX, selY);
            canvas.Invalidate();
            MarkDirty();
            UpdateInfo();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!TextFocused() && (keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)))
            {
                Undo(keyData == (Keys.Control | Keys.Z));
                return true;
            }
            if (!TextFocused() && (keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == 0)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.Escape) { if (picking) { picking = false; ShowObjPanel(); } else SetTool(Tool.Select, null); return true; }
                if (code == Keys.D1) { SetTool(Tool.Wall, null); return true; }
                if (code == Keys.D2) { SetTool(Tool.Floor, null); return true; }
                if (code == Keys.D3) { SetTool(Tool.Start, null); return true; }
                int k = "4567890".IndexOf((char)('0' + ((int)code - (int)Keys.D0)));
                if (code >= Keys.D0 && code <= Keys.D9 && k >= 0 && k < pack.Types.Count) { SetTool(Tool.Object, pack.Types[k].Id); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================================================================ object types
        void RefreshTypes(int select)
        {
            bool was = loading;
            loading = true;
            typeList.Items.Clear();
            foreach (ObjType t in pack.Types) typeList.Items.Add(t.Id + "  -  " + t.Name);
            if (pack.Types.Count > 0) typeList.SelectedIndex = Math.Max(0, Math.Min(select, pack.Types.Count - 1));
            loading = was;
        }

        ObjType SelType() { int i = typeList.SelectedIndex; return i >= 0 && i < pack.Types.Count ? pack.Types[i] : null; }

        void ShowType()
        {
            if (loading || pack == null) return;
            loading = true;
            ObjType t = SelType();
            foreach (Control k in new Control[] { tTId, tTName, tTSymbol, tTNouns, tTDesc, tBeh, chSolid, chCarry, chKeep, nTValue, cTLook }) k.Enabled = t != null;
            if (t != null)
            {
                tTId.Text = t.Id; tTName.Text = t.Name; tTSymbol.Text = t.Symbol == '\0' ? "" : t.Symbol.ToString();
                cTLook.SelectedIndex = Math.Max(0, Array.IndexOf(ObjType.LookCodes, t.Look));
                chSolid.Checked = t.Solid; chCarry.Checked = t.Carry; chKeep.Checked = t.Keep;
                tTNouns.Text = t.Nouns; SetNum(nTValue, t.Value);
                tTDesc.Text = t.Desc.Replace("|", "\r\n");
                tBeh.Script = t.Script;
                int n = pack.Levels.Sum(l => l.Objs.Count(o => o.Type.Equals(t.Id, StringComparison.OrdinalIgnoreCase)));
                lTypeUse.Text = n + " placed in the pack.";
            }
            else { lTypeUse.Text = "No kinds of object yet. Press New to make one, or Library to copy ready-made ones."; tBeh.Script = ""; }
            loading = false;
        }

        void TypeChanged(object sender, EventArgs e)
        {
            if (loading) return;
            ObjType t = SelType();
            if (t == null) return;
            string oldId = t.Id, nid = tTId.Text.Trim();
            if (sender == tTId && nid.Length > 0 && Util.IsWord(nid) && pack.Type(nid) == null)
            {
                t.Id = nid;                 // rename: placed objects follow
                foreach (Level l in pack.Levels) foreach (Inst o in l.Objs) if (o.Type.Equals(oldId, StringComparison.OrdinalIgnoreCase)) o.Type = nid;
                if (toolType == oldId) toolType = nid;
            }
            t.Name = tTName.Text;
            t.Symbol = tTSymbol.Text.Length > 0 ? tTSymbol.Text[0] : '\0';
            t.Look = ObjType.LookCodes[Math.Max(0, cTLook.SelectedIndex)];
            t.Solid = chSolid.Checked; t.Carry = chCarry.Checked; t.Keep = chKeep.Checked;
            t.Nouns = tTNouns.Text.Trim();
            t.Value = (int)nTValue.Value;
            t.Desc = tTDesc.Text.Replace("\r\n", "|");
            if (sender == tTId || sender == tTName)
            {
                int i = typeList.SelectedIndex;
                loading = true; typeList.Items[i] = t.Id + "  -  " + t.Name; loading = false;
            }
            if (sender != tTDesc && sender != tTNouns) { RebuildPalette(); canvas.Invalidate(); }
            MarkDirty();
        }

        void NewType(ObjType from)
        {
            if (pack.Types.Count >= Pack.MaxTypes) { Error("A pack can have at most " + Pack.MaxTypes + " kinds of object."); return; }
            ObjType t = from == null ? new ObjType { Id = "thing", Name = "A THING", Look = 19, Carry = true, Nouns = "THING" } : from.Clone();
            string id = t.Id;
            for (int k = 2; pack.Type(t.Id) != null; k++) t.Id = id + k;
            int at = typeList.SelectedIndex + 1;
            pack.Types.Insert(at, t);
            RefreshTypes(at);
            ShowType();
            RebuildPalette();
            MarkDirty();
        }

        void DeleteType()
        {
            ObjType t = SelType();
            if (t == null) return;
            int n = pack.Levels.Sum(l => l.Objs.Count(o => o.Type.Equals(t.Id, StringComparison.OrdinalIgnoreCase)));
            if (MessageBox.Show(this, "Delete the object type " + t.Id + "?" + (n > 0 ? "\n" + n + " placed object(s) of this type will be removed too." : ""), "ZorEdit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (Level l in pack.Levels) l.Objs.RemoveAll(o => o.Type.Equals(t.Id, StringComparison.OrdinalIgnoreCase));
            int i = typeList.SelectedIndex;
            pack.Types.Remove(t);
            RefreshTypes(i);
            ShowType();
            RebuildPalette();
            Select(selX, selY);
            canvas.Invalidate();
            MarkDirty();
            UpdateInfo();
        }

        void MoveType(int d)
        {
            int i = typeList.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= pack.Types.Count) return;
            ObjType t = pack.Types[i];
            pack.Types[i] = pack.Types[j];
            pack.Types[j] = t;
            RefreshTypes(j);
            ShowType();
            RebuildPalette();
            MarkDirty();
        }

        // ================================================================ levels
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
            if (MessageBox.Show(this, "Replace the walls of level " + (levelList.SelectedIndex + 1) + " with a random maze?\nThe start moves to the top left; objects left inside walls are removed.",
                "Generate maze", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Level g = MazeGen.Generate(rng, Cur);
            Level l = Cur;
            l.Wall = g.Wall; l.Objs = g.Objs; l.StartX = g.StartX; l.StartY = g.StartY; l.Facing = g.Facing;
            ShowLevel();
            MarkDirty();
        }

        void ClearMap()
        {
            if (MessageBox.Show(this, "Clear the walls of this level to open floor? (Objects stay.)", "Clear map", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++)
                    Cur.Wall[y, x] = Level.IsBorder(x, y);
            canvas.Invalidate();
            MarkDirty();
        }

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

        void CheckScript()
        {
            List<string> errs = Checker.Check(pack).Where(s => s.StartsWith("ERROR")).ToList();
            lScriptResult.ForeColor = errs.Count == 0 ? Color.DarkGreen : Color.DarkRed;
            lScriptResult.Text = errs.Count == 0 ? "All scripts compile." : string.Join("\n", errs.Take(6).Select(e => e.Substring(6)));
            UpdateInfo();
        }

        bool CheckPack(bool showOk) { return CheckPack(pack, showOk); }

        bool CheckPack(Pack p, bool showOk)
        {
            List<string> all = Checker.Check(p);
            bool errors = all.Any(s => s.StartsWith("ERROR"));
            if (all.Count == 0) { if (showOk) MessageBox.Show(this, "All " + p.Levels.Count + " level(s) compile and look complete.", "Check pack", MessageBoxButtons.OK, MessageBoxIcon.Information); return true; }
            if (showOk || errors)
                MessageBox.Show(this, string.Join("\n\n", all.Take(20)), errors ? "Check pack - fix these before exporting" : "Check pack", MessageBoxButtons.OK, errors ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
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

        /// Play only the level being edited, from its start or from the selected cell.
        void PlayLevel(bool fromCell)
        {
            Pack p = Zlp.Parse(Zlp.Lines(pack).ToArray());      // a copy
            int li = levelList.SelectedIndex;
            Level l = p.Levels[li];
            p.Levels.Clear();
            p.Levels.Add(l);
            if (fromCell)
            {
                if (selX < 0) { Error("Select a cell first (Select tool, click a cell), then play from it."); return; }
                if (l.Wall[selY, selX]) { Error("The selected cell " + selX + "," + selY + " is a wall."); return; }
                if (l.At(selX, selY).Any(o => { ObjType t = p.Type(o.Type); return t != null && t.Solid; }))
                { Error("Something solid stands on the selected cell " + selX + "," + selY + "."); return; }
                l.StartX = selX;
                l.StartY = selY;
            }
            Play(false, p);
        }

        void Play(bool fromDisk) { Play(fromDisk, pack); }

        void Play(bool fromDisk, Pack pack)
        {
            if (!CheckPack(pack, false)) return;
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
                    // Boot TRSDOS, copy the engine and level file onto a blank disk in
                    // drive 2 with IMPORT2 (utilities disk in drive 1), then run ZOR.
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
                 "Map: walls and floor only. Pick Wall, Floor or Start on the toolbar (keys 1-3) and paint. The outer ring is always wall. Click the start again to turn the player.\n\n" +
                 "Objects: everything else (keys, doors, monsters, characters, traps, exits...) is an object. The Objects tab lists the kinds of object in this pack: what each looks like, whether it blocks the way or can be carried, the words the player can use for it, and its behaviours. Each kind gets a toolbar button (keys 4-0): click a cell to place one. A new pack has no objects at all: press Library on the Objects tab to copy ready-made ones (keys, doors, monsters, signs, exits...), or New to make your own.\n\n" +
                 "Right-click a cell (Select or object tool) for its menu: place an object, add a behaviour to one object, to every object of its kind, or to the cell itself (something happens when the player steps there), move the start, or change wall / floor. Cells with behaviours have a green corner; objects with their own behaviours a magenta dot.\n\n" +
                 "Behaviours are built from When (a command, stepping onto it, walking into it, every turn, level start), If (conditions) and Do (actions). Double-click a behaviour to change it; tick 'As text' to edit the rules directly.\n\n" +
                 "Selected object (Level tab): every placed object has its own value (hit points, uses, gold...), two numbers P1,P2 (for example a teleporter's or plate's target: Pick target on map), a description, texts 2 and 3 for 'say this 2' / 'speak this 2', and behaviours of its own.\n\n" +
                 "A level is finished by a behaviour that says 'next' (the old packs use a ladder you climb with the amulet), and the game is won by 'win' or by 'next' on the last level.\n\n" +
                 "Test > Check Pack (F7) compiles everything and looks for problems. Test > Play in Emulator (F5) runs the pack in trs80gp. F6 plays only the level you are editing, and Shift+F6 (or right-click > Play this level from here) starts it at the selected cell.\n\n" +
                 "Edit > Undo (Ctrl+Z) and Redo (Ctrl+Y) step back and forward through your changes to the pack. Inside a text box, Ctrl+Z undoes typing in that box instead.\n\n" +
                 "Older packs are converted when opened: their fixed cells become ordinary object types you can change.");
        }

        void ShowScriptHelp()
        {
            Info("RULES  (one per line; # comment; a line starting with a space continues the last)\n\n" +
                 "on VERB [NOUN | any] [if CONDITION and ...] then ACTION; ACTION...\n" +
                 "on arrive | on bump | on start | every turn  [if ...] then ...\n" +
                 "synonym NEWWORD = WORD\n\n" +
                 "In an object type's behaviours 'this' is the placed object; 'value' its value, 'p1' its P1 and 'target' the cell P1,P2. " +
                 "For a typed command the rules of the objects here, then in front, then carried are tried; the first that acts ends the command (unless it says continue). " +
                 "For arrive, bump, start and every turn all rules that hold act, unless one says stop.\n\n" +
                 "CONDITIONS ('not' in front reverses; OBJ = this or an object name, TYPE = a kind of object):\n" +
                 "  carried OBJ | carried TYPE [N]   has TYPE [N]   here OBJ | TYPE   present OBJ   exists OBJ   near OBJ\n" +
                 "  ahead OBJ | TYPE | X,Y   at X,Y   is OBJ | TYPE   matches TYPE   flag NAME   chance PERCENT\n" +
                 "  counter NAME >= N  (> = < <=)   value [OBJ] >= N   hp >= N\n\n" +
                 "ACTIONS (N may be a number, value or p1 where shown as N*):\n" +
                 "  say \"TEXT\" | say OBJ N   speak \"TEXT\" | speak OBJ N   die \"TEXT\"\n" +
                 "  set FLAG   clear FLAG   counter NAME = N   add NAME N*   sub NAME N*   score N*\n" +
                 "  get OBJ   drop OBJ   remove OBJ   put OBJ at X,Y   swap OBJ OBJ   become [OBJ] TYPE\n" +
                 "  value [OBJ] = N | + N | - N   give TYPE [N]   take TYPE [N]   create TYPE at X,Y   useup TYPE\n" +
                 "  cell X,Y floor | wall | open   goto X,Y   hurt N* [random]   heal N* (0 = fully)\n" +
                 "  next   win   inventory   look   continue   stop\n\n" +
                 "X,Y may be 'target' in put, create, cell and goto. Flags (32), counters (16) and words are shared by all levels.");
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
        public event Action<int, int, bool, bool> Painted;     // x, y, right button, first cell of the drag
        public event Action<int, int> Hover;
        public Pack Pack;
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
        Inst selObj;
        public Inst SelectedObj { get { return selObj; } set { selObj = value; Invalidate(); } }
        public bool Hold;                   // ignore the rest of this mouse drag
        int lastX = -1, lastY = -1;

        public static Color LookColour(int look)
        {
            switch (look)
            {
                case 1: return Color.FromArgb(150, 155, 200);
                case 2: return Color.FromArgb(205, 127, 50);
                case 3: return Color.LightGreen;
                case 4: return Color.OrangeRed;
                case 5: return Color.Gold;
                case 6: return Color.LightSkyBlue;
                case 7: return Color.Violet;
                case 8: return Color.Khaki;
                case 9: return Color.Gold;
                case 10: return Color.Wheat;
                case 11: return Color.Red;
                case 12: return Color.Cyan;
                case 13: return Color.DeepSkyBlue;
                case 14: return Color.Silver;
                case 16: return Color.LightGreen;
                case 17: return Color.Goldenrod;
                case 18: return Color.WhiteSmoke;
                case 0: return Color.Gray;
            }
            return Color.Orange;
        }

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
            for (int y = 0; y < Level.H; y++)
                for (int x = 0; x < Level.W; x++)
                {
                    Rectangle r = new Rectangle(x * CS, y * CS, CS, CS);
                    if (level.Wall[y, x]) using (Brush b = new SolidBrush(Level.IsBorder(x, y) ? BorderC : WallC)) g.FillRectangle(b, r);
                    else g.FillRectangle(Brushes.DimGray, r.X + CS / 2 - 1, r.Y + CS / 2 - 1, 2, 2);
                }
            // start and facing
            {
                Point m = new Point(level.StartX * CS + CS / 2, level.StartY * CS + CS / 2);
                Point tip = new Point(m.X + Util.DX[level.Facing & 3] * 9, m.Y + Util.DY[level.Facing & 3] * 9);
                using (Brush b = new SolidBrush(Color.FromArgb(60, Color.Yellow))) g.FillRectangle(b, level.StartX * CS + 1, level.StartY * CS + 1, CS - 2, CS - 2);
                using (Pen p = new Pen(Color.Yellow, 3) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor }) g.DrawLine(p, m, tip);
            }
            // objects: the last one placed on a cell is drawn on top
            foreach (IGrouping<int, Inst> cell in level.Objs.Where(o => o.OnMap && o.X < Level.W && o.Y < Level.H).GroupBy(o => o.Y * Level.W + o.X))
            {
                Inst o = cell.Last();
                ObjType t = Pack == null ? null : Pack.Type(o.Type);
                Rectangle r = new Rectangle(o.X * CS, o.Y * CS, CS, CS);
                DrawObj(g, r, t);
                if (cell.Count() > 1) using (Font f = new Font("Segoe UI", 6f)) g.DrawString(cell.Count().ToString(), f, Brushes.White, r.X, r.Y - 1);
                if (cell.Any(i => !string.IsNullOrWhiteSpace(i.Script))) g.FillEllipse(Brushes.Magenta, r.Right - 6, r.Y + 1, 5, 5);
            }
            // cells that level behaviours refer to (at X,Y / ahead X,Y): a green corner
            foreach (Match mm in Regex.Matches(level.Script ?? "", @"\b(?:at|ahead)\s+(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase))
            {
                int cx = int.Parse(mm.Groups[1].Value), cy = int.Parse(mm.Groups[2].Value);
                if (cx >= Level.W || cy >= Level.H) continue;
                Point[] tri = { new Point(cx * CS + 1, cy * CS + CS - 1), new Point(cx * CS + 8, cy * CS + CS - 1), new Point(cx * CS + 1, cy * CS + CS - 8) };
                g.FillPolygon(Brushes.LimeGreen, tri);
            }
            // targets (P1,P2) of objects whose behaviours use "target"
            foreach (Inst o in level.Objs)
            {
                if (!o.OnMap || o.P1 <= 0 || o.P2 <= 0 || o.P1 >= Level.W || o.P2 >= Level.H) continue;
                ObjType t = Pack == null ? null : Pack.Type(o.Type);
                if (!UsesTarget(t == null ? "" : t.Script) && !UsesTarget(o.Script)) continue;
                bool sel = o == selObj;
                Color col = t == null ? Color.Silver : LookColour(t.Look);
                using (Pen p = new Pen(Color.FromArgb(sel ? 255 : 110, col), sel ? 2.5f : 1.5f) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor })
                {
                    p.DashStyle = sel ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash;
                    g.DrawLine(p, o.X * CS + CS / 2, o.Y * CS + CS / 2, o.P1 * CS + CS / 2, o.P2 * CS + CS / 2);
                }
                using (Pen p = new Pen(Color.FromArgb(sel ? 255 : 110, col), 1.5f)) g.DrawRectangle(p, o.P1 * CS + 2, o.P2 * CS + 2, CS - 4, CS - 4);
            }
            if (selected.X >= 0)
                using (Pen p = new Pen(Color.Cyan, 2)) g.DrawRectangle(p, selected.X * CS, selected.Y * CS, CS, CS);
            if (hx >= 0)
                using (Pen p = new Pen(Color.Yellow, 2)) g.DrawRectangle(p, hx * CS + 1, hy * CS + 1, CS - 2, CS - 2);
        }

        static bool UsesTarget(string s) { return Regex.IsMatch(s ?? "", @"\btarget\b", RegexOptions.IgnoreCase); }

        static void DrawObj(Graphics g, Rectangle r, ObjType t)
        {
            if (t != null && t.Look == 1 && t.Symbol == '\0')     // looks like a wall: hatched
            {
                using (Brush b = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.WideDownwardDiagonal, Color.FromArgb(120, 125, 165), WallC))
                    g.FillRectangle(b, r);
                return;
            }
            Color col = t == null ? Color.Red : LookColour(t.Look);
            string s = t == null ? "?" : t.MapSymbol.ToString();
            using (Brush b = new SolidBrush(Color.FromArgb(t != null && t.Solid ? 90 : 45, col))) g.FillEllipse(b, r.X + 2, r.Y + 2, CS - 4, CS - 4);
            using (Font f = new Font("Consolas", 11f, FontStyle.Bold))
            using (Brush b = new SolidBrush(col))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static Bitmap TypeSwatch(ObjType t)
        {
            Bitmap b = new Bitmap(CS, CS);
            using (Graphics g = Graphics.FromImage(b)) { g.Clear(Bg); DrawObj(g, new Rectangle(0, 0, CS, CS), t); }
            return b;
        }

        public static Bitmap TerrainSwatch(bool wall)
        {
            Bitmap b = new Bitmap(CS, CS);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Bg);
                if (wall) using (Brush br = new SolidBrush(WallC)) g.FillRectangle(br, 0, 0, CS, CS);
                else g.FillRectangle(Brushes.DimGray, CS / 2 - 1, CS / 2 - 1, 2, 2);
            }
            return b;
        }

        public static Bitmap StartSwatch()
        {
            Bitmap b = new Bitmap(CS, CS);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Bg);
                using (Pen p = new Pen(Color.Yellow, 3) { EndCap = System.Drawing.Drawing2D.LineCap.ArrowAnchor }) g.DrawLine(p, 3, CS / 2, CS - 3, CS / 2);
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

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); lastX = lastY = -1; Apply(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int x, y;
            if (HitCell(e, out x, out y)) { if (x != hx || y != hy) { hx = x; hy = y; Invalidate(); if (Hover != null) Hover(x, y); } }
            Apply(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hx = hy = -1; Invalidate(); if (Hover != null) Hover(-1, -1); }
        protected override void OnMouseUp(MouseEventArgs e) { Hold = false; lastX = lastY = -1; }

        void Apply(MouseEventArgs e)
        {
            int x, y;
            if (Hold || level == null || !HitCell(e, out x, out y) || Painted == null) return;
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if (x == lastX && y == lastY) return;
            bool first = lastX < 0;
            lastX = x; lastY = y;
            Painted(x, y, e.Button == MouseButtons.Right, first);
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
