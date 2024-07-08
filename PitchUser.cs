using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class PitchUser : MonoBehaviour
{
    public AudioPitchEstimator estimator;

    public TextMeshProUGUI freqText;
    public TextMeshProUGUI noteText;

    public float tolPercent;
    // Start is called before the first frame update
    void Start()
    {
        estimator.ShowMicrophoneDevices();
        estimator.BeginMicrophoneStream(0).AttachStreamToAudioSource().InvokeUpdateNote(0.01f);
        
    }

    // Update is called once per frame
    void Update()
    {
        noteText.text = estimator.CurrentNote.ToString();
        freqText.text = estimator.CurrentFrequency.ToString("F2");

        if (Input.GetKeyDown(KeyCode.Q))
        {
            estimator.EndMicrophoneStream();
            estimator.DetachStreamFromAudioSource();
        }
    }
}
