#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// [에디터 전용] ithappy 정식판 동물 → 레이서 프리팹 + AnimalDefinition SO 일괄 조립.
///
/// 조립 내용 (말프리팹을 기준 삼아 동일 구성):
///   루트 = Animator(Animal_X 컨트롤러, 루트모션 끔, AlwaysAnimate)
///        + PhotonView(Observed = TransformView·AnimatorView, UnreliableOnChange, Fixed)
///        + PhotonTransformView / PhotonAnimatorView(동기 설정은 말프리팹 것을 직렬화 복사)
///        + NetworkRacerSetup + RacerNumberPlate
///   번호판 = 양 옆구리 2세트(PlateCubeL/R + PlateNumL/R) — 등뼈 본의 자식 (달릴 때 출렁임).
///           옆구리 x는 정점 실측 + 판 반높이 25% 여유 (v4/v5 규칙), 판 크기는 몸 높이 비례.
///   Racer/RacerMotor/Rigidbody/캡슐은 프리팹에 안 넣는다 — RaceManager가 스폰 때 붙인다 (말프리팹과 동일).
///
/// 결과물: Assets/Resources/&lt;한글이름&gt;프리팹.prefab + Assets/ScriptableObject/Animal_&lt;한글이름&gt;.asset
/// 재실행하면 기존 프리팹/SO를 같은 경로에 덮어써 GUID가 유지된다.
/// ⚠ 씬 배선(RaceManager.animalPool)과 strings.csv 키는 이 툴 밖에서 별도 처리.
/// </summary>
public static class RacerPrefabBuilder
{
    private class Spec
    {
        public string korean;        // 프리팹/SO 이름에 쓰는 한글
        public string sourcePath;    // ithappy 원본 프리팹
        public string controller;    // Animal_X.controller 이름
        public string nameKey;       // 로컬라이제이션 키
        public float scale;          // 루트 스케일 (비둘기처럼 작은 종 보정)
        public float min, max;       // 속도 스탯 (0~100)
        public int accel;
    }

