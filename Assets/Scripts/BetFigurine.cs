using UnityEngine;

/// <summary>
/// 베팅 방의 동물 피규어 (내 방에만 로컬 생성 — 비밀 유지 + 네트워크 0).
/// 출전 동물 하나를 대표하며 선반 ↔ 손 ↔ 상자를 오간다.
/// 생성/스트립은 BettingRoomManager가, 집기/놓기는 FigurineBetting이 담당.
/// </summary>
public class BetFigurine : MonoBehaviour
{
    public int RacerId { get; private set; } = -1;
    public int PostNumber { get; private set; }          // 출전 번호 (1부터)
    public string AnimalName { get; private set; }

    /// <summary>원본 동물 정의 — HUD 손 칸 아이콘/모니터 상세가 참조.</summary>
    public AnimalDefinition Def { get; private set; }

    /// <summary>조준 안내문: "4번 펭귄"</summary>
    public string HoverName => $"{PostNumber}번 {AnimalName}";

    /// <summary>선반 위 제자리 (BettingRoomManager가 만든 슬롯 앵커).</summary>
    public Transform HomeSlot { get; private set; }

    /// <summary>선반/상자에 놓였을 때의 크기 — 손에 쥘 땐 더 작게 줄였다가 이 값으로 되돌린다.</summary>
    public float ShelfScale { get; private set; } = 1f;

    /// <summary>지금 들어가 있는 상자 (없으면 null).</summary>
    public BetBox InBox { get; set; }

    /// <summary>지금 올라가 있는 관찰 전시대 (없으면 null).</summary>
    public InspectStand InStand { get; set; }

    /// <summary>집기 레이캐스트용 콜라이더 — 손에 들리면 끈다.</summary>
    public Collider PickCollider { get; private set; }

    public void Init(int racerId, int postNumber, AnimalDefinition def, Transform homeSlot, Collider pickCollider)
    {
        RacerId = racerId;
        PostNumber = postNumber;
        Def = def;
        AnimalName = def != null ? def.displayName : "?";
        HomeSlot = homeSlot;
        PickCollider = pickCollider;
        ShelfScale = transform.localScale.x;

        // 달리기 연출용 — 평소엔 꺼둔 채(정지 포즈) 전시대에서만 켠다
        anim = GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            body = anim.transform;
            anim.applyRootMotion = false;
            anim.enabled = false;
        }
    }

    private Animator anim;
    private Transform body;      // 프리팹 루트 — 루트 모션 누적을 매 프레임 되돌린다
    private bool running;

    /// <summary>전시대 위에서 달리게 한다 (레이스와 같은 애니 규약: Vert/State).</summary>
    public void SetRunning(bool on)
    {
        running = on;
        if (anim == null) return;
        anim.enabled = on;
        if (!on) return;
        anim.applyRootMotion = false;
        anim.SetFloat("Vert", 1f);
        anim.SetFloat("State", 1f);
        anim.speed = 1.1f;
    }

    // 달리기 클립의 루트 모션이 프리팹 루트에 누적돼 뼈대가 전시대 밖으로 달려나간다
    // (레이스에선 RacerMotor가 매 프레임 위치를 다시 써서 상쇄되던 것 — §11 법칙)
    private void LateUpdate()
    {
        if (!running || body == null) return;
        body.localPosition = Vector3.zero;
        body.localRotation = Quaternion.identity;
    }

    /// <summary>선반 제자리로 복귀 (상자/손 어디에 있었든).</summary>
    public void ReturnHome()
    {
        if (InBox != null && InBox.Current == this) InBox.Current = null;
        InBox = null;
        if (InStand != null) InStand.Take();
        SetRunning(false);
        transform.SetParent(HomeSlot, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * ShelfScale;   // 손에서 줄여둔 크기 원복
        if (PickCollider != null) PickCollider.enabled = true;
    }
}
