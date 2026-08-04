「짜고 치는 레이스」(가제, 영문 후보 Dirty Derby — §8) — Claude Code 인수인계 문서 (v5)
> 이 문서는 이전 Claude 세션이 다음 세션에게 넘기는 완전 인수인계서다.
> 독자(Claude Code)는 프로젝트 파일에 직접 접근할 수 있다 — 이 문서는 코드의 지도이자 맥락이다.
> 코드와 문서가 어긋나면 **코드가 진실**이다 (실제로 문서-코드 불일치 사고 이력 있음).
> v3 세션(Claude Code + Unity MCP)에서 실맵 트랙 구축·전광판·완주 연출 등 대규모 갱신.
> v4 세션에서 미니맵 전광판·옆구리 번호판·플레이어-동물 충돌 오프·구름 컨베이어 등. MCP 검증 루프(플레이 진입→계측→캡처→종료)가 표준 작업 방식으로 정착.
> **v5 세션에서 부스트 먼지 연출·캐릭터 커스터마이징(타이틀+인게임+네트워크 동기화)·빌드 실패 수정·MPPM 멀티 실기 테스트 — §6 참조.**
> **v5의 최대 사건: Multiplayer Play Mode(MPPM)로 빌드 없이 멀티 테스트가 가능해져, 그동안 0회였던 멀티 실기 검증이 처음으로 실행됐다 (§10).**
---
0. 유저와 협업 규칙 (가장 먼저 숙지)
유저 = 제비스튜디오 솔로 인디 개발자 (HorSteal, The DeadLine, Outta Space 출시 경력). AI 협업(vibe coding) 중심 — 코드는 Claude가 전부 작성, 유저는 Unity 에디터 작업 + 테스트 담당.
에디터 작업 지시는 클릭 단위로 상세하게 (어느 오브젝트, 어느 슬롯, 어떤 순서). 유저는 에디터에 능숙하지만 "무엇을 해야 하는지"는 코드를 모르니 알 수 없다.
파일 수정 규칙 (Claude Code용): 수정 후 "이번 배치: N개 파일" + 각각 한 줄 사유로 요약 보고. 파일 삭제가 포함되면 삭제 목록을 보고 맨 위에 눈에 띄게 (놓치면 중복 정의 컴파일 에러 — 실제 사고 이력). 컴파일 성공 여부는 유저가 에디터 콘솔로 확인해 준다.
씬(.unity)/프리팹(.prefab)/메타 파일은 직접 편집 금지 — YAML 수동 수정은 Photon 컴포넌트/GUID 오염 위험. 씬/프리팹 작업은 Unity MCP(에디터 API라 안전)로 직접 수행이 기본 (v3~), MCP 불가 상황에만 에디터 지시로 폴백. 뮤테이션 전 Application.productName == "AnimalRacing" 검증 습관 필수.
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
경제(확정): 포인트제. **1등 예측 적중 +70 / 2등 +50 / 3등 +10** (슬롯별 독립 채점) — v5에서 실행 중 SO 실값 계측으로 확정. 이전 문서의 100/50/30은 오기였고, GameConfig.cs 기본값(70/50/10)이 맞다. 인게임 정산 130점(70+50+10) 지급까지 실측 확인됨. 아이템은 현재 무료 로드아웃 3+3.
캐릭터 커스터마이징(v5 확정+구현): 타이틀 화면에서 부위 10종을 골라 확정 → PlayerPrefs 저장 + Photon 커스텀 속성으로 방송 → 인게임 아바타(내 것/남의 것 모두)에 반영. §3-8 참조.
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
3-2. 코너 감속 시스템 (v2 세션 확정+구현+체감 합격) ★
가속 스탯이 죽은 스탯이던 문제(순항 중 무의미)의 해결책. 코너 속도 상한 = CurrentMaxSpeed × (1 − senseT × cornerDecelRate), senseT = clamp01(|전방 6m 창 곡률|/curvatureSaturation).
핵심 설계 통찰 (반드시 이해할 것): 상한에 곱하기만 하면 가속 스탯은 여전히 무의미하다. 기존 모터는 가감속을 같은 게인으로 처리하는 대칭 거버너라, 굼뜬 가속 = 굼뜬 제동 → 코너 진입에서 번 거리와 탈출에서 잃는 거리가 수학적으로 정확히 상쇄된다. 그래서 제동을 분리했다: 상한 초과 시(코너 진입·스턴·Slow 피격) gain = max(cornerBrakeGain, 자기 AccelGain) — 제동은 전원 동일하게 강하고, 탈출에서 상한을 되찾는 속도만 가속 스탯 소관. "코너 탈출 = 가속의 무대"는 이 비대칭이 있어야 성립한다.
GameConfig "주행 — 코너 감속" 섹션: cornerDecelEnabled=true(A/B 토글), cornerDecelRate=0.22, cornerSenseAhead=6(라인 감지 9m보다 짧아야 탈출 가속이 코너 끝에서 터짐), cornerBrakeGain=4.5(최대 가속 게인 4.0보다 커야 함).
부수효과(의도된 개선): 스턴/감속 아이템 제동이 전원 균일·즉각화 (구버전은 피격자 가속 스탯에 따라 멈춤 속도가 달랐음).
파생: 맵 개성 레버 탄생 — 급코너 맵=가속형(개·고양이) 유리, 직선 맵=항속형(펭귄) 유리. 수치는 3-3 참조.
3-3. 밸런스 V7 (v2 세션 — V6 폐기) ★
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
3-4. 밸런스 파이프라인 (v2 세션 구축) ★
"맵 따라 동물따라 알아서" — 수동 데이터 전달과 시뮬-게임 상수 드리프트를 원천 차단하는 자동 루프:
유니티 메뉴 Tools > 짜고치는레이스 > 밸런스 데이터 내보내기 (BalanceExporter.cs, 에디터 전용) → 프로젝트 루트에 `BalanceExport.json` (트랙 1m 곡률·폭 프로파일 + 씬 RaceManager.animalPool의 출전 SO + 씬 GameManager의 GameConfig + SkillTuning 상수 전부).
파이썬 시뮬 `corner_sim.py`(게임 물리 미러) + `tune_v7b.py`(비례 감쇠 튜너)에 JSON을 물려 검증/재밸런싱.
스탯/스킬/코너 튜닝/맵이 바뀔 때마다 이 루프 반복. 상수를 절대 손으로 옮겨 적지 말 것.
시뮬 사양(재작성 시 필수 준수): dt=0.05, 정지 출발(v=0), 리롤 MoveTowards 수렴 (max−min)/1.5초, 거버너 dv=clip(gain×(cap−v), ±maxAssistAccel), 제동 비대칭(v>cap+0.15 → gain=max(brakeGain, own)), 코너 상한 6m 창, 스킬 미러(사슴 제외), 라인업 7종+무작위 중복 1, EV=P1×100+P2×50+P3×30, 목표 밴드 23~28. 튜너: 폭·가속 고정, 중앙값만 비례 스텝(K=0.08, ±0.8 클램프)+제로섬 보정 — 주의: 스텝 ±1 이상 고정폭은 발산한다 (좁은 범위 동물은 분포가 스파이크라 중앙값 민감도 극단).
시뮬 파일 2종은 유저에게 전달됨 — 프로젝트에 없으면 위 사양으로 재작성 가능.
3-5. 멀티 이탈/복귀 규칙 (5-3 확정)
방 생성 시 PlayerTtl=60000. 명시적 퇴장(창닫기) = 자리 즉시 삭제 vs 비정상 끊김 = TTL 보존+ReconnectAndRejoin 복귀.
게스트 이탈(매치 중): 호스트가 노는 봇을 그 PlayerState에 Bind → 이름 그대로 봇 대타. 복귀 시 봇 해제+로스터 재전송. **v5 실기 검증: 이탈→봇 대타 통과** (§10).
⚠ 대타 여부는 `PlayerState.IsBot`으로 판단하면 안 된다 — 그건 생성 시점 플래그라 대타가 붙어도 false 그대로다. 진짜 판정은 `BotController.BoundId`(-1=놀고 있음). v5에서 이걸 착각해 "봇 대타 실패"로 오진할 뻔했다.
매치 중 방장 이탈 = AbortMatch: 정산 없이 전원 같은 방 대기실 복귀, 새 방장 레버 승계, 방 재개방. 대기실 방장 이탈은 조용히 승계.
크래시 방장의 유령 자리 60초 점유(4/4로 보임)는 수용된 한계.
3-6. 기타 확정
베팅 슬롯: 드래그 교체만, 클릭 취소 없음. 같은 동물 다른 존 이동 시 기존 존 자동 비움.
안내판(B) 확정: 출전표 행 클릭 → 중앙 팝업 (구현+조립 완료, v3에서 카드 600×500 확대, v4에서 Card 스케일 1.5 = 체감 900×750 — 양쪽 tablet 모두). 짧은 탭=팝업, 드래그=베팅 (EventSystem이 드래그 시 클릭 자동 무효화 — DraggableBetIcon이 IPointerDown을 안 먹어서 가능한 구조).
단말기 2대, 점유 비동기화 수용(점유 RPC는 백로그).
등번호판: 새들클로스 팔레트(1흰2검3빨4파5노6초7주황8분홍), RacerColors가 단일 출처 — 안내판 팝업 배지·전광판(대형/미니맵)·미니맵 마커 전부 이것 참조. v4: 등 1판 → 양 옆구리 2판 (등에선 잘 안 보인다는 유저 결정 — §5 프리팹 참조).
동물끼리 물리 충돌 전면 오프 (원본 문서 누락이었음): RaceManager가 스폰 후 "Racer" 레이어 상호 무시(레이어 없으면 쌍별 IgnoreCollision 폴백). 몸싸움은 물리가 아니라 RacerMotor의 sideRepel 스프링+회피 행동으로만 연출. ApplyFrictionless(무마찰)는 벽/지면 대상 잔존. → "충돌 순간 감속" 류 기능은 물리 이벤트로 못 만들고 근접 판정 기반으로 해야 함.
플레이어-동물 충돌 오프 확정 (v4 — §8의 F 결정 완료, 유령 통과 채택): RaceManager.IgnorePlayerCollisions()가 씬의 모든 CharacterController × 동물 콜라이더를 쌍별 IgnoreCollision. 호출 3곳 = 호스트 스폰 직후 + RegisterNetworkRacer(클라 등록 — 미러 동물도 CC를 밀어내서 필수) + NetworkPlayerSetup.Awake(재접속 복귀 아바타 커버). 중복 호출 무해. 레이어 방식 불가 사유: 플레이어가 Default 레이어라 Default↔Racer를 끄면 동물이 도로(Default)까지 뚫고 떨어짐.
3-7. 실맵 1호 트랙 (v3 세션 구축) ★
유저가 모듈 도로 에셋(Road 오브젝트, 조각 54개)으로 깐 서킷을 InnerLine/OuterLine 웨이포인트로 자동 변환 (MCP 에디터 스크립트 — 각 조각의 차선 라인 메시 정점을 읽어 가장자리 추출 → 조각 연결 그래프 → 루프 워크). 결과: 단면 177쌍, 총 519m, 폭 6.6m(S커브 구간 최대 9.8 — 단면 기울기 과대치, 잔디 밟으면 조임 필요).
구조 특징: 8자(figure-8) 자기 교차 + 터널 + 다리(터널 지붕 램프, 최고 y≈3) — GetProgressNear 연속성 투영이 자기 교차를 처리, 전역 안/밖 구분 없음(좌우 레일 길이 동일이 정상).
출발선=결승선 통일: 웨이포인트 시작/끝점(P000/P176)이 스타트라인 페인트(-42, 2)에서 만남. 스타트 슬롯(Gates 자식 8개=RaceManager.startSlots)은 라인 뒤 z=0에 0.8m 간격 일렬. 구 프로토타입 라인은 Track 밑 InnerLine_OLD/OuterLine_OLD로 비활성 보관(지워도 됨).
테마: 사실상 "레이싱 서킷"으로 진행 중 (타이어 배리어/피트스톱/관중석/터레인 에셋 배치됨). 맵 수정 시 웨이포인트 재생성은 MCP로 재실행 가능(§11 MCP 지식 참조).
⚠ V7 밸런스는 구맵 187m 기준 — 새 맵 확정 시 BalanceExport 재추출→시뮬 검증 필요 (§3-4 루프).
3-8. 캐릭터 커스터마이징 (v5 세션 구축) ★
에셋팩 ithappy/Creative_Characters_FREE 기반. **부위 프리팹 34개가 전부 동일한 44개 본에 스킨돼 있다는 것을 실측 확인** — 그래서 슬롯 렌더러의 sharedMesh만 갈아끼우면 애니메이션을 그대로 따라간다 (부위 프리팹을 통째로 붙이면 자기 뼈대 복사본이 따라와서 옷만 T포즈로 굳는다. 반드시 메시만 쓸 것).
슬롯 10종 (부위 수): 몸 1(고정) / 표정 3(고정) / 상의 5 / 하의 3 / 소품 5 / 안경 2 / 장갑 2 / 머리 3 / 모자 4 / 신발 4. 몸·표정 외에는 "안 씀"(-1) 선택 가능 → 이론상 388,800 조합. 단 **멀리서 구분되는 신호는 상의·모자·머리뿐**(6×5×4=120)이고, 몸이 1종·색 커스터마이징 불가(전 부위가 텍스처 아틀라스 하나를 공유하는 단일 머티리얼)라 실전 체감 다양성은 8~12종 수준.
외형 코드: `CharacterCustomization.Encode()` = "0,2,1,0,-1,..." (슬롯 순서대로 인덱스, -1=안 씀). 30바이트 남짓이라 그대로 저장·전송한다.
저장/동기화 흐름: 타이틀 확정 → PlayerPrefs("characterLook") + `PlayerLook.Publish()`(Photon 플레이어 커스텀 속성 "look") → 게임 씬 스폰 시 NetworkPlayerSetup이 내 것은 PlayerPrefs, 남의 것은 그 사람의 속성으로 착용. 입장 시 1회 전송이라 대역폭 비용 사실상 0. **v5에서 2인 실기 검증 완료.**
비활성 슬롯은 정점 4개짜리 빈 껍데기 메시(Base_Mesh 기본값)라 렌더 비용 0.
미지원: 전신 코스튬(FullBody, 2벌) — 여러 슬롯을 한 번에 덮어쓰는 구조라 별도 처리가 필요해 1차에서 제외.
⚠ 에셋팩 커스터마이징 툴(Tools > ithappy > ... > Character Customization)은 **에디터 전용**이다. 그 툴은 프리팹을 굽는 용도고, 우리 인게임 커마와는 무관하게 공존한다.
---
4. 파일별 상세 (58개 = v4의 52 + v5 신규 6: BoostDustFx, CharacterPartLibrary, CharacterCustomization, CustomizationPanel, PlayerLook, MppmTestClient)
Core — 게임 흐름
GameConfig.cs (SO): 전 튜닝의 집. 포인트(100/50/30), 페이즈 시간, racerCount=8, 주행 기본(lookAhead=4/maxAssistAccel=20), 레이싱라인(racingLineLookAhead=9/insideBiasStrength=0.7/curvatureSaturation=6/roadMargin=1.2), 코너 감속(cornerDecelEnabled/Rate=0.22/SenseAhead=6/BrakeGain=4.5), 완주 연출(finishCoastMin=3/Max=8/finishSpread=2.2 — v3), 회피(avoidLookAhead=2.6/bodyClearance=1.1/overtakeShift=1.6/blockedSpeedFactor=0.9/lateralSmoothTime=0.45/lateralMaxSpeed=3.5/sideBySideRange=1.5), 디버그 토글.
GameManager.cs: 페이즈 상태머신 껍데기(싱글턴). 실질 진행은 MatchManager.
GameEvents.cs: static 이벤트 버스 + RaceResult 클래스(round, firstId/secondId/thirdId, pointsGained).
MatchManager.cs: 매치 순환(Betting→Loadout→Countdown→Racing→Settlement×N), SubmitBet 관문(3픽 검증), SettlePoints(슬롯별 채점), AutoBet, AbortMatch, 클라 거울 API.
PlayerState.cs: Points/AddPoints/ResetPoints, BetTicket(firstId/secondId/thirdId, IsValid), 아이템 로드아웃/쿨다운.
BotController.cs: 봇 두뇌. Bind(PlayerState) 대타 겸용. 아이템 AI: 1등픽이 선두 아니면 부스트, 내 3픽 아닌 침입자가 선두면 감속.
PrototypeBootstrap.cs: 씬 시작 오케스트라. 온라인 "입장 확정까지 대기", 오프라인 나+봇. 로드아웃 배포+봇 랜덤 3픽.
ItemExecutor.cs: 아이템 사용 단일 관문(페이즈/쿨다운/보유/타겟 검증).
Racing — 레이스·주행
TrackPath.cs (v2 — 경계 두 줄): InnerLine/OuterLine 자식 쌍이 경로 정의(같은 개수·순서·i번끼리 단면). API: TotalLength, GetProgressNear(연속성 투영), GetPoint/GetTangent(평활)/GetNormal/GetPointAt/GetLateralOffset/GetHalfWidth/GetLateralLimit, GetSignedCurvatureAhead(도/m), GetTargetOnSection(두 레일 보간 — 퇴화 원천 차단), Build()는 public(에디트 모드 호출 가능 — BalanceExporter가 사용). 빌드 검진 로그+기즈모(안 하늘/밖 노랑/쌍 가로대).
RacerMotor.cs: 진짜 레이스 주행(호스트 전용). ① 레이싱 라인(전방 9m 곡률→인코스, personalMargin 분산) ①.5 코너 감속(6m 창 senseT→상한 곱, baseCap/speedCap 분리 — "더 빠른 놈" 비교는 baseCap 기준) ② 회피/추월(양쪽 열리면 인코스 방향) ③ 간격 스프링(sideRepel, 포개짐은 번호 홀짝) ④ 교착 감시견(0.8초) ⑤ GetTargetOnSection 목표 ⑥ 횡 SmoothDamp ⑦ transform 직접 회전 — v3: 지면 법선 정렬(RotateToward/SampleGroundUp: 발밑 레이캐스트→경사·다리에서 몸 기울임, 동물/벽면 법선 제외). 제동/가속 게인 분리(§3-2). v3 완주 연출: FinishCoast — 결승 후 랜덤 3~8m 관성 주행+좌우 ±2.2m 산개 후 정지(휴식 지점은 GetTargetOnSection로 1회 산출). 디버그 라벨: `#id prog / lat→desired / v 현재/상한 curv T 막힘`.
Racer.cs: 시뮬 두뇌(리롤 15초, 상태이상, 스킬 상태, 펭귄 면역=AddEffect 관문). CurrentMaxSpeed=리롤×효과×스킬(스턴=0).
StatusEffects.cs: Boost/Slow/Stun.
AnimalSkill.cs: 스킬 enum+SkillTuning 상수 단일 출처+DisplayName/Description (안내판 팝업이 사용 중).
RacerColors.cs / RacerNumberPlate.cs: 번호 색 단일 출처 / 번호판 — v4 재작성: 자식의 일반 MeshRenderer 전부(=판 큐브들)+TMP 전부에 일괄 적용. 판 개수 가정 없음(현재 옆구리 2판), 직렬화 슬롯 제거·순수 자동 탐색. 컴포넌트는 프리팹 루트에 있음.
RaceManager.cs: 스폰(랜덤 7종+중복1, InstantiationData), 스폰 후 동물 간 충돌 전면 오프(§3-6), 시뮬 루프(연속성 투영+투영점프/NaN 감시, UpdateSkillContext=개 꼴등+호랑이 습격), GetFinalRanking, EnsureBodyCollider(v3 개정: SkinnedMesh 바운즈 기반 "바닥 정렬 캡슐" — 바닥=발끝, 반지름=몸 반높이, 루트 아래 0.05 클램프(펭귄 바인드포즈 -0.3 부양 방지). 박스는 이음새 고스트 충돌로 급정지해서 금지 — §11. v4: radius/height를 lossyScale로 나눔 — 콜라이더 값은 로컬 단위라 스케일된 프리팹(치킨/고양이 1.5배)에서 겹으로 곱해져 몸이 떠오르던 버그 픽스), ApplyFrictionless. v3: 정산 페이즈에도 모터 SimEnabled 유지(완주 산개 연출 재생 — 끄면 꼴등이 무마찰로 영원히 미끄러짐). v4: IgnorePlayerCollisions(§3-6 플레이어-동물 충돌 오프).
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
BetDropZone.cs / DraggableBetIcon.cs(IBeginDrag/IDrag/IEndDrag만 구현 — IPointerDown 안 먹는 게 클릭 공존의 전제, 추가하지 말 것) / BettingTerminal.cs(E→패널, occupied 로컬. v3: Awake에서 matchManager 비면 자동 탐색 — 단말기 복제 시 배선 누락 NRE 사고 재발 방지).
SettlementPanel.cs / ResultRowView.cs / BetChipView.cs("① 이름 +100P", 적중 초록).
Scoreboard.cs: 월드스페이스 소형 전광판(페이즈/타이머/라운드) — 원본 문서의 "Tab 현황" 서술은 오류였음. Tab UI는 존재하지 않음.
ScoreboardBoard.cs (v3 신규): 거대 전광판 두뇌 — 러닝타임 시계(Racing 시작 리셋/정산 정지) + 실시간 순위표(레인 배지=RacerColors, 아이콘=AnimalDefinition.icon 자동, 이름, 현재 속도) + 순위 변동 시 행 슬라이드(anchoredPosition Lerp) + 완주 시 속도→최종 기록 전환(OnRacerFinished 이벤트 — Gateway 중계라 클라도 동작). 진행도·속도는 로컬 위치 기반 계산(GetProgressNear 연속성 투영 — 클라는 미러 위치로 동일 계산). 라인업 변동 자동 감지→행 재구성. 씬의 전광판 오브젝트는 통째로 복붙 가능(내부 참조 자동 리매핑, 로컬 전용이라 네트워크 무관). 튜닝: rowMoveSpeed/speedSmooth 인스펙터.
MinimapBoard.cs (v4 신규): 미니맵 전광판 두뇌 (전광판1·2에 부착). 좌측 = 트랙 실루엣 미니맵: TrackPath 중심선을 0.4m 간격 샘플→반폭 원 스탬핑으로 640px 텍스처를 런타임 1회 굽기(트랙 정적) + 출발선 표시. 동물마다 "가운데 빈 원"(도넛) 마커가 실시간 이동 — 도넛 스프라이트도 코드 생성(64px, rOut 29/rIn 8/검정 테두리 3.5 — 테두리를 검정으로 구워서 흰/검 레인도 어떤 배경에서든 보임. v4 중 유저 요청으로 rIn 15→8 구멍 축소). 우측 = 간단 순위표(배지+이름만, 행 슬라이드). 진행도는 ScoreboardBoard와 동일한 로컬 위치 기반(GetProgressNear 연속성 투영) — 클라 동작, 네트워크 무관. 라인업 변동 자동 재구성. 튜닝 인스펙터: textureSize/trackColor/startLineColor/markerSize(48)/rowHeight/rowMoveSpeed. ⚠ 전광판2는 몸체가 180° 돌아 있어 미니맵 방향도 반대로 보임(보는 방향 기준) — 유저가 통일 원하면 한쪽 뒤집기 옵션 필요.
CloudField.cs (v4 신규): 구름 컨베이어 (씬 Clouds에 부착, 자식 25개). 표류 축 driftAxis(기본 월드 Z), 방향 부호는 방 이름 수제 해시(31곱 누적)의 홀짝 — 방 파질 때 확정·전 클라 동일·통신 불필요, 오프라인은 판마다 랜덤. 영역(초기 배치에서 역산)을 margin(60m) 넘게 벗어나면 반대편 가장자리에서 횡위치/높이/크기/회전/속도 재추첨 후 재등장 = 보기엔 절차 생성, 실제론 재활용(스폰/GC 0). 구름별 속도 편차 ±35%로 시차감. 순수 장식 로컬 처리. ⚠ Clouds는 움직이므로 Batching Static 금지.
TimelineFeed.cs(우측 사건 피드) / ItemSlotView.cs / StartLever.cs(방장 레버, wallsToHide) / PixelBorder.cs([유저 자작] 건드리지 말 것).
CustomizationPanel.cs (v5 신규): 타이틀 커마 UI. 슬롯 개수가 라이브러리에 따라 변하므로 **UI를 코드로 자체 조립**(MinimapBoard와 같은 방식). 슬롯별 ◀ 이름 ▶ 행 + 랜덤/취소/확정. 확정=PlayerPrefs 저장+방송, 취소=열기 전 스냅샷 복원, Esc=취소. hideWhileOpen 배열(메인 메뉴·닉네임·상태문구)을 커마 중 숨긴다. 열려 있는 동안 매 프레임 라벨을 갱신하되 값이 같으면 TMP를 건드리지 않는다.
Racing/연출 — 신규
BoostDustFx.cs (v5 신규): 부스트 먼지구름. RaceManager가 스폰(호스트)/등록(클라) 시 붙인다 — 아이템 사용은 게이트웨이가 이미 전 클라로 중계(OnItemUsed)하므로 **네트워크 추가 통신 0**, 각자 로컬 재생. 파티클 시스템·머티리얼·텍스처를 전부 코드 생성(프리팹 7종 무수정). 배출 지점은 스킨드메시 로컬 바운즈를 루트 로컬로 옮겨 실측한 "뒷다리 뒤 바닥", 크기는 몸 높이 비례(하한 0.45m — 고양이/치킨이 점처럼 작아지는 것 방지). 펭귄(무관심)은 부스트가 안 먹히므로 먼지도 안 난다(연출이 거짓말하지 않게). 스턴/정지(1.5m/s 미만)에도 안 남. `Play(초)`가 public이라 스킬 부스트에도 재사용 가능. 튜닝은 GameConfig "연출 — 부스트 먼지구름" 섹션.
Content/커마 — 신규
CharacterPartLibrary.cs (v5 신규, SO): 슬롯 정의(렌더러 이름·한글 표기·끄기 허용·빈 메시·부위 메시 목록). 에셋팩의 SlotLibrary에서 MCP 스크립트로 자동 생성했다 — 부위가 늘면 같은 방식으로 재생성.
CharacterCustomization.cs (v5 신규): 외형 조립기. 슬롯 이름으로 렌더러를 찾아 sharedMesh 교체(+localBounds 갱신 — 안 하면 컬링이 틀어져 옷이 사라진다). Encode/Decode/SaveToPrefs/Randomize. defaultCode = 저장값 없을 때의 기본 차림(알몸 방지). 타이틀 전시용 캐릭터와 인게임 아바타가 같은 컴포넌트를 쓴다.
Network
NetworkLauncher.cs(타이틀: 접속 kr/방 목록/CreateRoom PlayerTtl=60000/LoadLevel) / TitleMenu.cs / RoomListItem.cs.
NetworkPlayers.cs(LocalPlayerId, IsAuthority, BotIdBase=100).
NetworkGateway.cs: 요청/중계 허브. RequestSubmitBet→호스트 검증→RpcBetResult+개인 영수증. 경제 방송 1초(ids/points/boost/slow/submitted). RpcSettled→클라 RaceResult 재조립. 5-3(봇 대타/해제/방 재개방/로스터).
NetworkMatchSync.cs(1초 페이즈/타이머/라운드) / NetworkPlayerSpawner.cs(입장 확정 대기) / NetworkPlayerSetup.cs(미접속=내 것. v4: Awake 끝에 RaceManager.IgnorePlayerCollisions 호출 — 재접속 복귀 아바타 커버) / LocalPlayerBinder.cs(HUD/단말기/레버 배선) / NetworkRacerSetup.cs(동물 클라 등록) / NetworkSessionGuard.cs(ReconnectAndRejoin, 방장 교체→AbortMatch).
PlayerLook.cs (v5 신규, static): 외형 코드 방송/수신. `Publish()`는 Photon 플레이어 커스텀 속성 "look"에 얹는다 — **방 밖에서 호출해도 PUN이 캐시했다가 입장 때 함께 보낸다**(타이틀에서 확정한 값이 그대로 반영되는 이유). `Of(player)`로 남의 코드를 읽는다. `Override`는 테스트 전용 우회 통로(§11 MPPM 항목).
MppmTestClient.cs (v5 신규, `#if UNITY_EDITOR`로 전체 감쌈 = 빌드 제외): 멀티플레이어 플레이 모드의 **가상 플레이어 창에서만** 살아난다(`CurrentPlayer.IsMainEditor`가 false일 때). 창마다 닉네임(제비#2/#3)과 외형 프리셋을 다르게 주고, 로비에 들어가면 비번 없는 방에 자동 입장. 가상 플레이어가 PlayerPrefs를 본체와 공유하는 문제(§11)를 우회하는 장치.
Editor 전용
BalanceExporter.cs (신규): §3-4 파이프라인의 유니티 쪽 절반. #if UNITY_EDITOR 전체 래핑(빌드 무포함). 씬 GameManager.config/RaceManager.animalPool을 SerializedObject로 읽음(비공개 필드 접근) — 우선순위: 씬 참조 > 프로젝트 검색.
---
5. 씬/프리팹 배선 현황
SampleScene: Manager 오브젝트(GameManager+MatchManager+RaceManager+Gateway+MatchSync+SessionGuard+Binder+Spawner+PlayerItemController+Bootstrap), Road(모듈 도로 54조각 — §3-7), Track(신 InnerLine/OuterLine 177점 + 구 라인 _OLD 비활성 보관), Gates(스타트 슬롯 8개 — 출발선 뒤 z=0, 0.8m 간격), 전광판 3대: 대형전광판(x≈80 상공, ScreenBody 21×13 + ScoreboardBoard — 러닝타임/속도형 순위표 유지) + 전광판1(z≈59)·전광판2(z≈-57, 180° 회전) — v4에서 둘 다 ScoreboardBoard 제거→MinimapBoard로 교체 (캔버스 2000×1200: 좌 MapImage 1080×1080+Markers, 우 RowContainer 760폭·배지+이름 행. TimeText/Icon/ValueText 삭제됨. MapImage는 에디터에서 알파 0.04 — 런타임에 텍스처 굽고 흰색 복원), 레이싱 환경 에셋(타이어 배리어/피트스톱/관중석/터레인/나무), Clouds(자식 25개 + CloudField — v4), 대기실+벽(StartLever.wallsToHide), 베팅 단말기 2대(tablet/tablet(1) — 팝업 Card 스케일 1.5), HUD 캔버스, 끝선(비활성 — 통일로 불필요. 출발선 오브젝트는 v4에서 삭제됨 — 회색 큐브 2+초록 큐브 1짜리 장식, 미니맵 출발선은 웨이포인트 기준이라 무관), BotA×3.
TitleScene(빌드 0) — v5에서 대폭 개조: Managerrs(NetworkLauncher+TitleMenu), Canvas(기존 UI + **TitleText "Dirty Derby"** + **CustomizationPanel**), **TitleStage(신규: Ground 60m 평면 + TitleCharacter)**, Main Camera(0.62, 1.28, -3.3 / FOV 52 — 얼굴 높이로 맞춰야 고개 젖힌 것처럼 안 보임).
· 배경 검은 Image와 MainPanel의 Image는 **알파 0으로 투명화**(오브젝트는 남겨둠 — 되돌리기 쉽게). 뒤의 3D 무대가 보인다. 유저가 나중에 배경 아트를 깔 예정.
· 메인 버튼 4개: 방만들기/방참가/게임종료/**커스터마이징**(종료 버튼 복제, y-101 간격). 커스터마이징 → CustomizationPanel.Open()을 영속 리스너로 연결.
· TitleCharacter = Base_Mesh 프리팹 언팩본 + Character_Movement 컨트롤러(Idle_Relaxed 재생) + CharacterCustomization(defaultCode "0,2,1,0,-1,-1,-1,0,-1,2").
· 상태 문구(StatusText)는 캐릭터 얼굴을 가려서 좌하단으로 이동, 닉네임 입력은 타이틀과 겹쳐 아래로 내림.
NetPlayer 프리팹(Resources): FPC+카메라+PlayerInteractor+PhotonView+TransformView+AnimatorView+NetworkPlayerSetup+NameLabel. **v5: 커마 13슬롯 구조로 개조** — 기존 `Body_011` 렌더러를 `Body` 슬롯으로 승격하고 나머지 9개 슬롯 렌더러(표정·상의·하의·소품·안경·장갑·머리·모자·신발)를 추가. 전부 기존 뼈대(bones/rootBone)를 그대로 공유하고 기본 메시는 빈 껍데기. CharacterCustomization 부착 + NetworkPlayerSetup.look에 배선. **네트워크 컴포넌트 배선은 하나도 안 건드렸다**(렌더러만 얹은 방식이라 동기화 위험 없음).
동물 프리팹 7종(Resources): 모델+Animator+Rigidbody(FreezeRotation)+Racer+RacerMotor+PhotonView+TransformView+AnimatorView+NetworkRacerSetup+RacerNumberPlate(루트)+번호판. v4: 등 1판 → 양 옆구리 2세트(PlateCubeL/R + PlateNumL/R, 형제 구조)로 교체 — 부모는 v3 그대로 등뼈 본(개 spine.005/고양이 spine.006/말 spine.006/치킨 spine.003/펭귄 spine.005/호랑이 spine.010/사슴 spine.004)이라 달릴 때 출렁임 유지. 옆구리 x오프셋은 스킨드메시 정점 실측(뿔/날개로 바운즈 과대인 사슴·펭귄 대응), TMP는 LookRotation(±x, up)으로 좌우 각각 바깥면·숫자 직립 보장. v4: 치킨·고양이 루트 스케일 1.5배(유저 요청 — 몸통 콜라이더는 스폰 시 자동 산출이라 무추가작업, 대신 §11 콜라이더 로컬 단위 법칙 참조).
Run In Background 설정됨. 동기화 컴포넌트 변경 시 재빌드 철칙 (v3·v4 변경분은 전부 동기화 무변경 — 단 프리팹 비주얼 변경(번호판/크기)은 클라 로컬 스폰이라 멀티 테스트 시 빌드 갱신 필요).
---
6. 최근 완료 (v5 세션 하이라이트)
부스트 먼지 연출 (BoostDustFx.cs + 텍스처/머티리얼 에셋): 플레이어가 부스트를 먹였을 때 뒷다리 뒤로 카툰 먼지가 남는다. 1차는 에어브러시식이었는데 유저 요청으로 **셀 애니메이션식(굵은 검정 테두리 + 단색 + 팔랑팔랑 회전 + 조각 4종 랜덤)** 으로 재작업. 아틀라스 2×2를 코드로 굽고 TextureSheetAnimation으로 조각을 랜덤 배정. 관전 거리 캡처로 크기/테두리 두께 2회 재조정.
번호판 몸통 밀착 (동물 프리팹 7종): "너무 떠 있다"는 지적 → v4 배치가 **밴드 최대폭**(갈비/날개 폭) 기준이었던 게 원인. 판 중앙 지점의 실제 표면을 다시 재고 **표면 + 판 반높이의 25%** 로 재배치. 호랑이 0.394→0.276, 펭귄 0.294→0.211. 중간에 딱 붙였다가 달리기 근육 부풀림에 숫자가 묻히는 걸 캡처로 잡아내고 여유를 다시 넣음.
1인칭 몸통 뚫림 수정 (FirstPersonController): 달릴 때 아래를 보면 몸통이 뚫려 보이던 문제. 원인은 근접 클리핑이 아니라 **달리기 애니가 상체를 0.27m 숙이면서 카메라(루트 고정)가 몸 안으로 들어가는 것** — 몸 안에서는 표면이 전부 뒷면이라 컬링돼 사라진다. 카메라를 **머리 본에 물리는 방식**(위치만, 회전은 마우스/몸통이 결정)으로 해결. near clip도 0.3→0.05.
캐릭터 커스터마이징 전체 (§3-8): 라이브러리 SO 자동 생성 → 조립기 → 타이틀 UI → 인게임 아바타 → Photon 동기화까지 한 줄로 연결. 2인 실기 검증 완료.
타이틀 화면 개조: 검은 배경 투명화 + 3D 무대(지면/캐릭터/조명) + Dirty Derby 타이틀 + 커스터마이징 버튼/패널. 유저 목업대로.
빌드 실패 수정 (asmdef): 에셋팩 asmdef가 Editor 폴더까지 빌드에 끌고 가 **8개 컴파일 에러로 빌드 불가**였던 것을 Editor 전용 asmdef 추가로 해결. PlayerBuildInterface로 실제 플레이어 스크립트 컴파일까지 돌려 통과 확인.
MPPM 멀티 실기 테스트 (§10): 빌드 없이 2인 테스트 성공. 옷 동기화 / 베팅→레이스→정산 / 이탈→봇 대타 검증.
---
6-1. 이전 세션 완료 (v4 하이라이트 — MCP 검증 루프 정착: 매 작업을 플레이 진입→계측→캡처→종료로 자동 검증)
미니맵 전광판 (MinimapBoard.cs + 씬 조립): 전광판1·2를 미니맵+간단 순위표로 교체 (대형전광판은 유지). 트랙 실루엣 굽기·도넛 마커·순위 슬라이드 전부 자동 검증+캡처 확인.
번호판 옆구리 2판 (프리팹 7종 + RacerNumberPlate 재작성): 등판이 잘 안 보인다는 유저 지적 → 좌우 옆구리 각 1판. 정점 실측 배치+TMP 방향 직립. 7종 좌/우 캡처 검증.
플레이어-동물 충돌 오프 (§8 F 결정 완료): IgnorePlayerCollisions 쌍별 무시, 훅 3곳(호스트 스폰/클라 등록/아바타 스폰). 8쌍 전부 GetIgnoreCollision=true 실측.
구름 컨베이어 (CloudField.cs): Clouds 25개 재활용 순환, 방 이름 해시로 방향 확정. 1800m 강제 순환 검증.
치킨·고양이 1.5배 + 부양 버그 픽스: 확대 후 공중부양 → EnsureBodyCollider가 콜라이더 로컬 단위에 스케일 미반영이 원인(§11 신규 법칙). lossyScale 나눔으로 해결, 발끝-지면 3cm 실측 확인.
자잘: 출발선 오브젝트 삭제, 안내판 팝업 카드 1.5배, 도넛 구멍 축소(rIn 15→8).
성능 진단 (처방 대기 — §7): URP+SRP Batcher ON, SetPass 76(건강). 문제 = 씬 렌더러 1337개 중 Batching Static 0개(배경745/Track354/Road184가 전부 비정적) + Shadow casters 1279(Shadow Distance 50m). 에디터 147FPS라 당장 문제없으나 저사양 대비 여지 큼.
게임 제목 논의: 영문 Dirty Derby 추천(§8), 스플래시아트 AI 생성 프롬프트 세트 전달됨.
---
7. ★ 다음 작업 큐 (우선순위순)
★ 버그 (v5에서 발견, 미수정): **원격 아바타가 외형 코드를 못 받으면 내 저장 옷을 입는다.** CharacterCustomization.Awake가 무조건 PlayerPrefs를 읽기 때문에, 커마를 한 번도 안 한 사람의 아바타가 내 화면에서 "내 옷"을 입고 나타난다. 처방: `loadSavedOnAwake` 같은 플래그를 넣어 NetPlayer는 Awake에서 defaultCode만 적용하고, 실제 착용은 NetworkPlayerSetup.ApplyLook이 전담(코드가 비면 defaultCode). 10분짜리 수정.
감속 아이템 연출 구현 (§8에 기획 정리됨 — 유저 결정 대기): 부스트 먼지의 짝. 추천안은 "검은 끈끈이(타르) + 머리 위 표식". 확정되면 BoostDustFx와 같은 구조로 붙이면 됨.
멀티 실기 잔여 테스트 (§10): ① 복귀(끊김→재접속) — UserId 고정 + TTL 임시 연장 필요 ② 호스트 이탈 → 게스트 방장 승계 + 대기실 복귀 + 방 재개방 ③ 클라 화면 코너 감속 대열/전광판/완주 연출 육안 확인.
아이템 로드아웃 개편 확정 (§8 — 유저 고민 중): 확정되면 로드아웃/특수권 구현.
새 맵 밸런스 검증: 트랙 확정되면 BalanceExport 재추출 → corner_sim 검증 (V7은 구맵 187m 기준, 현 맵은 519m — §3-7 ⚠).
V7 SO 입력 여부 확인 — 미입력이면 §3-3 표대로 입력 안내(7종×3필드).
치킨 꼴등 39.8% 승인 확인 (§3-3 ⚠) — 처형권 논의(§8)와도 얽힘.
커마 후속 (v5 잔여): ① 대기실(인게임)에서도 커마 열기 — 패널이 씬 독립적이라 붙이는 건 금방 ② 전신 코스튬(FullBody 2벌) 지원 ③ 부위 이름이 "상의 2" 같은 자동 번호라 감성 이름으로 다듬을 여지.
킥 결정 대기: "관전 90초 밀도 패키지" — 전광판(C)·미니맵 전광판·플레이어-동물 충돌(F)은 해결됨. 잔여: 익명 저격("누군가 3번을 저격했다!") + 호랑이 습격 연출(포효/흔들림). 유저 ㄱ 답 오면 즉시 구현.
성능 최적화 실행 대기 (v4 진단 완료): ① 배경/Track/Road에 Batching Static 일괄 체크 (MCP로 가능. ⚠ Clouds는 움직이니 제외, Gates/동물/단말기 등 게임플레이 오브젝트 제외) ② Shadow Distance 50→25~30 — 그림이 달라지니 유저가 보면서 결정.
~~포인트값 불일치~~ — v5에서 실행 중 계측으로 해결. **SO 실값 70/50/10 확정**, 문서 §1 수정 완료.
유저 육안 확인 잔여 (v5 추가): 타이틀 화면 배치(캐릭터 위치·크기·카메라 각도, 패널 위치/크기/색), 지면 색, 커마 부위 이름, "게임 종료" 버튼 유지 여부(목업엔 없었으나 남겨둠), 부스트 먼지 색(현재 크림색)·개수·꼬리 길이.
유저 육안 확인 잔여 (v4부터): 다리 오르막에서 동물 기울기(경사 정렬 — 수치 검증 못 함), 완주 산개 체감(밋밋하면 finishCoastMax/finishSpread 조절), S커브 잔디 밟기 여부, 미니맵 전광판·옆구리 번호판·구름 흐름·치킨/고양이 1.5배 체감(전부 v4 자동 검증은 통과, 감성 판단만 남음), 구름 속도 4m/s 체감(느리면 speed만 ↑).
---
8. 미결 기획 (결정 대기)
아이템 로드아웃 개편 (v3 논의 — 유저 고민 중): 재화 없이 판마다 공평 5개 = 자유 조합 4개(슬로우/부스터 4:0~0:4) + 특수 1개(발동권 vs 처형권 택1). Claude 평가: 자유 조합 4개 승인 권장(비밀 베팅과 연계된 정보전, 단 519m 트랙에 4개는 밀도 확인 필요 — 개수는 GameConfig로). 발동권은 스킬별 "즉시 발동" 정의표 선행 필요(호랑이=습격 타이밍 조작이 본체, 펭귄=꽝 유지 추천). 처형권은 원안 반대 — "꼴등" 키가 치킨(슬럼프 꼴등이 설계)·개(충성심=꼴등 엔진)와 구조 충돌 + 4인 연쇄 처형 문제. 역제안 A: 진행 70%+ & 판 전체 1회 한정. 역제안 B: 처형권→봉인권(지정 동물 스킬 1회 무효 — 발동권과 공수 대칭).
감속 아이템 연출 (v5 논의 — 유저 결정 대기): 부스트=크림색 먼지가 뒤로 남는 그림이니, 감속은 **반대 축**(어두운 색·발밑·아래로 처짐)을 써야 대비가 산다. 제약 3가지 = ① 관전 거리 12m에서 누가 당했는지 즉시 읽혀야 함 ② 뒤 동물을 가리면 순위 판독이 죽음 ③ 먼지와 헷갈리면 안 됨. 후보: **A 검은 끈끈이(타르)** — 명중 시 철퍽, 3초간 발이 들러붙고 도로에 얼룩이 남음(당한 자리가 흔적으로 남아 뒤늦게 본 사람도 파악 가능. 단 발밑이라 가려질 수 있음) / **B 머리 위 쇳덩이·닻** — 하늘 배경이라 어디서도 안 가림, "무겁다=느리다"가 설명 없이 통함(단 총 쏴서 앵커가 나오는 그림의 정합성 문제) / **C 헐떡임(땀+처진 자세)** — 자연스럽고 귀엽지만 "남이 한 짓"으로 안 읽혀서 저격의 통쾌함이 죽음. Claude 추천 = **A + B의 머리 위 표식만 결합**(타르가 "무슨 일", 표식이 "누가 당했나" 담당). 덤 결정: 감속 연출은 **아이템 피격에만** 붙이고 스킬 자멸(고양이 변덕 실패·치킨 슬럼프)에는 안 붙이거나 C안 약한 버전만 — 안 그러면 저격의 의미가 희석됨.
킥(최대 난제): 패키지 중 전광판(C)은 완료. 잔여 = 익명 저격 + 호랑이 습격 연출. 진단 = "레이스 관전 90초가 죽은 시간".
~~플레이어-동물 충돌(F)~~ — v4에서 유령 통과로 확정+구현 완료 (§3-6).
게임 제목 (v4 논의): 영문 유력 후보 = **Dirty Derby** (Claude 톱픽 — 동물 경마+베팅+반칙이 한 이름에, 검색 안전). Racing Casino는 기각 권고(스팀 "casino" 검색 오염+동물/훅 부재), 우마무스메 변형명은 강력 반대(Cygames 상표 리스크+아류작 인상). 한국명 「짜고 치는 레이스」 유지 투트랙 안 제시됨. 미확정 — 확정 시 스팀 동명 선점 검색 먼저.
1호 맵 테마: 씬은 사실상 레이싱 서킷 테마로 진행 중(§3-7) — 열대 섬/산골짜기 안은 2호 맵 후보로 이월.
치킨 꼴등 40% 승인 (§3-3).
오프라인 자동 시작 vs 레버 통일(표류 중).
신규 동물 종 추가: 시스템 준비됨(스킬 재사용+SO+프리팹+Resources+파이프라인 자동 밸런싱)이나 출시 후 업데이트 카드로 아껴두기로 방향 잡음.
---
9. 백로그
~~전광판 실시간 순위+변동 연출(C)~~ — v3 완료 (ScoreboardBoard)
경사 트랙(경사 감속+시뮬 프로필 미구현 — 지면 스냅/몸 기울기는 v3 경사 정렬로 해결됨. 현 맵 다리가 첫 실전 경사)
5-4 폴리싱: SendRate/SerializationRate 20, 접속 실패 안내 UI, 원격 애니, 단말기 점유 동기화, Input System 전환(한글 IME — 한국 출시 필수)
시네마틱 인트로 캠, 엘리베이터 시작 연출
동물 3D 초상화 → AnimalDefinition.icon에 넣으면 안내판 팝업+전광판 아이콘 칸에 자동 표시(양쪽 연동 완료)
아이템 상점(대기획), Steam 연동(닉네임+친구 초대), 지역 선택 드롭다운
호랑이 습격 연출 — 킥 패키지 후보
~~Unity MCP 실험~~ — v3부터 상용 도구화 (씬 조립·자동 검증 루프까지, §11 운용 지식 참조)
S커브 웨이포인트 단면 조임 (폭 9.8 과대 — 동물이 잔디 밟으면)
출시 전: 디버그 토글 끄기, cornerDecelEnabled 등 A/B 토글 정리, 전광판 위치/크기 확정
---
10. 테스트 현황
★ v5 멀티 실기 검증 완료분 (MPPM 2인 — 본체=호스트, 가상 플레이어=게스트):
· **커마 동기화 통과** — 게스트가 방송한 코드 `0,1,4,2,1,-1,-1,2,-1,0`이 호스트 화면의 원격 아바타에 정확히 적용됨(로그 실측).
· **베팅→레이스→정산 한 바퀴 통과** — 레버 경로로 로스터 2명 구성 → 베팅 60초(미제출자 자동 예측) → 로드아웃 → 카운트다운 → 레이싱 → 8두 완주·순위 부여 → **정산 130점(70+50+10) 정확 지급** → 라운드 2 자동 진행. 3라운드까지 굴러감.
· **이탈 → 봇 대타 통과** — 게스트 창을 끄니 IsInactive 전환(TTL 60초 보존 경로) 후 `BotA.BoundId=2`로 자리 인수. 장부에 이름도 그대로 유지.
· 베팅 관문(3픽 검증)은 창이 60초라 MCP 왕복으로 실시간 검증 실패 — 대신 자동 베팅 경로가 정상 동작하는 것으로 간접 확인.
v5 멀티 미검증 (다음 세션 최우선):
· **복귀(끊김→재접속)** — 두 가지가 막는다: ① TTL 60초가 지나면 자리 회수 ② 창을 껐다 켜면 Photon UserId가 새로 생겨 "아까 그 사람"으로 인식 안 됨. 처방: MppmTestClient에서 `PhotonNetwork.AuthValues = new AuthenticationValues("mppm-2")`로 UserId 고정 + 테스트 동안 PlayerTtl 임시 연장(후 원복).
· **호스트 이탈 → 방장 승계 + 대기실 복귀 + 방 재개방** — 현 구조로 바로 테스트 가능.
· 클라 화면의 코너 감속 대열/전광판/완주 연출 육안 확인.
· 부스트 먼지·번호판의 멀티 화면 확인 (로컬 연출이라 이론상 동일).
포인트제 멀티 사이클 + 5-3 실기 (일부 v5 완료 — 위 참조)
V7 스탯 실전 체감("매 판 우승자가 바뀌는가", 개가 코너 탈출마다 치고 나오는지, 출발선 가속 격차 — 개 총알/펭귄 뒤뚱은 의도된 그림)
다리 경사 기울기 육안 확인 (v3 구현 — 수치 검증 못 함, §7 마지막 항목)
스킬 인게임 체감(특히 호랑이 무는 순간)
코너 감속·완주 연출·전광판의 멀티 화면 검증(TransformView 받아쓰기라 이론상 동일, 전광판은 클라 로컬 계산)
v3 자동 검증 완료분 (재확인 불요): 신트랙 전원 완주, 이음새 무정지, 펭귄 착지, 완주 산개·기록 전환, 전광판 풀사이클, 순위 슬라이드
v4 자동 검증 완료분 (재확인 불요): 미니맵 텍스처 굽기·마커 8색 이동·순위 행 재구성(캡처 2회), 옆구리 번호판 7종 좌/우 위치·숫자 직립(캡처 3회), 플레이어×동물 8쌍 충돌 무시 실측, 구름 1800m 순환·영역 유지, 치킨/고양이 착지(발끝-지면 3cm)
v4 미검증: 옆구리 번호판이 달리기 애니에서 몸을 뚫는지(정지 자세로만 캡처 — 육안 확인 필요), 미니맵 마커의 멀티 클라 화면(로컬 계산이라 이론상 동일)
---
11. 지식 아카이브 (버그/설계 패턴 사전)
★ Mathf.SmoothStep은 문턱값 함수가 아니다 (v5, 실사고): GLSL의 `smoothstep(edge0, edge1, x)`와 이름만 같고 동작이 다르다. Unity의 `Mathf.SmoothStep(from, to, t)`는 **from~to 사이를 부드럽게 보간**하는 함수라, 문턱값처럼 쓰면 결과가 from~to 범위로 나온다. 먼지 텍스처 알파를 `1 - SmoothStep(0.74, 1, d)`로 구웠다가 **알파 최대치가 0.26**이 되어 "입자가 너무 곱다"는 증상으로 나타났다. 처방 = 직접 구현: `t=clamp01((x-e0)/(e1-e0)); return t*t*(3-2t);`. 코드로 텍스처/마스크를 구울 때 항상 의심할 것.
★ ParticleSystem은 AddComponent 직후 이미 재생 중 (v5): 재생 중에는 `main.duration` 설정이 거부되고 콘솔 에러가 뜬다. 조립 전에 `ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear)` 먼저.
★ 몸에 붙이는 부착물은 "밴드 최대폭"이 아니라 "부착 지점의 표면"을 재라 (v5): v4에서 번호판을 판 높이 구간의 최대 폭으로 붙였더니 갈비/날개 폭이 기준이 되어 호랑이·펭귄이 10~20cm 떠 있었다. 판 중앙 지점 근방만 좁게 샘플링해 표면을 잡아야 한다. 그리고 **딱 붙이면 안 된다** — 달리기 애니에서 어깨 근육이 부풀어 숫자가 묻힌다. 여유는 고정값이 아니라 **판 크기에 비례**(반높이의 25%, 최소 1.5cm)로 줘야 동물 크기에 자동으로 따라간다.
★ 1인칭에서 몸이 뚫려 보이면 근접 클리핑이 아니라 "카메라가 몸 안"을 의심하라 (v5): 닫힌 메시 내부에서는 모든 면이 뒷면이라 컬링돼 사라진다 = 몸이 투명해 보인다. 이 프로젝트는 카메라가 루트에 고정(z=0.25)인데 달리기 애니가 상체를 0.27m 숙여서 머리가 카메라를 지나쳐 나갔다. near clip을 줄이면 오히려 더 깨져 보인다(잘린 단면이 드러남). 정답은 **카메라를 머리 본에 물리는 것** — 위치만 빌리고 회전은 마우스/몸통이 결정(고갯짓이 화면을 흔들면 멀미). 대신 달릴 때는 몸이 눈보다 뒤로 가서 가슴이 잘 안 보인다(트레이드오프. 더 보이게 하려면 머리를 숨겨야 하는데 그러면 그림자가 머리 없이 나온다).
★ asmdef 범위 안에서는 "Editor 폴더" 관례가 무효다 (v5, 빌드 불가 사고): 에셋팩이 `Scripts/` 최상위에 asmdef 하나만 두면 그 아래 `Editor/`까지 전부 플레이어 빌드 대상이 되어 `UnityEditor` 참조로 컴파일이 깨진다. **에디터에서는 멀쩡하고 빌드할 때만 터지는** 유형. 처방 = Editor 폴더에 `includePlatforms: ["Editor"]` asmdef 추가(런타임 asmdef 참조 걸기). 검증은 `CompilationPipeline.GetAssemblies(Player)`로 Editor 파일 수를 세거나, `PlayerBuildInterface.CompilePlayerScripts`로 전체 빌드 없이 스크립트 컴파일만 돌리면 된다(빠르고 결정적).
★ 온라인 매치는 반드시 `NetworkGateway.RequestStartMatch()`를 타야 한다 (v5): `MatchManager.StartMatch()`를 직접 부르면 **로스터가 0명인 채로 매치가 돌아간다**(레버 경로가 접속 인원으로 로스터를 만들고 방을 잠그고 라운드 수를 읽는다). 디버그로 매치를 띄울 때도 게이트웨이 경유할 것.
★ 부위 프리팹은 메시만 쓰고 뼈대는 본체 것을 공유하라 (v5): 캐릭터 팩의 옷/모자 프리팹은 저마다 44본짜리 뼈대 복사본을 달고 있다. 통째로 자식으로 붙이면 그 뼈대는 본체 Animator가 안 움직여서 **옷만 T포즈로 굳는다**. 슬롯 렌더러에 `sharedMesh`만 갈아끼우거나, 새 렌더러를 만들 땐 `bones`/`rootBone`을 본체 것으로 복사할 것. 메시 교체 후 `localBounds` 갱신 필수(안 하면 컬링이 틀어져 옷이 사라진다).
★ 멀티플레이어 플레이 모드(MPPM) 운용 (v5) — 빌드 없이 2~4인 테스트가 되지만 함정 셋:
 ① **MCP가 가상 플레이어에 붙는다** — 가상 플레이어도 유니티 프로세스라 디스커버리 파일을 쓴다. 방금 켠 쪽이 최신이라 그리로 붙어버리고, 가상 플레이어는 씬 편집 권한이 없어서 내 명령이 조용히 무시된다. 확인법 = `Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor` / `Application.dataPath`가 `Library/VP/...`인지. 본체 창에 포커스를 주면 돌아오기도 하지만 즉시는 아니다(v5에서 몇 분 뒤 자연 복귀). 매 명령 앞에 어느 쪽인지 찍어보는 습관 권장.
 ② **PlayerPrefs를 본체와 공유한다** — 에디터 PlayerPrefs는 작업 폴더가 아니라 레지스트리(`HKCU\Software\Unity\UnityEditor\<회사>\<제품>`)에 있어서 창을 4개 띄워도 닉네임·커마가 전부 같다. 그대로 두면 "옷이 상대에게 잘 가는가"를 검증할 수 없다(다 같은 옷이라 통과처럼 보임). 처방 = MppmTestClient가 창마다 다른 신원을 덮어쓴다.
 ③ **창을 껐다 켜면 다른 사람이 된다** — Photon UserId가 새로 생겨서 원래 자리를 되찾지 못한다. 복귀 테스트를 하려면 UserId를 고정해야 한다(§10).
 ④ 가상 플레이어를 코드로 켜고 끌 수는 없다(내부 API). 체크박스는 유저가 눌러야 한다.
★ 봇 대타 판정은 BotController.BoundId로 (v5): `PlayerState.IsBot`은 생성 시점 플래그라 대타가 붙어도 false 그대로다 — 이걸로 판단하면 "대타 실패"로 오진한다.
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
이음새 고스트 충돌 법칙 (v3 세션): 바닥이 평평한 콜라이더(박스)가 지면 콜라이더 이음새(타일 경계)를 끌면 유령 모서리 충돌로 급정지한다 — 지면에 닿는 동적 콜라이더는 바닥이 둥근 캡슐/스피어 필수(썰매처럼 타넘음). 역으로 콜라이더가 지면에 안 닿으면(배높이 캡슐) 이음새 문제는 없지만 시각적 파묻힘 발생. 해법 = "바닥 정렬 캡슐". 스킨드메시 바인드 포즈가 루트 아래로 뻗은 모델(펭귄 -0.3)은 바운즈 기반 콜라이더가 그만큼 떠 보이니 루트 기준 클램프 필요.
Unity MCP 운용 (v3 세션): ① 유니티 에디터 2개 열려 있으면 최신 디스커버리 파일 쪽에 복불복 연결 — 작업 프로젝트만 열어두고, 뮤테이션 전 Application.productName 검증 습관 필수 (실제로 딴 프로젝트에 붙은 사고 있음, 읽기만 해서 무해했음). ② 도메인 리로드(코드 저장/컴파일) 중 브리지 일시 사망 — "Unity not detected"는 재시도하거나 에디터 포커스 주면 복구. ③ 플레이 모드 진입→페이즈 폴링→끼임 순간 OverlapBox/상태 덤프→종료의 자동 검증 루프가 실전 검증됨 — 레이스류 버그는 이 루프로 재현·특정 가능. ④ 씬 오브젝트 생성·UI 조립·프리팹 구조 변경(PrefabUtility)·SerializedObject 배선 전부 가능 — "에디터 지시" 대신 MCP 직접 수행이 기본이 됨 (YAML 수동 편집 금지 규칙은 여전히 유효, MCP는 에디터 API라 안전).
프리팹 일괄 수정: PrefabUtility.LoadPrefabContents→수정→SaveAsPrefabAsset 패턴 (번호판 본 부착·옆구리 2판 교체·크기 조정에 사용). 부착 본은 이름이 아니라 "대상과 최근접 본 탐색"으로 골라야 리그별 번호 차이를 흡수한다.
콜라이더 로컬 단위 법칙 (v4 세션): Collider의 radius/height/center 수치는 로컬 단위 — 유니티가 트랜스폼 스케일을 곱해서 최종 크기를 만든다. 월드 크기(렌더러 바운즈)로 계산한 값을 그대로 넣으면 스케일된 프리팹에서 겹으로 곱해짐 (치킨/고양이 1.5배 → 캡슐 1.5배 → 바닥이 발끝보다 낮아져 공중부양 실사고). 처방 = lossyScale로 나눠 넣기 (center는 InverseTransformPoint가 알아서 처리). 동물 크기를 또 만질 일 있으면 이제 자동으로 맞음.
바운즈 vs 정점 실측 (v4 세션): 부위(뿔/날개)가 튀어나온 모델은 렌더러 바운즈가 몸통보다 크게 잡힌다 (사슴 뿔 x, 펭귄 날개 x) — 몸 표면에 뭔가 붙일 땐 스킨드메시 sharedMesh.vertices를 높이/구간 밴드로 필터해 실측할 것 (옆구리 번호판에 사용, localToWorldMatrix 변환).
MCP 플레이 검증 시 SO 주의 (v4 세션): 플레이 모드 중 GameConfig 같은 SO 에셋 값 변경(예: bettingSeconds 30→4로 단축해 레이스 빨리 돌리기)은 씬 오브젝트와 달리 플레이 종료 후에도 남는다 — 검증 끝나면 반드시 원값 복원. 씬 오브젝트/컴포넌트 값 변경은 종료 시 자동 롤백이라 자유. 에디트 모드에서 renderer.material 접근 시 "Instantiating material" 에러 로그 = 무해(임시 오브젝트면 삭제로 정리됨).
전광판1 정면 캡처 팁 (v4): 임시 카메라 (7.3, 15.9, 33)에서 +z 방향, FOV 46 — 단 +x 원거리에 대형전광판이 걸려 배경에 검은 판이 찍히는 건 정상.
UI 캡처 팁 (v5): Screen Space **Overlay** 캔버스는 카메라 렌더에 안 잡혀서 Unity_Camera_Capture로 UI가 안 나온다. 플레이 중에만 `canvas.renderMode = ScreenSpaceCamera` + `worldCamera = 임시캠`으로 잠깐 바꾸면 3D+UI가 한 장에 담긴다(플레이 종료 시 자동 롤백이라 안전). 플레이어 본인 카메라는 캡처가 거부되므로 `cam.CopyFrom(Camera.main)`으로 복제해 쓸 것.
MCP RunCommand 제약 (v5): ① `result.Log`는 `{0}`만 치환하고 `{0:F3}` 같은 서식 지정자는 그대로 출력된다 → `.ToString("F3")`을 쓸 것. ② `System.Reflection.BindingFlags`가 차단돼 있어 리플렉션으로 내부 API를 부를 수 없다. ③ 주입 네임스페이스 때문에 `Mesh`/`Image`/`CompilationPipeline` 같은 짧은 타입명이 다른 네임스페이스와 충돌한다 → `UnityEngine.Mesh`처럼 정식 이름으로.
MCP 폴링으로 시간 흐름을 보는 건 비효율 (v5): 명령 왕복이 수십 초라 60초짜리 베팅 창 같은 건 계속 놓친다. 대신 **이벤트에 람다를 걸어 Debug.Log를 남기고 나중에 콘솔을 읽는 방식**이 정확하다(플레이 중에는 동적 어셈블리 델리게이트가 살아 있다). v5에서 `OnRacerFinished`(3두째 완주 시 예측 바꿔치기) + `OnRaceSettled`(정산 결과 기록)로 채점 검증에 사용.
---
12. 디버그 도구 (켜져 있음 — 출시 전 끌 것)
GameConfig.debugMotorGizmos: Scene 뷰 동물 목표선+라벨 `#id prog / lat→desired / v 현재속도/상한 / curv T 막힘` — v의 상한이 코너에서 내려가고 탈출에서 회복을 동물별 다른 기울기로 쫓아가면 코너 감속 정상 작동.
GameConfig.cornerDecelEnabled: 코너 감속 A/B 토글.
GameConfig.debugProgressLog: [투영점프]/NaN 감시.
TrackPath 빌드 검진 로그+기즈모.
---
13. v5 세션에서 생긴 에셋/파일 (경로 지도)
`Assets/Scripts/BoostDustFx.cs` · `CharacterPartLibrary.cs` · `CharacterCustomization.cs` · `CustomizationPanel.cs`
`Assets/Scripts/Network/PlayerLook.cs` · `MppmTestClient.cs`
`Assets/Art/VFX/T_BoostDustPuff.png`(256px 2×2 아틀라스 — 조각 4종, 굵은 검정 테두리) · `M_BoostDust.mat`(URP Particles Unlit)
`Assets/Art/Materials/M_TitleGround.mat`
`Assets/ScriptableObject/CharacterPartLibrary.asset`(슬롯 10종)
`Assets/ExternalAssets/ithappy/Creative_Characters_FREE/Scripts/Editor/ithappy.Creative_Characters_FREE.Editor.asmdef`(빌드 수정)
수정: GameConfig.cs(먼지 섹션) · RaceManager.cs(EnsureDustFx) · FirstPersonController.cs(머리 본 추종) · NetworkPlayerSetup.cs(외형 적용·MonoBehaviourPunCallbacks로 변경) · NetworkLauncher.cs(접속 시 외형 방송) · 동물 프리팹 7종(번호판) · NetPlayer 프리팹(13슬롯) · TitleScene
---
— 끝. 코드가 진실, 이 문서는 지도다. 첫 안건은 §7 순서대로: **원격 아바타 외형 폴백 버그 수정** → 감속 아이템 연출 확정/구현 → 멀티 잔여 테스트(복귀·호스트 이탈) → 아이템 개편 확정 → 새 맵 밸런스 검증 → V7 입력 확인 → 치킨 40% → 커마 후속 → 킥 ㄱ/아니오 → 성능 최적화.