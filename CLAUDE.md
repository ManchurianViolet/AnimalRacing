「짜고 치는 레이스」(가제) — Claude Code 인수인계 문서 (v2)
> 이 문서는 이전 Claude 세션(claude.ai 채팅)이 **Claude Code**에게 넘기는 완전 인수인계서다.
> 독자(Claude Code)는 프로젝트 파일에 직접 접근할 수 있다 — 이 문서는 코드의 지도이자 맥락이다.
> 코드와 문서가 어긋나면 **코드가 진실**이다 (실제로 문서-코드 불일치 사고 이력 있음).
---
0. 유저와 협업 규칙 (가장 먼저 숙지)
유저 = 제비스튜디오 솔로 인디 개발자 (HorSteal, The DeadLine, Outta Space 출시 경력). AI 협업(vibe coding) 중심 — 코드는 Claude가 전부 작성, 유저는 Unity 에디터 작업 + 테스트 담당.
에디터 작업 지시는 클릭 단위로 상세하게 (어느 오브젝트, 어느 슬롯, 어떤 순서). 유저는 에디터에 능숙하지만 "무엇을 해야 하는지"는 코드를 모르니 알 수 없다.
파일 수정 규칙 (Claude Code용): 수정 후 "이번 배치: N개 파일" + 각각 한 줄 사유로 요약 보고. 파일 삭제가 포함되면 삭제 목록을 보고 맨 위에 눈에 띄게 (놓치면 중복 정의 컴파일 에러 — 실제 사고 이력). 컴파일 성공 여부는 유저가 에디터 콘솔로 확인해 준다.
씬(.unity)/프리팹(.prefab)/메타 파일은 직접 편집 금지 — YAML 수동 수정은 Photon 컴포넌트/GUID 오염 위험. 전부 에디터 지시로 해결할 것.
Unity 6 (6.3): `rb.velocity` 금지 → 반드시 `rb.linearVelocity`. `PhysicMaterial` → `PhysicsMaterial`.
코드 수정 후 중괄호/소괄호 짝 검증 습관 유지. 정규식 치환 시 `\n` 이스케이프 해석 사고 이력(PlayerHUD 파괴) — 문자열 replace 우선, 치환 후 결과 검증 필수. 기존 파일의 줄바꿈(CRLF/LF 혼재)은 파일별로 보존.
코드 주석은 한국어. 튜닝 값은 하드코딩하지 말고 GameConfig에 [Tooltip]과 함께 노출.
유저 성향: 근본 원인 이해를 원함("쉬운 말로 뭐가 문제였는지"), 좋은 직감의 디버거(계기판 보고 스스로 가설 세움), 기획 결정은 단호함 — 반대 의견은 한 번 성실히 제시하되 확정하면 전력 실행.
대화 언어: 한국어, 반말 섞인 편한 톤, ㅋㅋ 사용.
---
1. 게임 정체성 (확정 컨셉)
Unity 6.3 / PUN2 / Steam 지향 / 2~4인 온라인 파티게임.
플레이어(1인칭)는 비밀 도박장에서 동물 8마리 레이스의 1·2·3등을 예측하고, 레이스 중 아이템(부스트/감속)을 동물에게 직접 조준 사격으로 개입한다. 예측은 비밀, 정산 때 공개. 포인트 최다 획득자가 매치 우승.
무대(확정): 자연 경관(열대 섬/산골짜기 등) 속 주회 트랙 — F1식, 레인 없음.
경제(확정): 포인트제. 1등 예측 적중 +100 / 2등 +50 / 3등 +30 (슬롯별 독립 채점). 아이템은 현재 무료 로드아웃 3+3.
로비(확정): 리썰컴퍼니식 — 게임 씬 안이 곧 대기실, 방장 전용 3D 레버(E)로 시작, 벽은 페이즈 연동.
방 시스템(확정): 타이틀 씬(빌드 0)에서 공개 방 목록 + 선택적 비밀번호, 인원 2~4/라운드 수 설정. Fixed Region kr 고정. 닉네임은 타이틀 입력(PlayerPrefs), 스팀 연동 시 GetPersonaName 대체 예정.
Photon 상용화 메모: Steam 출시 시 100CCU $95/12개월 필요 (무료 20CCU는 비상업 전용).
---
2. 아키텍처 대원칙
호스트(마스터) 권위: 레이스 물리 시뮬, 포인트 장부, 예측 접수, 봇 두뇌 전부 호스트에서만. 클라는 "거울" — 방송을 받아쓰기만.
관문 패턴: UI/입력 → `NetworkGateway`(요청 RPC) → 호스트 `MatchManager` 검증 → 결과 방송. 오프라인이면 게이트웨이가 로컬 직결.
비밀 정보 규율: 예측 티켓은 제출자 본인에게만 개인 RPC(영수증), 정산 때 전체 공개.
이벤트 버스: `GameEvents`(static) — 시스템 간 결합은 전부 이벤트로. 네트워크 중계도 이벤트 구독→RPC 재발행.
동기화 채널: 위치/애니 = PhotonTransformView/AnimatorView, 게임 상태 = NetworkMatchSync(1초 주기), 사건 = Gateway RPC.
Resources 규칙: Photon 이름 스폰 대상만 (NetPlayer, 동물 7종). UI 프리팹은 인스펙터 참조.
씬 2개: TitleScene(0) → SampleScene(게임+대기실). AutomaticallySyncScene.
두뇌 분리 철학 (중요): 승부는 `Racer`(스탯 주사위+스킬)가, 주행 연기는 `RacerMotor`가 담당. 모터의 판단 AI는 의도적으로 단순 유지 — 동물이 영리하게 작전을 짜면 베팅 예측력(안내판 스탯표의 정직성)이 죽는다. 조향 행동 기반 반응형 에이전트 수준이 상한. 동물을 더 똑똑하게 만들자는 제안이 오면 이 저울을 상기시킬 것.
---
3. 확정 기획 상세
3-1. 동물 스킬 7종 (구현 완료)
동물	유형	스킬	효과 (SkillTuning 상수)
말	패시브	우승 본능	진행 85%+ 속도 +12%
사슴	패시브	경계 본능	반경 2m 내 플레이어 아이템 명중 시 0.5초 후 3초 +15% (본인 피격도 발동)
호랑이	액티브(라운드 1회)	포식자의 습격	무작위 진행률(15~85%)에 사거리 무제한 최근접 3초 스턴. 펭귄에겐 무효("꿈쩍도 안 한다!")
고양이	액티브(라운드 1회)	변덕	3초 ±30% 반반
개	패시브	충성심	꼴등일 때 +15%
치킨	패시브	냅다 달리기	출발 5초 +25%, 이후 4초 -22%
펭귄	패시브	무관심	모든 스킬·아이템 효과 완전 면역 (이로운 것 포함) — Racer.AddEffect 관문 차단. 단, 코너 감속은 트랙 물리라 적용됨
발동 전부 자동/조건부. 발동 소식 = GameEvents.OnSkillProc → TimelineFeed 하늘색 + Gateway 중계.
사슴만 밸런스 시뮬 미반영(플레이어 의존, 의도적 "숨은 가치픽"). 펭귄의 아이템 면역도 시뮬 밖 — 실전 가치는 표기 EV보다 높음(의도).
3-2. 코너 감속 시스템 (이번 세션 확정+구현+체감 합격) ★
가속 스탯이 죽은 스탯이던 문제(순항 중 무의미)의 해결책. 코너 속도 상한 = CurrentMaxSpeed × (1 − senseT × cornerDecelRate), senseT = clamp01(|전방 6m 창 곡률|/curvatureSaturation).
핵심 설계 통찰 (반드시 이해할 것): 상한에 곱하기만 하면 가속 스탯은 여전히 무의미하다. 기존 모터는 가감속을 같은 게인으로 처리하는 대칭 거버너라, 굼뜬 가속 = 굼뜬 제동 → 코너 진입에서 번 거리와 탈출에서 잃는 거리가 수학적으로 정확히 상쇄된다. 그래서 제동을 분리했다: 상한 초과 시(코너 진입·스턴·Slow 피격) gain = max(cornerBrakeGain, 자기 AccelGain) — 제동은 전원 동일하게 강하고, 탈출에서 상한을 되찾는 속도만 가속 스탯 소관. "코너 탈출 = 가속의 무대"는 이 비대칭이 있어야 성립한다.
GameConfig "주행 — 코너 감속" 섹션: cornerDecelEnabled=true(A/B 토글), cornerDecelRate=0.22, cornerSenseAhead=6(라인 감지 9m보다 짧아야 탈출 가속이 코너 끝에서 터짐), cornerBrakeGain=4.5(최대 가속 게인 4.0보다 커야 함).
부수효과(의도된 개선): 스턴/감속 아이템 제동이 전원 균일·즉각화 (구버전은 피격자 가속 스탯에 따라 멈춤 속도가 달랐음).
파생: 맵 개성 레버 탄생 — 급코너 맵=가속형(개·고양이) 유리, 직선 맵=항속형(펭귄) 유리. 수치는 3-3 참조.
3-3. 밸런스 V7 (이번 세션 — V6 폐기) ★
유저 요청 "스탯 표준편차 크게"에 따라 재밸런싱. min σ 3.1→7.1, max σ 1.2→4.5, 가속 σ 17.6→25.5. 코너 감속이 만든 가속 화폐로 속도 격차를 정산하는 구조.
중요 발견: 구 시뮬(추상 100m)의 V6 밴드는 실전 물리(실맵 187m+정지 출발+실제 거버너)에서 이미 깨져 있었다(개 34.6, 치킨 18.0). 시뮬은 반드시 실맵+실물리를 미러해야 한다.
SO	min/max/accel	1등	2등	3등	꼴등	EV	캐릭터
말	61/83/45	14.7%	16.1%	14.8%	9.5%	27.2	만능+막판쇼
사슴	60/85/60	14.2%	14.4%	12.9%	12.6%	25.3	숨은 가치
호랑이	52/90/35	18.9%	10.8%	8.3%	18.5%	26.8	1등픽의 왕
고양이	58/86/75	15.8%	12.9%	11.1%	17.0%	25.5	민첩+변덕
개	66/80/95	9.6%	17.6%	20.6%	1.5%	24.6	시상대 기계
치킨	46/93/55	18.9%	8.8%	6.8%	39.8%	25.3	극단 도박
펭귄	68/80/10	7.9%	19.4%	25.5%	1.0%	25.2	3등 슬롯의 신
실맵 4만 판 검증 밴드 24.6~27.2 (목표 23~28 충족). 아키타입 검증: 코너 조밀맵 23.1~27.5(개↑펭귄↓), 직선맵 23.6~27.4(펭귄↑개↓) — 거울상 메타, 전부 밴드 내.
밸런싱 방침(확정): 스탯은 전역 1세트. 맵마다 자동 조정하지 않음(안내판 신뢰/학습 재미 보호, 맵 개성은 기능). 시뮬은 새 맵이 밴드를 벗어나는지 "검사"하는 도구 — 벗어나면 동물이 아니라 맵 지형을 고친다.
⚠ 확인 대기 2건: ① 유저가 V7을 SO에 입력했는지 미확인 ② 치킨 꼴등 39.8%(V6은 30%) 승인 여부 미확인 — 30% 선을 원하면 치킨 폭 축소 필요(분산 요청과 상충함을 안내).
리롤 간격은 전 동물 15초 유지.
3-4. 밸런스 파이프라인 (이번 세션 구축) ★
"맵 따라 동물따라 알아서" — 수동 데이터 전달과 시뮬-게임 상수 드리프트를 원천 차단하는 자동 루프:
유니티 메뉴 Tools > 짜고치는레이스 > 밸런스 데이터 내보내기 (BalanceExporter.cs, 에디터 전용) → 프로젝트 루트에 `BalanceExport.json` (트랙 1m 곡률·폭 프로파일 + 씬 RaceManager.animalPool의 출전 SO + 씬 GameManager의 GameConfig + SkillTuning 상수 전부).
파이썬 시뮬 `corner_sim.py`(게임 물리 미러) + `tune_v7b.py`(비례 감쇠 튜너)에 JSON을 물려 검증/재밸런싱.
스탯/스킬/코너 튜닝/맵이 바뀔 때마다 이 루프 반복. 상수를 절대 손으로 옮겨 적지 말 것.
시뮬 사양(재작성 시 필수 준수): dt=0.05, 정지 출발(v=0), 리롤 MoveTowards 수렴 (max−min)/1.5초, 거버너 dv=clip(gain×(cap−v), ±maxAssistAccel), 제동 비대칭(v>cap+0.15 → gain=max(brakeGain, own)), 코너 상한 6m 창, 스킬 미러(사슴 제외), 라인업 7종+무작위 중복 1, EV=P1×100+P2×50+P3×30, 목표 밴드 23~28. 튜너: 폭·가속 고정, 중앙값만 비례 스텝(K=0.08, ±0.8 클램프)+제로섬 보정 — 주의: 스텝 ±1 이상 고정폭은 발산한다 (좁은 범위 동물은 분포가 스파이크라 중앙값 민감도 극단).
시뮬 파일 2종은 유저에게 전달됨 — 프로젝트에 없으면 위 사양으로 재작성 가능.
3-5. 멀티 이탈/복귀 규칙 (5-3 확정)
방 생성 시 PlayerTtl=60000. 명시적 퇴장(창닫기) = 자리 즉시 삭제 vs 비정상 끊김 = TTL 보존+ReconnectAndRejoin 복귀.
게스트 이탈(매치 중): 호스트가 노는 봇을 그 PlayerState에 Bind → 이름 그대로 봇 대타. 복귀 시 봇 해제+로스터 재전송.
매치 중 방장 이탈 = AbortMatch: 정산 없이 전원 같은 방 대기실 복귀, 새 방장 레버 승계, 방 재개방. 대기실 방장 이탈은 조용히 승계.
크래시 방장의 유령 자리 60초 점유(4/4로 보임)는 수용된 한계.
3-6. 기타 확정
베팅 슬롯: 드래그 교체만, 클릭 취소 없음. 같은 동물 다른 존 이동 시 기존 존 자동 비움.
안내판(B) 확정: 출전표 행 클릭 → 중앙 팝업 (이번 세션 구현 — §7 에디터 잔여 참조). 짧은 탭=팝업, 드래그=베팅 (EventSystem이 드래그 시 클릭 자동 무효화 — DraggableBetIcon이 IPointerDown을 안 먹어서 가능한 구조).
단말기 2대, 점유 비동기화 수용(점유 RPC는 백로그).
등번호판: 새들클로스 팔레트(1흰2검3빨4파5노6초7주황8분홍), RacerColors가 단일 출처 — 안내판 팝업 배지도 이것 참조(구현됨), 미래 전광판도 참조할 것.
동물끼리 물리 충돌 전면 오프 (원본 문서 누락이었음): RaceManager가 스폰 후 "Racer" 레이어 상호 무시(레이어 없으면 쌍별 IgnoreCollision 폴백). 몸싸움은 물리가 아니라 RacerMotor의 sideRepel 스프링+회피 행동으로만 연출. ApplyFrictionless(무마찰)는 벽/지면 대상 잔존. → "충돌 순간 감속" 류 기능은 물리 이벤트로 못 만들고 근접 판정 기반으로 해야 함.
---
4. 파일별 상세 (49개 = 기존 47 + 신규 2)
Core — 게임 흐름
GameConfig.cs (SO): 전 튜닝의 집. 포인트(100/50/30), 페이즈 시간, racerCount=8, 주행 기본(lookAhead=4/maxAssistAccel=20), 레이싱라인(racingLineLookAhead=9/insideBiasStrength=0.7/curvatureSaturation=6/roadMargin=1.2), 코너 감속(cornerDecelEnabled/Rate=0.22/SenseAhead=6/BrakeGain=4.5), 회피(avoidLookAhead=2.6/bodyClearance=1.1/overtakeShift=1.6/blockedSpeedFactor=0.9/lateralSmoothTime=0.45/lateralMaxSpeed=3.5/sideBySideRange=1.5), 디버그 토글.
GameManager.cs: 페이즈 상태머신 껍데기(싱글턴). 실질 진행은 MatchManager.
GameEvents.cs: static 이벤트 버스 + RaceResult 클래스(round, firstId/secondId/thirdId, pointsGained).
MatchManager.cs: 매치 순환(Betting→Loadout→Countdown→Racing→Settlement×N), SubmitBet 관문(3픽 검증), SettlePoints(슬롯별 채점), AutoBet, AbortMatch, 클라 거울 API.
PlayerState.cs: Points/AddPoints/ResetPoints, BetTicket(firstId/secondId/thirdId, IsValid), 아이템 로드아웃/쿨다운.
BotController.cs: 봇 두뇌. Bind(PlayerState) 대타 겸용. 아이템 AI: 1등픽이 선두 아니면 부스트, 내 3픽 아닌 침입자가 선두면 감속.
PrototypeBootstrap.cs: 씬 시작 오케스트라. 온라인 "입장 확정까지 대기", 오프라인 나+봇. 로드아웃 배포+봇 랜덤 3픽.
ItemExecutor.cs: 아이템 사용 단일 관문(페이즈/쿨다운/보유/타겟 검증).
Racing — 레이스·주행
TrackPath.cs (v2 — 경계 두 줄): InnerLine/OuterLine 자식 쌍이 경로 정의(같은 개수·순서·i번끼리 단면). API: TotalLength, GetProgressNear(연속성 투영), GetPoint/GetTangent(평활)/GetNormal/GetPointAt/GetLateralOffset/GetHalfWidth/GetLateralLimit, GetSignedCurvatureAhead(도/m), GetTargetOnSection(두 레일 보간 — 퇴화 원천 차단), Build()는 public(에디트 모드 호출 가능 — BalanceExporter가 사용). 빌드 검진 로그+기즈모(안 하늘/밖 노랑/쌍 가로대).
RacerMotor.cs: 진짜 레이스 주행(호스트 전용). ① 레이싱 라인(전방 9m 곡률→인코스, personalMargin 분산) ①.5 코너 감속(6m 창 senseT→상한 곱, baseCap/speedCap 분리 — "더 빠른 놈" 비교는 baseCap 기준) ② 회피/추월(양쪽 열리면 인코스 방향) ③ 간격 스프링(sideRepel, 포개짐은 번호 홀짝) ④ 교착 감시견(0.8초) ⑤ GetTargetOnSection 목표 ⑥ 횡 SmoothDamp ⑦ transform 직접 회전. 제동/가속 게인 분리(§3-2). 디버그 라벨: `#id prog / lat→desired / v 현재/상한 curv T 막힘`.
Racer.cs: 시뮬 두뇌(리롤 15초, 상태이상, 스킬 상태, 펭귄 면역=AddEffect 관문). CurrentMaxSpeed=리롤×효과×스킬(스턴=0).
StatusEffects.cs: Boost/Slow/Stun.
AnimalSkill.cs: 스킬 enum+SkillTuning 상수 단일 출처+DisplayName/Description (안내판 팝업이 사용 중).
RacerColors.cs / RacerNumberPlate.cs: 번호 색 단일 출처 / 등번호판(SkinnedMesh 제외 자동 탐색).
RaceManager.cs: 스폰(랜덤 7종+중복1, InstantiationData), 스폰 후 동물 간 충돌 전면 오프(§3-6), 시뮬 루프(연속성 투영+투영점프/NaN 감시, UpdateSkillContext=개 꼴등+호랑이 습격), GetFinalRanking, EnsureBodyCollider(SkinnedMesh 한정 캡슐), ApplyFrictionless.
Content — SO
AnimalDefinition.cs: 이름/프리팹/스탯(100단위, 100=6.0m/s)/리롤/skill/icon(Sprite — 안내판 팝업 초상화로 자동 연동, 비면 숨김).
ItemDefinition.cs: Boost/Slow, duration/magnitude, 아이콘.
Player
FirstPersonController.cs(CharacterController, 한글 IME WASD 함정 → Input System 전환 백로그) / PlayerInteractor.cs(E 레이캐스트) / PlayerItemController.cs(1·2키/클릭+조준 사격) / IInteractable.cs.
UI
PlayerHUD.cs: 지갑("N P"), 페이즈/타이머, 대기실 문구, B키 예측 요약, 조준점, 아이템 슬롯. BindLocalPlayer.
BettingPanel.cs: 3존 드래그 예측+확정. infoPopup 슬롯(신규) — Open/Close 시 팝업 잔상 정리, Esc는 팝업 먼저 닫고 다음에 패널, BuildRows가 행에 팝업 참조 주입.
BetRowView.cs: 출전표 행. IPointerClickHandler 구현(신규) — Bind(racer, canvas, popup) 시그니처 변경됨, 짧은 클릭=팝업.
AnimalInfoPopup.cs (신규): 중앙 팝업. 루트=반투명 차단막(IPointerClickHandler — 어디든 클릭=닫힘), 카드=번호 배지(RacerColors)+이름+초상화(선택, icon 자동)+본문(최저/최고/가속 100단위+스킬명+설명 리치텍스트). 표시 전용, 상태 없음. ⚠ 에디터 조립 미완(§7).
BetDropZone.cs / DraggableBetIcon.cs(IBeginDrag/IDrag/IEndDrag만 구현 — IPointerDown 안 먹는 게 클릭 공존의 전제, 추가하지 말 것) / BettingTerminal.cs(E→패널, occupied 로컬).
SettlementPanel.cs / ResultRowView.cs / BetChipView.cs("① 이름 +100P", 적중 초록).
Scoreboard.cs: 월드스페이스 전광판(페이즈/타이머/라운드) — 원본 문서의 "Tab 현황" 서술은 오류였음. Tab UI는 존재하지 않음.
TimelineFeed.cs(우측 사건 피드) / ItemSlotView.cs / StartLever.cs(방장 레버, wallsToHide) / PixelBorder.cs([유저 자작] 건드리지 말 것).
Network
NetworkLauncher.cs(타이틀: 접속 kr/방 목록/CreateRoom PlayerTtl=60000/LoadLevel) / TitleMenu.cs / RoomListItem.cs.
NetworkPlayers.cs(LocalPlayerId, IsAuthority, BotIdBase=100).
NetworkGateway.cs: 요청/중계 허브. RequestSubmitBet→호스트 검증→RpcBetResult+개인 영수증. 경제 방송 1초(ids/points/boost/slow/submitted). RpcSettled→클라 RaceResult 재조립. 5-3(봇 대타/해제/방 재개방/로스터).
NetworkMatchSync.cs(1초 페이즈/타이머/라운드) / NetworkPlayerSpawner.cs(입장 확정 대기) / NetworkPlayerSetup.cs(미접속=내 것) / LocalPlayerBinder.cs(HUD/단말기/레버 배선) / NetworkRacerSetup.cs(동물 클라 등록) / NetworkSessionGuard.cs(ReconnectAndRejoin, 방장 교체→AbortMatch).
Editor 전용
BalanceExporter.cs (신규): §3-4 파이프라인의 유니티 쪽 절반. #if UNITY_EDITOR 전체 래핑(빌드 무포함). 씬 GameManager.config/RaceManager.animalPool을 SerializedObject로 읽음(비공개 필드 접근) — 우선순위: 씬 참조 > 프로젝트 검색.
---
5. 씬/프리팹 배선 현황
SampleScene: Manager 오브젝트(GameManager+MatchManager+RaceManager+Gateway+MatchSync+SessionGuard+Binder+Spawner+PlayerItemController+Bootstrap), Track(InnerLine/OuterLine 웨이포인트 큐브들), 대기실+벽(StartLever.wallsToHide), 베팅 단말기 2대, HUD 캔버스(지갑/타이머/피드/전광판/정산판/베팅패널 3존), 스폰 포인트들, BotA×3. NetworkLauncher 잔재 여부 확인 필요(게임 씬엔 불필요할 수 있음).
TitleScene(빌드 0): NetworkLauncher+TitleMenu+방 목록 UI.
NetPlayer 프리팹(Resources): FPC+카메라+PlayerInteractor+PhotonView+TransformView+AnimatorView+NetworkPlayerSetup+NameLabel.
동물 프리팹 7종(Resources): 모델+Animator+Rigidbody(FreezeRotation)+Racer+RacerMotor+PhotonView+TransformView+AnimatorView+NetworkRacerSetup+RacerNumberPlate+번호판 Cube+TMP.
Run In Background 설정됨. 동기화 컴포넌트 변경 시 재빌드 철칙 (코너 감속/안내판은 동기화 무변경이라 재빌드 불요).
⚠ 안내판 팝업 UI 미조립 — §7의 절차가 다음 에디터 작업.
---
6. 최근 완료 (이번 세션 하이라이트)
코너 감속 시스템 구현+유저 체감 합격 (§3-2). GameConfig/RacerMotor 2파일.
밸런스 파이프라인 구축 (§3-4): BalanceExporter.cs + corner_sim.py/tune_v7b.py. 구 시뮬 폐기.
V6 실전 밴드 붕괴 발견 → V7 재밸런싱 (§3-3): 스탯 분산 대폭 확대하면서 밴드는 더 타이트하게. 아키타입 강건성 검증 포함.
안내판(B) 결정+구현: 클릭+중앙 팝업. AnimalInfoPopup.cs 신규 + BetRowView/BettingPanel 수정. 에디터 조립만 잔여.
문서-코드 정합 감사: 동물 충돌 오프/Scoreboard 실체 등 원본 문서 오류 정정 (본 문서에 반영됨).
---
7. ★ 다음 작업 큐 (우선순위순)
안내판 팝업 에디터 조립 (파일은 프로젝트에 있음, UI만 만들면 됨). 절차:
① BettingPanel 오브젝트 우클릭 > UI > Image → 이름 `AnimalInfoPopup`, 앵커 Alt+Shift stretch-stretch(패널 전체), 색 검정 알파≈150, Raycast Target 유지, Add Component > Animal Info Popup
② 그 밑에 UI > Image `Card`(middle-center, ≈380×320, 불투명 어두운 색)
③ Card 밑에 Image `Badge`(좌상단 44×44) → Badge 밑에 TMP `BadgeText`(stretch, 가운데, Bold)
④ Card 밑에 TMP `NameText`(Badge 오른쪽, 크게+Bold)
⑤ (선택) Card 밑에 Image `Portrait`(96×96 — 지금 icon 없으니 생략 가능)
⑥ Card 밑에 TMP `BodyText`(나머지 stretch, 왼쪽 정렬, 5줄)
⑦ AnimalInfoPopup 컴포넌트 슬롯 4~5개 연결, BettingPanel 컴포넌트의 "안내판 팝업 > Info Popup" 슬롯에 AnimalInfoPopup 오브젝트 드래그
테스트: 행 짧은 클릭=팝업(배지 색=등번호판 색), 드래그=베팅, 아무데나 클릭/Esc=팝업만 닫힘. 행 전체 클릭영역을 원하면 행 프리팹에 투명 Image(더블클릭 편집 모드).
V7 SO 입력 여부 확인 — 미입력이면 §3-3 표대로 입력 안내(7종×3필드). 입력 후 BalanceExport 재추출로 검증하면 완벽.
치킨 꼴등 39.8% 승인 확인 (§3-3 ⚠).
멀티 실기 테스트 (여전히 0회 — 최대 미검증 리스크): 최신 빌드 1개 뽑고 에디터=호스트로 ① 포인트제 풀사이클(게스트 3픽 제출→영수증→정산 공개→최종 순위) ② 5-3 삼종(게스트 창닫기→봇 대타/재입장→해제, 호스트 창닫기→대기실 복귀+레버 승계+방 재개방, 인터넷 차단→60초 내 복귀) ③ 클라 화면 코너 감속 대열 확인.
킥 결정 대기: "관전 90초 밀도 패키지" 제안 상태 — 익명 저격("누군가 3번을 저격했다!") + 전광판 실시간 순위(C, 배관 완료) + 호랑이 습격 연출(포효/흔들림). 유저 ㄱ 답 오면 즉시 구현. 플레이어-동물 충돌(F)도 이때 연동 결정.
1호 맵 테마 결정: 열대 섬 vs 산골짜기 (두 세션 연속 섬에 한 표 — 헤어핀을 해안 절벽으로 정당화 가능, 코너 감속과 궁합). 경사 트랙은 지면 스냅 미구현이라 1호는 평지 권장.
---
8. 미결 기획 (결정 대기)
킥(최대 난제): §7-5 패키지 제안 중. 진단 = "레이스 관전 90초가 죽은 시간". 익명 저격이 생존 후보(구현 거의 공짜).
플레이어-동물 충돌(F): 유령 통과 vs 몸 개입+옐로카드. 킥과 연동. (현재 동물끼리는 충돌 오프 — 플레이어와의 충돌은 별개 결정)
1호 맵 테마 (§7-6).
치킨 꼴등 40% 승인 (§3-3).
아이템 가격/재화 관계 재고(현재 무료 3+3).
오프라인 자동 시작 vs 레버 통일(표류 중).
신규 동물 종 추가: 시스템 준비됨(스킬 재사용+SO+프리팹+Resources+파이프라인 자동 밸런싱)이나 출시 후 업데이트 카드로 아껴두기로 방향 잡음.
---
9. 백로그
전광판 실시간 순위+변동 연출(C) — 배관 완료, 킥 패키지 후보
경사 트랙(경사 감속+시뮬 프로필+지면 스냅 미구현)
5-4 폴리싱: SendRate/SerializationRate 20, 접속 실패 안내 UI, 원격 애니, 단말기 점유 동기화, Input System 전환(한글 IME — 한국 출시 필수)
시네마틱 인트로 캠, 엘리베이터 시작 연출
동물 3D 초상화 → AnimalDefinition.icon에 넣으면 안내판 팝업에 자동 표시(연동 완료)
아이템 상점(대기획), Steam 연동(닉네임+친구 초대), 지역 선택 드롭다운
호랑이 습격 연출 — 킥 패키지 후보
Unity MCP 실험(CoplayDev unity-mcp)
출시 전: 디버그 토글 끄기, cornerDecelEnabled 등 A/B 토글 정리
---
10. 테스트 미완 목록
포인트제 멀티 사이클 + 5-3 실기 (§7-4 — 최우선)
V7 스탯 실전 체감("매 판 우승자가 바뀌는가", 개가 코너 탈출마다 치고 나오는지, 출발선 가속 격차 — 개 총알/펭귄 뒤뚱은 의도된 그림)
안내판 팝업 (§7-1 조립 후)
스킬 인게임 체감(특히 호랑이 무는 순간)
코너 감속의 멀티 화면 검증(TransformView 받아쓰기라 이론상 동일)
---
11. 지식 아카이브 (버그/설계 패턴 사전)
멀티 증상 "온라인인데 오프라인처럼/한쪽에만 존재" = 타이밍 경쟁 1순위 의심. 처방 = "입장 확정까지 대기" (3회 실전).
대칭 거버너 상쇄 법칙 (이번 세션): 가감속을 같은 게인으로 묶으면 어떤 상한 프로파일이든 순효과가 0에 수렴 — 스탯을 무대에 세우려면 반대 방향을 분리·고정해야 한다. 코너 감속의 제동 분리가 그 사례.
좁은 분포 = 스파이크 민감도 (이번 세션): 범위 좁은 동물의 EV는 중앙값에 극단적으로 민감(±1스탯에 EV 20 요동) — 자동 튜너는 고정 스텝 대신 비례 감쇠+제로섬 보정 필수.
시뮬은 실맵+실물리 미러 필수 (이번 세션): 추상 트랙 밸런스는 실전에서 무효였음. BalanceExport 파이프라인 항상 경유.
Unity 6: FreezeRotation이 MoveRotation까지 차단 → transform 직접 회전. PhysicMaterial→PhysicsMaterial.
오프라인(미접속) PhotonView.IsMine=false → "미접속=내 것" 명시 판정.
프리팹 수정은 더블클릭 편집 모드. 동기화 설정 변경=재빌드(AnimatorView 불일치→스트림 오염 사례).
자동 수집+인스펙터 배열 공존: 배열이 채워지면 자동이 꺼진다.
장식물 콜라이더는 끄면 끝(EnsureBodyCollider가 꺼진 것 무시). 월드 TMP는 렉트 1×1+FontSize.
한글 IME→WASD 간헐 무반응. 에디터 잔상 에러(SerializedObjectNotCreatable 등)=무해.
빌드 로그: %USERPROFILE%\AppData\LocalLow<회사><제품>\Player.log. 진단 팁: 에디터=호스트로 역할 교체.
UI 클릭/드래그 공존: 드래그 핸들러가 IPointerDown을 구현하지 않으면 부모의 IPointerClickHandler와 자연 공존(EventSystem이 드래그 시 클릭 무효화). DraggableBetIcon에 IPointerDown 추가 금지.
---
12. 디버그 도구 (켜져 있음 — 출시 전 끌 것)
GameConfig.debugMotorGizmos: Scene 뷰 동물 목표선+라벨 `#id prog / lat→desired / v 현재속도/상한 / curv T 막힘` — v의 상한이 코너에서 내려가고 탈출에서 회복을 동물별 다른 기울기로 쫓아가면 코너 감속 정상 작동.
GameConfig.cornerDecelEnabled: 코너 감속 A/B 토글.
GameConfig.debugProgressLog: [투영점프]/NaN 감시.
TrackPath 빌드 검진 로그+기즈모.
— 끝. 코드가 진실, 이 문서는 지도다. 첫 안건은 §7 순서대로: 안내판 조립 → V7 입력 확인 → 치킨 40% → 멀티 실기 → 킥 ㄱ/아니오.