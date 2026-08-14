using UnityEngine;

/// <summary>
/// 하늘돔이 카메라를 따라다니게 한다 — 하늘·태양이 "무한히 멀리" 있는 것처럼 보이게 하는 표준 기법.
///
/// [왜 필요한가] 태양 원반은 원점 기준 252m에 놓인 유한 거리 오브젝트다. 돔을 고정해두면
/// 플레이어가 트랙을 돌수록 카메라에서 본 원반의 방향이 크게 바뀐다(맵 반대편이면 수십 도).
/// 반면 Beautify 렌즈 플레어는 `카메라 위치 - 태양forward × 1000`, 즉 무한 방향으로 위치를 잡는다.
/// 그래서 둘이 갈수록 벌어진다 — 돔이 카메라를 따라오면 원반의 상대 방향이 늘 같아 정확히 겹친다.
///
/// 회전은 건드리지 않는다 (하늘 그림·태양 방위는 그대로).
/// [멀티] 순수 로컬 연출 — 네트워크 추가 통신 0.
/// </summary>
[DefaultExecutionOrder(200)]   // 카메라가 제자리를 잡은 뒤(FPC LateUpdate 이후) 따라간다
public class SkyFollowCamera : MonoBehaviour
{
    private Transform cam;

    private void LateUpdate()
    {
        if (cam == null)
        {
            var c = Camera.main;
            if (c == null) return;      // 내 아바타 스폰 전 — 다음 프레임에 다시 찾는다
            cam = c.transform;
        }
        transform.position = cam.position;
    }
}
