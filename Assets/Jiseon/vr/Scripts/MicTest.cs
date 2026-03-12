using UnityEngine;

public class MicTest : MonoBehaviour
{
    void Start()
    {
        foreach (var device in Microphone.devices)
            Debug.Log("MIC: " + device);

        if (Microphone.devices.Length > 0)
        {
            AudioSource a = gameObject.AddComponent<AudioSource>();
            a.clip = Microphone.Start(null, true, 10, 44100);
            a.loop = true;

            while (!(Microphone.GetPosition(null) > 0)) { }
            a.Play();
        }
        else
        {
            Debug.LogError("NO MICROPHONE FOUND");
        }
    }
}
