using System.Collections.Generic;

/// <summary>
/// [글로벌] Photon 지역 코드의 단일 출처 (RacerColors와 같은 철학).
/// [로컬라이제이션] 이름은 strings.csv의 region.* 키 — 새 지역은 코드 추가 + CSV 한 줄.
///
/// ⚠ Choices는 Photon 대시보드의 "리전 허가 리스트"(현재 kr;us;eu)와 맞춰 유지할 것.
/// 지역을 늘리는 절차: ① 대시보드 허가 리스트에 코드 추가(패치 불필요, 즉시 반영)
///                    ② 여기 Choices에 한 줄 추가(수동 선택 버튼에 노출 — 클라 재빌드 필요).
/// 대시보드에만 있고 여기 없는 지역도 자동(Best Region)으로는 붙을 수 있다 — Of()가 이름을 안다.
/// </summary>
public static class PhotonRegions
{
    /// <summary>타이틀 서버 선택 팝업에 노출되는 선택지. 빈 코드 = 자동(Best Region).</summary>
    public static readonly string[] Choices = { "", "kr", "us", "eu" };

    /// <summary>선택지 표시 이름 (빈 코드 = "자동 (권장)").</summary>
    public static string ChoiceName(string code) =>
        string.IsNullOrEmpty(code) ? Loc.Get("region.autofull") : Of(code);

    /// <summary>지역 코드 → 현재 언어 이름. Best Region 표식("kr/*")도 정리해서 받는다.
    /// CSV에 없는 미래 지역은 코드 대문자로 폴백 (조용히 — 키 경고 스팸 방지).</summary>
    public static string Of(string code)
    {
        if (string.IsNullOrEmpty(code)) return Loc.Get("region.auto");
        code = code.Split('/')[0].Trim();   // CloudRegion은 Best Region이면 "kr/*" 꼴
        return Loc.Get("region." + code, code.ToUpper());
    }
}
