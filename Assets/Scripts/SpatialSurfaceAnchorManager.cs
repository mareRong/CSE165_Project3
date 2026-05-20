using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public sealed class SpatialSurfaceAnchorManager : MonoBehaviour
{
    public enum SurfaceKind
    {
        Floor,
        Wall
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
    [SerializeField] private bool _useDetectedRoomWalls = true;
    [SerializeField] private bool _showConfiguredWallsWhileDetecting = true;
    [SerializeField] private bool _requestSceneCaptureIfNoRoom = true;
    [SerializeField] private bool _useConfiguredWallsWhenDetectionFails = true;
    [SerializeField] private int _detectedRoomWallFetchAttempts = 20;
    [SerializeField] private float _detectedRoomWallFetchRetryDelaySeconds = 0.75f;
    [SerializeField] private bool _showDebugStatus = true;
    [SerializeField] private TextMeshProUGUI _debugStatusText;
    [SerializeField] private Color _wallOverlayColor = new Color(1f, 0.82f, 0f, 0.72f);
    [SerializeField] private string _surfaceLayerName = "Surface";
    [SerializeField] private float _floorWorldY = 0f;
    [SerializeField] private float _configuredWallBaseWorldY = 0f;
    [SerializeField] private bool _centerFloorUnderInitialHeadset = false;
    [SerializeField] private bool _lockFloorToAvatarFeet = true;
    [SerializeField] private bool _spatiallyAnchorConfiguredFloor = true;
    [SerializeField] private bool _waitForTrackedHeadsetBeforeLockingFloor = false;
    [SerializeField] private float _floorLockDelay = 0f;
    [SerializeField] private float _floorOffsetBelowAvatar = 0.03f;
    [SerializeField] private SurfaceDefinition[] _surfaces =
    {
        new SurfaceDefinition
        {
            Name = "Floor",
            Kind = SurfaceKind.Floor,
            LocalPosition = Vector3.zero,
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(200f, 200f),
            Color = new Color(0.08f, 0.42f, 1f, 0.32f)
        },
        new SurfaceDefinition
        {
            Name = "Front Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(0f, 1.25f, 2.3f),
            LocalEulerAngles = new Vector3(0f, 180f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.82f, 0f, 0.72f)
        },
        new SurfaceDefinition
        {
            Name = "Back Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(0f, 1.25f, -0.7f),
            LocalEulerAngles = Vector3.zero,
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.82f, 0f, 0.72f)
        },
        new SurfaceDefinition
        {
            Name = "Left Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(-1.5f, 1.25f, 0.8f),
            LocalEulerAngles = new Vector3(0f, 90f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.82f, 0f, 0.72f)
        },
        new SurfaceDefinition
        {
            Name = "Right Wall",
            Kind = SurfaceKind.Wall,
            LocalPosition = new Vector3(1.5f, 1.25f, 0.8f),
            LocalEulerAngles = new Vector3(0f, -90f, 0f),
            Size = new Vector2(3f, 2.5f),
            Color = new Color(1f, 0.82f, 0f, 0.72f)
        }
    };

    private const string RootName = "[Task 2] Spatial Surface Anchors";
    private readonly List<GameObject> _createdSurfaces = new List<GameObject>();
    private readonly List<ConfiguredWallAnchor> _configuredWallAnchors = new List<ConfiguredWallAnchor>();
    private Material _surfaceMaterial;
    private Transform _floorAnchor;
    private Vector3 _lockedFloorPosition;
    private Quaternion _lockedFloorRotation;
    private bool _hasLockedFloorPose;
    private float _floorLockStartTime;
    private SurfaceDefinition _pendingFloorLockSurface;

    private sealed class ConfiguredWallAnchor
    {
        public Transform Anchor;
        public SurfaceDefinition Surface;
    }

    private void Start()
    {
        _floorLockStartTime = Time.time;

        if (_buildOnStart)
        {
            BuildSurfaces();
        }
    }

    private void LateUpdate()
    {
        if (_floorAnchor != null && !_hasLockedFloorPose && _pendingFloorLockSurface != null)
        {
            TryLockFloorPose(_floorAnchor, _pendingFloorLockSurface);
        }

        if (_floorAnchor != null && _hasLockedFloorPose && !_spatiallyAnchorConfiguredFloor)
        {
            _floorAnchor.SetPositionAndRotation(_lockedFloorPosition, _lockedFloorRotation);
        }
    }

    [ContextMenu("Build Task 2 Surfaces")]
    public void BuildSurfaces()
    {
        ResolveDebugStatusText();
        ReportStatus("Building spatial floor and wall anchors...");

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

            if (_useDetectedRoomWalls && surface.Kind == SurfaceKind.Wall && !_showConfiguredWallsWhileDetecting)
            {
                continue;
            }

            CreateConfiguredSurfaceAnchor(root, surface);
        }

        if (_useDetectedRoomWalls)
        {
            LoadDetectedRoomWalls(root);
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
        _configuredWallAnchors.Clear();
        _floorAnchor = null;
        _hasLockedFloorPose = false;
        _pendingFloorLockSurface = null;

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

    private GameObject CreateConfiguredSurfaceAnchor(Transform root, SurfaceDefinition surface)
    {
        GameObject anchorObject = new GameObject("SpatialAnchor_" + SanitizeName(surface.Name));
        anchorObject.transform.SetParent(root, false);
        anchorObject.transform.SetPositionAndRotation(
            TransformSurfacePoint(surface),
            TransformRotation(surface.LocalEulerAngles));
        ApplySurfaceLayer(anchorObject);

        if (surface.Kind == SurfaceKind.Floor)
        {
            _floorAnchor = anchorObject.transform;
            _pendingFloorLockSurface = surface;
            AddSpatialAnchorIfEnabled(anchorObject);
            TryLockFloorPose(anchorObject.transform, surface);
        }
        else
        {
            anchorObject.AddComponent<OVRSpatialAnchor>();
            if (surface.Kind == SurfaceKind.Wall)
            {
                _configuredWallAnchors.Add(new ConfiguredWallAnchor
                {
                    Anchor = anchorObject.transform,
                    Surface = surface
                });
            }
        }

        _createdSurfaces.Add(anchorObject);
        CreateSurfaceVisual(anchorObject.transform, surface.Name, surface.Kind, surface.Size, surface.Color, Vector3.zero);

        SpatialSurfaceMarker marker = anchorObject.AddComponent<SpatialSurfaceMarker>();
        marker.Initialize(surface.Name, surface.Kind, surface.Size);
        return anchorObject;
    }

    private async void LoadDetectedRoomWalls(Transform root)
    {
        try
        {
            await LoadDetectedRoomWallsAsync(root);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Task 2 wall detection failed, using configured yellow walls instead. {exception}", this);
            ReportStatus("Room wall detection threw an exception. Showing configured yellow walls.");
            if (root != null && _useConfiguredWallsWhenDetectionFails && _configuredWallAnchors.Count == 0)
            {
                CreateConfiguredWallAnchors(root);
            }
        }
    }

    private async Task LoadDetectedRoomWallsAsync(Transform root)
    {
        if (!await EnsureScenePermissionAsync())
        {
            ReportStatus("Scene permission is not granted. Showing configured placeholder walls.");
            return;
        }

        int maxAttempts = Mathf.Max(1, _detectedRoomWallFetchAttempts);
        int wallCount = 0;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!HasReliableTrackingSpace())
            {
                string trackingMessage = $"Waiting for reliable headset tracking before loading detected room walls. Attempt {attempt}/{maxAttempts}.";
                Debug.Log(trackingMessage, this);
                ReportStatus(trackingMessage);
                await DelayDetectedRoomWallRetry();
                continue;
            }

            List<OVRAnchor> rooms = new List<OVRAnchor>();
            OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> roomFetchResult =
                await OVRAnchor.FetchAnchorsAsync(rooms, new OVRAnchor.FetchOptions
                {
                    SingleComponentType = typeof(OVRRoomLayout),
                });

            Debug.Log(
                $"Room layout fetch attempt {attempt}/{maxAttempts}: success={roomFetchResult.Success}, rooms={rooms.Count}.",
                this);

            if ((!roomFetchResult.Success || rooms.Count == 0) && _requestSceneCaptureIfNoRoom)
            {
                ReportStatus($"No room layout yet. Requesting headset scene setup... ({attempt}/{maxAttempts})");
                bool sceneCaptured = await OVRScene.RequestSpaceSetup();
                Debug.Log($"Scene setup request returned {sceneCaptured} on attempt {attempt}/{maxAttempts}.", this);
                if (sceneCaptured)
                {
                    roomFetchResult = await OVRAnchor.FetchAnchorsAsync(rooms, new OVRAnchor.FetchOptions
                    {
                        SingleComponentType = typeof(OVRRoomLayout),
                    });
                    Debug.Log(
                        $"Room layout fetch after scene setup attempt {attempt}/{maxAttempts}: success={roomFetchResult.Success}, rooms={rooms.Count}.",
                        this);
                }
            }

            if (roomFetchResult.Success)
            {
                ReportStatus($"Found {rooms.Count} room layout anchor(s). Loading walls... ({attempt}/{maxAttempts})");
                for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
                {
                    wallCount += await CreateDetectedRoomWallAnchors(root, rooms[roomIndex], wallCount);
                }
            }

            if (wallCount > 0)
            {
                RemoveConfiguredWallAnchors();
                Debug.Log($"Task 2 created {wallCount} detected yellow wall anchors from the room scene model on attempt {attempt}/{maxAttempts}.", this);
                ReportStatus($"Detected room walls: {wallCount}. Placeholder walls removed.");
                return;
            }

            string retryMessage = $"Detected room wall fetch returned no usable walls on attempt {attempt}/{maxAttempts}.";
            Debug.Log(retryMessage, this);
            ReportStatus(retryMessage);
            await DelayDetectedRoomWallRetry();
        }

        if (_configuredWallAnchors.Count > 0)
        {
            Debug.Log($"Room wall detection did not return any walls, so Task 2 is keeping {_configuredWallAnchors.Count} configured yellow wall anchors.", this);
            ReportStatus("No detected room walls. Showing configured placeholder walls.");
        }
        else if (_useConfiguredWallsWhenDetectionFails)
        {
            wallCount = CreateConfiguredWallAnchors(root);
            Debug.Log($"No detected room walls were available, so Task 2 created {wallCount} configured yellow wall anchors.", this);
            ReportStatus($"No detected room walls. Created {wallCount} configured placeholder walls.");
        }
        else
        {
            Debug.LogWarning("No detected room walls were available, and configured fallback walls are disabled to avoid showing walls that do not match the headset room layout.", this);
            ReportStatus("No detected room walls, and placeholder fallback is disabled.");
        }
    }

    private async Task DelayDetectedRoomWallRetry()
    {
        int delayMilliseconds = Mathf.RoundToInt(Mathf.Max(0.05f, _detectedRoomWallFetchRetryDelaySeconds) * 1000f);
        await Task.Delay(delayMilliseconds);
    }

    private async Task<bool> EnsureScenePermissionAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
        {
            ReportStatus("Scene permission granted. Querying room layout...");
            return true;
        }

        ReportStatus("Scene permission is missing. Requesting permission...");
        TaskCompletionSource<bool> permissionResult = new TaskCompletionSource<bool>();
        UnityEngine.Android.PermissionCallbacks callbacks = new UnityEngine.Android.PermissionCallbacks();
        callbacks.PermissionGranted += permissionName =>
        {
            if (permissionName == OVRPermissionsRequester.ScenePermission)
            {
                permissionResult.TrySetResult(true);
            }
        };
        callbacks.PermissionDenied += permissionName =>
        {
            if (permissionName == OVRPermissionsRequester.ScenePermission)
            {
                permissionResult.TrySetResult(false);
            }
        };
        callbacks.PermissionDeniedAndDontAskAgain += permissionName =>
        {
            if (permissionName == OVRPermissionsRequester.ScenePermission)
            {
                permissionResult.TrySetResult(false);
            }
        };

        UnityEngine.Android.Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
        Task completedTask = await Task.WhenAny(permissionResult.Task, Task.Delay(10000));
        bool granted = completedTask == permissionResult.Task && permissionResult.Task.Result;
        if (!granted)
        {
            granted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
        }

        ReportStatus(granted
            ? "Scene permission granted. Querying room layout..."
            : "Scene permission was not granted. Enable Spatial Data/Scene permission for this app.");
        return granted;
