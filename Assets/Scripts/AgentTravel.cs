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

    private Vector3 targetPosition;
    private bool hasTarget = false;

    void Start()
    {
        if (avatar == null)
            avatar = transform;

        if (avatarAnimator == null)
            avatarAnimator = avatar.GetComponent<Animator>();

        if (navMeshAgent == null)
            navMeshAgent = avatar.GetComponent<NavMeshAgent>();

        SetWalking(false);
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