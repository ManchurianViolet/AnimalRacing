using UnityEngine;

/// <summary>
/// 몬테카를로 배당 계산: 이번 출전 라인업으로 가상 레이스를 수식만으로 N회 고속 시뮬.
/// 물리/조향/아이템 제외 — "아이템 개입 전 자연 상태" 확률이 배당의 정의.
/// 시뮬 모델: 리롤 주기마다 범위 내 속도 랜덤 + 가속에 따른 수렴을 이산 근사.
/// 스킬 반영: 말/개/치킨/고양이/호랑이/펭귄 포함 — 배당이 스킬을 아는 도박장.
/// 사슴(경계 본능)만 제외: 플레이어 아이템 의존이라 아이템과 같은 취급으로 시뮬 밖.
/// </summary>
public static class OddsCalculator
{
    public struct AnimalOdds
    {
        public float winProbability;
        public float lastProbability;
        public float winOdds;    // 지급 배수 (원금 포함)
        public float lastOdds;
    }

    private const float HouseMargin = 0.9f;   // 배당 = 확률역수 × 0.9 (도박장 마진 10%)
    private const float MinOdds = 1.1f;
    private const float MaxOdds = 50f;

    /// <param name="lineup">이번 출전 동물들 (중복 포함, 순서 = racerId)</param>
    /// <param name="trackLength">트랙 총 길이</param>
    /// <param name="simCount">시뮬 횟수 (1000 권장)</param>
    public static AnimalOdds[] Calculate(AnimalDefinition[] lineup, float trackLength, int simCount = 1000)
    {
        int n = lineup.Length;
        int[] winCount = new int[n];
        int[] lastCount = new int[n];

        const float dt = 0.25f;   // 굵은 타임스텝 (정밀 물리 불필요 — 통계만 맞으면 됨)
        float[] progress = new float[n];
        float[] speed = new float[n];
        float[] target = new float[n];
        float[] timer = new float[n];

        // 스킬 상태
        float[] triggerAt = new float[n];   // 액티브 발동 진행률
        bool[] consumed = new bool[n];
        float[] stunLeft = new float[n];
        float[] whimLeft = new float[n];
        float[] whimMult = new float[n];

        for (int s = 0; s < simCount; s++)
        {
            for (int i = 0; i < n; i++)
            {
                progress[i] = 0f;
                target[i] = Random.Range(lineup[i].MinSpeedMs, lineup[i].MaxSpeedMs);
                speed[i] = target[i];
                timer[i] = lineup[i].speedRerollInterval;

                triggerAt[i] = Random.Range(SkillTuning.ActiveMinRatio, SkillTuning.ActiveMaxRatio);
                consumed[i] = false;
                stunLeft[i] = 0f;
                whimLeft[i] = 0f;
                whimMult[i] = 1f;
            }

            int finished = 0, firstId = -1, lastId = -1;

            // 안전 상한: 트랙길이/최저속 기준 넉넉히
            int maxSteps = Mathf.CeilToInt(trackLength / 1.5f / dt) + 200;
            for (int step = 0; step < maxSteps && finished < n; step++)
            {
                float raceTime = step * dt;

                // 꼴등 판정 (개)
                int lastIdx = -1; float lastProg = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (progress[i] < trackLength && progress[i] < lastProg)
                    { lastProg = progress[i]; lastIdx = i; }

                for (int i = 0; i < n; i++)
                {
                    if (progress[i] >= trackLength) continue;
                    var skill = lineup[i].skill;

                    // 액티브 발동 (호랑이/고양이)
                    if (!consumed[i] && progress[i] / trackLength >= triggerAt[i])
                    {
                        consumed[i] = true;
                        if (skill == AnimalSkill.Ambush)
                        {
                            int prey = -1; float best = float.MaxValue;   // 사거리 무제한
                            for (int j = 0; j < n; j++)
                            {
                                if (j == i || progress[j] >= trackLength) continue;
                                float d = Mathf.Abs(progress[j] - progress[i]);
                                if (d < best) { best = d; prey = j; }
                            }
                            if (prey >= 0 && lineup[prey].skill != AnimalSkill.Apathy)
                                stunLeft[prey] = SkillTuning.AmbushStun;
                        }
                        else if (skill == AnimalSkill.Whim)
                        {
                            whimLeft[i] = SkillTuning.WhimDuration;
                            whimMult[i] = Random.value < 0.5f ? SkillTuning.WhimUp : SkillTuning.WhimDown;
                        }
                    }

                    // 스턴: 정지 + 속도 리셋 (풀리면 가속으로 재출발 — 본 시뮬과 동일 감각)
                    if (stunLeft[i] > 0f)
                    {
                        stunLeft[i] -= dt;
                        speed[i] = 0f;
                        continue;
                    }

                    timer[i] -= dt;
                    if (timer[i] <= 0f)
                    {
                        target[i] = Random.Range(lineup[i].MinSpeedMs, lineup[i].MaxSpeedMs);
                        timer[i] = lineup[i].speedRerollInterval;
                    }
                    // 가속 게인 기반 추적 (모터 근사 — 가속 스탯이 배당에 반영되는 지점)
                    speed[i] += (target[i] - speed[i])
                              * Mathf.Min(1f, lineup[i].AccelGain * dt);

                    // 패시브/액티브 배율
                    float mult = 1f;
                    if (skill == AnimalSkill.FinalSprint
                        && progress[i] / trackLength >= SkillTuning.FinalSprintZone)
                        mult *= SkillTuning.FinalSprintMult;
                    else if (skill == AnimalSkill.Loyalty && i == lastIdx)
                        mult *= SkillTuning.LoyaltyMult;
                    else if (skill == AnimalSkill.Dash)
                    {
                        if (raceTime < SkillTuning.DashTime) mult *= SkillTuning.DashMult;
                        else if (raceTime < SkillTuning.DashTime + SkillTuning.DashFatigueTime)
                            mult *= SkillTuning.DashFatigueMult;
                    }
                    if (whimLeft[i] > 0f) { whimLeft[i] -= dt; mult *= whimMult[i]; }

                    progress[i] += speed[i] * mult * dt;

                    if (progress[i] >= trackLength)
                    {
                        finished++;
                        if (firstId < 0) firstId = i;
                        if (finished == n) lastId = i;
                    }
                }
            }

            if (firstId >= 0) winCount[firstId]++;
            if (lastId >= 0) lastCount[lastId]++;
        }

        var result = new AnimalOdds[n];
        for (int i = 0; i < n; i++)
        {
            float pWin = Mathf.Max(0.001f, (float)winCount[i] / simCount);
            float pLast = Mathf.Max(0.001f, (float)lastCount[i] / simCount);
            result[i] = new AnimalOdds
            {
                winProbability = pWin,
                lastProbability = pLast,
                winOdds = Mathf.Clamp(HouseMargin / pWin, MinOdds, MaxOdds),
                lastOdds = Mathf.Clamp(HouseMargin / pLast, MinOdds, MaxOdds)
            };
        }
        return result;
    }
}
