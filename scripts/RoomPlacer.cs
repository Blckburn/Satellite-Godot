using Godot;
using System;
using System.Collections.Generic;

public sealed class RoomPlacer
{
    private readonly Random _random;
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly int _minRoomSize;
    private readonly int _maxRoomSize;
    private readonly int _maxRooms;
    private readonly int _minRoomDistance;

    public RoomPlacer(Random random, int mapWidth, int mapHeight, int minRoomSize, int maxRoomSize, int maxRooms, int minRoomDistance)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _minRoomSize = minRoomSize;
        _maxRoomSize = maxRoomSize;
        _maxRooms = maxRooms;
        _minRoomDistance = minRoomDistance;
    }

    public int GenerateSectionRooms(LevelGenerator.MapSection section, Action<Rect2I> onRoomAccepted)
    {
        int attempts = 0;
        int createdRooms = 0;
        while (createdRooms < _maxRooms && attempts < _maxRooms * 5)
        {
            attempts++;
            int width = _random.Next(_minRoomSize, _maxRoomSize + 1);
            int height = _random.Next(_minRoomSize, _maxRoomSize + 1);
            int x = _random.Next(2, _mapWidth - width - 2);
            int y = _random.Next(2, _mapHeight - height - 2);
            Rect2I newRoom = new Rect2I(x, y, width, height);

            bool overlaps = false;
            foreach (var room in section.Rooms)
            {
                Rect2I expanded = new Rect2I(
                    room.Position - new Vector2I(_minRoomDistance, _minRoomDistance),
                    room.Size + new Vector2I(_minRoomDistance * 2, _minRoomDistance * 2)
                );
                if (expanded.Intersects(newRoom)) { overlaps = true; break; }
            }

            if (!overlaps)
            {
                section.Rooms.Add(newRoom);
                onRoomAccepted?.Invoke(newRoom);
                createdRooms++;
            }
        }
        return attempts;
    }
}


