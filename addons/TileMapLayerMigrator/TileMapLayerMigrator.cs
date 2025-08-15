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

        Node searchRoot = root;
        var iso = root.GetNodeOrNull<Node2D>("isometric_tileset");
        if (iso != null)
            searchRoot = iso;

        // Try convert known names first; if nothing converted, fallback to converting any TileMap under the root
        bool converted = false;
        converted |= ConvertTileMapToLayers(searchRoot, "Floors");
        converted |= ConvertTileMapToLayers(searchRoot, "Walls");

        if (!converted)
        {
            GD.Print("TileMapLayerMigrator: Named TileMaps not found, converting all TileMap nodes under the scene root...");
            ConvertAllTileMaps(searchRoot);
        }

        GD.Print("TileMapLayerMigrator: Done. Please save the scene.");
    }

    private bool ConvertTileMapToLayers(Node parent, string tileMapName)
    {
        var tm = parent.GetNodeOrNull<TileMap>(tileMapName);
        if (tm == null)
        {
            return false;
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
        return true;
    }

    private void ConvertAllTileMaps(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is TileMap tm)
            {
                // Preserve the original node name
                ConvertTileMapToLayers(parent, tm.Name);
            }
            else
            {
                ConvertAllTileMaps(child);
            }
        }
    }
}
#endif


