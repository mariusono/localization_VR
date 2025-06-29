using System;
using System.IO;
using System.Collections.Generic; 
using UnityEngine;
using UnityEngine.InputSystem;
using FMODUnity;
using FMOD.Studio;

public class SpatialSoundTrial : MonoBehaviour
{
    private bool flag_lookingAtRedMarkerCheck = true;
    public bool playTic = false;

    private float soundDistance = 2.0f;
    private readonly float[] azimuths = { 0f, 45f, 90f, 270f, 315f };
    //private readonly float[] azimuths = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };



    private Dictionary<Key, float> unityKeyToAzimuth = new();
    private Dictionary<Key, float> fmodKeyToAzimuth = new();
    private Dictionary<Key, float> bothKeyToAzimuth = new();

    private readonly Key[] unityKeys = { Key.Q, Key.W, Key.E, Key.R, Key.T };
    private readonly Key[] fmodKeys = { Key.A, Key.S, Key.D, Key.F, Key.G };
    private readonly Key[] bothKeys = { Key.Z, Key.X, Key.C, Key.V, Key.B };


    public Transform playerHead;
    public Transform controllerTransform;
    public AudioSource spatialAudioPrefab;
    public InputActionReference triggerAction;
    public Canvas crosshairCanvas;
    public Material sphereMaterial; // Assign in Inspector to fix purple spheres

    private float currentAzimuth, currentElevation;
    private string csvPath;

    private GameObject[] fixedMarkers;
    private bool markersPlaced = false;
    private int trialCount = 0;
    private LineRenderer aimLine;

    private bool waitingForGuess = false;

    private GameObject redMarker;
    private Material redMarkerMaterialInstance;


    void Start()
    {
        aimLine = controllerTransform.GetComponent<LineRenderer>();
        aimLine.positionCount = 2;
        aimLine.useWorldSpace = true;
        aimLine.startWidth = 0.01f;
        aimLine.endWidth = 0.01f;

        csvPath = Application.persistentDataPath + "/" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_localization_results.csv";
        if (!File.Exists(csvPath))
            File.WriteAllText(csvPath, "targetAzimuth,targetElevation,guessAzimuth,guessElevation\n");

        triggerAction.action.Enable();
        triggerAction.action.performed += ctx => SubmitGuess();

        if (crosshairCanvas != null)
        {
            crosshairCanvas.renderMode = RenderMode.WorldSpace;
            crosshairCanvas.transform.SetParent(playerHead);
            crosshairCanvas.transform.localPosition = new Vector3(0, 0, soundDistance - 0.2f);
            crosshairCanvas.transform.localRotation = Quaternion.identity;
            crosshairCanvas.transform.localScale = Vector3.one * 0.001f;
        }

        Debug.Log(Application.persistentDataPath);
        Debug.Log("Unity Audio Sample Rate: " + AudioSettings.outputSampleRate);

        if (playTic)
        {
            AudioClip ticClip = Resources.Load<AudioClip>("tic");
            if (ticClip != null)
            {
                spatialAudioPrefab.clip = ticClip;
            }
            else
            {
                Debug.LogWarning("tic.wav not found in Resources folder.");
            }
        }
    }

    void OnDestroy()
    {
        triggerAction.action.performed -= ctx => SubmitGuess();
    }

