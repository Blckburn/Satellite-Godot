using Godot;
using System;
using System.Collections.Generic;

public static class SpawnPlacement
{
    public sealed class Context
    {
        public Node Owner { get; init; }
        public Node2D YSortContainer { get; init; }
        public PackedScene PlayerScene { get; init; }
        public Func<Vector2I, Vector2> MapTileToIsometricWorld { get; init; }
        public Action<string> LogInfo { get; init; } = (m) => Logger.Info(m);
        public Action<string> LogError { get; init; } = (m) => Logger.Error(m);
    }

    public static void CreateSpawnPointNodes(Context ctx, List<(string name, Vector2 position, bool isValid)> spawnPoints)
    {
        foreach (var spawn in spawnPoints)
        {
            Node2D spawnNode = new Node2D
            {
                Name = $"SpawnPoint_{spawn.name}",
                Position = spawn.position
            };

            spawnNode.AddToGroup("SpawnPoints");
            if (spawn.isValid) spawnNode.AddToGroup("ValidSpawnPoints");

            if (ctx.YSortContainer != null) ctx.YSortContainer.AddChild(spawnNode);
            else ctx.Owner.AddChild(spawnNode);
        }
    }

    public static void CreatePlayerAtPosition(Context ctx, Vector2 position)
    {
        var existingPlayers = ctx.Owner.GetTree().GetNodesInGroup("Player");
        foreach (Node player in existingPlayers) player.QueueFree();

        if (ctx.PlayerScene == null)
        {
            ctx.LogError("PlayerScene is null! Cannot create player!");
            return;
        }

        try
        {
            Node2D player = ctx.PlayerScene.Instantiate<Node2D>();
            if (player == null)
            {
                ctx.LogError("Failed to instantiate player!");
                return;
            }

            player.Position = position;
            player.AddToGroup("Player");

            if (ctx.YSortContainer != null) ctx.YSortContainer.AddChild(player);
            else ctx.Owner.AddChild(player);
        }
        catch (Exception ex)
        {
            ctx.LogError($"Failed to create player: {ex.Message}");
        }
    }

    public static void HandlePlayerSpawn(Context ctx, Vector2 spawnPosition, bool teleportExisting, string playerGroup)
    {
        if (ctx.Owner == null) return;
        var tree = ctx.Owner.GetTree();
        if (tree == null) return;

        Node2D existing = null;
        var players = tree.GetNodesInGroup(playerGroup);
        if (players.Count > 0 && players[0] is Node2D p) existing = p;

        if (existing != null && teleportExisting)
        {
            existing.Position = spawnPosition;
        }
        else if (ctx.PlayerScene != null)
        {
            var player = ctx.PlayerScene.Instantiate<Node2D>();
            player.Position = spawnPosition;
            if (!player.IsInGroup(playerGroup)) player.AddToGroup(playerGroup);
            if (ctx.YSortContainer != null) ctx.YSortContainer.AddChild(player); else ctx.Owner.AddChild(player);
        }
    }

    public static Node2D FindPlayer(Node owner, string playerGroup)
    {
        var players = owner.GetTree().GetNodesInGroup(playerGroup);
        if (players.Count > 0 && players[0] is Node2D player) return player;
        return null;
    }
}


