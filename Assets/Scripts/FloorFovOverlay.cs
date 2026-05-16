using UnityEngine;

public sealed class FloorFovOverlay : MonoBehaviour
{
    [SerializeField] private Camera _viewCamera;
    [SerializeField] private float _maxProjectionDistance = 6f;
    [SerializeField] private float _edgePadding = 0.15f;

    private static readonly Vector3[] ViewportCorners =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(0f, 1f, 0f),
        new Vector3(1f, 1f, 0f),
        new Vector3(1f, 0f, 0f)
    };

    private readonly Vector3[] _worldCorners = new Vector3[4];
    private readonly Vector3[] _localVertices = new Vector3[4];
    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private Mesh _mesh;

    public void Initialize(Camera viewCamera, MeshCollider meshCollider)
    {
        _viewCamera = viewCamera;
        _meshCollider = meshCollider;
    }

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null)
        {
            _meshCollider = GetComponent<MeshCollider>();
        }
    }

    private void LateUpdate()
    {
        if (_viewCamera == null)
        {
            _viewCamera = Camera.main;
        }

        if (_viewCamera == null || _meshFilter == null)
        {
            return;
        }

        UpdateFloorFootprint();
    }

    private void UpdateFloorFootprint()
    {
        Plane floorPlane = new Plane(Vector3.up, transform.position);
        Vector3 center = Vector3.zero;

        for (int i = 0; i < ViewportCorners.Length; i++)
        {
            Ray ray = _viewCamera.ViewportPointToRay(ViewportCorners[i]);
            _worldCorners[i] = IntersectFloorOrProject(ray, floorPlane);
            center += _worldCorners[i];
        }

        center /= ViewportCorners.Length;

        for (int i = 0; i < _worldCorners.Length; i++)
        {
            Vector3 paddedCorner = _worldCorners[i];
            Vector3 fromCenter = Vector3.ProjectOnPlane(paddedCorner - center, Vector3.up);
            if (fromCenter.sqrMagnitude > 0.0001f)
            {
                paddedCorner += fromCenter.normalized * _edgePadding;
            }

            _localVertices[i] = transform.InverseTransformPoint(paddedCorner);
        }

        EnsureWritableMesh();
        _mesh.vertices = _localVertices;
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        if (_meshCollider != null)
        {
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }
    }

    private Vector3 IntersectFloorOrProject(Ray ray, Plane floorPlane)
    {
        if (floorPlane.Raycast(ray, out float distance) && distance > 0f)
        {
            return ray.GetPoint(distance);
        }

        Vector3 planarDirection = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = Vector3.ProjectOnPlane(_viewCamera.transform.forward, Vector3.up);
        }

        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            planarDirection = Vector3.forward;
        }

        Vector3 projectedPoint = ray.origin + planarDirection.normalized * _maxProjectionDistance;
        projectedPoint.y = transform.position.y;
        return projectedPoint;
    }

    private void EnsureWritableMesh()
    {
        if (_mesh != null)
        {
            return;
        }

        _mesh = new Mesh
        {
            name = "Runtime_Floor_FOV_Overlay"
        };
        _meshFilter.sharedMesh = _mesh;
    }
}
