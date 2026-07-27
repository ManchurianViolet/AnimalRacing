using TMPro;
using UnityEngine;

/// <summary>
/// 동물 몸의 등번호판: 배경(큐브) 색 + 번호 텍스트를 출전 번호에 맞게 세팅.
/// 슬롯을 비워두면 자동 탐색: TMP는 자식에서, 배경은 자식 중 MeshRenderer
/// (동물 몸은 SkinnedMesh라 일반 MeshRenderer = 번호판 큐브라는 가정).
/// RaceManager가 스폰/등록 시 Apply 호출 — 호스트/클라 공통.
/// </summary>
public class RacerNumberPlate : MonoBehaviour
{
    [Header("비워두면 자동 탐색")]
    [SerializeField] private Renderer plateRenderer;
    [SerializeField] private TMP_Text numberText;

    public void Apply(int postNumber)
    {
        if (numberText == null) numberText = GetComponentInChildren<TMP_Text>(true);
        if (plateRenderer == null)
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TMP_Text>() != null) continue;   // TMP 자체 렌더러 제외
                plateRenderer = r;
                break;
            }
        }

        if (plateRenderer != null)
            plateRenderer.material.color = RacerColors.Of(postNumber);

        if (numberText != null)
        {
            numberText.text = postNumber.ToString();
            numberText.color = RacerColors.TextOn(postNumber);
        }
    }
}
