using Fusion;
using Photon.Voice.Unity;

public class PlayerVoiceBinder : NetworkBehaviour
{
    Recorder recorder;

    public override void Spawned()
    {
        recorder = GetComponent<Recorder>();

        if (Object.HasInputAuthority)
        {
            recorder.TransmitEnabled = true;
            recorder.RecordingEnabled = true;
        }
        else
        {
            recorder.TransmitEnabled = false;
            recorder.RecordingEnabled = false;
        }
    }
}
