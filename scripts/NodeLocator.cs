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
            FloorsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMapLayer>("Floors")
                               ?? FindNodeRecursive<Godot.TileMapLayer>(IsometricTileset, "Floors");
            YSortContainer ??= IsometricTileset.GetNodeOrNull<Node2D>("YSortContainer")
                               ?? FindNodeRecursive<Node2D>(IsometricTileset, "YSortContainer");
            WallsTileMap ??= IsometricTileset.GetNodeOrNull<Godot.TileMapLayer>("Walls")
                               ?? FindNodeRecursive<Godot.TileMapLayer>(IsometricTileset, "Walls");
        }
        else
        {
            FloorsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Floors");
            WallsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Walls");
            YSortContainer ??= FindNodeRecursive<Node2D>(context.GetTree().Root, "YSortContainer");
        }

        // Fallback: if TileMapLayer nodes not found but old TileMap nodes exist, create layers at runtime
        if (FloorsTileMap == null)
        {
            var floorsLegacy = IsometricTileset?.GetNodeOrNull<TileMap>("Floors")
                               ?? context.GetTree().Root.GetNodeOrNull<TileMap>("Floors");
            FloorsTileMap = TryCreateLayerFromLegacy(floorsLegacy);
        }

        if (WallsTileMap == null)
        {
            var wallsLegacy = IsometricTileset?.GetNodeOrNull<TileMap>("Walls")
                              ?? context.GetTree().Root.GetNodeOrNull<TileMap>("Walls");
            WallsTileMap = TryCreateLayerFromLegacy(wallsLegacy);
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

    private Godot.TileMapLayer TryCreateLayerFromLegacy(TileMap legacy)
    {
        if (legacy == null)
        {
            return null;
        }

        var parent = legacy.GetParent();
        var layer = new Godot.TileMapLayer
        {
            Name = legacy.Name,
            TileSet = legacy.TileSet,
            Transform = legacy.Transform
        };
        parent.AddChild(layer);

        // Do not delete legacy node at runtime; just hide to avoid visual overlap
        legacy.Visible = false;
        return layer;
    }
}


