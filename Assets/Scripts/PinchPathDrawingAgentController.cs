using System.Collections.Generic;
using UnityEngine;

public sealed class PinchPathDrawingAgentController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform _avatar;
    [SerializeField] private OVRHand _drawingHand;
    [SerializeField] private OVRSkeleton _drawingHandSkeleton;

    [Header("Gesture")]
    [SerializeField] private OVRHand.HandFinger _pinchFinger = OVRHand.HandFinger.Index;
    [SerializeField] private float _pinchStartThreshold = 0.72f;
    [SerializeField] private float _pinchEndThreshold = 0.45f;

    [Header("Path Drawing")]
    [SerializeField] private float _floorWorldY = 0f;
    [SerializeField] private float _sampleSpacing = 0.08f;
    [SerializeField] private int _maxPathPoints = 120;
    [SerializeField] private float _lineWidth = 0.035f;
    [SerializeField] private Color _drawingColor = new Color(0.2f, 0.95f, 1f, 0.9f);
    [SerializeField] private Color _committedColor = new Color(0.15f, 1f, 0.45f, 0.95f);

    [Header("Avatar Follow")]
    [SerializeField] private float _moveSpeed = 1.15f;
    [SerializeField] private float _turnSpeed = 540f;
    [SerializeField] private float _arrivalDistance = 0.06f;
    [SerializeField] private float _obstacleProbeRadius = 0.2f;
    [SerializeField] private LayerMask _obstacleMask = ~0;

    private readonly List<Vector3> _path = new List<Vector3>();
    private LineRenderer _lineRenderer;
    private Material _lineMaterial;
    private bool _isDrawing;
    private bool _isFollowingPath;
    private int _currentFollowIndex;

    private void Awake()
    {
        CreateLineRenderer();
    }

    private void Start()
    {
        ResolveSceneReferences();
    }

    private void Update()
    {
        ResolveSceneReferences();

        bool pinchHeld = IsPinchHeld();
        if (!_isDrawing && pinchHeld)
        {
            BeginDrawing();
        }
        else if (_isDrawing && !pinchHeld && IsPinchReleased())
        {
            CommitDrawing();
        }

        if (_isDrawing)
        {
            AddCurrentFingerPoint();
        }

        if (_isFollowingPath)
        {
            FollowCommittedPath();
        }
    }

    private void ResolveSceneReferences()
    {
        if (_avatar == null)
        {
            GameObject avatarObject = GameObject.Find("Michelle");
            if (avatarObject != null)
            {
                _avatar = avatarObject.transform;
            }
        }

        if (_drawingHand == null)
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach (OVRHand hand in hands)
            {
                if (hand != null && IsRightHand(hand))
                {
                    _drawingHand = hand;
                    break;
                }
            }

            if (_drawingHand == null && hands.Length > 0)
            {
                _drawingHand = hands[0];
            }
        }

        if (_drawingHand != null && _drawingHandSkeleton == null)
        {
            _drawingHandSkeleton = _drawingHand.GetComponent<OVRSkeleton>();
        }
    }

    private static bool IsRightHand(OVRHand hand)
    {
        if (hand.name.ToLowerInvariant().Contains("right"))
        {
            return true;
        }

        OVRSkeleton skeleton = hand.GetComponent<OVRSkeleton>();
        return skeleton != null
            && (skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.HandRight
                || skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight);
    }

    private bool IsPinchHeld()
    {
        if (_drawingHand == null || !_drawingHand.IsDataValid || !_drawingHand.IsTracked)
        {
            return false;
        }

        return _drawingHand.GetFingerIsPinching(_pinchFinger)
            || _drawingHand.GetFingerPinchStrength(_pinchFinger) >= _pinchStartThreshold;
    }

    private bool IsPinchReleased()
    {
        if (_drawingHand == null || !_drawingHand.IsDataValid)
        {
            return true;
        }

        return _drawingHand.GetFingerPinchStrength(_pinchFinger) <= _pinchEndThreshold;
    }

    private void BeginDrawing()
    {
        _isDrawing = true;
        _isFollowingPath = false;
        _currentFollowIndex = 0;
        _path.Clear();
        SetLineColor(_drawingColor);
        _lineRenderer.positionCount = 0;
        AddCurrentFingerPoint(force: true);
    }

    private void CommitDrawing()
    {
        _isDrawing = false;

        if (_path.Count < 2)
        {
            _lineRenderer.positionCount = 0;
            return;
        }

        SetLineColor(_committedColor);
        _currentFollowIndex = 0;
        _isFollowingPath = _avatar != null;
    }

    private void AddCurrentFingerPoint(bool force = false)
    {
        if (!TryGetDrawingPoint(out Vector3 point))
        {
            return;
        }

        if (!force && _path.Count > 0 && Vector3.Distance(_path[_path.Count - 1], point) < _sampleSpacing)
        {
            return;
        }

        if (_path.Count >= _maxPathPoints)
        {
            _path.RemoveAt(0);
        }

        _path.Add(point);
        _lineRenderer.positionCount = _path.Count;
        _lineRenderer.SetPositions(_path.ToArray());
    }

    private bool TryGetDrawingPoint(out Vector3 point)
    {
        if (TryGetIndexFingerTip(out Vector3 fingertip))
        {
            point = ProjectToFloor(fingertip);
            return true;
        }

        if (_drawingHand != null && _drawingHand.IsPointerPoseValid)
        {
            point = ProjectToFloor(_drawingHand.PointerPose.position + _drawingHand.PointerPose.forward * 0.35f);
            return true;
        }

        point = default;
        return false;
    }

    private bool TryGetIndexFingerTip(out Vector3 fingertip)
    {
        if (_drawingHandSkeleton == null || _drawingHandSkeleton.Bones == null)
        {
            fingertip = default;
            return false;
        }

        foreach (OVRBone bone in _drawingHandSkeleton.Bones)
        {
            if ((bone.Id == OVRSkeleton.BoneId.Hand_IndexTip
                    || bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip
                    || bone.Id == OVRSkeleton.BoneId.Hand_Index3)
                && bone.Transform != null)
            {
                fingertip = bone.Transform.position;
                return true;
            }
        }

        fingertip = default;
        return false;
    }

    private Vector3 ProjectToFloor(Vector3 worldPoint)
    {
        return new Vector3(worldPoint.x, _floorWorldY, worldPoint.z);
    }

    private void FollowCommittedPath()
    {
        if (_avatar == null || _path.Count < 2 || _currentFollowIndex >= _path.Count)
        {
            _isFollowingPath = false;
            return;
        }

        Vector3 target = _path[_currentFollowIndex];
        Vector3 avatarPosition = _avatar.position;
        Vector3 flatPosition = new Vector3(avatarPosition.x, _floorWorldY, avatarPosition.z);
        Vector3 toTarget = target - flatPosition;
        toTarget.y = 0f;

        if (toTarget.magnitude <= _arrivalDistance)
        {
            _currentFollowIndex++;
            return;
        }

        Vector3 direction = toTarget.normalized;
        if (Physics.SphereCast(
                flatPosition + Vector3.up * 0.35f,
                _obstacleProbeRadius,
                direction,
                out _,
                Mathf.Min(toTarget.magnitude, 0.35f),
                _obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            _isFollowingPath = false;
            return;
        }

        Vector3 nextPosition = Vector3.MoveTowards(flatPosition, target, _moveSpeed * Time.deltaTime);
        _avatar.position = new Vector3(nextPosition.x, avatarPosition.y, nextPosition.z);

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        _avatar.rotation = Quaternion.RotateTowards(_avatar.rotation, targetRotation, _turnSpeed * Time.deltaTime);
    }

    private void CreateLineRenderer()
    {
        GameObject lineObject = new GameObject("Pinch Drawn Path");
        lineObject.transform.SetParent(transform, false);

        _lineRenderer = lineObject.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.widthMultiplier = _lineWidth;
        _lineRenderer.numCornerVertices = 4;
        _lineRenderer.numCapVertices = 4;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;
        _lineRenderer.positionCount = 0;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        _lineMaterial = new Material(shader)
        {
            name = "Task3_Pinch_Path_Line"
        };
        _lineRenderer.sharedMaterial = _lineMaterial;
        SetLineColor(_drawingColor);
    }

    private void SetLineColor(Color color)
    {
        if (_lineMaterial != null)
        {
            _lineMaterial.color = color;
        }

        if (_lineRenderer != null)
        {
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
        }
    }
}
