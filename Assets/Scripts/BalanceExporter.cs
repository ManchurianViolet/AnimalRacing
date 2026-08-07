#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 밸런스 데이터 내보내기 (에디터 전용 — 빌드에 포함 안 됨).
/// 맵(TrackPath 곡률·폭 프로파일) + 출전 동물 SO + GameConfig + SkillTuning 상수를
/// JSON 하나로 덤프 — 파이썬 밸런스 시뮬의 입력.
/// "맵 따라 동물따라 알아서": 수동 전달·상수 드리프트 원천 차단. 새 맵이든 스탯
/// 수정이든, 메뉴 한 번 → JSON 업로드가 전부.
/// 사용: 게임 씬(트랙 있는 씬)을 연 상태에서 메뉴 Tools > 짜고치는레이스 > 밸런스 데이터 내보내기.
/// </summary>
public static class BalanceExporter
{
    // ---- JSON 구조 (JsonUtility 직렬화용) ----

    [System.Serializable]
    public class AnimalDump
    {
        public string name;
        public float minSpeed, maxSpeed;      // 100단위
        public int acceleration;              // 100단위
        public float rerollInterval;          // 초
        public string skill;
    }

    [System.Serializable]
    public class ConfigDump
    {
        public int racerCount;
        public int pointsFirst, pointsSecond, pointsThird;
        public float maxAssistAccel;
        public float curvatureSaturation;
        public bool cornerDecelEnabled;
        public float cornerDecelRate, cornerSenseAhead, cornerBrakeGain;
        // 변환 상수 (AnimalDefinition — 시뮬이 게임과 같은 자로 재도록)
        public float speedUnitToMs, accelBaseGain, accelUnitGain;
    }

    [System.Serializable]
    public class SkillDump
    {
        public float activeMinRatio, activeMaxRatio;
        public float finalSprintZone, finalSprintMult;
        public float rudolphLeadSeconds, rudolphFlightSeconds;
        public float roarDuration, roarMult;
        public float catWalkDuration;
        public float loyaltyMult;
        public float dashDuration, dashMult;
    }

    [System.Serializable]
    public class BalanceDump
    {
        public string map;
        public string exportedAt;
        public float trackLength;
        public ConfigDump config;
        public SkillDump skills;
        public List<AnimalDump> animals = new();
        // 1m 구간별 프로파일: yawPerMeter[m] = (m, m+1] 구간의 방향 변화량 합 (도/m, +=우회전)
        public float[] yawPerMeter;
        public float[] halfWidthPerMeter;
    }

    [MenuItem("Tools/짜고치는레이스/밸런스 데이터 내보내기")]
    public static void Export()
    {
        // ---- 1) 트랙: 씬에서 찾고 에디트 모드에서 직접 빌드 ----
        var track = Object.FindFirstObjectByType<TrackPath>();
        if (track == null)
        {
            Debug.LogError("[BalanceExporter] 씬에 TrackPath가 없습니다 — 게임 씬을 열고 실행하세요.");
            return;
        }
        track.Build();
        if (track.TotalLength < 2f)
        {
            Debug.LogError("[BalanceExporter] TrackPath 빌드 실패 — 위 콘솔의 TrackPath 에러를 먼저 해결하세요.");
            return;
        }

        // ---- 2) GameConfig: 씬 GameManager가 참조하는 에셋 우선, 없으면 프로젝트 검색 ----
        GameConfig config = null;
        string configSource;
        var gm = Object.FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            var prop = new SerializedObject(gm).FindProperty("config");
            config = prop != null ? prop.objectReferenceValue as GameConfig : null;
        }
        if (config != null) configSource = "씬 GameManager 참조";
        else
        {
            var guids = AssetDatabase.FindAssets("t:GameConfig");
            if (guids.Length == 0)
            {
                Debug.LogError("[BalanceExporter] GameConfig 에셋을 찾지 못했습니다.");
                return;
            }
            config = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            configSource = $"프로젝트 검색 ({guids.Length}개 중 첫 번째)";
        }

