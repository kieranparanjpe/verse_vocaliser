using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// Fundamental frequency estimation by SRH (Summation of Residual Harmonics)
// T. Drugman and A. Alwan: "Joint Robust Voicing Detection and Pitch Estimation Based on Residual Harmonics", Interspeech'11, 2011.
// Adapted by Kieran Paranjpe for use in The Verse

public class AudioPitchEstimator : MonoBehaviour
{
    [Range(16000, 96000)]
    [SerializeField] private int sampleRate = 44100;
    
    [Tooltip("Minimum frequency [Hz]")]
    [Range(0, 150)]
    [SerializeField] private int frequencyMin = 40;

    [Tooltip("Maximum frequency [Hz]")]
    [Range(300, 1200)]
    [SerializeField] private int frequencyMax = 600;

    [Tooltip("Number of harmonics to use for estimation")]
    [Range(1, 8)]
    [SerializeField] private int harmonicsToUse = 5;

    [Tooltip("Spectral moving average bandwidth [Hz]\nThe larger the width, the smoother it becomes, but the accuracy decreases")]
    [SerializeField] private float smoothingWidth = 500;

    [Tooltip("Threshold for voiced sound determination\nThe larger the value, the stricter the determination")]
    [SerializeField] private float thresholdSRH = 7;

    const int spectrumSize = 1024;
    const int outputResolution = 200; // Number of elements on the frequency axis of SRH (reducing this reduces computational load)
    float[] spectrum = new float[spectrumSize];
    float[] specRaw = new float[spectrumSize];
    float[] specCum = new float[spectrumSize];
    float[] specRes = new float[spectrumSize];
    float[] srh = new float[outputResolution];

    private List<float> SRH => new List<float>(srh);

    public AudioSource Source = null;
    
    public float TolerancePercent = 0;
    
    private int microphoneDeviceIndex = 0;
    private AudioClip microphoneStream = null;

    public Note CurrentNote { get; private set; } = Note.NONE;
    public float CurrentFrequency { get; private set; } = float.NaN;

    /// <summary>
    /// Each frequency of piano notes from A0 - C8 in order.
    /// </summary>
    public float[] NoteFrequencies { get; private set; } = new float[]
    {
        27.50f, 29.14f, 30.87f, 32.70f, 34.65f, 36.71f, 38.89f, 41.20f, 43.65f, 46.25f, 49.00f, 51.91f, 55.00f, 58.27f,
        61.74f, 65.41f, 69.30f, 73.42f, 77.78f, 82.41f, 87.31f, 92.50f, 98.00f, 103.83f, 110.00f, 116.54f, 123.47f,
        130.81f, 138.59f, 146.83f, 155.56f, 164.81f, 174.61f, 185.00f, 196.00f, 207.65f, 220.00f, 233.08f, 246.94f,
        261.63f, 277.18f, 293.66f, 311.13f, 329.63f, 349.23f, 369.99f, 392.00f, 415.30f, 440.00f, 466.16f, 493.88f,
        523.25f, 554.37f, 587.33f, 622.25f, 659.25f, 698.46f, 739.99f, 783.99f, 830.61f, 880.00f, 932.33f, 987.77f,
        1046.50f, 1108.73f, 1174.66f, 1244.51f, 1318.51f, 1396.91f, 1479.98f, 1567.98f, 1661.22f, 1760.00f, 1864.66f,
        1975.53f, 2093.00f, 2217.46f, 2349.32f, 2489.02f, 2637.02f, 2793.83f, 2959.96f, 3135.96f, 3322.44f, 3520.00f,
        3729.31f, 3951.07f, 4186.01f
    };

    /// <summary>
    /// Begin updating the current note and frequency periodically 
    /// </summary>
    /// <param name="updateRate">Amount of time in seconds between updates. Must be greater than or equal to 0.005</param>
    /// <returns>Itself</returns>
    public AudioPitchEstimator InvokeUpdateNote(float updateRate)
    {
        if (updateRate < 0.005)
            Debug.LogError("Update Rate is too low.");

        InvokeRepeating("UpdateNote", 0, updateRate);
        
        return this;
    }
    /// <summary>
    /// End updating the current note and frequency periodically 
    /// </summary>
    public void CancelUpdateNote()
    {
        CancelInvoke("UpdateNote");
    }
    /// <summary>
    /// Begin updating an AudioClip with the data from microphone. Does not attach to an AudioSource.
    /// </summary>
    /// <param name="device">Microphone Device to use.</param>
    /// <returns>Itself</returns>
    public AudioPitchEstimator BeginMicrophoneStream(int device)
    {
        microphoneDeviceIndex = device;
        if (microphoneDeviceIndex >= Microphone.devices.Length || microphoneDeviceIndex < 0)
            Debug.LogError($"Microphone Device Index of {microphoneDeviceIndex} is not a valid microphone index.");
        
        string microphoneName = Microphone.devices[microphoneDeviceIndex];
        microphoneStream = Microphone.Start(microphoneName, true, 1, sampleRate);
        return this;
    }

