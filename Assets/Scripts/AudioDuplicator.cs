using UnityEngine;
using System;

[RequireComponent(typeof(AudioSource))]
public class AudioDuplicator : MonoBehaviour
{
    public static Action<float[], int> OnAudioChunkReady;
    int count = 0;

    void OnAudioFilterRead(float[] data, int channels)
    {
        count++;
        if (count % 30 == 0)
            Debug.Log($"[Dup] OnAudioFilterRead: {data.Length} samples, {channels} ch");

        OnAudioChunkReady?.Invoke(data, channels);
    }

    void OnDestroy()
    {
        OnAudioChunkReady = null; // prevent lingering callbacks
    }
}
