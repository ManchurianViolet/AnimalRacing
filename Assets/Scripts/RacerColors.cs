using UnityEngine;

/// <summary>
/// 출전 번호별 색상 팔레트 — 단일 출처.
/// 등번호판, (예정) 전광판 순위표, UI 등 번호 색이 필요한 모든 곳이 여기만 참조.
/// 실제 경마 새들클로스 관례 기반 (1=흰 2=검 3=빨 4=파 5=노 6=초 7=주황 8=분홍).
/// </summary>
public static class RacerColors
{
    private static readonly Color[] palette =
    {
        new Color(0.95f, 0.95f, 0.95f),   // 1 흰
        new Color(0.12f, 0.12f, 0.12f),   // 2 검
        new Color(0.86f, 0.20f, 0.18f),   // 3 빨
        new Color(0.16f, 0.42f, 0.85f),   // 4 파
        new Color(0.98f, 0.83f, 0.10f),   // 5 노
        new Color(0.15f, 0.65f, 0.35f),   // 6 초
        new Color(0.95f, 0.55f, 0.12f),   // 7 주황
        new Color(0.93f, 0.45f, 0.65f),   // 8 분홍
        new Color(0.22f, 0.75f, 0.72f),   // 9 청록
    };

    /// <summary>출전 번호(1부터)의 배경색.</summary>
    public static Color Of(int postNumber)
    {
        int idx = Mathf.Clamp(postNumber - 1, 0, palette.Length - 1);
        return palette[idx];
    }

    /// <summary>배경 밝기에 따른 대비 글자색 (밝은 판=검정 글자, 어두운 판=흰 글자).</summary>
    public static Color TextOn(int postNumber)
    {
        var c = Of(postNumber);
        float luminance = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        return luminance > 0.6f ? Color.black : Color.white;
    }
}
