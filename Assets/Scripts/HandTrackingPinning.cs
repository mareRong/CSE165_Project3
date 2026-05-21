using System.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using TMPro;

public class HandTrackingPinning : MonoBehaviour
{
    private const string RuntimeRayName = "RuntimeCurvedRay";
    private const string RuntimePreviewName = "RuntimePinPreview";
    private const string RuntimePinName = "RuntimePin";
    private const string SurfaceLayerName = "Surface";

    [Header("Travel Script")]
    public AgentTravel agentTravel;

    [Header("Pinning Ray")]
    public LayerMask groundLayer;
    public LineRenderer curvedRay;
    public GameObject pinPreviewCircle;
    public GameObject pinPrefab;

    [Header("Gesture Settings")]
    public float fistThreshold = 0.075f;
    public float thumbHeightThreshold = 0.08f;
    public float pinchThreshold = 0.035f;

    [Header("Ray Settings")]
    public float rayForwardDistance = 3.5f;
    public float rayCurveHeight = 0.6f;
    public float rayVerticalDrop = 0.8f;
    public int raySegments = 24;
    public float rayWidth = 0.01f;
    public Color rayColor = new Color(0.2f, 0.85f, 1f, 0.95f);

    [Header("Preview Pulse Animation")]
    public float previewPulseSpeed = 3f;
    public float minPreviewAlpha = 0.25f;
    public float maxPreviewAlpha = 1f;

    [Header("Generated Pin Visuals")]
    public float previewCircleRadius = 0.12f;
    public float previewCircleThickness = 0.005f;
    public Color previewColor = new Color(0.15f, 0.75f, 1f, 0.75f);
    public float pinStemHeight = 0.18f;
    public float pinStemRadius = 0.012f;
    public float pinHeadRadius = 0.04f;
    public Color pinColor = new Color(1f, 0.25f, 0.2f, 1f);
    public float pinArrivalDistance = 0.32f;

    [Header("UI Message")]
    public TextMeshProUGUI statusText;
    public float statusMessageDuration = 3f;

    private XRHandSubsystem handSubsystem;

    private bool isPinningMode = false;
    private bool wasPinching = false;

    private Vector3 currentRayEndPoint;
    private Vector3 currentRayHitNormal = Vector3.up;
    private bool hasValidRayHit = false;

    private GameObject currentPin;
    private AgentTravel subscribedAgentTravel;
    private Coroutine statusCoroutine;

    void Start()
    {
        TryInitializeAgentTravel();
        TryInitializeHands();
        HidePinningVisuals();

        if (statusText != null)
            statusText.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (subscribedAgentTravel != null)
        {
            subscribedAgentTravel.DestinationReached -= HandleDestinationReached;
            subscribedAgentTravel = null;
        }
    }

    void Update()
    {
        if (agentTravel == null)
        {
            TryInitializeAgentTravel();
        }

        if (currentPin != null && agentTravel != null && !agentTravel.HasActiveDestination)
        {
            ClearCurrentPin();
        }

        if (currentPin != null &&
            agentTravel != null &&
            HasAvatarReachedPinRange() &&
            agentTravel.IsWithinWallThresholdForPoint(currentPin.transform.position))
        {
            agentTravel.CompleteDestinationIfActive();
            ClearCurrentPin();
        }

        if (handSubsystem == null)
        {
            TryInitializeHands();
            return;
        }

        XRHand rightHand = handSubsystem.rightHand;

        if (!rightHand.isTracked)
        {
            HidePinningVisuals();
            return;
        }

        bool thumbsUp = IsRightThumbsUp(rightHand);
        bool thumbsDown = IsRightThumbsDown(rightHand);
        bool isPinching = IsRightPinching(rightHand);

        if (thumbsUp && !isPinningMode)
        {
            isPinningMode = true;
            ShowStatusMessage("Pinning Mode Activated");
        }

        if (thumbsDown && isPinningMode)
        {
            isPinningMode = false;
            HidePinningVisuals();
            ShowStatusMessage("Pinning Mode Cancelled");
        }

        if (isPinningMode)
        {
            if (isPinching)
            {
                UpdateCurvedRay(rightHand);
            }
            else if (!wasPinching)
            {
                HidePinningVisuals();
            }

            if (wasPinching && !isPinching)
            {
                PlacePin();
            }
        }

        wasPinching = isPinching;
    }

