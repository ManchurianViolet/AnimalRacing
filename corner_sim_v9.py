# -*- coding: utf-8 -*-
"""
corner_sim_v9 — v9 스킬 개편(2바퀴·9마리·액티브 4종) 기준 몬테카를로.
게임 물리 미러 (§3-4 사양): dt=0.05, 정지 출발, 리롤 MoveTowards 수렴 (max-min)/1.5초,
거버너 dv=clip(gain×(cap−v), ±maxAssistAccel), 제동 비대칭(v>cap+0.15 → gain=max(4.5, own)),
코너 상한 6m 창. 입력은 BalanceExport.json (상수 손 이식 금지).

v9 스킬 미러:
  말   FinalSprint  진행 85%+ ×1.20 (패시브)
  개   Loyalty      꼴등인 동안 ×1.30 (패시브)
  펭귄 Apathy       외부 효과 면역 (여기선 포효만 해당)
  호랑이 Roar       1회, 자신 제외 전원 5초 ×0.5 (펭귄/비행 면역)
  사슴 Rudolph      1회, "속도×5초" 앞 트랙 지점까지 직선(현) 비행 — 중심선 좌표 재구성으로 현 길이 산출
  고양이 CatWalk    1회, 8초 코너 감속 무시
  치킨 Dash         1회, 8초 ×1.5 (슬럼프 없음)
액티브 자동 발동: 진행률 uniform(0.15, 0.85). 무전기/아이템/플레이어 개입 없음 (순수 베이스라인).

미반영(rough 명시): 횡이동·회피·정체(자리다툼), 출발 그리드 오프셋, 무전기 개입,
사슴 비행의 지형 충돌/착지 미세 조정. 중심선은 yaw 적분 재구성이라 누적 오차 존재(폐합 오차 출력).
"""
import json, math, time
import numpy as np

rng = np.random.default_rng(11)

# ---------- 입력 ----------
with open("BalanceExport.json", encoding="utf-8") as f:
    D = json.load(f)

CFG, SK = D["config"], D["skills"]
L_PATH = D["trackLength"]
LAPS = 2
L_TOTAL = L_PATH * LAPS
FINISH = L_TOTAL - 0.1
DT = 0.05
N_RACERS = 9
R = 12000            # 판 수 (rough)
TMAX = 480.0

yaw = np.array(D["yawPerMeter"], dtype=np.float64)       # 도/m (부호 = 우회전+)
M = len(yaw)

# ---------- 중심선 재구성 (사슴 현 길이용) ----------
heading = np.cumsum(np.deg2rad(yaw))
hx = np.cos(np.concatenate([[0.0], heading[:-1]]))
hy = np.sin(np.concatenate([[0.0], heading[:-1]]))
cx = np.concatenate([[0.0], np.cumsum(hx)])
cy = np.concatenate([[0.0], np.cumsum(hy)])              # cx[m], cy[m] = m미터 지점 좌표
closure = math.hypot(cx[M] - cx[0], cy[M] - cy[0])

# ---------- 코너 감속 프로파일 (전방 6m 창) ----------
sense = int(round(CFG["cornerSenseAhead"]))
yaw_ext = np.concatenate([yaw, yaw[:sense]])
ahead = np.array([abs(yaw_ext[m:m+sense].sum()) / sense for m in range(M)])
senseT = np.clip(ahead / CFG["curvatureSaturation"], 0.0, 1.0)
CF = 1.0 - senseT * CFG["cornerDecelRate"] if CFG["cornerDecelEnabled"] else np.ones(M)

# ---------- 동물 ----------
A = D["animals"]                     # 순서 고정: 개,고양이,사슴,말,펭귄,호랑이,치킨
NAMES = [a["name"] for a in A]
S = len(A)
u2ms = CFG["speedUnitToMs"]
vmin = np.array([a["minSpeed"] for a in A]) * u2ms
vmax = np.array([a["maxSpeed"] for a in A]) * u2ms
gain = CFG["accelBaseGain"] + np.array([a["acceleration"] for a in A]) * CFG["accelUnitGain"]
reroll_iv = np.array([a["rerollInterval"] for a in A])
IDX = {a["skill"]: i for i, a in enumerate(A)}
I_DOG, I_CAT, I_DEER, I_HORSE, I_PENG, I_TIGER, I_CHICK = (
    IDX["Loyalty"], IDX["CatWalk"], IDX["Rudolph"], IDX["FinalSprint"],
    IDX["Apathy"], IDX["Roar"], IDX["Dash"])

