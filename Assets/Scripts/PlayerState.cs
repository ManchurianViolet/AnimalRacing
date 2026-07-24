using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어 상태: 자산(달러)/빚, 베팅, 로드아웃, 쿨다운.</summary>
public class PlayerState
{
    public int PlayerId { get; private set; }
    public string Nickname { get; private set; }
    public bool IsBot { get; private set; }

    /// <summary>보유 자산 (달러).</summary>
    public int Money { get; private set; }
    /// <summary>미상환 원리금 (라운드마다 이자 복리 적용).</summary>
    public int Debt { get; private set; }
    /// <summary>누적 대출 원금 (한도 검사용).</summary>
    public int TotalBorrowed { get; private set; }
    /// <summary>이번 라운드에 ATM 대출을 이미 썼는가.</summary>
    public bool BorrowedThisRound { get; set; }

    /// <summary>최종 점수 = 자산 - 빚 (음수 허용 = 파산 엔딩).</summary>
    public int NetWorth => Money - Debt;

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
    }

    // ---- 경제 ----
    public void ResetEconomy(int startMoney)
    {
        Money = startMoney;
        Debt = 0;
        TotalBorrowed = 0;
        BorrowedThisRound = false;
    }

    public void AddMoney(int amount) => Money += amount;

    /// <summary>차감 시도. 잔액 부족이면 false.</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || Money < amount) return false;
        Money -= amount;
        return true;
    }

    public void Borrow(int amount)
    {
        Money += amount;
        Debt += amount;
        TotalBorrowed += amount;
    }

    /// <summary>라운드 경과 이자 (복리). rate 0.3 = +30%.</summary>
    public void ApplyInterest(float rate)
    {
        if (Debt > 0) Debt = Mathf.CeilToInt(Debt * (1f + rate));
    }

    // ---- 네트워크 거울 반영 (클라 전용 — 진실은 호스트) ----
    public void ApplyNetworkEconomy(int money, int debt, bool borrowedThisRound)
    {
        Money = money;
        Debt = debt;
        BorrowedThisRound = borrowedThisRound;
    }

    public void ApplyNetworkItems(int boostCount, int slowCount,
                                  ItemDefinition boostDef, ItemDefinition slowDef)
    {
        items.Clear();
        for (int i = 0; i < boostCount; i++) items.Add(boostDef);
        for (int i = 0; i < slowCount; i++)  items.Add(slowDef);
    }

    // ---- 베팅/아이템 ----
    public void SetBet(BetTicket bet) => Bet = bet;

    /// <summary>라운드 시작 시 호출 — 지난 라운드 베팅 무효화 (firstId=lastId=-1 = IsValid false).</summary>
    public void ClearBet() => Bet = new BetTicket { firstId = -1, lastId = -1 };

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
/// 베팅 티켓: 픽 + 금액. 우승/꼴등 둘 다 최소 $1 필수.
/// 비밀 정보 = 픽 + 금액 전부.
/// </summary>
[System.Serializable]
public struct BetTicket
{
    public int firstId;
    public int lastId;
    public int firstAmount;
    public int lastAmount;

    public int Total => firstAmount + lastAmount;

    public bool IsValid(int racerCount) =>
        firstId != lastId &&
        firstId >= 0 && firstId < racerCount &&
        lastId >= 0 && lastId < racerCount &&
        firstAmount >= 10 && firstAmount % 10 == 0 &&
        lastAmount >= 10 && lastAmount % 10 == 0;
}