    /// <summary>
    /// End updating an AudioClip with the data from microphone. Does not detach from AudioSource.
    /// </summary>
    public void EndMicrophoneStream()
    {
        if (microphoneDeviceIndex >= Microphone.devices.Length || microphoneDeviceIndex < 0)
            Debug.LogError($"Microphone Device Index of {microphoneDeviceIndex} is not a valid microphone index.");
        string microphoneName = Microphone.devices[microphoneDeviceIndex];
        Microphone.End(microphoneName);
        microphoneStream = null;
    }
    
    /// <summary>
    /// Prints out the microphone devices available.
    /// </summary>
    public void ShowMicrophoneDevices()
    {
        Debug.Log("Microphone Options: " + string.Join(',', Microphone.devices));
    }

    /// <summary>
    /// Attach the microphone stream to audio source. Audio source will be the source attached to this object, or will throw error if there is not AudioSource on this object.
    /// </summary>
    /// <returns>Itself</returns>
    public AudioPitchEstimator AttachStreamToAudioSource()
    {
        if (Source == null)
        {
            Source = GetComponent<AudioSource>();
            if (Source == null)
                Debug.LogError("Could not find an Audio Source to attach to!");
        }
        if (microphoneStream == null)
            Debug.LogError("Microphone Stream is null");
        
        Source.clip = microphoneStream;
        Source.Play();
        Source.loop = true;

        return this;
    }
    /// <summary>
    /// Attach the microphone stream to audio source.
    /// </summary>
    /// <param name="source">Audio Source to attach to</param>>
    /// <returns>Itself</returns>
    public AudioPitchEstimator AttachStreamToAudioSource(AudioSource source)
    {
        Source = source;
        if (Source == null)
        {
            Source = GetComponent<AudioSource>();
            if (Source == null)
                Debug.LogError("Could not find an Audio Source to attach to!");
        }
        if (microphoneStream == null)
            Debug.LogError("Microphone Stream is null");
        
        Source.clip = microphoneStream;
        Source.Play();
        Source.loop = true;

        return this;
    }

    /// <summary>
    /// Detach current microphone stream from audio source.
    /// </summary>
    public void DetachStreamFromAudioSource()
    {
        if (Source == null)
        {
            Debug.LogError("Could not find an Audio Source to detach from!");
        }
        Source.clip = null;
        Source.loop = false;
        Source.Stop();
    }

    /// <summary>
    /// Estimate the fundamental frequency
    /// </summary>
    /// <param name="audioSource">Input audio source</param>
    /// <returns>Fundamental frequency[Hz] (float.NaN when not present)</returns>
    public float EstimateFrequency(AudioSource audioSource)
    {
        var nyquistFreq = AudioSettings.outputSampleRate / 2.0f;

        // Get audio spectrum
        if (!audioSource.isPlaying) return float.NaN;
        audioSource.GetSpectrumData(spectrum, 0, FFTWindow.Hanning);

        // Calculate the logarithm of the amplitude spectrum
        // All spectra from here on are treated as logarithmic amplitudes (this is different from the original paper)
        for (int i = 0; i < spectrumSize; i++)
        {
            // When the amplitude is zero, it becomes -∞, so add a small value
            specRaw[i] = Mathf.Log(spectrum[i] + 1e-9f);
        }

        // Cumulative sum of the spectrum (used later)
        specCum[0] = 0;
        for (int i = 1; i < spectrumSize; i++)
        {
            specCum[i] = specCum[i - 1] + specRaw[i];
        }

        // Calculate residual spectrum
        var halfRange = Mathf.RoundToInt((smoothingWidth / 2) / nyquistFreq * spectrumSize);
        for (int i = 0; i < spectrumSize; i++)
        {
            // Smooth the spectrum (moving average using cumulative sum)
            var indexUpper = Mathf.Min(i + halfRange, spectrumSize - 1);
            var indexLower = Mathf.Max(i - halfRange + 1, 0);
            var upper = specCum[indexUpper];
            var lower = specCum[indexLower];
            var smoothed = (upper - lower) / (indexUpper - indexLower);

            // Remove smooth components from the original spectrum
            specRes[i] = specRaw[i] - smoothed;
        }

        // Calculate the score of SRH (Summation of Residual Harmonics)
        float bestFreq = 0, bestSRH = 0;
        for (int i = 0; i < outputResolution; i++)
        {
            var currentFreq = (float)i / (outputResolution - 1) * (frequencyMax - frequencyMin) + frequencyMin;

            // Calculate the SRH score at the current frequency: Equation (1) in the paper
            var currentSRH = GetSpectrumAmplitude(specRes, currentFreq, nyquistFreq);
            for (int h = 2; h <= harmonicsToUse; h++)
            {
                // At h times the frequency, the stronger the signal, the better
                currentSRH += GetSpectrumAmplitude(specRes, currentFreq * h, nyquistFreq);

                // At the intermediate frequency between h-1 times and h times, the stronger the signal, the worse
                currentSRH -= GetSpectrumAmplitude(specRes, currentFreq * (h - 0.5f), nyquistFreq);
            }
            srh[i] = currentSRH;

            // Record the frequency with the highest score
            if (currentSRH > bestSRH)
            {
                bestFreq = currentFreq;
                bestSRH = currentSRH;
            }
        }

        // If the SRH score does not meet the threshold → It is considered that there is no clear fundamental frequency
        if (bestSRH < thresholdSRH) return float.NaN;

        return bestFreq;
    }