#else
        return true;
#endif
    }

    private static bool HasReliableTrackingSpace()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return false;
        }

        return OVRManager.tracker == null || OVRManager.tracker.isPositionTracked;
    }

    private void ResolveDebugStatusText()
    {
        if (!_showDebugStatus || _debugStatusText != null)
        {
            return;
        }

        TextMeshProUGUI[] textComponents = FindObjectsByType<TextMeshProUGUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < textComponents.Length; i++)
        {
            if (textComponents[i].name == "Status Message")
            {
                _debugStatusText = textComponents[i];
                return;
            }
        }
    }

    private void ReportStatus(string message)
    {
        if (!_showDebugStatus)
        {
            return;
        }

        ResolveDebugStatusText();
        if (_debugStatusText != null)
        {
            _debugStatusText.gameObject.SetActive(true);
            _debugStatusText.text = message;
        }

        Debug.Log($"SpatialSurfaceAnchorManager status: {message}", this);
    }

    private int CreateConfiguredWallAnchors(Transform root)
    {
        int wallCount = 0;
        foreach (SurfaceDefinition surface in _surfaces)
        {
            if (surface != null && surface.Kind == SurfaceKind.Wall)
            {
                CreateConfiguredSurfaceAnchor(root, surface);
                wallCount++;
            }
        }

        return wallCount;
    }

    private void RemoveConfiguredWallAnchors()
    {
        for (int i = _configuredWallAnchors.Count - 1; i >= 0; i--)
        {
            Transform wallAnchor = _configuredWallAnchors[i].Anchor;
            if (wallAnchor != null)
            {
                _createdSurfaces.Remove(wallAnchor.gameObject);
                DestroySurfaceObject(wallAnchor.gameObject);
            }
        }

        _configuredWallAnchors.Clear();
    }

    private async Task<int> CreateDetectedRoomWallAnchors(Transform root, OVRAnchor room, int existingWallCount)
    {
        if (!room.TryGetComponent(out OVRRoomLayout roomLayout))
        {
            Debug.LogWarning($"Room anchor {room.Uuid} does not have OVRRoomLayout.", this);
            return 0;
        }

        if (!roomLayout.TryGetRoomLayout(out _, out _, out Guid[] wallUuids) || wallUuids == null || wallUuids.Length == 0)
        {
            Debug.LogWarning($"Room anchor {room.Uuid} did not provide wall UUIDs.", this);
            return 0;
        }

        HashSet<Guid> wallUuidSet = new HashSet<Guid>(wallUuids);
        List<OVRAnchor> roomAnchors = new List<OVRAnchor>();
        OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> roomAnchorResult = await roomLayout.FetchAnchorsAsync(roomAnchors);
        Debug.Log(
            $"Room {room.Uuid} layout has {wallUuids.Length} wall UUID(s); fetched {roomAnchors.Count} room anchor(s), success={roomAnchorResult.Success}.",
            this);
        if (!roomAnchorResult.Success)
        {
            return 0;
        }

        int createdCount = 0;
        for (int i = 0; i < roomAnchors.Count; i++)
        {
            OVRAnchor roomAnchor = roomAnchors[i];
            if (!wallUuidSet.Contains(roomAnchor.Uuid))
            {
                continue;
            }

            if (await TryCreateDetectedWallAnchor(root, roomAnchor, existingWallCount + createdCount + 1))
            {
                createdCount++;
            }
        }

        return createdCount;
    }

    private async Task<bool> TryCreateDetectedWallAnchor(Transform root, OVRAnchor wallAnchor, int wallNumber)
    {
        if (!wallAnchor.TryGetComponent(out OVRLocatable locatable))
        {
            Debug.LogWarning($"Detected wall anchor {wallAnchor.Uuid} has no OVRLocatable.", this);
            return false;
        }

        await locatable.SetEnabledAsync(true);
        if (!locatable.TryGetSceneAnchorPose(out OVRLocatable.TrackingSpacePose pose))
        {
            Debug.LogWarning($"Detected wall anchor {wallAnchor.Uuid} did not provide a scene anchor pose.", this);
            return false;
        }

        Transform trackingSpace = ResolveTrackingSpace();
        Vector3? worldPosition = pose.ComputeWorldPosition(trackingSpace);
        Quaternion? worldRotation = pose.ComputeWorldRotation(trackingSpace);
        if (!worldPosition.HasValue || !worldRotation.HasValue)
        {
            Debug.LogWarning($"Detected wall anchor {wallAnchor.Uuid} pose could not be converted to world space.", this);
            return false;
        }

        if (!wallAnchor.TryGetComponent(out OVRBounded2D bounds2D) || !bounds2D.IsEnabled)
        {
            Debug.LogWarning($"Detected wall anchor {wallAnchor.Uuid} has no enabled OVRBounded2D bounds.", this);
            return false;
        }

        Rect wallBounds = bounds2D.BoundingBox;
        Vector2 wallSize = new Vector2(
            Mathf.Max(0.01f, wallBounds.size.x),
            Mathf.Max(0.01f, wallBounds.size.y));
        string wallName = $"Detected Wall {wallNumber}";

        Debug.Log(
            $"{wallName}: uuid={wallAnchor.Uuid}, worldPosition={worldPosition.Value}, worldRotation={worldRotation.Value.eulerAngles}, size={wallSize}, boundsCenter={wallBounds.center}.",
            this);

        GameObject anchorObject = new GameObject("SpatialAnchor_" + SanitizeName(wallName));
        anchorObject.transform.SetParent(root, false);
        anchorObject.transform.SetPositionAndRotation(worldPosition.Value, worldRotation.Value);
        ApplySurfaceLayer(anchorObject);
        anchorObject.AddComponent<OVRSpatialAnchor>();
        _createdSurfaces.Add(anchorObject);

        Vector3 localCenter = new Vector3(wallBounds.center.x, wallBounds.center.y, 0f);
        CreateSurfaceVisual(anchorObject.transform, wallName, SurfaceKind.Wall, wallSize, _wallOverlayColor, localCenter);

        SpatialSurfaceMarker marker = anchorObject.AddComponent<SpatialSurfaceMarker>();
        marker.Initialize(wallName, SurfaceKind.Wall, wallSize);
        return true;
    }

    private Transform ResolveTrackingSpace()
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            return cameraRig.trackingSpace;
        }

        return transform;
    }

    private void CreateSurfaceVisual(
        Transform anchor,
        string surfaceName,
        SurfaceKind surfaceKind,
        Vector2 surfaceSize,
        Color surfaceColor,
        Vector3 localPosition)
    {
        GameObject meshObject = new GameObject("PhysicalSpaceMesh_" + SanitizeName(surfaceName));
        meshObject.transform.SetParent(anchor, false);
        meshObject.transform.localPosition = localPosition;
        ApplySurfaceLayer(meshObject);

        SurfaceDefinition visualDefinition = new SurfaceDefinition
        {
            Name = surfaceName,
            Kind = surfaceKind,
            Size = surfaceSize,
            Color = surfaceColor
        };

        MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateSurfaceMesh(visualDefinition);

        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = CreateSurfaceMaterialInstance(ResolveSurfaceColor(visualDefinition));

        if (_addMeshColliders)
        {
            MeshCollider meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
        }
    }

    private Color ResolveSurfaceColor(SurfaceDefinition surface)
    {
        return surface.Kind == SurfaceKind.Wall ? _wallOverlayColor : surface.Color;
    }

    private void ApplySurfaceLayer(GameObject target)
    {
        int layer = LayerMask.NameToLayer(_surfaceLayerName);
        if (layer >= 0)
        {
            target.layer = layer;
        }
    }

    private void AddSpatialAnchorIfEnabled(GameObject anchorObject)
    {
        if (!_spatiallyAnchorConfiguredFloor || anchorObject.GetComponent<OVRSpatialAnchor>() != null)
        {
            return;
        }

        anchorObject.AddComponent<OVRSpatialAnchor>();
    }

    private Vector3 TransformSurfacePoint(SurfaceDefinition surface)
    {
        if (surface.Kind == SurfaceKind.Floor)
        {
            return ResolveFloorWorldPosition(surface);
        }

        Vector3 originPosition = ResolveConfiguredRoomOriginPosition();
        Quaternion yawOnly = ResolveConfiguredRoomYaw();
        Vector3 localPlanarOffset = new Vector3(surface.LocalPosition.x, 0f, surface.LocalPosition.z);
        Vector3 worldPosition = originPosition + yawOnly * localPlanarOffset;
        worldPosition.y = ResolveConfiguredRoomFloorY() + surface.LocalPosition.y;
        return worldPosition;
    }

    private Vector3 ResolveConfiguredRoomOriginPosition()
    {
        if (_hasLockedFloorPose)
        {
            return _lockedFloorPosition;
        }

        if (_origin != null)
        {
            return _origin.position;
        }

        Transform centerReference = GetInitialFloorCenterReference();
        return centerReference != null ? centerReference.position : Vector3.zero;
    }

    private float ResolveConfiguredRoomFloorY()
    {
        return _configuredWallBaseWorldY;
    }

    private Vector3 ResolveFloorWorldPosition(SurfaceDefinition surface)
    {
        Vector3 floorPosition = new Vector3(
            surface.LocalPosition.x,
            ResolveFloorWorldY(surface),
            surface.LocalPosition.z);

        if (!_centerFloorUnderInitialHeadset)
        {
            return floorPosition;
        }

        Transform centerReference = GetInitialFloorCenterReference();
        if (centerReference != null)
        {
            floorPosition.x = centerReference.position.x + surface.LocalPosition.x;
            floorPosition.z = centerReference.position.z + surface.LocalPosition.z;
        }

        return floorPosition;
    }

    private float ResolveFloorWorldY(SurfaceDefinition surface)
    {
        if (!_lockFloorToAvatarFeet)
        {
            return _floorWorldY + surface.LocalPosition.y;
        }

        if (TryGetAvatarFootY(out float avatarFootY))
        {
            return avatarFootY - Mathf.Max(0f, _floorOffsetBelowAvatar);
        }

        return _floorWorldY + surface.LocalPosition.y;
    }

    private static bool TryGetAvatarFootY(out float footY)
    {
        footY = 0f;

        AgentTravel agentTravel = FindFirstObjectByType<AgentTravel>();
        Transform avatar = agentTravel != null ? agentTravel.avatar : null;
        if (avatar == null && agentTravel != null)
        {
            avatar = agentTravel.transform;
        }

        if (avatar == null)
        {
            return false;
        }

        Renderer[] renderers = avatar.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            footY = avatar.position.y;
            return true;
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        footY = combinedBounds.min.y;
        return true;
    }

    private static Transform GetInitialFloorCenterReference()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform;
        }

        AgentTravel agentTravel = FindFirstObjectByType<AgentTravel>();
        if (agentTravel == null)
        {
            return null;
        }

        return agentTravel.avatar != null ? agentTravel.avatar : agentTravel.transform;
    }

    private void LockFloorPose(Transform floorAnchor, SurfaceDefinition surface)
    {
        _floorAnchor = floorAnchor;
        _lockedFloorPosition = ResolveFloorWorldPosition(surface);
        _lockedFloorRotation = Quaternion.identity;
        _hasLockedFloorPose = true;
        _floorAnchor.SetPositionAndRotation(_lockedFloorPosition, _lockedFloorRotation);
        RepositionConfiguredWallAnchors();

        Debug.Log(
            $"Task 2 floor locked at world position {_lockedFloorPosition} with size {surface.Size}. " +
            (_spatiallyAnchorConfiguredFloor
                ? "It is mapped with OVRSpatialAnchor so the headset pose can move independently."
                : "It is not parented to the headset and does not use OVRSpatialAnchor."),
            floorAnchor);
    }

    private void RepositionConfiguredWallAnchors()
    {
        for (int i = 0; i < _configuredWallAnchors.Count; i++)
        {
            ConfiguredWallAnchor wallAnchor = _configuredWallAnchors[i];
            if (wallAnchor.Anchor == null || wallAnchor.Surface == null)
            {
                continue;
            }

            wallAnchor.Anchor.SetPositionAndRotation(
                TransformSurfacePoint(wallAnchor.Surface),
                TransformRotation(wallAnchor.Surface.LocalEulerAngles));
        }
    }

    private void TryLockFloorPose(Transform floorAnchor, SurfaceDefinition surface)
    {
        if (_waitForTrackedHeadsetBeforeLockingFloor && !HasUsableInitialFloorCenter())
        {
            return;
        }

        LockFloorPose(floorAnchor, surface);
        _pendingFloorLockSurface = null;
    }

    private bool HasUsableInitialFloorCenter()
    {
        if (Time.time - _floorLockStartTime < Mathf.Max(0f, _floorLockDelay))
        {
            return false;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return false;
        }

        return mainCamera.transform.position.sqrMagnitude > 0.001f;
    }

    private Quaternion TransformRotation(Vector3 localEulerAngles)
    {
        return ResolveConfiguredRoomYaw() * Quaternion.Euler(localEulerAngles);
    }

    private Quaternion ResolveConfiguredRoomYaw()
    {
        Vector3 originForward = _origin != null ? _origin.forward : Vector3.forward;
        if (_origin == null)
        {
            Transform centerReference = GetInitialFloorCenterReference();
            if (centerReference != null)
            {
                originForward = centerReference.forward;
            }
        }

        Vector3 flattenedForward = Vector3.ProjectOnPlane(originForward, Vector3.up).normalized;
        if (flattenedForward.sqrMagnitude < 0.001f)
        {
            flattenedForward = Vector3.forward;
        }

        return Quaternion.LookRotation(flattenedForward, Vector3.up);
    }

    private static Mesh CreateSurfaceMesh(SurfaceDefinition surface)
    {
        float halfWidth = Mathf.Max(0.01f, surface.Size.x) * 0.5f;
        float halfHeight = Mathf.Max(0.01f, surface.Size.y) * 0.5f;
        Vector3[] vertices;

        if (surface.Kind == SurfaceKind.Floor)
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
            triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                2, 1, 0,
                3, 2, 0
            },
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

    private Material CreateSurfaceMaterialInstance(Color color)
    {
        Material material = new Material(GetSurfaceMaterial());
        ApplyColorToMaterial(material, color);
        return material;
    }

    private static void ApplyColorToMaterial(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
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

        if (_surfaceMaterial.HasProperty("_Cull"))
        {
            _surfaceMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
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
