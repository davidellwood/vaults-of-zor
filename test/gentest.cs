using System; using System.IO; using ZorEdit;
class T { static int Main() {
  Random r = new Random(7); Pack p = new Pack(); int ok = 0;
  for (int i = 0; i < 30; i++) {
    Level l = MazeGen.Generate(r, new Level { Name = "GEN " + i });
    string why; bool s = Checker.Solve(l, out why);
    bool parts = l.Count(Cell.Ladder) == 1 && l.Count(Cell.Amulet) == 1 && l.Count(Cell.Door) == 1 && l.Count(Cell.Guard) == 1 && l.Count(Cell.Key) == 1 && l.Count(Cell.Sword) == 1;
    if (s && parts) ok++; else Console.WriteLine("level " + i + " solvable=" + s + " parts=" + parts);
    if (i < 9) p.Levels.Add(l);
  }
  byte[] b = Compiler.Build(p); Zlp.Save(p, "test/gen9.zlp"); File.WriteAllBytes("test/gen9.lev", b);
  Pack q = Zlp.Load("test/gen9.zlp"); byte[] b2 = Compiler.Build(q);
  bool same = b.Length == b2.Length; for (int i = 0; same && i < b.Length; i++) same = b[i] == b2[i];
  Console.WriteLine(ok + "/30 generated levels complete and solvable; 9-level pack " + b.Length + " bytes; save/load round trip identical: " + same);
  return 0; } }
