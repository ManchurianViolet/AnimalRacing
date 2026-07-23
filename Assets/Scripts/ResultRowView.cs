using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 정산판 한 행: [순위] [레인번호] [아이콘] [이름] ...... [베팅 칩들]
/// 왼쪽에서 슬라이드 인. 1위 = 금색, 꼴등 = 붉은 강조.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ResultRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Image laneBox;
    [SerializeField] private TMP_Text laneText;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private GameObject lastTag;       // "꼴등" 뱃지 (선택)
    [SerializeField] private Image rowBackground;      // 행 배경 (선택)
    [SerializeField] private Transform chipContainer;  // 칩들이 붙는 부모 (HorizontalLayoutGroup)

    public Transform ChipContainer => chipContainer;

    private static readonly Color GoldColor = new Color32(0xFF, 0xD7, 0x00, 0xFF);
    private static readonly Color RedColor  = new Color32(0xF0, 0x99, 0x9B, 0xFF);
    private static readonly Color TopBg     = new Color32(0x1E, 0x1A, 0x10, 0xFF);
    private static readonly Color LastBg    = new Color32(0x1E, 0x12, 0x14, 0xFF);

    private CanvasGroup cg;
    private RectTransform rt;

    public void Bind(int rank, Racer racer, bool isTop, bool isLast)
    {
        cg = GetComponent<CanvasGroup>();
        rt = (RectTransform)transform;

        if (rankText != null)
        {
            rankText.text = rank.ToString();
            rankText.color = isTop ? GoldColor : isLast ? RedColor : Color.white;
        }
        if (laneText != null) laneText.text = (racer.RacerId + 1).ToString();
        if (laneBox != null)
            laneBox.color = Color.HSVToRGB((racer.RacerId * 0.137f) % 1f, 0.55f, 0.75f);
        if (iconImage != null && racer.Definition.icon != null)
            iconImage.sprite = racer.Definition.icon;
        if (nameText != null) nameText.text = racer.Definition.displayName;
        if (lastTag != null) lastTag.SetActive(isLast);
        if (rowBackground != null)
            rowBackground.color = isTop ? TopBg : isLast ? LastBg
                                : new Color(0, 0, 0, 0);

        // 등장 전 상태: 투명 + 왼쪽 40px
        cg.alpha = 0f;
    }

    /// <summary>슬라이드 인 (패널 코루틴이 순서대로 호출).</summary>
    public IEnumerator Appear(float seconds = 0.35f)
    {
        Vector2 end = rt.anchoredPosition;
        Vector2 start = end + new Vector2(-40f, 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / seconds;
            float s = Mathf.SmoothStep(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(start, end, s);
            cg.alpha = s;
            yield return null;
        }
        rt.anchoredPosition = end;
        cg.alpha = 1f;
    }
}
