using TMPro;
using UnityEngine;

/// <summary>
/// 동물 몸의 등번호판 (양 옆구리 2개): 배경(큐브) 색 + 번호 텍스트를 출전 번호에 맞게 세팅.
/// 자동 탐색으로 자식의 판 전부 적용: 일반 MeshRenderer 전부 = 번호판 큐브
/// (동물 몸은 SkinnedMesh라는 가정), TMP 전부 = 번호 텍스트. 판이 몇 개든 동일 적용.
/// RaceManager가 스폰/등록 시 Apply 호출 — 호스트/클라 공통.
/// </summary>
public class RacerNumberPlate : MonoBehaviour
{
    public void Apply(int postNumber)
    {
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<TMP_Text>() != null) continue;   // TMP 자체 렌더러 제외
            r.material.color = RacerColors.Of(postNumber);
        }

        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
        {
            t.text = postNumber.ToString();
            t.color = RacerColors.TextOn(postNumber);
        }
    }
}