# ---------- 라인업: 7종 + 무작위 중복 2 ----------
sp = np.empty((R, N_RACERS), dtype=np.int64)
sp[:, :S] = np.arange(S)
sp[:, S:] = rng.integers(0, S, size=(R, N_RACERS - S))

Vmin, Vmax, Gain, RerollIv = vmin[sp], vmax[sp], gain[sp], reroll_iv[sp]

# ---------- 상태 ----------
prog = np.zeros((R, N_RACERS))
v = np.zeros((R, N_RACERS))
rolled = rng.uniform(Vmin, Vmax)
smoothed = rolled.copy()
reroll_t = RerollIv.copy()
boost_end = np.full((R, N_RACERS), -1.0)   # 치킨 폭주
slow_end = np.full((R, N_RACERS), -1.0)    # 포효 피격
cat_end = np.full((R, N_RACERS), -1.0)     # 발놀림
trig = rng.uniform(SK["activeMinRatio"], SK["activeMaxRatio"], size=(R, N_RACERS))
used = ~np.isin(sp, [I_TIGER, I_DEER, I_CAT, I_CHICK])   # 패시브는 발동 개념 없음
flying = np.zeros((R, N_RACERS), dtype=bool)
fly_t0 = np.zeros((R, N_RACERS)); fly_dur = np.ones((R, N_RACERS))
fly_p0 = np.zeros((R, N_RACERS)); fly_p1 = np.zeros((R, N_RACERS))
fly_v = np.zeros((R, N_RACERS))
finished = np.zeros((R, N_RACERS), dtype=bool)
fin_key = np.full((R, N_RACERS), np.inf)   # 완주 시각 (동률은 초과분으로 미세 분해)

fly_gain_sum, fly_gain_n = 0.0, 0          # 비행 숏컷 이득 통계

is_dog, is_horse, is_peng = sp == I_DOG, sp == I_HORSE, sp == I_PENG
is_tiger, is_deer, is_cat, is_chick = sp == I_TIGER, sp == I_DEER, sp == I_CAT, sp == I_CHICK

t0 = time.time()
t = 0.0
while t < TMAX and not finished.all():
    alive = ~finished
    ratio = prog / L_TOTAL

    # ---- 액티브 자동 발동 ----
    fire = alive & ~used & (ratio >= trig)
    if fire.any():
        used |= fire
        # 호랑이 포효: 그 레이스의 자신 제외 전원 슬로우 (펭귄/비행 면역)
        roar_r = (fire & is_tiger).any(axis=1)
        if roar_r.any():
            tgt = alive & ~is_peng & ~flying & ~(fire & is_tiger) & roar_r[:, None]
            slow_end[tgt] = np.maximum(slow_end[tgt], t + SK["roarDuration"])
        # 고양이 / 치킨
        cat_end[fire & is_cat] = t + SK["catWalkDuration"]
        boost_end[fire & is_chick] = t + SK["dashDuration"]
        # 사슴 루돌프: 12초 앞 지점을 5초 만에 (비행 시간은 리드 클램프 시 비례 축소 — 모터 미러)
        df = fire & is_deer
        if df.any():
            ri, ci = np.nonzero(df)
            p_now = prog[ri, ci]
            eff = np.where(t < boost_end[ri, ci], SK["dashMult"], 1.0) \
                * np.where(t < slow_end[ri, ci], SK["roarMult"], 1.0)
            cap_free = smoothed[ri, ci] * eff                      # 코너 인자 제외 (모터 미러)
            speed_f = np.maximum(3.0, np.maximum(v[ri, ci], cap_free))
            full_lead = speed_f * SK["rudolphLeadSeconds"]
            p_tgt = np.minimum(p_now + full_lead, L_TOTAL - 1.0)
            ok = p_tgt > p_now + 1.0
            dur = np.maximum(0.5, SK["rudolphFlightSeconds"] * (p_tgt - p_now) / full_lead)
            flying[ri[ok], ci[ok]] = True
            fly_t0[ri[ok], ci[ok]] = t
            fly_dur[ri[ok], ci[ok]] = dur[ok]
            fly_p0[ri[ok], ci[ok]] = p_now[ok]
            fly_p1[ri[ok], ci[ok]] = p_tgt[ok]
            fly_v[ri[ok], ci[ok]] = speed_f[ok]
            gain = (SK["rudolphLeadSeconds"] - SK["rudolphFlightSeconds"]) * speed_f
            fly_gain_sum += float(gain[ok].sum()); fly_gain_n += int(ok.sum())

    # ---- 개 꼴등 판정 (미완주 최소 진행) ----
    p_masked = np.where(alive, prog, np.inf)
    last_val = p_masked.min(axis=1)
    is_last = alive & (p_masked <= last_val[:, None] + 1e-9)

    # ---- 속도 상한 ----
    eff = np.where(t < boost_end, SK["dashMult"], 1.0) * np.where(t < slow_end, SK["roarMult"], 1.0)
    skill = np.ones((R, N_RACERS))
    skill = np.where(is_horse & (ratio >= SK["finalSprintZone"]), SK["finalSprintMult"], skill)
    skill = np.where(is_dog & is_last, SK["loyaltyMult"], skill)
    m_idx = (np.mod(prog, L_PATH)).astype(np.int64) % M
    cf = CF[m_idx]
    cf = np.where(is_cat & (t < cat_end), 1.0, cf)
    cap = smoothed * eff * skill * cf

    # ---- 거버너 (제동 비대칭) ----
    g = np.where(v > cap + 0.15, np.maximum(CFG["cornerBrakeGain"], Gain), Gain)
    dv = np.clip(g * (cap - v), -CFG["maxAssistAccel"], CFG["maxAssistAccel"])
    ground = alive & ~flying
    v = np.where(ground, np.maximum(v + dv * DT, 0.0), v)
    prog = np.where(ground, prog + v * DT, prog)

    # ---- 비행 진행 (선형 보간) + 착지 ----
    if flying.any():
        ft = np.clip((t + DT - fly_t0) / fly_dur, 0.0, 1.0)
        prog = np.where(flying, fly_p0 + (fly_p1 - fly_p0) * ft, prog)
        land = flying & (ft >= 1.0)
        v = np.where(land, fly_v, v)
        flying &= ~land

    # ---- 완주 ----
    newly = alive & (prog >= FINISH)
    if newly.any():
        fin_key[newly] = t - np.minimum(prog[newly] - FINISH, 0.9) * 1e-4
        finished |= newly

    # ---- 리롤 ----
    reroll_t -= DT
    need = reroll_t <= 0.0
    if need.any():
        rolled = np.where(need, rng.uniform(Vmin, Vmax), rolled)
        reroll_t = np.where(need, reroll_t + RerollIv, reroll_t)
    step = (Vmax - Vmin) / 1.5 * DT
    smoothed = np.clip(smoothed + np.clip(rolled - smoothed, -step, step), None, None)

    t += DT

