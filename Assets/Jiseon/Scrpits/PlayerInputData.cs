using Fusion;
using UnityEngine;

public struct PlayerInputData : INetworkInput
{
    public Vector2 move;
    public Vector2 look;
    public bool jump;
    public bool run;
    public bool NextEquipPressed;
    public bool PrevEquipPressed;
}
