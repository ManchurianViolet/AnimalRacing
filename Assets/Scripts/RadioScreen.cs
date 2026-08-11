using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 무전기 앞면 디스플레이(주황 패널) 위에 얹히는 LCD 텍스트.
///
/// 연출: 평소엔 "0000"이 떠 있다가, 무전기를 쓰면 그 자리가 <b>한 글자씩 대상 동물 이름으로 바뀐다</b>
/// (예: 000 → 호00 → 호랑0 → 호랑이). 5초 지연 발동이 화면에서 카운트다운처럼 읽힌다.
///
/// [멀티] 네트워크 추가 통신 0 — 아이템 사용은 게이트웨이가 이미 OnItemUsed로 전 클라에 중계하므로
/// 각자 그 이벤트를 받아 로컬로 재생한다 (BoostDustFx와 같은 철학).
/// </summary>
public class RadioScreen : MonoBehaviour
{
    [Tooltip("발동 대기 중이 아닐 때 띄워둘 문자열")]
    [SerializeField] private string idleText = "0000";

    [Tooltip("아직 안 밝혀진 자리를 채울 글자")]
    [SerializeField] private string blankChar = "0";

    [Tooltip("이 화면이 붙은 무전기의 종류 — 자기 것으로 쓴 아이템에만 반응한다")]
    [SerializeField] private ItemKind kind = ItemKind.SkillTrigger;

    [Tooltip("처형 무전기가 겨눌 대상이 없을 때(레이스 전/후) 띄울 문구")]
    [SerializeField] private string executeIdleText = "----";

    [Header("발동 완료 연출")]
    [Tooltip("이름이 다 뜬 뒤 램프가 깜빡이는 횟수")]
    [SerializeField] private int blinkCount = 3;
    [Tooltip("램프 점등 발광 강도 — 베이스 컬러만으론 어두운 방에서 티가 안 나서 에미션으로 빛낸다 (블룸 대상, 실측 14)")]
    [SerializeField] private float lampGlowIntensity = 14f;
    [Tooltip("한 번 깜빡이는 데 걸리는 시간 (켜짐 + 꺼짐, 초)")]
    [SerializeField] private float blinkPeriod = 0.24f;
    [Tooltip("깜빡임이 끝나면 무전기를 치우고 맨손으로 돌아간다 (다 쓴 소모품이므로)")]
    [SerializeField] private bool stowWhenDone = true;

    private TMP_Text label;
    private RaceManager raceManager;
    private Coroutine routine;

    /// <summary>이 화면의 주인이 로컬 플레이어인가 — 남이 쓴 아이템에 내 화면이 반응하면 안 된다.</summary>
    private bool IsMine
    {
        get
        {
            if (owner == null) owner = GetComponentInParent<PlayerEquipment>();
            return owner != null && owner.IsLocalAvatar;
        }
    }

    private PlayerEquipment owner;

    /// <summary>소품이 코드 생성이라 인스펙터가 없다 — 만든 쪽(PlayerEquipment)이 배선해 준다.</summary>
    public void Init(ItemKind k, PlayerEquipment equipment, Renderer lamp, Color lampOnColor)
    {
        kind = k;
        owner = equipment;
        lampRenderer = lamp;
        lampOn = lampOnColor;
        lampOff = lampOnColor * 0.18f;
        lampOff.a = 1f;
        // sharedMaterial을 그대로 쓰면 두 무전기가 같은 머티리얼을 물고 흔들린다 — 인스턴스로 분리
        if (lampRenderer != null) lampMat = lampRenderer.material;
    }

