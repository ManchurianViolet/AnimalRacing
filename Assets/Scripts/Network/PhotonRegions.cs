using System.Collections.Generic;

/// <summary>
/// [글로벌] Photon 지역 코드의 단일 출처 — 선택지 목록 + 한글 이름 (RacerColors와 같은 철학).
///
/// ⚠ Choices는 Photon 대시보드의 "리전 허가 리스트"(현재 kr;us;eu)와 맞춰 유지할 것.
/// 지역을 늘리는 절차: ① 대시보드 허가 리스트에 코드 추가(패치 불필요, 즉시 반영)
///                    ② 여기 Choices에 한 줄 추가(수동 선택 버튼에 노출 — 클라 재빌드 필요).
/// 대시보드에만 있고 여기 없는 지역도 자동(Best Region)으로는 붙을 수 있다 — Of()가 이름을 안다.
/// </summary>
public static class PhotonRegions
{
    /// <summary>타이틀 서버 선택 팝업에 노출되는 선택지. 빈 코드 = 자동(Best Region).</summary>
    public static readonly (string code, string name)[] Choices =
    {
        ("",   "자동 (권장)"),
        ("kr", "한국"),
        ("us", "미국"),
        ("eu", "유럽"),
    };

    // 표시용 전체 사전 — 대시보드에서 지역을 늘려도 이름표는 미리 준비돼 있게.
    private static readonly Dictionary<string, string> Names = new()
    {
        { "kr", "한국" }, { "jp", "일본" }, { "asia", "아시아" }, { "in", "인도" },
        { "us", "미국" }, { "usw", "미국 서부" }, { "ussc", "미국 중남부" },
        { "eu", "유럽" }, { "uae", "중동" }, { "sa", "남미" }, { "za", "남아공" },
        { "au", "호주" }, { "cae", "캐나다" }, { "tr", "튀르키예" }, { "hk", "홍콩" },
    };

    /// <summary>지역 코드 → 한글 이름. Best Region 표식("kr/*")도 정리해서 받는다.</summary>
    public static string Of(string code)
    {
        if (string.IsNullOrEmpty(code)) return "자동";
        code = code.Split('/')[0].Trim();   // CloudRegion은 Best Region이면 "kr/*" 꼴
        return Names.TryGetValue(code, out var name) ? name : code.ToUpper();
    }
}
