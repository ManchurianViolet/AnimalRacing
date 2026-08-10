using UnityEngine;

/// <summary>
/// 베팅 방의 예측 상자 (금/은/동, 반투명 — 안의 피규어가 보인다).
/// 피규어를 담으면 그 동물이 해당 슬롯 예측이 된다. 상자 자체는 순수 그릇 —
/// 검증/제출은 RoomConfirmButton이 담당.
/// </summary>
public class BetBox : MonoBehaviour
{
    [Tooltip("예측 슬롯: 0=1등 / 1=2등 이상 / 2=3등 이상")]
    [SerializeField] private int rank;

    [Tooltip("피규어가 놓일 자리 (상자 안 바닥). 비우면 상자 원점")]
    [SerializeField] private Transform slot;

    /// <summary>담겨 있는 피규어 (없으면 null).</summary>
    public BetFigurine Current { get; set; }

    public int Rank => rank;

    /// <summary>들고 있는 채로 조준했을 때 안내문.</summary>
    public string PlaceHint =>
        rank == 0 ? "1등에 예측하기" :
        rank == 1 ? "2등 이상에 예측하기" : "3등 이상에 예측하기";

    private Transform Slot => slot != null ? slot : transform;

    /// <summary>런타임 셋업용 (씬 조립 스크립트가 호출).</summary>
    public void Setup(int rank, Transform slot)
    {
        this.rank = rank;
        this.slot = slot;
    }

    /// <summary>피규어를 상자에 넣는다. 이미 있던 피규어는 선반으로 돌려보낸다.</summary>
    public void Place(BetFigurine fig)
    {
        if (Current != null && Current != fig) Current.ReturnHome();

        if (fig.InStand != null) fig.InStand.Take();   // 전시대에서 가져온 경우 정리
        Current = fig;
        fig.InBox = this;
        fig.transform.SetParent(Slot, false);
        fig.transform.localPosition = Vector3.zero;
        fig.transform.localRotation = Quaternion.identity;
        fig.transform.localScale = Vector3.one * fig.ShelfScale;         // 손에서 줄여둔 크기 원복
        fig.SetHeld(false);                                              // 받침대 복원
        if (fig.PickCollider != null) fig.PickCollider.enabled = true;   // 다시 집을 수 있게
    }

    /// <summary>상자에서 피규어를 꺼낸다 (호출자가 손/선반으로 옮김).</summary>
    public BetFigurine Take()
    {
        var fig = Current;
        Current = null;
        if (fig != null) fig.InBox = null;
        return fig;
    }
}