    // Get the amplitude at frequency[Hz] from the spectrum data
    float GetSpectrumAmplitude(float[] spec, float frequency, float nyquistFreq)
    {
        var position = frequency / nyquistFreq * spec.Length;
        var index0 = (int)position;
        var index1 = index0 + 1; // Array boundary check is omitted
        var delta = position - index0;
        return (1 - delta) * spec[index0] + delta * spec[index1];
    }

    /// <summary>
    /// Estimate the frequency of input, and find the note closest to it. Not recommended to call this and Estimate, as GetNote will also call Estimate.
    /// </summary>
    /// <param name="source">Input audio source</param>
    /// <param name="tolerancePercent">How close you must be to the note as a percent. Space between note frequencies increases as pitch increases, so the tolerance must be relative.</param>
    /// <returns>Piano Note as given by enum Note defined below.</returns>
    public void UpdateNote(AudioSource source, float tolerancePercent)
    {
        if (source == null)
            Debug.LogError("There is no audio source");
        CurrentFrequency = EstimateFrequency(source);
        
        if (float.IsNaN(CurrentFrequency))
        {
            CurrentNote = Note.NONE;
        }
        else
        {
            CurrentNote = NearestNote(0, NoteFrequencies.Length, CurrentFrequency, tolerancePercent, float.MaxValue, 0);
        }
    }
    
    private void UpdateNote()
    {
        if (Source == null)
            Debug.LogError("There is no audio source");
        
        CurrentFrequency = EstimateFrequency(Source);
        
        if (float.IsNaN(CurrentFrequency))
        {
            CurrentNote = Note.NONE;
        }
        else
        {
            CurrentNote = NearestNote(0, NoteFrequencies.Length, CurrentFrequency, TolerancePercent, float.MaxValue, 0);
        }
    }

    private Note NearestNote(int lo, int hi, float freq, float tolerance, float closestDistance, int closestIndex)
    {
        if (lo > hi)
        {
            float relDist = 0;
            if (closestIndex == 0)
                relDist = NoteFrequencies[1] - NoteFrequencies[0];
            else if (closestIndex == NoteFrequencies.Length - 1)
                relDist = NoteFrequencies[closestIndex] - NoteFrequencies[closestIndex - 1];
            else
                relDist = (NoteFrequencies[closestIndex + 1] - NoteFrequencies[closestIndex - 1]) / 2;
            if (closestDistance <= tolerance * relDist)
                return (Note)closestIndex;
            else
                return Note.NONE;
        }
        
        int index = (lo + hi) / 2;
        float newFreq = NoteFrequencies[index];

        float distance = Mathf.Abs(newFreq - freq);
        if (distance < closestDistance)
        {
            closestDistance = distance;
            closestIndex = index;     
        }

        if (freq > newFreq)
            return NearestNote(lo + 1, hi, freq, tolerance, closestDistance, closestIndex);
        else if (freq < newFreq)
            return NearestNote(lo, hi - 1, freq, tolerance, closestDistance, closestIndex);
        else
            return (Note)index;
    }

    public enum Note
    {
        A0, Bb0, B0, C1, Db1, D1, Eb1, E1, F1, Gb1, G1, Ab1, A1, Bb1, B1, C2, Db2, D2, Eb2, E2, F2, Gb2, G2, Ab2, A2, Bb2, B2, C3, Db3, D3, Eb3, E3, F3, Gb3, G3, Ab3, A3, Bb3, B3, C4, Db4, D4, Eb4, E4, F4, Gb4, G4, Ab4, A4, Bb4, B4, C5, Db5, D5, Eb5, E5, F5, Gb5, G5, Ab5, A5, Bb5, B5, C6, Db6, D6, Eb6, E6, F6, Gb6, G6, Ab6, A6, Bb6, B6, C7, Db7, D7, Eb7, E7, F7, Gb7, G7, Ab7, A7, Bb7, B7, C8, NONE
    }

}

