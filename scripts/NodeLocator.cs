using Godot;
using System;

public sealed class NodeLocator
{
    public Node2D IsometricTileset { get; private set; }
    public Godot.TileMap FloorsTileMap { get; private set; }
    public Godot.TileMap WallsTileMap { get; private set; }
    public Node2D YSortContainer { get; private set; }

    public void FindRequiredNodes(Node context,
        Node2D explicitIsometric,
        Godot.TileMap explicitFloors,
        Godot.TileMap explicitWalls,
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
            FloorsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMap>("Floors");
            WallsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMap>("Walls");
            YSortContainer ??= IsometricTileset.GetNodeOrNull<Node2D>("YSortContainer");
        }
        else
        {
            FloorsTileMap ??= FindNodeRecursive<Godot.TileMap>(context.GetTree().Root, "Floors");
            WallsTileMap ??= FindNodeRecursive<Godot.TileMap>(context.GetTree().Root, "Walls");
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


