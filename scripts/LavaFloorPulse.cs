using Godot;

public static class LavaFloorPulse
{
	/// <summary>
	/// Creates/updates a dedicated overlay layer for floor tile pulsing in biome 6 (lava) without using wall layers.
	/// Mirrors only floor tile (atlasId: floorsSourceId, coords: (9,8)) across the provided world extents,
	/// then tween-modulates that overlay to produce a subtle lightening/dimming pulse.
	/// </summary>
    public static void Apply(Node context, TileMapLayer floors, TileMapLayer walls, int floorsSourceId, int worldTilesX, int worldTilesY)
	{
        if (context == null || floors == null) return;
        var parent = floors.GetParent();
        if (parent == null) return;

		// Ensure or create floor pulse overlay
		var overlay = parent.GetNodeOrNull<TileMapLayer>("FloorsPulseOverlay");
		if (overlay == null)
		{
			overlay = new TileMapLayer
			{
				Name = "FloorsPulseOverlay",
				TileSet = floors.TileSet,
				Transform = floors.Transform
			};
			parent.AddChild(overlay);
			overlay.Owner = parent.Owner;
		}

		// Match Floors sorting exactly (do not render above Floors)
		overlay.YSortEnabled = floors.YSortEnabled;
		overlay.ZIndex = floors.ZIndex;

		// Place overlay between Floors and Walls in the child order, if possible
		int floorsIdx = -1, wallsIdx = -1, overlayIdx = -1, idx = 0;
		foreach (var ch in parent.GetChildren())
		{
			if (ch == floors) floorsIdx = idx;
			if (ch == walls) wallsIdx = idx;
			if (ch == overlay) overlayIdx = idx;
			idx++;
		}
		if (floorsIdx >= 0 && overlayIdx != floorsIdx)
			parent.MoveChild(overlay, floorsIdx); // draw before Floors at same ZIndex

		// Clear existing overlay content and re-place targeted floor tiles
		overlay.Clear();
		Vector2I target = new Vector2I(9, 8);
		for (int x = 0; x < worldTilesX; x++)
		for (int y = 0; y < worldTilesY; y++)
		{
			var cell = new Vector2I(x, y);
			int src = floors.GetCellSourceId(cell);
			if (src != floorsSourceId) continue;
			Vector2I coords = floors.GetCellAtlasCoords(cell);
			if (coords == target)
			{
				overlay.SetCell(cell, floorsSourceId, target);
			}
		}

		// Gentle pulsing tween on overlay modulate
		if (overlay.Modulate == default(Color)) overlay.Modulate = new Color(1, 1, 1, 1);
		var tween = overlay.CreateTween();
		tween.SetLoops();
		tween.TweenProperty(overlay, "modulate", new Color(1.16f, 1.16f, 1.16f, 1.0f), 1.2f)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(overlay, "modulate", new Color(0.90f, 0.90f, 0.90f, 1.0f), 1.2f)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}
}


