using UnityEngine;
using System;
using NAudio.Wave;

public class AudioMirrorToNAudio : MonoBehaviour
{
    private WaveOutEvent waveOut;
    private BufferedWaveProvider bufferProvider;
    private bool isReady = false;

    private int frameCount = 0;
    private double peakBufferedMs = 0;

    void Update()
    {
        frameCount++;
        if (frameCount % 60 == 0 && bufferProvider != null)
        {
            double bufferedMs = bufferProvider.BufferedDuration.TotalMilliseconds;
            if (bufferedMs > peakBufferedMs) peakBufferedMs = bufferedMs;

            string status = bufferedMs > 90 ? "⚠️" : "";
            Debug.Log($"[Buffer] Buffered: {bufferProvider.BufferedBytes} bytes, " +
                      $"Time: {bufferedMs:F1} ms (peak: {peakBufferedMs:F1} ms) {status}, " +
                      $"DSP: {AudioSettings.dspTime:F3}, ΔTime: {Time.deltaTime:F3}");
        }
    }

    void Start()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        bufferProvider = new BufferedWaveProvider(format)
        {
            BufferLength = 48000 * 32,           // Increased buffer
            DiscardOnBufferOverflow = true,
            ReadFully = true                     // Avoid underruns
        };

        waveOut = new WaveOutEvent
        {
            DesiredLatency = 30,                 // Slightly safer latency
            NumberOfBuffers = 4                  // Give NAudio more room
        };

        waveOut.Init(bufferProvider);
        waveOut.Play();

        AudioDuplicator.OnAudioChunkReady += HandleAudio;
        isReady = true;

        Debug.Log("[Mirror] Audio mirror initialized.");
    }

    void HandleAudio(float[] data, int channels)
    {
        if (!isReady || data == null || data.Length == 0 || channels != 2)
        {
            Debug.LogWarning("[Mirror] Dropped chunk: invalid data.");
            return;
        }

        int byteLength = data.Length * 4;
        if (bufferProvider.BufferedBytes > bufferProvider.BufferLength - byteLength)
        {
            Debug.LogWarning($"[Mirror] Skipping chunk — buffer full ({bufferProvider.BufferedDuration.TotalMilliseconds:F1} ms buffered)");
            return;
        }

        byte[] byteData = new byte[byteLength];
        Buffer.BlockCopy(data, 0, byteData, 0, byteLength);
        bufferProvider.AddSamples(byteData, 0, byteLength);
    }

    void OnDestroy()
    {
        AudioDuplicator.OnAudioChunkReady -= HandleAudio;
        waveOut?.Stop();
        waveOut?.Dispose();
    }

    void OnDisable()
    {
        peakBufferedMs = 0;
    }
}
