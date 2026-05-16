using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class SpatialSurfaceAnchorManager : MonoBehaviour
{
    public enum SurfaceKind
    {
        Floor,
        Wall,
        Table,
        Chair
    }

    [Serializable]
    public sealed class SurfaceDefinition
    {
        public string Name = "Floor";
        public SurfaceKind Kind = SurfaceKind.Floor;
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public Vector2 Size = new Vector2(3f, 3f);
        public Color Color = new Color(0.1f, 0.8f, 0.65f, 0.28f);
    }

    [SerializeField] private Transform _origin;
    [SerializeField] private bool _placeRelativeToHeadset = false;
    [SerializeField] private bool _buildOnStart = true;
    [SerializeField] private bool _rebuildExistingSurfaces = true;
    [SerializeField] private bool _addMeshColliders = true;
    [SerializeField] private float _floorWorldY = -15f;
    [SerializeField] private SurfaceDefinition[] _surfaces =
    {
        new SurfaceDefinition
        {
            Name = "Floor",
            Kind = SurfaceKind.Floor,
            LocalPosition = new Vector3(0f, 0f, 0.8f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(3f, 3f),
            Color = new Color(0.1f, 0.8f, 0.65f, 0.28f)
        },
        new SurfaceDefinition
        {
            Name = "Front Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(0f, 1.25f, 2.3f),
            LocalEulerAngles = new Vector3(0f, 180f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(0.1f, 0.45f, 1f, 0.24f)
        },
        new SurfaceDefinition
        {
            Name = "Back Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(0f, 1.25f, -0.7f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(3f, 2.5f),
            Color = new Color(0.55f, 0.35f, 1f, 0.24f)
        },
        new SurfaceDefinition
        {
            Name = "Left Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(-1.5f, 1.25f, 0.8f),
            LocalEulerAngles = new Vector3(0f, 90f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.75f, 0.1f, 0.24f)
        },
        new SurfaceDefinition
        {
            Name = "Right Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(1.5f, 1.25f, 0.8f),
            LocalEulerAngles = new Vector3(0f, -90f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.35f, 0.2f, 0.24f)
        },
        new SurfaceDefinition
        {
            Name = "Table",
            Kind = SurfaceKind.Table,
            LocalPosition = new Vector3(0f, 0.75f, 1.15f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(1.2f, 0.8f),
            Color = new Color(1f, 0.92f, 0.08f, 0.36f)
        },
        new SurfaceDefinition
        {
            Name = "Chair Left",
            Kind = SurfaceKind.Chair,
            LocalPosition = new Vector3(-0.75f, 0.45f, 1.15f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(0.55f, 0.55f),
            Color = new Color(1f, 0.18f, 0.55f, 0.38f)
        },
        new SurfaceDefinition
        {
            Name = "Chair Right",
            Kind = SurfaceKind.Chair,
            LocalPosition = new Vector3(0.75f, 0.45f, 1.15f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(0.55f, 0.55f),
            Color = new Color(1f, 0.18f, 0.55f, 0.38f)
        }
    };

    private const string RootName = "[Task 2] Spatial Surface Anchors";
    private readonly List<GameObject> _createdSurfaces = new List<GameObject>();
    private Material _surfaceMaterial;

    private void Start()
    {
        if (_buildOnStart)
        {
            BuildSurfaces();
        }
    }

    [ContextMenu("Build Task 2 Surfaces")]
    public void BuildSurfaces()
    {
        if (_rebuildExistingSurfaces)
        {
            ClearSurfaces();
        }

        if (_placeRelativeToHeadset && _origin == null)
        {
            Camera mainCamera = Camera.main;
            _origin = mainCamera != null ? mainCamera.transform : transform;
        }

        Transform root = GetOrCreateRoot();

        foreach (SurfaceDefinition surface in _surfaces)
        {
            if (surface == null)
            {
                continue;
            }

            GameObject anchorObject = new GameObject("SpatialAnchor_" + SanitizeName(surface.Name));
            anchorObject.transform.SetParent(root, false);
            anchorObject.transform.SetPositionAndRotation(
                TransformSurfacePoint(surface),
                TransformRotation(surface.LocalEulerAngles));

            anchorObject.AddComponent<OVRSpatialAnchor>();
            _createdSurfaces.Add(anchorObject);

            GameObject meshObject = new GameObject("PhysicalSpaceMesh_" + SanitizeName(surface.Name));
            meshObject.transform.SetParent(anchorObject.transform, false);

            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateSurfaceMesh(surface);

            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetSurfaceMaterial();
            meshRenderer.material.color = surface.Color;

            if (_addMeshColliders)
            {
                MeshCollider meshCollider = meshObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
            }

            SpatialSurfaceMarker marker = anchorObject.AddComponent<SpatialSurfaceMarker>();
            marker.Initialize(surface.Name, surface.Kind, surface.Size);
        }
    }

    [ContextMenu("Clear Task 2 Surfaces")]
    public void ClearSurfaces()
    {
        for (int i = _createdSurfaces.Count - 1; i >= 0; i--)
        {
            if (_createdSurfaces[i] != null)
            {
                DestroySurfaceObject(_createdSurfaces[i]);
            }
        }

        _createdSurfaces.Clear();

        Transform existingRoot = transform.Find(RootName);
        if (existingRoot != null)
        {
            DestroySurfaceObject(existingRoot.gameObject);
        }
    }

    private Transform GetOrCreateRoot()
    {
        Transform existingRoot = transform.Find(RootName);
        if (existingRoot != null)
        {
            return existingRoot;
        }

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        return root.transform;
    }

    private Vector3 TransformSurfacePoint(SurfaceDefinition surface)
    {
        Vector3 originPosition = _origin != null ? _origin.position : Vector3.zero;
        Vector3 originForward = _origin != null ? _origin.forward : Vector3.forward;
        Vector3 flattenedForward = Vector3.ProjectOnPlane(originForward, Vector3.up).normalized;
        if (flattenedForward.sqrMagnitude < 0.001f)
        {
            flattenedForward = Vector3.forward;
        }

        Quaternion yawOnly = Quaternion.LookRotation(flattenedForward, Vector3.up);
        Vector3 localPlanarOffset = new Vector3(surface.LocalPosition.x, 0f, surface.LocalPosition.z);
        Vector3 worldPosition = originPosition + yawOnly * localPlanarOffset;
        worldPosition.y = _floorWorldY + surface.LocalPosition.y;
        return worldPosition;
    }

    private Quaternion TransformRotation(Vector3 localEulerAngles)
    {
        Vector3 originForward = _origin != null ? _origin.forward : Vector3.forward;
        Vector3 flattenedForward = Vector3.ProjectOnPlane(originForward, Vector3.up).normalized;
        if (flattenedForward.sqrMagnitude < 0.001f)
        {
            flattenedForward = Vector3.forward;
        }

        return Quaternion.LookRotation(flattenedForward, Vector3.up) * Quaternion.Euler(localEulerAngles);
    }

    private static Mesh CreateSurfaceMesh(SurfaceDefinition surface)
    {
        float halfWidth = Mathf.Max(0.01f, surface.Size.x) * 0.5f;
        float halfHeight = Mathf.Max(0.01f, surface.Size.y) * 0.5f;
        Vector3[] vertices;

        if (IsHorizontalSurface(surface.Kind))
        {
            vertices = new[]
            {
                new Vector3(-halfWidth, 0f, -halfHeight),
                new Vector3(-halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, halfHeight),
                new Vector3(halfWidth, 0f, -halfHeight)
            };
        }
        else
        {
            vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f)
            };
        }

        Mesh mesh = new Mesh
        {
            name = "Task2_" + SanitizeName(surface.Name) + "_Mesh",
            vertices = vertices,
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            }
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool IsHorizontalSurface(SurfaceKind kind)
    {
        return kind == SurfaceKind.Floor
            || kind == SurfaceKind.Table
            || kind == SurfaceKind.Chair;
    }

    private Material GetSurfaceMaterial()
    {
        if (_surfaceMaterial != null)
        {
            return _surfaceMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        _surfaceMaterial = new Material(shader)
        {
            name = "Task2_Spatial_Surface_Overlay"
        };
        if (_surfaceMaterial.HasProperty("_Surface"))
        {
            _surfaceMaterial.SetFloat("_Surface", 1f);
        }

        if (_surfaceMaterial.HasProperty("_Blend"))
        {
            _surfaceMaterial.SetFloat("_Blend", 0f);
        }

        if (_surfaceMaterial.HasProperty("_SrcBlend"))
        {
            _surfaceMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (_surfaceMaterial.HasProperty("_DstBlend"))
        {
            _surfaceMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (_surfaceMaterial.HasProperty("_ZWrite"))
        {
            _surfaceMaterial.SetFloat("_ZWrite", 0f);
        }

        _surfaceMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _surfaceMaterial.EnableKeyword("_ALPHABLEND_ON");
        _surfaceMaterial.renderQueue = 3000;
        return _surfaceMaterial;
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Surface";
        }

        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static void DestroySurfaceObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