# ---------- 순위/통계 ----------
order = np.argsort(fin_key, axis=1)          # 완주 시각 순
rank = np.empty_like(order)
rows = np.arange(R)[:, None]
rank[rows, order] = np.arange(N_RACERS)[None, :]   # 0 = 1등

p1 = np.zeros(S); p2 = np.zeros(S); p3 = np.zeros(S); plast = np.zeros(S); cnt = np.zeros(S)
tfin = np.zeros(S)
for s in range(S):
    m = sp == s
    cnt[s] = m.sum()
    p1[s] = (m & (rank == 0)).sum() / cnt[s]
    p2[s] = (m & (rank <= 1)).sum() / cnt[s]
    p3[s] = (m & (rank <= 2)).sum() / cnt[s]
    plast[s] = (m & (rank == N_RACERS - 1)).sum() / cnt[s]
    tfin[s] = fin_key[m].mean()
ev = p1 * CFG["pointsFirst"] + p2 * CFG["pointsSecond"] + p3 * CFG["pointsThird"]

print(f"판수 {R} / 트랙 {L_PATH:.1f}m x {LAPS}랩 = {L_TOTAL:.0f}m / 중심선 폐합오차 {closure:.1f}m")
print(f"평균 완주시각 전체 {fin_key[np.isfinite(fin_key)].mean():.0f}s / 미완주 {int((~finished).sum())} / 계산 {time.time()-t0:.0f}s")
if fly_gain_n:
    print(f"루돌프 비행 평균 숏컷 이득 {fly_gain_sum/fly_gain_n:.1f}m (표본 {fly_gain_n})")
print()
print(f"{'동물':<4} {'1등':>6} {'2등이내':>7} {'3등이내':>7} {'꼴등':>6} {'EV':>6} {'평균완주':>8}")
for s in np.argsort(-ev):
    print(f"{NAMES[s]:<4} {p1[s]*100:5.1f}% {p2[s]*100:6.1f}% {p3[s]*100:6.1f}% "
          f"{plast[s]*100:5.1f}% {ev[s]:6.1f} {tfin[s]:7.1f}s")
