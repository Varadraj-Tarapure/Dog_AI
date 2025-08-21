using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class PlayerCommand : MonoBehaviour
{
    [Header("Refs")]
    public DogAI dogAI;                   // drag your Dog (with DogAI) here, or use tag below
    public string dogTag = "Dog";         // optional: tag your Dog root as "Dog"
    public AudioSource audioSource;       // put an AudioSource on the Player and drag it here

    [Header("Voice Lines")]
    public AudioClip firstCommandClip;    // e.g. "Carl, get the potion!"
    public AudioClip nextCommandClip;     // e.g. "Get another potion."

    private PlayerInputActions input;
    private bool gaveFirstThisRun = false;  // tracks first vs subsequent requests

    void Awake()
    {
        input = new PlayerInputActions();
        input.Player.Command.performed += OnCommandPerformed;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        ResolveDog();
    }

    void Start()     { ResolveDog(); }
    void OnEnable()  { input?.Enable(); ResolveDog(); }
    void OnDisable() { input?.Disable(); }

    // Try to find the DogAI if not assigned
    void ResolveDog()
    {
        if (dogAI != null) return;

        if (!string.IsNullOrEmpty(dogTag))
        {
            var tagged = GameObject.FindWithTag(dogTag);
            if (tagged != null)
            {
                dogAI = tagged.GetComponent<DogAI>()
                      ?? tagged.GetComponentInChildren<DogAI>(true)
                      ?? tagged.GetComponentInParent<DogAI>();
                if (dogAI != null) return;
            }
        }

#if UNITY_2020_1_OR_NEWER
        var all = Resources.FindObjectsOfTypeAll<DogAI>();
        dogAI = all.FirstOrDefault(d => d.gameObject.scene.IsValid());
#else
        dogAI = FindObjectOfType<DogAI>();
#endif
    }

    private void OnCommandPerformed(InputAction.CallbackContext ctx)
    {
        if (dogAI == null)
        {
            ResolveDog();
            if (dogAI == null)
            {
                Debug.LogError("PlayerCommand: DogAI reference missing. Drag your Dog (with DogAI) into the PlayerCommand.dogAI field, or tag the Dog root as 'Dog'.");
                return;
            }
        }

        // Tell the dog to fetch
        dogAI.GiveCommand(0.5f); // small delay feels nicer

        // Choose which clip to play
        if (audioSource != null)
        {
            AudioClip clip = gaveFirstThisRun ? nextCommandClip : firstCommandClip;
            if (clip != null) audioSource.PlayOneShot(clip);
        }

        // After the first request, switch to the "next" line
        gaveFirstThisRun = true;

        // When all potions are done, reset so next session says the first line again
        if (dogAI.RemainingPotions == 0)
            gaveFirstThisRun = false;
    }
}
