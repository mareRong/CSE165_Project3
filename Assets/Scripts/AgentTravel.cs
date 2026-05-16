using System;
using UnityEngine;
using UnityEngine.AI;

public class AgentTravel : MonoBehaviour
{
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

    public event Action DestinationReached;

    private Vector3 targetPosition;
    private bool hasTarget = false;

    void Start()
    {
        if (avatar == null)
            avatar = transform;

        KeepAvatarInWorldSpace();

        if (avatarAnimator == null)
            avatarAnimator = avatar.GetComponent<Animator>();

        if (navMeshAgent == null)
            navMeshAgent = avatar.GetComponent<NavMeshAgent>();

        SetWalking(false);
    }

    void Update()
    {
        KeepAvatarInWorldSpace();

        if (useNavMesh && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            HandleNavMeshMovement();
        }
        else
        {
            HandleSimpleMovement();
        }
    }

    public void SetDestination(Vector3 destination)
    {
        KeepAvatarInWorldSpace();

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

        avatar.position += direction.normalized * moveSpeed * Time.deltaTime;

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

    private void KeepAvatarInWorldSpace()
    {
        if (avatar == null)
        {
            return;
        }

        if (avatar.parent == null || !IsTrackingRigTransform(avatar.parent))
        {
            return;
        }

        avatar.SetParent(null, true);
    }

    private static bool IsTrackingRigTransform(Transform candidate)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null
            && (candidate == mainCamera.transform
                || candidate.IsChildOf(mainCamera.transform)
                || mainCamera.transform.IsChildOf(candidate)))
        {
            return true;
        }

        while (candidate != null)
        {
            string objectName = candidate.name;
            if (objectName.Contains("Camera Rig")
                || objectName.Contains("TrackingSpace")
                || objectName.Contains("EyeAnchor")
                || objectName.Contains("HandAnchor")
                || objectName.Contains("ControllerAnchor"))
            {
                return true;
            }

            candidate = candidate.parent;
        }

        return false;
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
}