    void Update()
    {
        if (!markersPlaced && playerHead.position.y > 0.2f)
        {
            CreateFixedMarkers();
            markersPlaced = true;
        }


        MapKeysToAzimuths(unityKeys, unityKeyToAzimuth);
        MapKeysToAzimuths(fmodKeys, fmodKeyToAzimuth);
        MapKeysToAzimuths(bothKeys, bothKeyToAzimuth);

        if (flag_lookingAtRedMarkerCheck)
        {
            foreach (var kv in unityKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame && IsLookingAtRedMarker())
                    PlayUnitySound(kv.Value);
            }
            foreach (var kv in fmodKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame && IsLookingAtRedMarker())
                    PlayFMODSound(kv.Value);
            }
            foreach (var kv in bothKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame && IsLookingAtRedMarker())
                    PlayBothSounds(kv.Value);
            }
        }
        else
        {
            foreach (var kv in unityKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame)
                    PlayUnitySound(kv.Value);
            }
            foreach (var kv in fmodKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame)
                    PlayFMODSound(kv.Value);
            }
            foreach (var kv in bothKeyToAzimuth)
            {
                if (Keyboard.current[kv.Key].wasPressedThisFrame)
                    PlayBothSounds(kv.Value);
            }
        }

        // Original random trial via Spacebar
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            RunRandomTrial(); // Already does both
        }

        if (aimLine != null)
        {
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, controllerTransform.position);
            aimLine.SetPosition(1, controllerTransform.position + controllerTransform.forward * 10f);
        }

        if (redMarker != null && redMarkerMaterialInstance != null)
        {
            if (IsLookingAtRedMarker())
            {
                redMarkerMaterialInstance.SetColor("_EmissionColor", new Color(10f, 0f, 0f)); // Glow ON
            }
            else
            {
                redMarkerMaterialInstance.SetColor("_EmissionColor", Color.black); // Glow OFF
            }
        }
    }

    public void StartTrial(float azimuth, float elevation)
    {
        currentAzimuth = azimuth;
        currentElevation = elevation;


        if (flag_lookingAtRedMarkerCheck)
        {
            if (IsLookingAtRedMarker())
            {
                PlayUnitySound(azimuth);
                PlayFMODSound(azimuth);
            }
        }
        else
        {
            PlayUnitySound(azimuth);
            PlayFMODSound(azimuth);
        }
    }

    public void SubmitGuess()
    {
        if (!waitingForGuess)
        {
            Debug.Log("Submit ignored: no trial is active.");
            return;
        }

        var (guessAzEl, _) = GetControllerAngles();

        Debug.Log($"Trial azimuth: {currentAzimuth}°, Guess azimuth: {guessAzEl.x}°, Elevation: {guessAzEl.y}°");
        LogResult(trialCount, currentAzimuth, currentElevation, guessAzEl.x, guessAzEl.y);

        // Unity Sound
        AudioClip selectClip = Resources.Load<AudioClip>("Select");
        if (selectClip != null)
        {
            AudioSource.PlayClipAtPoint(selectClip, controllerTransform.position);
        }
        else
        {
            Debug.LogWarning("Select.wav not found in Resources folder.");
        }

        // ✅ FMOD Sound
        FMOD.Studio.EventInstance selectEvent = RuntimeManager.CreateInstance("event:/select");
        FMOD.ATTRIBUTES_3D fmodAttributes = RuntimeUtils.To3DAttributes(controllerTransform.position);
        selectEvent.set3DAttributes(fmodAttributes);

        selectEvent.setVolume(0.5f);

        selectEvent.start();
        selectEvent.release();

        waitingForGuess = false;
    }



    Vector3 DirectionFromAngles(float azimuth, float elevation)
    {
        float azRad = azimuth * Mathf.Deg2Rad;
        float elRad = elevation * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(elRad) * Mathf.Sin(azRad),
            Mathf.Sin(elRad),
            Mathf.Cos(elRad) * Mathf.Cos(azRad)
        ).normalized;
    }

    (Vector2, Vector3) GetControllerAngles()
    {
        Debug.DrawRay(controllerTransform.position, controllerTransform.forward * 10f, Color.green, 1f);

        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, 10f))
        {
            Vector3 dirToHit = (hit.point - playerHead.position).normalized;

            Debug.DrawLine(playerHead.position, hit.point, Color.yellow, 1f);

            float azimuth = Mathf.Atan2(dirToHit.x, dirToHit.z) * Mathf.Rad2Deg;
            if (azimuth < 0) azimuth += 360f;

            float elevation = Mathf.Asin(dirToHit.y) * Mathf.Rad2Deg;

            return (new Vector2(azimuth, elevation), dirToHit);
        }
        else
        {
            // If nothing is hit, fallback to controller forward
            Vector3 fallbackDir = controllerTransform.forward.normalized;

            float azimuth = Mathf.Atan2(fallbackDir.x, fallbackDir.z) * Mathf.Rad2Deg;
            if (azimuth < 0) azimuth += 360f;

            float elevation = Mathf.Asin(fallbackDir.y) * Mathf.Rad2Deg;

            return (new Vector2(azimuth, elevation), fallbackDir);
        }
    }



    void LogResult(float trialCount, float targetAz, float targetEl, float guessAz, float guessEl)
    {
        string line = $"Trial {trialCount}: {targetAz},{targetEl},{guessAz},{guessEl}";
        File.AppendAllText(csvPath, line + "\n");
    }

    void RunRandomTrial()
    {
        if (waitingForGuess)
        {
            // Re-play the same trial sound
            Debug.Log($"Repeating trial {trialCount}: az={currentAzimuth}°, el={currentElevation}°");
            StartTrial(currentAzimuth, currentElevation);
            return;
        }

       // float[] azimuthOptions = new float[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
        //float[] azimuthOptions = new float[] { 0f, 45f, 90f, 270f, 315f };
        float chosenAzimuth = azimuths[UnityEngine.Random.Range(0, azimuths.Length)];
        float elevation = 0f;

        trialCount++;
        currentAzimuth = chosenAzimuth;
        currentElevation = elevation;
        waitingForGuess = true;

        Debug.Log($"Trial {trialCount}: Starting new trial at az={chosenAzimuth}°, el={elevation}°");
        StartTrial(chosenAzimuth, elevation);
    }

    void CreateFixedMarkers()
    {
        fixedMarkers = new GameObject[azimuths.Length];

        // Dynamically generate enough colors (first one is always red)
        Color[] colors = new Color[azimuths.Length];
        colors[0] = Color.red;
        for (int i = 1; i < colors.Length; i++)
        {
            colors[i] = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
        }

        Debug.Log($"Creating fixed markers at head height: {playerHead.position.y}");

        for (int i = 0; i < azimuths.Length; i++)
        {
            Vector3 dir = DirectionFromAngles(azimuths[i], 0f);
            Vector3 pos = playerHead.position + dir * soundDistance;
            pos.y = playerHead.position.y;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = pos;
            sphere.transform.localScale = Vector3.one * 0.2f;

            Renderer renderer = sphere.GetComponent<Renderer>();

            if (i == 0)
            {
                sphere.name = "RedMarker";
                redMarker = sphere;

                if (sphereMaterial != null)
                {
                    redMarkerMaterialInstance = new Material(sphereMaterial);
                    redMarkerMaterialInstance.color = colors[i];
                    redMarkerMaterialInstance.EnableKeyword("_EMISSION");
                    redMarkerMaterialInstance.SetColor("_EmissionColor", new Color(5f, 0f, 0f)); // glow red
                    renderer.material = redMarkerMaterialInstance;
                }
            }
            else if (sphereMaterial != null)
            {
                renderer.material = new Material(sphereMaterial);
                renderer.material.color = colors[i];
            }

            fixedMarkers[i] = sphere;
        }
    }


    void PlayUnitySound(float azimuth)
    {
        Debug.Log($"[UNITY] playing at azimuth {azimuth} relative to head");

        Quaternion rotation = Quaternion.Euler(0f, azimuth, 0f);
        Vector3 dir = rotation * playerHead.forward;
        Vector3 spawnPos = playerHead.position + dir.normalized * soundDistance;
        spawnPos.y = playerHead.position.y;

        AudioSource src = Instantiate(spatialAudioPrefab, spawnPos, Quaternion.identity);
        src.spatialBlend = 1.0f;
        src.Play();
        Destroy(src.gameObject, src.clip.length + 0.25f);
    }

    void PlayFMODSound(float azimuth)
    {
        string eventPath = playTic ? "event:/tic" : "event:/burst_2";
        Debug.Log($"[FMOD] playing '{eventPath}' at azimuth {azimuth} relative to head");

        Quaternion rotation = Quaternion.Euler(0f, azimuth, 0f);
        Vector3 dir = rotation * playerHead.forward;
        Vector3 spawnPos = playerHead.position + dir.normalized * soundDistance;
        spawnPos.y = playerHead.position.y;

        FMOD.Studio.EventInstance instance = RuntimeManager.CreateInstance(eventPath);

        FMOD.ATTRIBUTES_3D attributes = RuntimeUtils.To3DAttributes(spawnPos);
        Vector3 toListener = (playerHead.position - spawnPos).normalized;

        attributes.forward = RuntimeUtils.ToFMODVector(toListener);
        attributes.up = RuntimeUtils.ToFMODVector(Vector3.up);

        instance.set3DAttributes(attributes);
        instance.start();
        instance.release();
    }

    void PlayBothSounds(float azimuth)
    {
        PlayUnitySound(azimuth);
        PlayFMODSound(azimuth);
    }

    bool IsLookingAtRedMarker()
    {
        Ray ray = new Ray(playerHead.position, playerHead.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, 10f))
        {
            Debug.DrawRay(ray.origin, ray.direction * 10f, Color.red, 1f);

            return hit.collider.gameObject.name == "RedMarker";
        }

        return false;
    }

    void MapKeysToAzimuths(Key[] keys, Dictionary<Key, float> keyToAzimuth)
    {
        int n = keys.Length;
        float total = azimuths.Length;

        for (int i = 0; i < n; i++)
        {
            // Find azimuth closest to this division
            float targetIndex = (i / (float)(n - 1)) * (total - 1);
            int closestAzimuthIndex = Mathf.RoundToInt(targetIndex);
            float closestAzimuth = azimuths[closestAzimuthIndex];

            keyToAzimuth[keys[i]] = closestAzimuth;
        }
    }

}