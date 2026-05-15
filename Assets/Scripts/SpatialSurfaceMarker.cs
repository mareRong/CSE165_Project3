using UnityEngine;

public sealed class SpatialSurfaceMarker : MonoBehaviour
{
    [SerializeField] private string _surfaceName;
    [SerializeField] private SpatialSurfaceAnchorManager.SurfaceKind _surfaceKind;
    [SerializeField] private Vector2 _surfaceSize;

    public string SurfaceName => _surfaceName;
    public SpatialSurfaceAnchorManager.SurfaceKind SurfaceKind => _surfaceKind;
    public Vector2 SurfaceSize => _surfaceSize;

    public void Initialize(
        string surfaceName,
        SpatialSurfaceAnchorManager.SurfaceKind surfaceKind,
        Vector2 surfaceSize)
    {
        _surfaceName = surfaceName;
        _surfaceKind = surfaceKind;
        _surfaceSize = surfaceSize;
    }
}
