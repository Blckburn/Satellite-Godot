using Godot;
using System;

#if TOOLS
[Tool]
public partial class TileMapLayerMigrator : EditorPlugin
{
    private Button _button;

    public override void _EnterTree()
    {
        _button = new Button { Text = "Extract TileMap layers" };
        _button.Pressed += OnPressed;
        AddControlToContainer(CustomControlContainer.Toolbar, _button);
    }

    public override void _ExitTree()
    {
        if (_button != null)
        {
            RemoveControlFromContainer(CustomControlContainer.Toolbar, _button);
            _button.QueueFree();
            _button = null;
        }
    }

    private void OnPressed()
    {
        var root = GetEditorInterface().GetEditedSceneRoot();
        if (root == null)
        {
            GD.PrintErr("TileMapLayerMigrator: No edited scene root.");
            return;
        }

        var iso = root.GetNodeOrNull<Node2D>("isometric_tileset");
        if (iso == null)
        {
            GD.PrintErr("TileMapLayerMigrator: Node '../isometric_tileset' not found.");
            return;
        }

        ConvertTileMapToLayers(iso, "Floors");
        ConvertTileMapToLayers(iso, "Walls");

        GD.Print("TileMapLayerMigrator: Done. Please save the scene.");
    }

    private void ConvertTileMapToLayers(Node parent, string tileMapName)
    {
        var tm = parent.GetNodeOrNull<TileMap>(tileMapName);
        if (tm == null)
        {
            GD.Print($"TileMapLayerMigrator: TileMap '{tileMapName}' not found, skipping.");
            return;
        }

        // Create a new TileMapLayer and copy basic settings
        var layer = new TileMapLayer();
        layer.Name = tileMapName;
        layer.TileSet = tm.TileSet;
        layer.Transform = tm.Transform;
        parent.AddChild(layer);
        layer.Owner = parent.Owner; // ensure saved to scene

        // Copy cells from TileMap default layer (0) into TileMapLayer
        Rect2I used = tm.GetUsedRect();
        for (int x = used.Position.X; x < used.Position.X + used.Size.X; x++)
        for (int y = used.Position.Y; y < used.Position.Y + used.Size.Y; y++)
        {
            Vector2I coords = new Vector2I(x, y);
            var td = tm.GetCellTileData(0, coords);
            if (td == null) continue;
            int sourceId = tm.GetCellSourceId(0, coords);
            Vector2I atlas = tm.GetCellAtlasCoords(0, coords);
            if (sourceId >= 0)
            {
                layer.SetCell(coords, sourceId, atlas);
            }
        }

        // Remove original TileMap
        tm.QueueFree();
    }
}
#endif