    // 스탯은 기존 8종 밴드(46~93) 안의 임시값 — 스킬 없는 깡통이라 중앙값 위주, 밸런스 세션에서 재조정.
    private static readonly Spec[] Specs =
    {
        new Spec { korean = "소",     sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Domectic_Animals/Cow_01.prefab",      controller = "Animal_Cow",        nameKey = "animal.cow",       scale = 1.0f,  min = 64, max = 78, accel = 30 },
        new Spec { korean = "비둘기", sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Domectic_Animals/Pigeon_01.prefab",   controller = "Animal_Pigeon",     nameKey = "animal.pigeon",    scale = 2.2f,  min = 50, max = 88, accel = 65 },
        new Spec { korean = "낙타",   sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Exotic_Animals/Camel_01.prefab",      controller = "Animal_Camel",      nameKey = "animal.camel",     scale = 1.0f,  min = 66, max = 79, accel = 25 },
        new Spec { korean = "원숭이", sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Exotic_Animals/Monkey_01.prefab",     controller = "Animal_Monkey",     nameKey = "animal.monkey",    scale = 1.5f,  min = 56, max = 85, accel = 80 },
        new Spec { korean = "북극곰", sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Polar_Animals/Polar_Bear_01.prefab",  controller = "Animal_Polar_Bear", nameKey = "animal.polarbear", scale = 0.85f, min = 60, max = 84, accel = 40 },   // v22: 유저 요청 "약간만 줄이자" 1.0→0.85
        new Spec { korean = "얼룩말", sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Savanna_Animals/Zebra_01.prefab",     controller = "Animal_Zebra",      nameKey = "animal.zebra",     scale = 1.0f,  min = 62, max = 84, accel = 50 },
        new Spec { korean = "기린",   sourcePath = "Assets/ithappy/Animals/Prefabs/Animals/Savanna_Animals/Giraffe_01.prefab",   controller = "Animal_Giraffe",    nameKey = "animal.giraffe",   scale = 0.42f, min = 63, max = 80, accel = 35 },   // 원본 5.91m → 2.5m (캡슐이 도로 반폭을 넘어 출발선에 끼던 실사고)
    };

    private const string TemplatePath = "Assets/Resources/말프리팹.prefab";   // 동기 설정·판 재질의 기준
    private const float HorsePlateSize = 0.295f;                              // 말(몸높이 1.86)의 판 크기 — 비례 기준
    private const float HorseBodyHeight = 1.86f;

    [MenuItem("Tools/짜고치는레이스/신규 레이서 프리팹 조립 (6종)")]
    public static void Build()
    {
        if (Application.productName != "AnimalRacing") { Debug.LogError("[레이서조립] 다른 프로젝트 — 중단"); return; }

        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/BMJUA SDF.asset");
        if (template == null || font == null) { Debug.LogError("[레이서조립] 말프리팹 또는 BMJUA SDF를 못 찾음"); return; }

        // 판 큐브의 메시/머티리얼은 말프리팹 것을 그대로 공유 (런타임 Apply가 color만 인스턴스화)
        Mesh cubeMesh = null; Material cubeMat = null;
        foreach (var t in template.GetComponentsInChildren<Transform>(true))
            if (t.name == "PlateCubeL")
            { cubeMesh = t.GetComponent<MeshFilter>().sharedMesh; cubeMat = t.GetComponent<MeshRenderer>().sharedMaterial; break; }

        var log = new StringBuilder();
        foreach (var spec in Specs)
        {
            string err = BuildOne(spec, template, font, cubeMesh, cubeMat, log);
            if (err != null) log.Append("  ✗ ").Append(spec.korean).Append(": ").Append(err).Append("\n");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[레이서조립] 완료\n" + log);
    }

    private static string BuildOne(Spec spec, GameObject template, TMP_FontAsset font, Mesh cubeMesh, Material cubeMat, StringBuilder log)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.sourcePath);
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>($"Assets/Art/Animations/Animals/{spec.controller}.controller");
        if (source == null) return "원본 프리팹 없음: " + spec.sourcePath;
        if (ctrl == null) return "컨트롤러 없음: " + spec.controller;

        // ---- 루트 구성: ithappy 인스턴스를 언팩해 순수 계층으로 ----
        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = spec.korean + "프리팹";
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * spec.scale;

        var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null) { Object.DestroyImmediate(root); return "스킨드메시 없음"; }
        smr.updateWhenOffscreen = true;   // 쓰러짐 등 큰 자세 변화에서 부위 실종 방지 (§3-8 규칙)

        // ---- 애니메이터 ----
        var anim = root.GetComponent<Animator>();
        if (anim == null) anim = root.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // ---- Photon 3종 (동기 설정은 말프리팹 것을 직렬화 복사 — 수동 재현으로 어긋나는 것 방지) ----
        var tv = root.AddComponent<PhotonTransformView>();
        EditorUtility.CopySerialized(template.GetComponent<PhotonTransformView>(), tv);
        var av = root.AddComponent<PhotonAnimatorView>();
        EditorUtility.CopySerialized(template.GetComponent<PhotonAnimatorView>(), av);

        var pv = root.AddComponent<PhotonView>();
        var pvSo = new SerializedObject(pv);
        var tplPvSo = new SerializedObject(template.GetComponent<PhotonView>());
        pvSo.FindProperty("Synchronization").enumValueIndex = tplPvSo.FindProperty("Synchronization").enumValueIndex;
        pvSo.FindProperty("OwnershipTransfer").enumValueIndex = tplPvSo.FindProperty("OwnershipTransfer").enumValueIndex;
        var obs = pvSo.FindProperty("ObservedComponents");
        obs.arraySize = 2;
        obs.GetArrayElementAtIndex(0).objectReferenceValue = tv;
        obs.GetArrayElementAtIndex(1).objectReferenceValue = av;
        pvSo.ApplyModifiedPropertiesWithoutUndo();

        root.AddComponent<NetworkRacerSetup>();
        root.AddComponent<RacerNumberPlate>();

        // ---- 번호판 (양 옆구리) ----
        string plateInfo = BuildPlates(root, smr, font, cubeMesh, cubeMat);

        // ---- 저장 ----
        string prefabPath = $"Assets/Resources/{root.name}.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        // ---- SO ----
        string soPath = $"Assets/ScriptableObject/Animal_{spec.korean}.asset";
        var def = AssetDatabase.LoadAssetAtPath<AnimalDefinition>(soPath);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<AnimalDefinition>();
            AssetDatabase.CreateAsset(def, soPath);
            def.skill = AnimalSkill.None;              // 신규만 깡통 — 발동 무전기는 IsActive(None)=false로 자동 거부
        }
        // ⚠ 기존 SO의 skill/hoverFlight는 보존 — 재실행이 뒤에 붙인 스킬(얼룩말 위장 등)을 지우면 안 된다
        def.displayName = spec.korean;
        def.nameKey = spec.nameKey;
        def.prefab = saved;
        def.minSpeed = spec.min;
        def.maxSpeed = spec.max;
        def.acceleration = spec.accel;
        def.speedRerollInterval = 15f;                 // 기존 8종과 동일 (밸런스 대전제 §3-3-b)
        EditorUtility.SetDirty(def);

        log.Append($"  ✓ {spec.korean} — {saved.name}.prefab (스케일 {spec.scale}) / {plateInfo}\n");
        return null;
    }

    /// <summary>등뼈 본을 골라 양 옆구리에 판+숫자를 앉힌다 (v4 방식 — 바인드 포즈 정점 실측).</summary>
    private static string BuildPlates(GameObject root, SkinnedMeshRenderer smr, TMP_FontAsset font, Mesh cubeMesh, Material cubeMat)
    {
        // 몸통 바운즈 (루트 기준, 스케일 반영된 월드 — 루트가 원점·무회전이라 월드=루트)
        var b = smr.bounds;
        float bodyHeight = b.size.y;
        float plateSize = Mathf.Clamp(HorsePlateSize * bodyHeight / HorseBodyHeight, 0.16f, 0.33f);
        float plateThickness = 0.018f;

        // 판 중앙 = 몸통 중심 (높이는 중심보다 살짝 위 — 다리 사이가 아니라 옆구리에 붙게)
        Vector3 center = b.center;
        center.y = b.center.y + bodyHeight * 0.05f;

        // 등뼈 본: 이름에 spine 포함 중 몸 중심 최근접 → 없으면 전체 본 중 최근접
        Transform bone = null; float best = float.MaxValue;
        var bones = smr.bones;
        for (int pass = 0; pass < 2 && bone == null; pass++)
        {
            foreach (var t in bones)
            {
                if (t == null) continue;
                if (pass == 0 && !t.name.ToLowerInvariant().Contains("spine")) continue;
                float d = (t.position - center).sqrMagnitude;
                if (d < best) { best = d; bone = t; }
            }
            if (bone != null) break;
            best = float.MaxValue;
        }
        if (bone == null) bone = smr.rootBone != null ? smr.rootBone : root.transform;

        // 옆구리 실측: 판이 앉을 자리(판 크기 밴드) 근방 정점만 좁게 샘플링 (v5 — 밴드 최대폭 금지)
        float halfBand = plateSize * 0.5f;
        var verts = smr.sharedMesh.vertices;
        var toWorld = smr.transform.localToWorldMatrix;
        float maxX = 0.02f, minX = -0.02f;
        foreach (var v in verts)
        {
            Vector3 w = toWorld.MultiplyPoint3x4(v);
            if (Mathf.Abs(w.y - center.y) > halfBand || Mathf.Abs(w.z - center.z) > halfBand) continue;
            if (w.x > maxX) maxX = w.x;
            if (w.x < minX) minX = w.x;
        }
        float gap = Mathf.Max(plateSize * 0.5f * 0.25f, 0.015f);   // 근육 부풀림 여유 (v5 규칙)

        MakeSidePlate(root, bone, font, cubeMesh, cubeMat, "L", new Vector3(maxX + gap, center.y, center.z), Vector3.right, plateSize, plateThickness);
        MakeSidePlate(root, bone, font, cubeMesh, cubeMat, "R", new Vector3(minX - gap, center.y, center.z), Vector3.left, plateSize, plateThickness);

        return $"판 {plateSize:F2}m @ {bone.name}, 옆구리 x {minX - gap:F2}~{maxX + gap:F2}";
    }

    private static void MakeSidePlate(GameObject root, Transform bone, TMP_FontAsset font, Mesh cubeMesh, Material cubeMat,
                                      string side, Vector3 worldPos, Vector3 outward, float size, float thickness)
    {
        // 큐브 (루트 스케일이 1이 아닐 수 있어 — 월드 크기를 목표로 로컬 스케일 역산)
        var cube = new GameObject("PlateCube" + side);
        cube.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
        cube.AddComponent<MeshRenderer>().sharedMaterial = cubeMat;
        cube.transform.position = worldPos;
        cube.transform.rotation = Quaternion.identity;   // 몸축=z, 옆구리 법선=x — 얇은 축이 x인 큐브
        cube.transform.SetParent(bone, true);
        FixWorldScale(cube.transform, new Vector3(thickness, size, size));

        // 숫자 — TMP의 로컬 +Z는 글자 뒤통수라 forward를 면 안쪽(-outward)으로 (§11)
        var num = new GameObject("PlateNum" + side, typeof(RectTransform));
        var rt = (RectTransform)num.transform;
        rt.sizeDelta = new Vector2(0.5f, 0.5f);
        var tmp = num.AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSize = 1.4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = "0";
        num.transform.position = worldPos + outward * (thickness * 0.5f + 0.004f);
        num.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
        num.transform.SetParent(bone, true);
        FixWorldScale(num.transform, Vector3.one * (size / HorsePlateSize * 1.778f));   // 말 판 글자 스케일에 비례
    }

    /// <summary>본 체인에 낀 스케일과 무관하게 월드 크기가 목표대로 나오게 로컬 스케일을 역산.</summary>
    private static void FixWorldScale(Transform t, Vector3 worldSize)
    {
        var parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(
            worldSize.x / Mathf.Max(1e-4f, parentScale.x),
            worldSize.y / Mathf.Max(1e-4f, parentScale.y),
            worldSize.z / Mathf.Max(1e-4f, parentScale.z));
    }
}
#endif
