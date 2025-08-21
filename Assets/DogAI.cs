using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// Tiny BT
public enum BTStatus { Success, Failure, Running }
public abstract class BTNode { public abstract BTStatus Tick(); }
public class Sequence : BTNode
{
    private readonly BTNode[] kids; private int i = 0;
    public Sequence(params BTNode[] kids) { this.kids = kids; }
    public override BTStatus Tick()
    {
        while (i < kids.Length)
        {
            var s = kids[i].Tick();
            if (s == BTStatus.Running) return BTStatus.Running;
            if (s == BTStatus.Failure) { i = 0; return BTStatus.Failure; }
            i++;
        }
        i = 0; return BTStatus.Success;
    }
}
public class ActionNode : BTNode
{
    private readonly System.Func<BTStatus> act;
    public ActionNode(System.Func<BTStatus> act) { this.act = act; }
    public override BTStatus Tick() => act();
}

public class DogAI : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public Transform carryPoint;        // mouth socket (blue Z forward)
    public AudioSource audioSource;
    public AudioClip thanksClip;

    [Header("Mouth attach")]
    public string potionGripName = "GripPoint";   // child on potion prefab

    [Header("Movement")]
    public float reachPotionDist = 1.2f;

    [Header("Handoff (dog stands here)")]
    public float handoffDistance = 1.2f;
    public float handoffArriveTolerance = 0.25f;

    [Header("Drop (physics straight down)")]
    [Tooltip("Small lift above mouth when releasing, avoids initial clipping")]
    public float releaseLift = 0.04f;
    [Tooltip("Ignore collisions with dog/player for this many seconds after release")]
    public float dropLeadTime  = 0.12f;
    [Tooltip("Consider settled when speed <= this")]
    public float settleSpeed   = 0.12f;
    [Tooltip("Timeout even if still moving")]
    public float maxSettleTime = 1.25f;
    [Tooltip("Physics material applied to potion at drop (choose small bounce + decent friction for natural feel)")]
    public PhysicsMaterial physicsMaterial;

    [Header("After settle (gentle stop)")]
    [Tooltip("Applied after settle so bottle doesn’t keep sliding forever")]
    public float settledDrag = 4f;
    public float settledAngularDrag = 5f;
    [Tooltip("If ON, hard-freeze after settle (not recommended if you want natural final pose)")]
    public bool freezeAfterSettle = false;

    [Header("Grounding (used for handoff point only)")]
    public float groundRayHeight = 5f;

    // Animation
    [Header("Animation")]
    public Animator animator;                 // drag Animator here (root or child)
    public string speedParam = "Speed";       // float in controller
    public string movingParam = "IsMoving";   // optional bool in controller
    public bool useRootMotion = false;        // keep false: Agent moves the dog
    [Tooltip("Meters/sec of your Run clip at 1.0x playback (for foot-slide match)")]
    public float runClipMetersPerSecond = 2.5f;

    private NavMeshAgent agent;
    private GameObject targetPotion;

    // state
    private bool hasPotion = false;
    private bool commandGiven = false;
    private bool movingToPotion = false;
    private bool movingToPlayer = false;
    private bool dropping = false;
    private bool saidThanks = false;

    private readonly List<GameObject> potions = new List<GameObject>();
    public int RemainingPotions => potions.Count;

    private BTNode tree;
    private Transform runtimeCarryPoint;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // Freeze until first command
        agent.isStopped = true;
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.ResetPath();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = useRootMotion;

        potions.AddRange(GameObject.FindGameObjectsWithTag("potion"));
        saidThanks = (potions.Count == 0);

        tree = new Sequence(
            new ActionNode(WaitForCommand),
            new ActionNode(FindNearestPotion),
            new ActionNode(MoveToPotion),
            new ActionNode(PickupPotion),
            new ActionNode(MoveToPlayerHandoff),
            new ActionNode(DropPotion),
            new ActionNode(ThanksIfDone)
        );
    }

    void Update()
    {
        tree.Tick();
        UpdateAnimator();
    }

    // --- BT actions ---
    private BTStatus WaitForCommand()
    {
        if (!commandGiven)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.updatePosition = false;
            agent.updateRotation = false;
            return BTStatus.Running;
        }
        agent.updatePosition = true;
        agent.updateRotation = true;
        agent.isStopped = false;
        return BTStatus.Success;
    }

    private BTStatus FindNearestPotion()
    {
        if (potions.Count == 0) return BTStatus.Success;

        GameObject best = null; float dBest = float.PositiveInfinity;
        foreach (var p in potions)
        {
            if (p == null) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < dBest) { dBest = d; best = p; }
        }
        if (best == null) return BTStatus.Failure;

        targetPotion = best; movingToPotion = false;
        return BTStatus.Success;
    }

    private BTStatus MoveToPotion()
    {
        if (targetPotion == null) return BTStatus.Failure;

        if (!movingToPotion)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPotion.transform.position);
            movingToPotion = true;
        }
        else agent.SetDestination(targetPotion.transform.position);

        float dist = Vector3.Distance(transform.position, targetPotion.transform.position);
        if (dist > reachPotionDist) return BTStatus.Running;

        agent.ResetPath();
        return BTStatus.Success;
    }

    private BTStatus PickupPotion()
    {
        if (targetPotion == null) return BTStatus.Failure;

        AttachPotionToMouth(targetPotion);
        hasPotion = true;
        movingToPlayer = false;
        return BTStatus.Success;
    }

    private BTStatus MoveToPlayerHandoff()
    {
        if (!hasPotion) return BTStatus.Failure;

        Vector3 handoff = GetHandoffPoint();
        if (!movingToPlayer)
        {
            agent.isStopped = false;
            agent.SetDestination(handoff);
            movingToPlayer = true;
        }
        else agent.SetDestination(handoff);

        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(handoff.x,           0, handoff.z)
        );
        if (dist > handoffArriveTolerance) return BTStatus.Running;

        agent.ResetPath();
        // face player
        Vector3 look = player.position - transform.position; look.y = 0;
        if (look != Vector3.zero) transform.rotation = Quaternion.LookRotation(look);

        return BTStatus.Success;
    }

    private BTStatus DropPotion()
    {
        if (!hasPotion || targetPotion == null) return BTStatus.Failure;

        if (!dropping)
        {
            dropping = true;

            // Detach at current mouth position; DO NOT teleport horizontally
            Transform cp = carryPoint != null ? carryPoint : GetOrMakeRuntimeCarry();
            Vector3 mouth = cp.position + cp.forward * 0.03f + Vector3.up * releaseLift; // tiny nudge forward/up
            targetPotion.transform.SetParent(null);

            StartCoroutine(PhysicsDropAndSettle(targetPotion, mouth, cp));
        }
        return dropping ? BTStatus.Running : BTStatus.Success;
    }

    private BTStatus ThanksIfDone()
    {
        if (!saidThanks && potions.Count == 0)
        {
            if (thanksClip && audioSource) audioSource.PlayOneShot(thanksClip);
            else Debug.Log("Thanks Carl!");
            saidThanks = true;
        }
        return BTStatus.Success;
    }

    // --- Animator driving ---
    private void UpdateAnimator()
    {
        if (animator == null) return;

        animator.applyRootMotion = useRootMotion;

        float speed = agent != null ? agent.velocity.magnitude : 0f;

        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, speed);

        bool moving = speed > 0.1f && (movingToPotion || movingToPlayer);
        if (!string.IsNullOrEmpty(movingParam))
            animator.SetBool(movingParam, moving);

        if (!useRootMotion)
        {
            if (moving && runClipMetersPerSecond > 0.01f)
            {
                float k = Mathf.Clamp(speed / runClipMetersPerSecond, 0.6f, 1.6f);
                animator.speed = k;
            }
            else
            {
                animator.speed = 1f;
            }
        }
        else
        {
            animator.speed = 1f;
        }
    }

    // --- Mouth attach ---
    private void AttachPotionToMouth(GameObject potion)
    {
        var col = potion.GetComponent<Collider>();
        var rb  = potion.GetComponent<Rigidbody>();
        if (col) col.enabled = false;
        if (rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Transform cp = carryPoint != null ? carryPoint : GetOrMakeRuntimeCarry();

        Transform grip = FindDeepChildByName(potion.transform, potionGripName);
        if (grip == null)
        {
            potion.transform.SetParent(cp, worldPositionStays: false);
            potion.transform.localPosition = Vector3.zero;
            potion.transform.localRotation = Quaternion.identity;
            return;
        }

        Vector3 gripLocalPosToRoot = potion.transform.InverseTransformPoint(grip.position);
        Quaternion gripLocalRotToRoot = Quaternion.Inverse(potion.transform.rotation) * grip.rotation;

        Quaternion rootWorldRot = cp.rotation * Quaternion.Inverse(gripLocalRotToRoot);
        Vector3 rootWorldPos = cp.position - (rootWorldRot * gripLocalPosToRoot);

        potion.transform.SetPositionAndRotation(rootWorldPos, rootWorldRot);
        potion.transform.SetParent(cp, worldPositionStays: true);
    }

    private Transform GetOrMakeRuntimeCarry()
    {
        if (runtimeCarryPoint == null)
        {
            runtimeCarryPoint = new GameObject("CarryPointRuntime").transform;
            runtimeCarryPoint.SetParent(transform);
            runtimeCarryPoint.localPosition = new Vector3(0f, 0.9f, 0.5f);
            runtimeCarryPoint.localRotation = Quaternion.identity;
        }
        return runtimeCarryPoint;
    }

    private Transform FindDeepChildByName(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var t = FindDeepChildByName(root.GetChild(i), name);
            if (t != null) return t;
        }
        return null;
    }

    // --- Helpers ---
    private Vector3 GetHandoffPoint()
    {
        Vector3 p = player.position - player.forward.normalized * handoffDistance;
        if (Physics.Raycast(p + Vector3.up * groundRayHeight, Vector3.down, out var hit, groundRayHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
            p.y = hit.point.y;
        return p;
    }

    // ---------- PHYSICS DROP ----------
    private IEnumerator PhysicsDropAndSettle(GameObject bottle, Vector3 start, Transform mouth)
    {
        var rb  = bottle.GetComponent<Rigidbody>();
        var col = bottle.GetComponent<Collider>();
        if (!rb)  rb  = bottle.AddComponent<Rigidbody>();
        if (!col) col = bottle.AddComponent<SphereCollider>(); // fallback

        // start at mouth (no horizontal teleport)
        bottle.transform.position = start;

        // apply chosen physics material (for natural bounce/ friction)
        if (physicsMaterial != null) col.sharedMaterial = physicsMaterial;
        col.isTrigger = false;
        col.enabled = true;

        // release to physics
        rb.constraints = RigidbodyConstraints.None;
        rb.isKinematic = false;
        rb.useGravity  = true;
        rb.detectCollisions = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.drag = 0.0f;
        rb.angularDrag = 0.05f;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();

        // temporarily ignore dog & player to prevent clipping on release
        var dogCols = GetComponentsInChildren<Collider>();
        var plyCols = player.GetComponentsInChildren<Collider>();
        foreach (var dc in dogCols) if (dc) Physics.IgnoreCollision(col, dc, true);
        foreach (var pc in plyCols) if (pc) Physics.IgnoreCollision(col, pc, true);

        yield return new WaitForSeconds(dropLeadTime);

        foreach (var dc in dogCols) if (dc) Physics.IgnoreCollision(col, dc, false);
        foreach (var pc in plyCols) if (pc) Physics.IgnoreCollision(col, pc, false);

        // wait to settle (or timeout)
        float t = 0f;
        while (t < maxSettleTime)
        {
            if (rb.velocity.magnitude <= settleSpeed && rb.angularVelocity.magnitude <= settleSpeed * 2f)
                break;
            t += Time.deltaTime;
            yield return null;
        }

        // gentle stop (not a hard snap/freeze)
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (freezeAfterSettle)
        {
            rb.isKinematic = true;
            rb.useGravity  = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }
        else
        {
            // keep physics enabled but damp further movement
            rb.drag = settledDrag;
            rb.angularDrag = settledAngularDrag;
            rb.isKinematic = false;
            rb.useGravity  = true;
            rb.constraints = RigidbodyConstraints.None;
        }

        // state cleanup
        if (potions.Contains(bottle)) potions.Remove(bottle);
        hasPotion = false; targetPotion = null; commandGiven = false; dropping = false;

        if (!saidThanks && potions.Count == 0)
        {
            if (thanksClip && audioSource) audioSource.PlayOneShot(thanksClip);
            else Debug.Log("Thanks Carl!");
            saidThanks = true;
        }
    }

    // Public API
    public void GiveCommand(float delay = 0.5f)
    {
        if (commandGiven || hasPotion || potions.Count == 0) return;
        StartCoroutine(DelayedCommand(delay));
    }

    private IEnumerator DelayedCommand(float delay)
    {
        yield return new WaitForSeconds(delay);
        agent.updatePosition = true; agent.updateRotation = true; agent.isStopped = false;
        commandGiven = true;
    }

    public void TriggerNextPickup() => GiveCommand(0.5f);
}