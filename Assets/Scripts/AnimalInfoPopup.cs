using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 동물 상세 안내 팝업 (베팅 패널 중앙) — 안내판(B) 확정안: 출전표 행 클릭으로 열기.
/// 구조: 이 컴포넌트가 붙은 루트 = 반투명 차단막(패널 전체 스트레치, Raycast Target 켬),
/// 자식 카드에 번호 배지(RacerColors) / 이름 / 초상화(선택) / 본문.
/// 아무 곳이나 클릭하면 닫힘. 표시 전용 — 게임 상태를 읽기만 한다 (계율).
/// </summary>
public class AnimalInfoPopup : MonoBehaviour, IPointerClickHandler
{
    [Header("번호 배지 (RacerColors 팔레트)")]
    [SerializeField] private Image badgeImage;
    [SerializeField] private TMP_Text badgeText;

    [Header("내용")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text bodyText;
    [Tooltip("동물 초상화 (선택) — SO icon이 비어 있으면 자동 숨김")]
    [SerializeField] private Image portraitImage;

    public bool IsOpen => gameObject.activeSelf;

    private void Awake() => gameObject.SetActive(false);

    public void Show(Racer racer)
    {
        if (racer == null || racer.Definition == null) return;
        var def = racer.Definition;
        int post = racer.RacerId + 1;

        if (badgeImage != null) badgeImage.color = RacerColors.Of(post);
        if (badgeText != null)
        {
            badgeText.text = post.ToString();
            badgeText.color = RacerColors.TextOn(post);
        }

        if (nameText != null) nameText.text = def.displayName;

        if (portraitImage != null)
        {
            bool has = def.icon != null;
            portraitImage.gameObject.SetActive(has);
            if (has) portraitImage.sprite = def.icon;
        }

        if (bodyText != null)
            bodyText.text =
                $"최저 속도  <b>{def.minSpeed:F0}</b>\n" +
                $"최고 속도  <b>{def.maxSpeed:F0}</b>\n" +
                $"가속  <b>{def.acceleration}</b>\n\n" +
                $"<b>{SkillTuning.DisplayName(def.skill)}</b>\n" +
                $"{SkillTuning.Description(def.skill)}";

        gameObject.SetActive(true);
        transform.SetAsLastSibling();   // 패널 내 최상단 (출전표/고스트보다 위)
    }

    public void Hide() => gameObject.SetActive(false);

    /// <summary>차단막이든 카드든 어디를 눌러도 닫힘 (자식 그래픽 클릭도 여기로 올라옴).</summary>
    public void OnPointerClick(PointerEventData e) => Hide();
}
