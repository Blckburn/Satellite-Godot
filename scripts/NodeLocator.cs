using Godot;
using System;

public sealed class NodeLocator
{
    public Node2D IsometricTileset { get; private set; }
    public Godot.TileMapLayer FloorsTileMap { get; private set; }
    public Godot.TileMapLayer WallsTileMap { get; private set; }
    public Node2D YSortContainer { get; private set; }

    public void FindRequiredNodes(Node context,
        Node2D explicitIsometric,
        Godot.TileMapLayer explicitFloors,
        Godot.TileMapLayer explicitWalls,
        Node2D explicitYSort)
    {
        IsometricTileset = explicitIsometric;
        FloorsTileMap = explicitFloors;
        WallsTileMap = explicitWalls;
        YSortContainer = explicitYSort;

        if (IsometricTileset == null)
        {
            IsometricTileset = context.GetNodeOrNull<Node2D>("../isometric_tileset")
                ?? FindNodeRecursive<Node2D>(context.GetTree().Root, "isometric_tileset");
        }

        if (IsometricTileset != null)
        {
            FloorsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMapLayer>("Floors");
            WallsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMapLayer>("Walls");
            YSortContainer ??= IsometricTileset.GetNodeOrNull<Node2D>("YSortContainer");
        }
        else
        {
            FloorsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Floors");
            WallsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Walls");
            YSortContainer ??= FindNodeRecursive<Node2D>(context.GetTree().Root, "YSortContainer");
        }
    }

    public void EnsureSortingWorks()
    {
        if (YSortContainer is Node2D ysortNode2D)
        {
            ysortNode2D.YSortEnabled = true;
        }

        if (IsometricTileset is Node2D isometricNode)
        {
            isometricNode.YSortEnabled = true;
        }
    }

    private T FindNodeRecursive<T>(Node node, string nodeName = null) where T : class
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T result && (string.IsNullOrEmpty(nodeName) || child.Name == nodeName))
            {
                return result;
            }

            var found = FindNodeRecursive<T>(child, nodeName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}


