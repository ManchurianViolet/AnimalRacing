using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어 상태: 포인트, 예측(1·2·3등), 로드아웃, 쿨다운.</summary>
public class PlayerState
{
    public int PlayerId { get; private set; }
    public string Nickname { get; private set; }
    public bool IsBot { get; private set; }

    /// <summary>누적 포인트 (라운드마다 예측 적중으로 획득, 최다 = 우승).</summary>
    public int Points { get; private set; }

    public BetTicket Bet { get; private set; }

    private readonly List<ItemDefinition> items = new();
    public IReadOnlyList<ItemDefinition> Items => items;

    private float cooldownEndTime = -1f;
    private float lastCooldownDuration = 1f;
    public bool IsCooldownReady => Time.time >= cooldownEndTime;
    public float CooldownRemaining => Mathf.Max(0f, cooldownEndTime - Time.time);
    public float CooldownRatio => lastCooldownDuration <= 0f ? 0f
        : Mathf.Clamp01(CooldownRemaining / lastCooldownDuration);

    public PlayerState(int id, string nickname, bool isBot = false)
    {
        PlayerId = id;
        Nickname = nickname;
        IsBot = isBot;
        ClearBet();
    }

    // ---- 포인트 ----
    public void ResetPoints() => Points = 0;
    public void AddPoints(int amount) => Points += amount;

    // ---- 네트워크 거울 반영 (클라 전용 — 진실은 호스트) ----
    public void ApplyNetworkEconomy(int points) => Points = points;

    public void ApplyNetworkItems(int boostCount, int slowCount,
                                  ItemDefinition boostDef, ItemDefinition slowDef)
    {
        items.Clear();
        for (int i = 0; i < boostCount; i++) items.Add(boostDef);
        for (int i = 0; i < slowCount; i++)  items.Add(slowDef);
    }

    // ---- 예측/아이템 ----
    public void SetBet(BetTicket bet) => Bet = bet;

    /// <summary>라운드 시작 시 호출 — 지난 라운드 예측 무효화.</summary>
    public void ClearBet() => Bet = new BetTicket { firstId = -1, secondId = -1, thirdId = -1 };

    public void SetLoadout(IEnumerable<ItemDefinition> loadout)
    {
        items.Clear();
        items.AddRange(loadout);
    }

    public bool HasItem(ItemDefinition item) => items.Contains(item);
    public void ConsumeItem(ItemDefinition item) => items.Remove(item);

    public void StartCooldown(float seconds)
    {
        cooldownEndTime = Time.time + seconds;
        lastCooldownDuration = seconds;
    }
}

/// <summary>
/// 예측 티켓: 1등·2등·3등 예상 (전부 필수, 서로 달라야 함).
/// 비밀 정보 — 정산 공개 전까지 남에게 전송되지 않음.
/// </summary>
[System.Serializable]
public struct BetTicket
{
    public int firstId;
    public int secondId;
    public int thirdId;

    public bool IsValid(int racerCount) =>
        firstId >= 0 && firstId < racerCount &&
        secondId >= 0 && secondId < racerCount &&
        thirdId >= 0 && thirdId < racerCount &&
        firstId != secondId && firstId != thirdId && secondId != thirdId;
}
