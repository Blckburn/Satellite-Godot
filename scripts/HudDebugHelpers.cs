using Godot;
using System;

public static class HudDebugHelpers
{
    public static void CreateCoordinateLabel(Node owner, Node2D ySortContainer, Func<Vector2I, Vector2> mapTileToIsometricWorld, bool showLabels, Vector2I tilePos, string text)
    {
        if (!showLabels) return;
        var label = new Label
        {
            Text = text,
            Name = $"CoordLabel_{tilePos.X}_{tilePos.Y}"
        };
        Vector2 worldPos = mapTileToIsometricWorld(tilePos);
        label.Position = new Vector2(worldPos.X - 25, worldPos.Y - 50);
        label.Modulate = Colors.Yellow;
        label.Scale = new Vector2(0.8f, 0.8f);
        var styleBox = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.7f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
        label.AddThemeStyleboxOverride("normal", styleBox);
        if (ySortContainer != null) ySortContainer.AddChild(label);
        else owner.GetTree().CurrentScene.AddChild(label);
    }

    public static void ClearCoordinateLabels(Node owner, Node2D ySortContainer)
    {
        if (ySortContainer != null)
        {
            var children = ySortContainer.GetChildren();
            foreach (Node child in children)
            {
                if (child.Name.ToString().StartsWith("CoordLabel_")) child.QueueFree();
            }
        }

        var sceneChildren = owner.GetTree().CurrentScene.GetChildren();
        foreach (Node child in sceneChildren)
        {
            if (child.Name.ToString().StartsWith("CoordLabel_")) child.QueueFree();
        }
    }
}


