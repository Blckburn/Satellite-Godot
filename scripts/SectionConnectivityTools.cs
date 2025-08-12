using Godot;
using System;
using System.Collections.Generic;

public static class SectionConnectivityTools
{
    public static void ConnectAllRoomComponentsToTrails(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, System.Func<Vector2I, bool> isCorridor, System.Func<Vector2I, Vector2I, System.Collections.Generic.List<Vector2I>> worldPath, System.Action<Vector2I> carveFloor)
    {
        var visited = new bool[mapWidth, mapHeight];
        var components = new System.Collections.Generic.List<System.Collections.Generic.List<Vector2I>>();
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (visited[x, y] || sectionMask[x, y] != LevelGenerator.TileType.Room) continue;
            var comp = new System.Collections.Generic.List<Vector2I>();
            var q = new System.Collections.Generic.Queue<Vector2I>();
            q.Enqueue(new Vector2I(x, y)); visited[x, y] = true;
            while (q.Count > 0)
            {
                var p = q.Dequeue(); comp.Add(p);
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= mapWidth || n.Y < 0 || n.Y >= mapHeight) continue;
                    if (visited[n.X, n.Y]) continue;
                    if (sectionMask[n.X, n.Y] != LevelGenerator.TileType.Room) continue;
                    visited[n.X, n.Y] = true; q.Enqueue(n);
                }
            }
            components.Add(comp);
        }
        if (components.Count <= 1) return;
        var corridors = new System.Collections.Generic.List<Vector2I>();
        for (int x = 0; x < mapWidth; x++) for (int y = 0; y < mapHeight; y++) if (sectionMask[x, y] == LevelGenerator.TileType.Corridor) corridors.Add(new Vector2I(x, y));
        if (corridors.Count == 0) return;
        foreach (var comp in components)
        {
            bool touches = false;
            foreach (var p in comp)
            {
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    int nx = p.X + d.X, ny = p.Y + d.Y;
                    if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight) continue;
                    if (sectionMask[nx, ny] == LevelGenerator.TileType.Corridor) { touches = true; break; }
                }
                if (touches) break;
            }
            if (touches) continue;
            Vector2I from = comp[comp.Count / 2];
            int best = int.MaxValue; Vector2I target = from;
            foreach (var c in corridors)
            {
                int dx = c.X - from.X, dy = c.Y - from.Y; int d2 = dx*dx + dy*dy;
                if (d2 < best) { best = d2; target = c; }
            }
            var path = worldPath(from, target);
            if (path == null) continue;
            foreach (var wp in path) carveFloor(wp);
        }
    }
    public static void PreserveLargestWalkableComponent(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight)
    {
        var visited = new bool[mapWidth, mapHeight];
        int best = 0; List<Vector2I> bestCells = null;
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (visited[x, y] || sectionMask[x, y] != LevelGenerator.TileType.Room) continue;
            var comp = new List<Vector2I>();
            var q = new Queue<Vector2I>();
            q.Enqueue(new Vector2I(x, y)); visited[x, y] = true;
            while (q.Count > 0)
            {
                var p = q.Dequeue(); comp.Add(p);
                foreach (var d in new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) })
                {
                    var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                    if (n.X < 0 || n.X >= mapWidth || n.Y < 0 || n.Y >= mapHeight) continue;
                    if (visited[n.X, n.Y]) continue;
                    if (sectionMask[n.X, n.Y] != LevelGenerator.TileType.Room) continue;
                    visited[n.X, n.Y] = true; q.Enqueue(n);
                }
            }
            if (comp.Count > best) { best = comp.Count; bestCells = comp; }
        }
        if (bestCells == null) return;
        var keep = new HashSet<Vector2I>(bestCells);
        for (int x = 0; x < mapWidth; x++)
        for (int y = 0; y < mapHeight; y++)
        {
            if (sectionMask[x, y] == LevelGenerator.TileType.Room && !keep.Contains(new Vector2I(x, y)))
                sectionMask[x, y] = LevelGenerator.TileType.Background;
        }
    }

    public static List<Vector2I> PickTrailNodes(Random rng, LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, int count, int minSpacing)
    {
        var nodes = new List<Vector2I>();
        int attempts = 0; int maxAttempts = count * 50;
        while (nodes.Count < count && attempts++ < maxAttempts)
        {
            int x = rng.Next(2, mapWidth - 2);
            int y = rng.Next(2, mapHeight - 2);
            if (sectionMask[x, y] != LevelGenerator.TileType.Room) continue;
            bool far = true;
            foreach (var n in nodes)
                if ((n - new Vector2I(x, y)).LengthSquared() < minSpacing * minSpacing) { far = false; break; }
            if (far) nodes.Add(new Vector2I(x, y));
        }
        return nodes;
    }

    // A* по проходимым (Room) клеткам внутри секции
    public static List<Vector2I> FindPathOverRooms(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, Vector2I start, Vector2I goal)
    {
        var open = new SortedSet<(int,int,Vector2I)>(Comparer<(int,int,Vector2I)>.Create((a,b)=> a.Item1!=b.Item1? a.Item1-b.Item1 : a.Item2!=b.Item2? a.Item2-b.Item2 : a.Item3.X!=b.Item3.X? a.Item3.X-b.Item3.X : a.Item3.Y-b.Item3.Y));
        var came = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, int>();
        int H(Vector2I p) => Math.Abs(p.X - goal.X) + Math.Abs(p.Y - goal.Y);
        open.Add((H(start), 0, start)); gScore[start] = 0;
        var dirs = new[]{ new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        while (open.Count > 0)
        {
            var cur = open.Min; open.Remove(cur);
            var p = cur.Item3;
            if (p == goal)
            {
                var path = new List<Vector2I>();
                while (came.ContainsKey(p)) { path.Add(p); p = came[p]; }
                path.Reverse(); return path;
            }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.X >= mapWidth || n.Y < 0 || n.Y >= mapHeight) continue;
                if (sectionMask[n.X, n.Y] != LevelGenerator.TileType.Room) continue;
                int ng = cur.Item2 + 1;
                if (!gScore.TryGetValue(n, out var old) || ng < old)
                {
                    gScore[n] = ng; came[n] = p; open.Add((ng + H(n), ng, n));
                }
            }
        }
        return null;
    }

    public static System.Collections.Generic.List<Vector2I> FindPathToNearestCorridor(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, System.Collections.Generic.IEnumerable<Vector2I> starts)
    {
        var queue = new System.Collections.Generic.Queue<Vector2I>();
        var came = new System.Collections.Generic.Dictionary<Vector2I, Vector2I>();
        var visited = new System.Collections.Generic.HashSet<Vector2I>();
        foreach (var s in starts)
        {
            if (s.X < 0 || s.X >= mapWidth || s.Y < 0 || s.Y >= mapHeight) continue;
            if (sectionMask[s.X, s.Y] == LevelGenerator.TileType.Room) continue;
            queue.Enqueue(s);
            visited.Add(s);
        }
        Vector2I? goal = null;
        var dirs = new Vector2I[] { new Vector2I(1,0), new Vector2I(-1,0), new Vector2I(0,1), new Vector2I(0,-1) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (sectionMask[p.X, p.Y] == LevelGenerator.TileType.Corridor) { goal = p; break; }
            foreach (var d in dirs)
            {
                var n = new Vector2I(p.X + d.X, p.Y + d.Y);
                if (n.X < 0 || n.X >= mapWidth || n.Y < 0 || n.Y >= mapHeight) continue;
                if (visited.Contains(n)) continue;
                var t = sectionMask[n.X, n.Y];
                if (t == LevelGenerator.TileType.Room) continue;
                visited.Add(n); came[n] = p; queue.Enqueue(n);
            }
        }
        if (goal == null) return null;
        var path = new System.Collections.Generic.List<Vector2I>();
        var cur = goal.Value;
        while (came.ContainsKey(cur)) { path.Add(cur); cur = came[cur]; }
        path.Reverse();
        return path;
    }

    public static void CarveTrailsBetweenNodes(LevelGenerator.TileType[,] sectionMask, int mapWidth, int mapHeight, System.Collections.Generic.List<Vector2I> nodes, int width, System.Action<Vector2I> carveLocal)
    {
        if (nodes == null || nodes.Count < 2) return;
        var edges = new System.Collections.Generic.List<(int a, int b, int w)>();
        for (int i = 0; i < nodes.Count; i++)
        for (int j = i + 1; j < nodes.Count; j++)
        {
            int dx = nodes[i].X - nodes[j].X; int dy = nodes[i].Y - nodes[j].Y;
            edges.Add((i, j, dx*dx + dy*dy));
        }
        edges.Sort((e1,e2)=>e1.w.CompareTo(e2.w));
        var parent = new int[nodes.Count]; for (int i=0;i<parent.Length;i++) parent[i]=i;
        int Find(int x){ while (parent[x]!=x) x=parent[x]=parent[parent[x]]; return x; }
        bool Union(int x,int y){ x=Find(x); y=Find(y); if (x==y) return false; parent[y]=x; return true; }
        var chosen = new System.Collections.Generic.List<(int a,int b)>();
        foreach (var e in edges) if (Union(e.a,e.b)) chosen.Add((e.a,e.b));
        // не добавляем extras здесь, чтобы не перегружать
        foreach (var c in chosen)
        {
            var path = FindPathOverRooms(sectionMask, mapWidth, mapHeight, nodes[c.a], nodes[c.b]);
            if (path == null) continue;
            foreach (var p in path)
            {
                for (int w = -(width/2); w <= (width/2); w++)
                {
                    foreach (var dir in new[]{new Vector2I(1,0), new Vector2I(0,1)})
                    {
                        int cx = p.X + dir.X * w;
                        int cy = p.Y + dir.Y * w;
                        if (cx < 0 || cx >= mapWidth || cy < 0 || cy >= mapHeight) continue;
                        carveLocal(new Vector2I(cx, cy));
                        sectionMask[cx, cy] = LevelGenerator.TileType.Corridor;
                    }
                }
            }
        }
    }
}


