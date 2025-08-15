using Godot;
using System;
using System.Collections.Generic;

public static class WorldPathfinder
{
    // Поиск пути в мировых тайлах для органичных мостов/коридоров (разрешаем резать фон/стены)
    public static List<Vector2I> FindWorldPathOrganic(Vector2I startWp, Vector2I goalWp)
    {
        var open = new SortedSet<(int,int,Vector2I)>(Comparer<(int,int,Vector2I)>.Create((a,b)=> a.Item1!=b.Item1? a.Item1-b.Item1 : a.Item2!=b.Item2? a.Item2-b.Item2 : a.Item3.X!=b.Item3.X? a.Item3.X-b.Item3.X : a.Item3.Y-b.Item3.Y));
        var came = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, int>();
        int H(Vector2I p) => Math.Abs(p.X - goalWp.X) + Math.Abs(p.Y - goalWp.Y);
        open.Add((H(startWp), 0, startWp)); gScore[startWp] = 0;
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            var p = cur.Item3;
            if (p == goalWp)
            {
                var path = new List<Vector2I>();
                while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                path.Reverse(); return path;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.Y < 0) continue; // ограничимся неотрицательными
                int ng = cur.Item2 + 1;
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                {
                    gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n));
                }
            }
        }
        return null;
    }
}


