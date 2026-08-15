using UnityEngine;

/// <summary>
/// 관찰 전시대 (테이블 위 투명 박스).
/// 피규어를 올리면 그 안에서 동물이 달리고, 위 스크린(RoomMonitorDetail)에 정보가 뜬다.
/// 예측과는 무관 — 살펴보는 용도 (BetBox와 달리 등수가 없다).
/// </summary>
public class InspectStand : MonoBehaviour
{
    [Tooltip("피규어가 놓일 자리 (박스 안 바닥). 비우면 이 오브젝트 원점")]
    [SerializeField] private Transform slot;

    /// <summary>올려져 있는 피규어 (없으면 null) — 스크린이 이걸 읽어 정보를 띄운다.</summary>
    public BetFigurine Current { get; private set; }

    public string PlaceHint => Loc.Get("bet.inspect");

    private Transform Slot => slot != null ? slot : transform;

    /// <summary>런타임 셋업용 (씬 조립 스크립트가 호출).</summary>
    public void Setup(Transform slot) => this.slot = slot;

    /// <summary>피규어를 올린다. 이미 있던 건 선반으로 돌려보낸다.</summary>
    public void Place(BetFigurine fig)
    {
        if (Current != null && Current != fig) Current.ReturnHome();

        Current = fig;
        fig.InStand = this;
        fig.transform.SetParent(Slot, false);
        fig.transform.localPosition = Vector3.zero;
        fig.transform.localRotation = Quaternion.identity;
        // 유리 진열장 안에서는 추가 축소 — 예측 상자와 같은 규칙 (GameConfig.figurineCaseScale)
        var cfg = GameManager.Instance != null ? GameManager.Instance.Config : null;
        float caseScale = cfg != null ? cfg.figurineCaseScale : 0.8f;
        fig.transform.localScale = Vector3.one * fig.ShelfScale * caseScale;
        fig.SetHeld(false);                                              // 받침대 복원
        if (fig.PickCollider != null) fig.PickCollider.enabled = true;   // 다시 집을 수 있게
        fig.SetRunning(true);                                            // 전시대 위에서만 달린다
    }

    /// <summary>전시대에서 내린다 (호출자가 손/상자/선반으로 옮김).</summary>
    public BetFigurine Take()
    {
        var fig = Current;
        Current = null;
        if (fig != null) { fig.InStand = null; fig.SetRunning(false); }
        return fig;
    }
}
