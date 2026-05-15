using UnityEngine;

public static class Task2SpatialAnchorBootstrap
{
    private const string ManagerName = "[Task 2] Spatial Surface Anchor Manager";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureManagerExists()
    {
        if (Object.FindFirstObjectByType<SpatialSurfaceAnchorManager>() != null)
        {
            return;
        }

        GameObject managerObject = new GameObject(ManagerName);
        managerObject.AddComponent<SpatialSurfaceAnchorManager>();
    }
}
