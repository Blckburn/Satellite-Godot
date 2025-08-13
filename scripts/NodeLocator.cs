using Godot;
using System;

public sealed class NodeLocator
{
    public Node2D IsometricTileset { get; private set; }
    public Godot.TileMapLayer FloorsTileMap { get; private set; }
    public Godot.TileMapLayer WallsTileMap { get; private set; }
    public Godot.TileMapLayer WallsOverlayTileMap { get; private set; }
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
            // Overlay слой стен (создаём при необходимости)
            WallsOverlayTileMap = IsometricTileset.GetNodeOrNull<Godot.TileMapLayer>("WallsOverlay")
                                   ?? FindNodeRecursive<Godot.TileMapLayer>(IsometricTileset, "WallsOverlay");
            if (WallsTileMap != null)
            {
                if (WallsOverlayTileMap == null)
                {
                    var overlay = new Godot.TileMapLayer
                    {
                        Name = "WallsOverlay",
                        TileSet = WallsTileMap.TileSet,
                        Transform = WallsTileMap.Transform,
                    };
                    IsometricTileset.AddChild(overlay);
                    overlay.Owner = IsometricTileset.Owner;
                    overlay.YSortEnabled = true;
                    overlay.ZIndex = WallsTileMap.ZIndex + 1; // над базовыми стенами и полом
                    // Перемещаем сразу после Walls, чтобы YSortContainer рисовался поверх
                    int wallsIdx = -1; int idx = 0;
                    foreach (var ch in IsometricTileset.GetChildren()) { if (ch == WallsTileMap) { wallsIdx = idx; break; } idx++; }
                    if (wallsIdx >= 0) IsometricTileset.MoveChild(overlay, wallsIdx + 1);
                    WallsOverlayTileMap = overlay;
                }
                else
                {
                    WallsOverlayTileMap.ZIndex = WallsTileMap.ZIndex + 1; // над базовыми стенами
                    int wallsIdx = -1; int overlayIdx = -1; int i = 0;
                    foreach (var ch in IsometricTileset.GetChildren())
                    {
                        if (ch == WallsTileMap) wallsIdx = i;
                        if (ch == WallsOverlayTileMap) overlayIdx = i;
                        i++;
                    }
                    if (wallsIdx >= 0 && overlayIdx > wallsIdx + 1)
                        IsometricTileset.MoveChild(WallsOverlayTileMap, wallsIdx + 1);
                }
            }
        }
        else
        {
            FloorsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Floors");
            WallsTileMap ??= FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "Walls");
            WallsOverlayTileMap = FindNodeRecursive<Godot.TileMapLayer>(context.GetTree().Root, "WallsOverlay");
            if (WallsTileMap != null)
            {
                var parent = WallsTileMap.GetParent();
                if (WallsOverlayTileMap == null)
                {
                    // Пытаемся создать рядом с найденным Walls
                    var overlay = new Godot.TileMapLayer
                    {
                        Name = "WallsOverlay",
                        TileSet = WallsTileMap.TileSet,
                        Transform = WallsTileMap.Transform,
                    };
                    parent.AddChild(overlay);
                    overlay.Owner = parent.Owner;
                    overlay.YSortEnabled = true;
                    overlay.ZIndex = WallsTileMap.ZIndex + 1;
                    // Вставляем сразу после Walls
                    int wallsIdx = -1; int idx = 0;
                    foreach (var ch in parent.GetChildren()) { if (ch == WallsTileMap) { wallsIdx = idx; break; } idx++; }
                    if (wallsIdx >= 0) parent.MoveChild(overlay, wallsIdx + 1);
                    WallsOverlayTileMap = overlay;
                }
                else
                {
                    WallsOverlayTileMap.ZIndex = WallsTileMap.ZIndex + 1;
                    int wallsIdx = -1; int overlayIdx = -1; int i2 = 0;
                    foreach (var ch in parent.GetChildren())
                    {
                        if (ch == WallsTileMap) wallsIdx = i2;
                        if (ch == WallsOverlayTileMap) overlayIdx = i2;
                        i2++;
                    }
                    if (wallsIdx >= 0 && overlayIdx > wallsIdx + 1)
                        parent.MoveChild(WallsOverlayTileMap, wallsIdx + 1);
                }
            }
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

        // Убеждаемся, что есть Overlay слой, если возможно
        if (WallsOverlayTileMap == null && WallsTileMap != null)
        {
            var overlay = new Godot.TileMapLayer
            {
                Name = "WallsOverlay",
                TileSet = WallsTileMap.TileSet,
                Transform = WallsTileMap.Transform,
            };
            var parent = IsometricTileset ?? WallsTileMap.GetParent() as Node2D;
            if (parent != null)
            {
                parent.AddChild(overlay);
                overlay.Owner = parent.Owner;
                overlay.YSortEnabled = true;
                overlay.ZIndex = Math.Max(1, WallsTileMap.ZIndex + 1);
                WallsOverlayTileMap = overlay;
            }
        }

        // Дополнительная сортировка, связанная со слоем «WallsTop», удалена
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