        // ---- 3) 동물: 씬 RaceManager의 animalPool 우선 (출전 풀 = 진실), 없으면 전체 검색 ----
        var animals = new List<AnimalDefinition>();
        string animalSource;
        var rm = Object.FindFirstObjectByType<RaceManager>();
        if (rm != null)
        {
            var pool = new SerializedObject(rm).FindProperty("animalPool");
            if (pool != null && pool.isArray)
                for (int i = 0; i < pool.arraySize; i++)
                    if (pool.GetArrayElementAtIndex(i).objectReferenceValue is AnimalDefinition a)
                        animals.Add(a);
        }
        if (animals.Count > 0) animalSource = "씬 RaceManager.animalPool";
        else
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimalDefinition"))
                if (AssetDatabase.LoadAssetAtPath<AnimalDefinition>(AssetDatabase.GUIDToAssetPath(guid)) is AnimalDefinition a)
                    animals.Add(a);
            animalSource = "프로젝트 전체 검색";
        }
        if (animals.Count == 0)
        {
            Debug.LogError("[BalanceExporter] AnimalDefinition을 하나도 찾지 못했습니다.");
            return;
        }

        // ---- 4) 덤프 조립 ----
        var dump = new BalanceDump
        {
            map = SceneManager.GetActiveScene().name,
            exportedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            trackLength = track.TotalLength,
            config = new ConfigDump
            {
                racerCount = config.racerCount,
                pointsFirst = config.pointsFirst,
                pointsSecond = config.pointsSecond,
                pointsThird = config.pointsThird,
                maxAssistAccel = config.maxAssistAccel,
                curvatureSaturation = config.curvatureSaturation,
                cornerDecelEnabled = config.cornerDecelEnabled,
                cornerDecelRate = config.cornerDecelRate,
                cornerSenseAhead = config.cornerSenseAhead,
                cornerBrakeGain = config.cornerBrakeGain,
                speedUnitToMs = AnimalDefinition.SpeedUnitToMs,
                accelBaseGain = AnimalDefinition.AccelBaseGain,
                accelUnitGain = AnimalDefinition.AccelUnitGain,
            },
            skills = new SkillDump
            {
                activeMinRatio = SkillTuning.ActiveMinRatio,
                activeMaxRatio = SkillTuning.ActiveMaxRatio,
                finalSprintZone = SkillTuning.FinalSprintZone,
                finalSprintMult = SkillTuning.FinalSprintMult,
                rudolphLeadSeconds = SkillTuning.RudolphLeadSeconds,
                rudolphFlightSeconds = SkillTuning.RudolphFlightSeconds,
                roarDuration = SkillTuning.RoarDuration,
                roarMult = SkillTuning.RoarMult,
                catWalkDuration = SkillTuning.CatWalkDuration,
                loyaltyMult = SkillTuning.LoyaltyMult,
                dashDuration = SkillTuning.DashDuration,
                dashMult = SkillTuning.DashMult,
            },
        };

        foreach (var a in animals)
            dump.animals.Add(new AnimalDump
            {
                name = a.displayName,
                minSpeed = a.minSpeed,
                maxSpeed = a.maxSpeed,
                acceleration = a.acceleration,
                rerollInterval = a.speedRerollInterval,
                skill = a.skill.ToString(),
            });

        // ---- 5) 1m 프로파일 샘플링 (공개 API만 사용) ----
        int n = Mathf.CeilToInt(track.TotalLength);
        dump.yawPerMeter = new float[n];
        dump.halfWidthPerMeter = new float[n];
        for (int m = 0; m < n; m++)
        {
            dump.yawPerMeter[m] = track.GetSignedCurvatureAhead(m, 1f);
            dump.halfWidthPerMeter[m] = track.GetHalfWidth(m + 0.5f);
        }

        // ---- 6) 저장: 프로젝트 루트 (Assets 밖 — 임포트 안 됨) ----
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BalanceExport.json"));
        File.WriteAllText(path, JsonUtility.ToJson(dump, true));

        Debug.Log($"[BalanceExporter] 완료 → {path}\n" +
                  $"맵 {dump.map} / 길이 {dump.trackLength:F1}m / 동물 {animals.Count}종({animalSource}) / " +
                  $"GameConfig: {configSource}");
        EditorUtility.RevealInFinder(path);
    }
}
#endif
