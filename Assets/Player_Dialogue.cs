using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private DogAI dogAI;          // Drag your Dog (with DogAI) here
    [SerializeField] private AudioSource audioSrc; // Drag an AudioSource here (Player, Dog, or Manager)
    [SerializeField] private AudioClip commandClip; // "Carl, get the potions!"

    [Header("Timing")]
    [SerializeField] private float delayBeforeMove = 0.5f; // wait after voice, then dog moves

    private bool isRunning;

    // Call this from your input (PlayerCommand) or a UI button
    public void SayAndCommand()
    {
        if (!isRunning) StartCoroutine(WaitThenCommand());
    }

    private System.Collections.IEnumerator WaitThenCommand()
    {
        isRunning = true;

        // 1) Voice (if set)
        if (audioSrc != null && commandClip != null)
        {
            audioSrc.PlayOneShot(commandClip);
            // wait until clip finishes
            yield return new WaitUntil(() => !audioSrc.isPlaying);
        }
        else
        {
            // No audio set => just a tiny wait so it still feels responsive
            yield return new WaitForSeconds(0.1f);
            if (audioSrc == null) Debug.LogWarning("[DialogueTrigger] AudioSource not set. Skipping voice.");
            if (commandClip == null) Debug.LogWarning("[DialogueTrigger] commandClip not set. Skipping voice.");
        }

        // 2) Optional pause before the dog starts moving
        if (delayBeforeMove > 0f)
            yield return new WaitForSeconds(delayBeforeMove);

        // 3) Tell the dog to fetch
        if (dogAI != null)
        {
            dogAI.GiveCommand(0f); // already waited above
        }
        else
        {
            Debug.LogError("[DialogueTrigger] dogAI is NULL. Drag your Dog (with DogAI) into this field.");
        }

        isRunning = false;
    }

    // Convenience: try to auto-fill refs if you forgot
    private void Reset()
    {
        if (dogAI == null)    dogAI = FindObjectOfType<DogAI>();
        if (audioSrc == null) audioSrc = GetComponent<AudioSource>();
    }
}
