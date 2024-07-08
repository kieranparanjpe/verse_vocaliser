# verse_vocaliser
<br>
## Basic Usage
<br>
There are two scripts, AudioPitchEstimator.cs and PitchUser.cs. AudioPitchEstimator contains the functionality, and PitchUser is an example of how you could use the library.
<br>
# AudioPitchEstimator
## Properties

- `sampleRate`: The sample rate for the audio. It can range from 16000 to 96000.
- `frequencyMin`: The minimum frequency in Hz. It can range from 0 to 150.
- `frequencyMax`: The maximum frequency in Hz. It can range from 300 to 1200.
- `harmonicsToUse`: The number of harmonics to use for estimation. It can range from 1 to 8.
- `smoothingWidth`: The spectral moving average bandwidth in Hz. The larger the width, the smoother it becomes, but the accuracy decreases.
- `thresholdSRH`: The threshold for voiced sound determination. The larger the value, the stricter the determination.

## Methods

- `InvokeUpdateNote(float updateRate)`: Begin updating the current note and frequency periodically. The `updateRate` parameter specifies the amount of time in seconds between updates. It must be greater than or equal to 0.005.
- `CancelUpdateNote()`: End updating the current note and frequency periodically.
- `BeginMicrophoneStream(int device)`: Begin updating an AudioClip with the data from the microphone. It does not attach to an AudioSource. The `device` parameter specifies the microphone device to use.
- `EndMicrophoneStream()`: End updating an AudioClip with the data from the microphone. It does not detach from AudioSource.
- `ShowMicrophoneDevices()`: Prints out the microphone devices available.
- `AttachStreamToAudioSource()`: Attach the microphone stream to the audio source. The audio source will be the source attached to this object, or it will throw an error if there is not AudioSource on this object.
- `AttachStreamToAudioSource(AudioSource source)`: Attach the microphone stream to the audio source. The `source` parameter specifies the AudioSource to attach to.
- `DetachStreamFromAudioSource()`: Detach the current microphone stream from the audio source.

Continuing with the rest of the code:

## Methods

- `EstimateFrequency(AudioSource audioSource)`: This method estimates the fundamental frequency of the input audio source. If the audio source is not playing, it returns `float.NaN`. It calculates the logarithm of the amplitude spectrum and then calculates the residual spectrum. It then calculates the score of SRH (Summation of Residual Harmonics) and records the frequency with the highest score. If the SRH score does not meet the threshold, it is considered that there is no clear fundamental frequency and returns `float.NaN`.

- `GetSpectrumAmplitude(float[] spec, float frequency, float nyquistFreq)`: This method gets the amplitude at a given frequency from the spectrum data.

- `UpdateNote(AudioSource source, float tolerancePercent)`: This method estimates the frequency of the input and finds the note closest to it. It is not recommended to call this and `EstimateFrequency`, as `UpdateNote` will also call `EstimateFrequency`.

- `UpdateNote()`: This method updates the current note and frequency periodically.

- `NearestNote(int lo, int hi, float freq, float tolerance, float closestDistance, int closestIndex)`: This method finds the nearest note to a given frequency.

## Enum

- `Note`: This enum represents the piano notes from A0 to C8 in order. It also includes a `NONE` value to represent the absence of a note.
