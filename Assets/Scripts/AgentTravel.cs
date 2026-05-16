using System;
using UnityEngine;
using UnityEngine.AI;

public class AgentTravel : MonoBehaviour
{
    private const string FootHighlightName = "RuntimeAvatarFootGroundHighlight";

    [Header("Agent")]
    public Transform avatar;
    public Animator avatarAnimator;

    [Header("Movement Without NavMesh")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 5f;
    public float stopDistance = 0.3f;

    [Header("NavMesh")]
    public bool useNavMesh = false;
    public NavMeshAgent navMeshAgent;

    [Header("Ground Collision")]
    public bool keepAvatarOnGround = true;
    public LayerMask groundLayers = ~0;
    public float groundProbeHeight = 2f;
    public float groundProbeDistance = 6f;
    public float groundOffset = 0.01f;

    [Header("Feet Ground Highlight")]
    public bool showFootGroundHighlight = true;
    public float footHighlightRadius = 0.35f;
    public Color footHighlightColor = new Color(0.2f, 0.95f, 1f, 0.68f);

    public event Action DestinationReached;

    private Vector3 targetPosition;
    private bool hasTarget = false;
    private GameObject footGroundHighlight;
    private Material footGroundHighlightMaterial;

    void Start()
    {
        if (avatar == null)
            avatar = transform;

        if (avatarAnimator == null)
            avatarAnimator = avatar.GetComponent<Animator>();

        if (navMeshAgent == null)
            navMeshAgent = avatar.GetComponent<NavMeshAgent>();

        SetWalking(false);
        EnsureFootGroundHighlight();
        SnapAvatarToGround();
    }

    void Update()
    {
        if (useNavMesh && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            HandleNavMeshMovement();
        }
        else
        {
            HandleSimpleMovement();
        }

        UpdateFootGroundHighlight();
    }

    public void SetDestination(Vector3 destination)
    {
        targetPosition = destination;
        hasTarget = true;

        if (useNavMesh && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.SetDestination(destination);
        }
    }

    private void HandleSimpleMovement()
    {
        if (!hasTarget || avatar == null)
        {
            SetWalking(false);
            return;
        }

        Vector3 flatTarget = new Vector3(
            targetPosition.x,
            avatar.position.y,
            targetPosition.z
        );

        Vector3 direction = flatTarget - avatar.position;

        if (direction.magnitude <= stopDistance)
        {
            hasTarget = false;
            SetWalking(false);
            DestinationReached?.Invoke();
            return;
        }

        SetWalking(true);

        Vector3 nextPosition = avatar.position + direction.normalized * moveSpeed * Time.deltaTime;
        avatar.position = ProjectOntoGround(nextPosition);

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up);

            avatar.rotation = Quaternion.Slerp(
                avatar.rotation,
                targetRotation,
                Time.deltaTime * rotationSpeed
            );
        }
    }

    private void HandleNavMeshMovement()
    {
        if (!hasTarget)
        {
            SetWalking(false);
            return;
        }

        bool reachedDestination =
            !navMeshAgent.pathPending &&
            navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance;

        if (reachedDestination)
        {
            hasTarget = false;
            SetWalking(false);
            DestinationReached?.Invoke();
            return;
        }

        bool isWalking = navMeshAgent.velocity.magnitude > 0.05f;
        SetWalking(isWalking);
    }

    private void SetWalking(bool walking)
    {
        if (avatarAnimator != null)
        {
            avatarAnimator.SetBool("Walking", walking);
        }
    }

    private void SnapAvatarToGround()
    {
        if (avatar != null)
        {
            avatar.position = ProjectOntoGround(avatar.position);
        }
    }

    private Vector3 ProjectOntoGround(Vector3 position)
    {
        if (!keepAvatarOnGround)
        {
            return position;
        }

        if (TryFindGround(position, out RaycastHit hit))
        {
            position.y = hit.point.y + groundOffset;
        }

        return position;
    }

    private bool TryFindGround(Vector3 aroundPosition, out RaycastHit hit)
    {
        Vector3 rayOrigin = aroundPosition + Vector3.up * Mathf.Max(0.01f, groundProbeHeight);
        float rayDistance = Mathf.Max(0.01f, groundProbeHeight + groundProbeDistance);
        LayerMask raycastMask = groundLayers.value == 0 ? Physics.DefaultRaycastLayers : groundLayers;

        return Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out hit,
            rayDistance,
            raycastMask,
            QueryTriggerInteraction.Ignore);
    }

    private void EnsureFootGroundHighlight()
    {
        if (!showFootGroundHighlight || footGroundHighlight != null)
        {
            return;
        }

        footGroundHighlight = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        footGroundHighlight.name = FootHighlightName;

        Collider highlightCollider = footGroundHighlight.GetComponent<Collider>();
        if (highlightCollider != null)
        {
            Destroy(highlightCollider);
        }

        Renderer highlightRenderer = footGroundHighlight.GetComponent<Renderer>();
        if (highlightRenderer != null)
        {
            highlightRenderer.sharedMaterial = GetFootGroundHighlightMaterial();
        }
    }

    private void UpdateFootGroundHighlight()
    {
        if (!showFootGroundHighlight)
        {
            if (footGroundHighlight != null)
            {
                footGroundHighlight.SetActive(false);
            }

            return;
        }

        EnsureFootGroundHighlight();

        if (avatar == null || footGroundHighlight == null)
        {
            return;
        }

        Vector3 highlightPosition = avatar.position;
        if (TryFindGround(avatar.position, out RaycastHit hit))
        {
            highlightPosition = hit.point;
        }

        highlightPosition.y += 0.012f;
        footGroundHighlight.SetActive(true);
        footGroundHighlight.transform.SetPositionAndRotation(highlightPosition, Quaternion.identity);
        footGroundHighlight.transform.localScale = new Vector3(
            Mathf.Max(0.01f, footHighlightRadius * 2f),
            0.004f,
            Mathf.Max(0.01f, footHighlightRadius * 2f));
    }

    private Material GetFootGroundHighlightMaterial()
    {
        if (footGroundHighlightMaterial != null)
        {
            return footGroundHighlightMaterial;
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        footGroundHighlightMaterial = new Material(shader)
        {
            name = "Avatar_Feet_Ground_Shine"
        };

        footGroundHighlightMaterial.color = footHighlightColor;
        SetMaterialFloat("_Mode", 3f);
        SetMaterialFloat("_Surface", 1f);
        SetMaterialFloat("_Metallic", 0.65f);
        SetMaterialFloat("_Smoothness", 0.95f);
        SetMaterialFloat("_Glossiness", 0.95f);
        SetMaterialFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetMaterialFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetMaterialFloat("_ZWrite", 0f);
        footGroundHighlightMaterial.EnableKeyword("_ALPHABLEND_ON");
        footGroundHighlightMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        footGroundHighlightMaterial.renderQueue = 3001;
        return footGroundHighlightMaterial;
    }

    private void SetMaterialFloat(string propertyName, float value)
    {
        if (footGroundHighlightMaterial != null && footGroundHighlightMaterial.HasProperty(propertyName))
        {
            footGroundHighlightMaterial.SetFloat(propertyName, value);
        }
    }
}
