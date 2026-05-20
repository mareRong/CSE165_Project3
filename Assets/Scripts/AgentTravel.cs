using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Animator))]
public class AgentTravel : MonoBehaviour
{
    private static readonly int WalkingParameter = Animator.StringToHash("Walking");

    [Header("Agent")]
    public Transform avatar;
    public Animator avatarAnimator;
    public NavMeshAgent navMeshAgent;

    [Header("Runtime NavMesh")]
    public bool useNavMesh = true;
    public NavMeshSurface navMeshSurface;
    public float bakeDelay = 2f;

    [Header("Movement")]
    public float moveSpeed = 1.5f;
    public float rotationSpeed = 5f;
    public float stopDistance = 0.3f;

    [Header("Ground / Feet")]
    public LayerMask groundLayers = ~0;
    public float groundProbeHeight = 2f;
    public float groundProbeDistance = 6f;
    public float groundOffset = 0.01f;

    [Header("Foot IK")]
    public bool useFootIK = true;
    public float footRaycastHeight = 0.5f;
    public float footRaycastDistance = 1.5f;
    public float footOffset = 0.02f;
    [Range(0f, 1f)] public float footPositionWeight = 1f;
    [Range(0f, 1f)] public float footRotationWeight = 0.6f;

    public event Action DestinationReached;

    private Vector3 targetPosition;
    private bool hasTarget;
    private Animator[] animators = Array.Empty<Animator>();

    private IEnumerator Start()
    {
        if (avatar == null)
        {
            avatar = transform;
        }

        if (avatarAnimator == null)
        {
            avatarAnimator = GetComponent<Animator>();
        }

        CacheAnimators();

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        navMeshAgent.speed = moveSpeed;
        navMeshAgent.angularSpeed = rotationSpeed * 120f;
        navMeshAgent.stoppingDistance = stopDistance;
        navMeshAgent.updatePosition = true;
        navMeshAgent.updateRotation = true;

        SetWalking(false);

        yield return new WaitForSeconds(bakeDelay);

        BakeNavMesh();
        SnapAvatarToNavMesh();
    }

    private void Update()
    {
        if (useNavMesh && navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            HandleNavMeshMovement();
        }
        else
        {
            HandleSimpleMovement();
        }
    }

    public void BakeNavMesh()
    {
        if (navMeshSurface == null)
        {
            navMeshSurface = FindAnyObjectByType<NavMeshSurface>();
        }

        if (navMeshSurface == null)
        {
            GameObject surfaceObject = new GameObject("Runtime NavMesh Surface");
            navMeshSurface = surfaceObject.AddComponent<NavMeshSurface>();
        }

        navMeshSurface.collectObjects = CollectObjects.All;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navMeshSurface.BuildNavMesh();

        Debug.Log("Runtime NavMesh baked from spatial anchor colliders.", this);
    }

    public void SetDestination(Vector3 destination)
    {
        targetPosition = destination;
        hasTarget = true;

        if (useNavMesh && navMeshAgent != null)
        {
            if (!navMeshAgent.isOnNavMesh)
            {
                SnapAvatarToNavMesh();
            }

            if (navMeshAgent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    navMeshAgent.SetDestination(hit.position);
                }
                else
                {
                    navMeshAgent.SetDestination(destination);
                }
            }
        }
    }

    private void HandleNavMeshMovement()
    {
        if (!hasTarget)
        {
            SetWalking(false);
            return;
        }

        if (!navMeshAgent.pathPending &&
            navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
        {
            hasTarget = false;
            SetWalking(false);
            DestinationReached?.Invoke();
            return;
        }

        SetWalking(navMeshAgent.velocity.magnitude > 0.05f);
    }

    private void HandleSimpleMovement()
    {
        if (!hasTarget || avatar == null)
        {
            SetWalking(false);
            return;
        }

        Vector3 flatTarget = new Vector3(targetPosition.x, avatar.position.y, targetPosition.z);
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
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            avatar.rotation = Quaternion.Slerp(
                avatar.rotation,
                targetRotation,
                Time.deltaTime * rotationSpeed
            );
        }
    }

    private void SnapAvatarToNavMesh()
    {
        if (avatar == null)
        {
            return;
        }

        if (NavMesh.SamplePosition(avatar.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            avatar.position = hit.position;

            if (navMeshAgent != null)
            {
                navMeshAgent.Warp(hit.position);
            }
        }
        else
        {
            avatar.position = ProjectOntoGround(avatar.position);
        }
    }

    private Vector3 ProjectOntoGround(Vector3 position)
    {
        if (TryFindGround(position, out RaycastHit hit))
        {
            position.y = hit.point.y + groundOffset;
        }

        return position;
    }

    private bool TryFindGround(Vector3 aroundPosition, out RaycastHit hit)
    {
        Vector3 rayOrigin = aroundPosition + Vector3.up * groundProbeHeight;
        float rayDistance = groundProbeHeight + groundProbeDistance;

        return Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out hit,
            rayDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private void CacheAnimators()
    {
        Transform searchRoot = avatar != null ? avatar : transform;
        animators = searchRoot.GetComponentsInChildren<Animator>(true);

        if ((avatarAnimator == null || avatarAnimator.runtimeAnimatorController == null) && animators.Length > 0)
        {
            avatarAnimator = animators[0];
        }
    }

    private void SetWalking(bool walking)
    {
        if (animators == null || animators.Length == 0)
        {
            CacheAnimators();
        }

        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null || animator.runtimeAnimatorController == null || !animator.HasParameterOfType(WalkingParameter, AnimatorControllerParameterType.Bool))
            {
                continue;
            }

            animator.SetBool(WalkingParameter, walking);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!useFootIK || avatarAnimator == null)
        {
            return;
        }

        StickFootToGround(AvatarIKGoal.LeftFoot);
        StickFootToGround(AvatarIKGoal.RightFoot);
    }

    private void StickFootToGround(AvatarIKGoal foot)
    {
        avatarAnimator.SetIKPositionWeight(foot, footPositionWeight);
        avatarAnimator.SetIKRotationWeight(foot, footRotationWeight);

        Vector3 footPosition = avatarAnimator.GetIKPosition(foot);
        Vector3 rayOrigin = footPosition + Vector3.up * footRaycastHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                footRaycastDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore))
        {
            avatarAnimator.SetIKPosition(foot, hit.point + Vector3.up * footOffset);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion footRotation = Quaternion.LookRotation(forward, hit.normal);
                avatarAnimator.SetIKRotation(foot, footRotation);
            }
        }
    }
}

internal static class AnimatorExtensions
{
    public static bool HasParameterOfType(this Animator animator, int parameterHash, AnimatorControllerParameterType parameterType)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == parameterHash && parameter.type == parameterType)
            {
                return true;
            }
        }

        return false;
    }
}
