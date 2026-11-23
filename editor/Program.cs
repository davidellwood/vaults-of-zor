// ZorEdit - Program.cs: entry point and command-line mode.
//
//   ZorEdit.exe [pack.zlp]                      open the editor
//   ZorEdit.exe /lev  pack.zlp out.lev          compile a level file
//   ZorEdit.exe /cmd  pack.zlp out.cmd          game with the pack built in
//   ZorEdit.exe /engine out.cmd                 game engine only (loads ZOR/LEV)
//   ZorEdit.exe /check pack.zlp                 check every level
//   ZorEdit.exe /preview pack.zlp title.png     save the title screen as a picture
// Exit code 0 = success. Messages go to the console when run from one.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZorEdit
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("/"))
            {
                AttachConsole(-1);
                try { return Cli(args); }
                catch (Exception ex) { Console.Error.WriteLine("ZorEdit: " + ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            return 0;
        }

        static int Cli(string[] a)
        {
            string cmd = a[0].ToLowerInvariant();
            if (cmd == "/engine" && a.Length == 2)
            {
                File.WriteAllBytes(a[1], Engine.Load().Write());
                Console.WriteLine("Wrote " + a[1] + " (engine only)");
                return 0;
            }
            if (a.Length < 2) return Usage();
            Pack p = Zlp.Load(a[1]);
            List<string> problems = new List<string>();
            for (int i = 0; i < p.Levels.Count; i++) problems.AddRange(Checker.Check(p.Levels[i], i));
            problems.AddRange(Checker.CheckScripts(p));
            foreach (string s in problems) Console.WriteLine(s);
            if (problems.Any(s => s.StartsWith("ERROR"))) return 2;
            switch (cmd)
            {
                case "/check":
                    Console.WriteLine(p.Levels.Count + " level(s) checked.");
                    return 0;
                case "/preview":
                    if (a.Length != 3) return Usage();
                    using (System.Drawing.Bitmap bmp = Trs80Screen.Render(Compiler.RenderTitle(p), 64, 16))
                        bmp.Save(a[2], System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("Wrote title screen preview " + a[2]);
                    return 0;
                case "/lev":
                    if (a.Length != 3) return Usage();
                    byte[] lev = Compiler.Build(p);
                    File.WriteAllBytes(a[2], lev);
                    Console.WriteLine("Wrote " + a[2] + " (" + lev.Length + " bytes, " + p.Levels.Count + " levels)");
                    return 0;
                case "/cmd":
                    if (a.Length != 3) return Usage();
                    CmdImage im = Engine.Load();
                    int top = im.SetPack(Compiler.Build(p));
                    File.WriteAllBytes(a[2], im.Write());
                    Console.WriteLine("Wrote " + a[2] + " (" + p.Levels.Count + " levels built in, memory 5200H-" + top.ToString("X4") + "H)");
                    return 0;
            }
            return Usage();
        }

        static int Usage()
        {
            Console.Error.WriteLine("usage: ZorEdit [pack.zlp] | /lev pack.zlp out.lev | /cmd pack.zlp out.cmd | /engine out.cmd | /check pack.zlp | /preview pack.zlp out.png");
            return 1;
        }
    }
}
