using Fusion;
using UnityEngine;

public class RaycastAttack : NetworkBehaviour
{
    public float Damage = 10;
    public PlayerMovement PlayerMovement;

    private void Start()
    {
        PlayerMovement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // 로컬 플레이어만 입력 감지
        if (!HasInputAuthority) return;

        if (Input.GetMouseButtonDown(0)) // 좌클릭
        {
            //FireRay();
        }
    }

    private void FireRay()
    {
        // 카메라 가져오기
        Camera cam = PlayerMovement && PlayerMovement.MYcamera
            ? PlayerMovement.MYcamera
            : Camera.main;
        if (!cam) return;

        // 화면 중앙에서 Ray 발사
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Debug.DrawRay(ray.origin, ray.direction * 100f, Color.red, 2f);

        // 충돌 체크
        if (Runner.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, Mathf.Infinity))
        {
            // 태그가 Player인 경우에만 처리
            if (hit.transform.CompareTag("Player"))
            {
                var playerInfo = hit.transform.GetComponent<PlayerInfo>();
                if (playerInfo != null)
                {
                    Debug.Log("FIRE: Player hit!");

                    if (playerInfo.Object == null)
                    {
                        Debug.LogError("PlayerInfo는 NetworkObject가 아님.");
                        return;
                    }

                    //Debug.Log($"플레이어 '{playerInfo.playerName}' 피격! 현재 체력: {playerInfo.NetworkedHealth}");
                    //playerInfo.DealDamageRpc(Damage);
                }
                else
                {
                    Debug.Log("Player 태그는 있지만 PlayerInfo 스크립트가 없음.");
                }
            }
            else
            {
                Debug.Log("Player 태그가 아닌 오브젝트를 맞춤.");
            }
        }
        else
        {
            Debug.Log("Raycast로 아무것도 맞지 않음.");
        }
    }
}