    private bool IsRightThumbsUp(XRHand hand)
    {
        if (!TryGetPalmPose(hand, out Pose palmPose))
            return false;

        if (!IsFistWithoutThumb(hand, palmPose.position))
            return false;

        float thumbOffset = GetThumbVerticalOffset(hand, palmPose.position);

        return thumbOffset > thumbHeightThreshold;
    }

    private bool IsRightThumbsDown(XRHand hand)
    {
        if (!TryGetPalmPose(hand, out Pose palmPose))
            return false;

        if (!IsFistWithoutThumb(hand, palmPose.position))
            return false;

        float thumbOffset = GetThumbVerticalOffset(hand, palmPose.position);

        return thumbOffset < -thumbHeightThreshold;
    }

    private bool IsRightPinching(XRHand hand)
    {
        XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!indexTip.TryGetPose(out Pose indexPose))
            return false;

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return false;

        return Vector3.Distance(indexPose.position, thumbPose.position) < pinchThreshold;
    }

    private bool IsFistWithoutThumb(XRHand hand, Vector3 palmPosition)
    {
        XRHandJointID[] fingerTips =
        {
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip
        };

        int curledCount = 0;

        foreach (XRHandJointID jointID in fingerTips)
        {
            XRHandJoint joint = hand.GetJoint(jointID);

            if (joint.TryGetPose(out Pose tipPose))
            {
                float distance = Vector3.Distance(tipPose.position, palmPosition);

                if (distance < fistThreshold)
                    curledCount++;
            }
        }

        return curledCount >= 4;
    }

    private float GetThumbVerticalOffset(XRHand hand, Vector3 palmPosition)
    {
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (!thumbTip.TryGetPose(out Pose thumbPose))
            return 0f;

        return thumbPose.position.y - palmPosition.y;
    }

    private void UpdateCurvedRay(XRHand hand)
    {
        if (!TryGetJointPose(hand, XRHandJointID.Wrist, out Pose wristPose))
            return;

        if (!TryGetJointPose(hand, XRHandJointID.IndexTip, out Pose indexPose))
            return;

        EnsureCurvedRay();

        Vector3 startPoint = wristPose.position;
        Vector3 forward = indexPose.position - wristPose.position;

        if (forward.sqrMagnitude < 0.0001f)
            return;

        forward.Normalize();

        Vector3 endPoint = startPoint + forward * rayForwardDistance;
        endPoint.y -= rayVerticalDrop;

        Vector3[] points = new Vector3[raySegments];
        hasValidRayHit = false;
        LayerMask raycastMask = ResolveGroundRaycastMask();

        for (int i = 0; i < raySegments; i++)
        {
            float t = i / (float)(raySegments - 1);

            Vector3 point = Vector3.Lerp(startPoint, endPoint, t);
            point.y += Mathf.Sin(t * Mathf.PI) * rayCurveHeight;

            points[i] = point;

            if (i > 0)
            {
                Vector3 previousPoint = points[i - 1];
                Vector3 direction = point - previousPoint;
                float distance = direction.magnitude;

                if (TryRaycastGroundSegment(previousPoint, direction.normalized, distance, raycastMask, out RaycastHit hit))
                {
                    currentRayEndPoint = hit.point;
                    currentRayHitNormal = ResolveGroundNormal(hit.normal);
                    hasValidRayHit = true;

                    for (int j = i; j < raySegments; j++)
                    {
                        points[j] = hit.point;
                    }

                    break;
                }
            }
        }

        if (curvedRay != null)
        {
            curvedRay.enabled = true;
            curvedRay.positionCount = raySegments;
            curvedRay.SetPositions(points);
        }

        EnsurePreviewCircle();

        if (pinPreviewCircle != null && hasValidRayHit)
        {
            pinPreviewCircle.SetActive(true);
            pinPreviewCircle.transform.position = currentRayEndPoint + currentRayHitNormal * 0.002f;
            pinPreviewCircle.transform.rotation = Quaternion.FromToRotation(Vector3.up, currentRayHitNormal);

            PulsePreviewCircle();
        }
    }

    private LayerMask ResolveGroundRaycastMask()
    {
        int mask = groundLayer.value == 0 ? Physics.DefaultRaycastLayers : groundLayer.value;
        int surfaceLayer = LayerMask.NameToLayer(SurfaceLayerName);
        if (surfaceLayer >= 0)
        {
            mask |= 1 << surfaceLayer;
        }

        return mask;
    }

    private static bool TryRaycastGroundSegment(
        Vector3 origin,
        Vector3 direction,
        float distance,
        LayerMask raycastMask,
        out RaycastHit bestHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            raycastMask,
            QueryTriggerInteraction.Ignore);

        bestHit = new RaycastHit();
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || !IsGroundHit(hit))
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        return bestDistance < float.PositiveInfinity;
    }

    private static bool IsGroundHit(RaycastHit hit)
    {
        SpatialSurfaceMarker marker = hit.collider.GetComponentInParent<SpatialSurfaceMarker>();
        if (marker != null)
        {
            return marker.SurfaceKind == SpatialSurfaceAnchorManager.SurfaceKind.Floor;
        }

        return hit.normal.y > 0.5f;
    }

    private static Vector3 ResolveGroundNormal(Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.001f)
        {
            return Vector3.up;
        }

        normal.Normalize();
        if (normal.y < 0f)
        {
            normal = -normal;
        }

        return normal;
    }

    private void PulsePreviewCircle()
    {
        Renderer circleRenderer = pinPreviewCircle.GetComponent<Renderer>();

        if (circleRenderer == null)
            return;

        Color color = circleRenderer.material.color;

        float alpha = Mathf.Lerp(
            minPreviewAlpha,
            maxPreviewAlpha,
            (Mathf.Sin(Time.time * previewPulseSpeed) + 1f) * 0.5f
        );

        color.a = alpha;
        circleRenderer.material.color = color;
    }

    private void PlacePin()
    {
        if (!hasValidRayHit)
            return;

        Vector3 pinDestination = currentRayEndPoint;
        if (agentTravel != null)
        {
            pinDestination = agentTravel.ResolvePinnedDestination(currentRayEndPoint);
        }

        if (currentPin != null)
            Destroy(currentPin);

        if (pinPrefab != null)
        {
            currentPin = Instantiate(pinPrefab, pinDestination, Quaternion.identity);
        }
        else
        {
            currentPin = CreateFallbackPin(pinDestination);
        }

        if (agentTravel != null)
            agentTravel.SetDestination(pinDestination);

        isPinningMode = false;
        HidePinningVisuals();
        ShowStatusMessage("Pin Placed");
    }

    private void HidePinningVisuals()
    {
        if (curvedRay != null)
            curvedRay.enabled = false;

        if (pinPreviewCircle != null)
            pinPreviewCircle.SetActive(false);

        hasValidRayHit = false;
    }

    private void ShowStatusMessage(string message)
    {
        if (statusText == null)
            return;

        if (statusCoroutine != null)
            StopCoroutine(statusCoroutine);

        statusCoroutine = StartCoroutine(StatusMessageRoutine(message));
    }

    private IEnumerator StatusMessageRoutine(string message)
    {
        statusText.gameObject.SetActive(true);
        statusText.text = message;

        yield return new WaitForSeconds(statusMessageDuration);

        statusText.gameObject.SetActive(false);
    }

    private void TryInitializeHands()
    {
        var manager = XRGeneralSettings.Instance?.Manager;

        if (manager?.activeLoader == null)
            return;

        handSubsystem = manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
    }

    private void TryInitializeAgentTravel()
    {
        AgentTravel resolvedAgentTravel = agentTravel != null ? agentTravel : FindAnyObjectByType<AgentTravel>();
        if (resolvedAgentTravel == subscribedAgentTravel)
        {
            agentTravel = resolvedAgentTravel;
            return;
        }

        if (subscribedAgentTravel != null)
        {
            subscribedAgentTravel.DestinationReached -= HandleDestinationReached;
        }

        agentTravel = resolvedAgentTravel;
        subscribedAgentTravel = resolvedAgentTravel;

        if (subscribedAgentTravel != null)
        {
            subscribedAgentTravel.DestinationReached += HandleDestinationReached;
        }
    }

    private void HandleDestinationReached()
    {
        ClearCurrentPin();
    }

    private void ClearCurrentPin()
    {
        if (currentPin == null)
        {
            return;
        }

        Destroy(currentPin);
        currentPin = null;
    }

    private bool HasAvatarReachedPinRange()
    {
        if (currentPin == null || agentTravel == null || agentTravel.avatar == null)
        {
            return false;
        }

        Vector3 avatarPosition = agentTravel.avatar.position;
        Vector3 pinPosition = currentPin.transform.position;
        avatarPosition.y = 0f;
        pinPosition.y = 0f;

        return Vector3.Distance(avatarPosition, pinPosition) <= Mathf.Max(0.01f, pinArrivalDistance);
    }

    private bool TryGetPalmPose(XRHand hand, out Pose pose)
    {
        XRHandJoint palm = hand.GetJoint(XRHandJointID.Palm);
        return palm.TryGetPose(out pose);
    }

    private bool TryGetJointPose(XRHand hand, XRHandJointID jointID, out Pose pose)
    {
        XRHandJoint joint = hand.GetJoint(jointID);
        return joint.TryGetPose(out pose);
    }

    private void EnsurePreviewCircle()
    {
        if (pinPreviewCircle != null)
            return;

        pinPreviewCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pinPreviewCircle.name = RuntimePreviewName;
        pinPreviewCircle.transform.SetParent(transform, false);
        pinPreviewCircle.transform.localScale = new Vector3(
            previewCircleRadius * 2f,
            previewCircleThickness,
            previewCircleRadius * 2f
        );

        ConfigurePrimitive(pinPreviewCircle, previewColor);
        pinPreviewCircle.SetActive(false);
    }

    private void EnsureCurvedRay()
    {
        if (curvedRay != null)
            return;

        GameObject rayObject = new GameObject(RuntimeRayName);
        rayObject.transform.SetParent(transform, false);

        curvedRay = rayObject.AddComponent<LineRenderer>();
        curvedRay.enabled = false;
        curvedRay.useWorldSpace = true;
        curvedRay.positionCount = 0;
        curvedRay.widthMultiplier = rayWidth;
        curvedRay.numCapVertices = 6;
        curvedRay.numCornerVertices = 4;
        curvedRay.material = new Material(Shader.Find("Sprites/Default"));
        curvedRay.startColor = rayColor;
        curvedRay.endColor = rayColor;
    }

    private GameObject CreateFallbackPin(Vector3 pinPosition)
    {
        GameObject pinRoot = new GameObject(RuntimePinName);
        pinRoot.transform.position = pinPosition;

        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.name = "Stem";
        stem.transform.SetParent(pinRoot.transform, false);
        stem.transform.localPosition = new Vector3(0f, pinStemHeight * 0.5f, 0f);
        stem.transform.localScale = new Vector3(pinStemRadius * 2f, pinStemHeight * 0.5f, pinStemRadius * 2f);
        ConfigurePrimitive(stem, pinColor);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(pinRoot.transform, false);
        head.transform.localPosition = new Vector3(0f, pinStemHeight, 0f);
        head.transform.localScale = Vector3.one * (pinHeadRadius * 2f);
        ConfigurePrimitive(head, pinColor);

        return pinRoot;
    }

    private void ConfigurePrimitive(GameObject primitive, Color color)
    {
        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        Renderer primitiveRenderer = primitive.GetComponent<Renderer>();
        if (primitiveRenderer == null)
            return;

        Material materialInstance = primitiveRenderer.material;
        materialInstance.color = color;
    }
}
