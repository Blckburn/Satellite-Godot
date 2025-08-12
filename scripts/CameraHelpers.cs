using Godot;

public static class CameraHelpers
{
    public static void CenterOnPlayer(Node owner, Node2D player)
    {
        if (owner == null || player == null) return;
        var cameraControllers = owner.GetTree().GetNodesInGroup("Camera");
        foreach (var cam in cameraControllers)
        {
            if (cam is CameraController controller)
            {
                controller.CenterOnPlayer();
                return;
            }
        }
        var camera = owner.GetViewport().GetCamera2D();
        if (camera != null) camera.Position = player.Position;
    }
}


