using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PotionStopper : MonoBehaviour
{
    [Tooltip("Only freeze when colliding with objects that have this tag.")]
    public string freezeOnTag = "Ground";

    [Tooltip("Delay before we allow freezing (lets the toss clear the dog).")]
    public float armAfterSeconds = 0.15f;

    private bool armed;

    void OnEnable()
    {
        // Default: arm after a short delay
        ArmAfter(armAfterSeconds);
    }

    // ── Called by DogAI when the dog picks up the potion ──
    public void DisarmForCarry()
    {
        armed = false;
        CancelInvoke();
    }

    // ── Called by DogAI right before/when the toss starts ──
    public void ArmAfter(float seconds)
    {
        armed = false;
        CancelInvoke();
        Invoke(nameof(Arm), seconds);
    }

    private void Arm() => armed = true;

    void OnCollisionEnter(Collision collision)
    {
        if (!armed) return;

        // Freeze only if we hit the ground (tagged)
        if (!collision.collider.CompareTag(freezeOnTag)) return;

        FreezeNow();
    }

    public void FreezeNow()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Snap slightly above ground to avoid tiny z-fighting
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, 2f))
            transform.position = hit.point + Vector3.up * 0.02f;

        enabled = false; // done
    }
}