    private Renderer lampRenderer;
    private Material lampMat;
    private Color lampOn = Color.yellow, lampOff = Color.black;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        ShowIdle();
    }

    private void OnEnable()
    {
        GameEvents.OnItemUsed += HandleItemUsed;
        ShowIdle();
    }

    private void OnDisable()
    {
        GameEvents.OnItemUsed -= HandleItemUsed;
        // ⚠ 내가 돌리던 것만 끈다 — 무조건 끄면 원격 아바타가 슬롯을 바꿀 때 내 연출 플래그까지 꺼진다
        if (routine != null) { StopCoroutine(routine); routine = null; AnyPlaying = false; }
    }

    /// <summary>
    /// 처형 무전기는 대상이 "발동 순간의 꼴등"이라 미리 확정할 수 없다.
    /// 들고 있는 내내 지금 터지면 죽을 동물을 실시간으로 띄운다 — 발동 후 5초 동안도 계속 갱신되므로
    /// 막판에 순위가 뒤집히면 화면의 이름도 그대로 따라 바뀐다.
    /// </summary>
    private void Update()
    {
        if (kind != ItemKind.Execute || !IsMine) return;
        if (blinking) return;                 // 확정 후 깜빡이는 동안엔 이름을 고정한다
        SetText(CurrentExecuteTarget() ?? executeIdleText);
    }

    private string CurrentExecuteTarget()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentPhase != GamePhase.Racing) return null;
        if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
        if (raceManager == null) return null;

        var victim = raceManager.GetLastPlaceRacer();   // 실제 처형과 같은 단일 출처
        return victim != null && victim.Definition != null ? victim.Definition.displayName : null;
    }

    private void HandleItemUsed(int playerId, ItemDefinition item, int racerId)
    {
        if (item == null || item.kind != kind) return;
        if (!IsMine || playerId != NetworkPlayers.LocalPlayerId) return;

        float duration = GameManager.Instance != null && GameManager.Instance.Config != null
            ? GameManager.Instance.Config.radioDelaySeconds
            : 5f;

        if (routine != null) StopCoroutine(routine);

        if (kind == ItemKind.Execute)
        {
            // 이름은 Update가 실시간으로 계속 쓴다 — 여기선 5초를 세다가 확정 연출만 한다
            routine = StartCoroutine(ExecuteCountdown(duration));
            return;
        }

        string target = ResolveRacerName(racerId);
        if (string.IsNullOrEmpty(target)) return;
        routine = StartCoroutine(TypeOut(target, duration));
    }

    /// <summary>처형: 5초 동안 실시간 표시를 유지하다가, 확정된 이름을 고정하고 램프를 깜빡인다.</summary>
    private IEnumerator ExecuteCountdown(float duration)
    {
        AnyPlaying = true;
        yield return new WaitForSeconds(duration);

        // 이 순간의 대상이 곧 실제로 죽는 동물 — 여기서부터 이름을 얼린다
        blinking = true;
        SetText(CurrentExecuteTarget() ?? executeIdleText);

        for (int i = 0; i < blinkCount; i++)
        {
            SetLamp(false);
            yield return new WaitForSeconds(blinkPeriod * 0.5f);
            SetLamp(true);
            yield return new WaitForSeconds(blinkPeriod * 0.5f);
        }

        blinking = false;
        routine = null;
        AnyPlaying = false;

        if (stowWhenDone && owner != null && owner.IsLocalAvatar)
            owner.Select(PlayerEquipment.SlotNone);
    }

    private bool blinking;

    private string ResolveRacerName(int racerId)
    {
        if (raceManager == null) raceManager = FindFirstObjectByType<RaceManager>();
        if (raceManager == null) return null;

        var racer = raceManager.GetRacer(racerId);
        return racer != null && racer.Definition != null ? racer.Definition.displayName : null;
    }

    /// <summary>
    /// 지금 어느 무전기든 발동 연출 중인가.
    /// 아이템은 쏘는 순간 이미 소모되므로, 이 플래그가 없으면 "재고 0 → 자동 수납"이
    /// 5초 연출을 첫 프레임에 끊어버린다.
    /// </summary>
    public static bool AnyPlaying { get; private set; }

    /// <summary>바깥에서 직접 재생 (검증용 + 나중에 다른 연출에서 재사용).</summary>
    public void PlayTyping(string text, float duration)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(TypeOut(text, duration));
    }

    /// <summary>duration에 걸쳐 빈칸을 한 글자씩 목표 문자열로 채운다.</summary>
    private IEnumerator TypeOut(string text, float duration)
    {
        int n = text.Length;
        if (n == 0) { ShowIdle(); yield break; }

        AnyPlaying = true;

        // 글자 사이 간격 — 마지막 글자가 딱 duration에 맞아떨어지게 나눈다
        float step = duration / n;

        for (int i = 0; i <= n; i++)
        {
            SetText(text.Substring(0, i) + Repeat(blankChar, n - i));
            // 글자가 하나 뜰 때마다 (2D — 이 연출은 내 손의 화면에서만 도는 것이라 거리 감쇠가 의미 없다)
            if (i > 0) SoundManager.PlaySfx(SfxId.RadioTyping);
            if (i < n) yield return new WaitForSeconds(step);
        }

        // 이름이 다 뜨면 램프가 세 번 깜빡 — "전송 완료" 신호
        for (int i = 0; i < blinkCount; i++)
        {
            SetLamp(false);
            yield return new WaitForSeconds(blinkPeriod * 0.5f);
            SetLamp(true);
            yield return new WaitForSeconds(blinkPeriod * 0.5f);
        }

        ShowIdle();
        routine = null;
        AnyPlaying = false;

        // 다 쓴 소모품이므로 손에서 치운다 — Select는 RPC를 타므로 남의 화면에서도 같이 사라진다
        if (stowWhenDone && owner != null && owner.IsLocalAvatar)
            owner.Select(PlayerEquipment.SlotNone);
    }

    private void SetLamp(bool on)
    {
        if (lampMat == null) return;
        var c = on ? lampOn : lampOff;
        if (lampMat.HasProperty("_BaseColor")) lampMat.SetColor("_BaseColor", c);
        else lampMat.color = c;

        // 점등은 진짜 발광으로 — HDR 에미션이라 밝은 곳에서도 확실히 빛나고, 블룸이 빛무리를 만든다
        if (lampMat.HasProperty("_EmissionColor"))
        {
            if (on)
            {
                lampMat.EnableKeyword("_EMISSION");
                lampMat.SetColor("_EmissionColor", lampOn * lampGlowIntensity);
            }
            else
            {
                lampMat.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    private static string Repeat(string s, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length * count);
        for (int i = 0; i < count; i++) sb.Append(s);
        return sb.ToString();
    }

    public void ShowIdle() => SetText(idleText);

    private void SetText(string s)
    {
        if (label != null) label.text = s;
    }
}
