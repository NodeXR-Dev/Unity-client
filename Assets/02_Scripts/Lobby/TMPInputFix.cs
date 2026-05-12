using TMPro;
using UnityEngine;

public class TMPInputFix : MonoBehaviour
{
    public TMP_InputField input;

    void Start()
    {
        input.text = "tester";
    }
}
