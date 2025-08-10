using Godot;
using System;
using System.Collections.Generic;

public sealed class EntitySpawner
{
    private readonly ResourceGenerator _resourceGenerator;
    private readonly ContainerGenerator _containerGenerator;

    public EntitySpawner(ResourceGenerator resourceGenerator, ContainerGenerator containerGenerator)
    {
        _resourceGenerator = resourceGenerator ?? throw new ArgumentNullException(nameof(resourceGenerator));
        _containerGenerator = containerGenerator ?? throw new ArgumentNullException(nameof(containerGenerator));
    }

    public int AddResources(List<Rect2I> rooms, int biomeType, LevelGenerator.TileType[,] sectionMask, Vector2 worldOffset, Node parent)
    {
        return _resourceGenerator.GenerateResources(rooms, biomeType, sectionMask, worldOffset, parent);
    }

    public int AddContainers(List<Rect2I> rooms, int biomeType, LevelGenerator.TileType[,] sectionMask, Vector2 worldOffset, Node parent, List<Vector2I> resourcePositions)
    {
        return _containerGenerator.GenerateContainers(rooms, biomeType, sectionMask, worldOffset, parent, resourcePositions);
    }
}


