#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// [에디터 전용] ithappy 정식판 동물 팩(100마리) → 레이서용 애니메이터 컨트롤러 일괄 굽기.
///
/// 왜 필요한가: 정식판 프리팹은 뼈대와 클립(Idle/Walk/Run)은 다 갖고 있는데
/// ① Animator 컴포넌트가 아예 없고 ② 딸려온 컨트롤러 67개는 전시용이라 Idle/Gesture만 물려 있다.
/// 우리 RacerMotor/Racer는 무료판 컨트롤러 모양(`Vert`/`State` 2단 블렌드트리)에 맞춰 값을 먹이므로
/// (Racer.DriveAnimator — vertID/stateID), 같은 모양을 동물 수만큼 손으로 만드는 대신 여기서 굽는다.
///
/// 굽는 모양 (무료판 Horse.controller와 동일):
///   파라미터: State(Float), Vert(Float)
///   Base Layer / 기본 상태 "Blend Tree"
///     └ 1D 트리 (Vert)  0 = Idle   1 = ┐
///                                     └ 1D 트리 (State)  0 = Walk   1 = Run
///
/// 산출물: Assets/Art/Animations/Animals/Animal_&lt;리그&gt;.controller
/// 이미 있으면 **같은 에셋을 비우고 다시 채운다** — GUID가 유지되므로 이미 배선한 프리팹이 안 깨진다.
///
/// ⚠ 벤더 에셋 중 클립의 루프 플래그가 꺼진 것(닭 3종)은 켜준다 — 안 켜면 한 번 재생하고 얼어붙는다(§11).
/// </summary>
public static class AnimalControllerBaker
{
    private const string PrefabRoot = "Assets/ithappy/Animals/Prefabs/Animals";
    private const string AnimRoot = "Assets/ithappy/Animals/Animations";
    private const string OutRoot = "Assets/Art/Animations/Animals";

    private const string ParamVert = "Vert";     // 0 = 정지, 1 = 이동  (Racer가 먹임)
    private const string ParamState = "State";   // 0 = 걷기, 1 = 달리기

    /// <summary>동물 한 종의 클립 묶음 (애니메이션 폴더 하나 = 리그 하나).</summary>
    private class ClipSet
    {
        public string folderName;
        public string prefix;                              // 클립 이름 앞머리 (폴더명과 다를 수 있다)
        public AnimationClip idle, walk, run, jump;
        public bool claimed;
    }

    [MenuItem("Tools/짜고치는레이스/정식판 동물 컨트롤러 굽기")]
    public static void Bake()
    {
        if (Application.productName != "AnimalRacing")
        {
            Debug.LogError("[동물컨트롤러] 다른 프로젝트에서 실행됨 — 중단");
            return;
        }
        if (!AssetDatabase.IsValidFolder(PrefabRoot))
        {
            Debug.LogError($"[동물컨트롤러] 정식판 폴더가 없다: {PrefabRoot}");
            return;
        }

        EnsureFolder(OutRoot);

        var rigs = FindRigs();                 // 리그 이름 → 그 뼈대를 쓰는 프리팹들
        var clipSets = IndexClipFolders();     // 폴더명 → 클립 묶음

        var log = new StringBuilder();
        int baked = 0, missed = 0, loopFixed = 0, fallbacks = 0;

        foreach (var rig in SortedKeys(rigs))
        {
            var set = MatchClips(rig, clipSets);
            if (set == null)
            {
                log.Append($"[건너뜀] {rig} — 애니메이션 폴더를 못 찾음\n");
                missed++;
                continue;
            }

            // 없는 동작은 있는 것으로 대체한다 (캥거루처럼 Walk/Run이 통째로 없는 종이 있다)
            var idle = First(set.idle, set.walk, set.run, set.jump);
            var walk = First(set.walk, set.jump, set.run, set.idle);
            var run = First(set.run, set.jump, set.walk, set.idle);
            if (idle == null || walk == null || run == null)
            {
                log.Append($"[건너뜀] {rig} — 쓸 수 있는 클립이 하나도 없음\n");
                missed++;
                continue;
            }

            bool substituted = set.walk == null || set.run == null || set.idle == null;
            if (substituted) fallbacks++;

            loopFixed += EnsureLooping(idle) + EnsureLooping(walk) + EnsureLooping(run);

            // ⚠ 벤더 클립은 같은 회전을 q/-q 반대 부호로 저장한 본이 섞여 있다 (낙타 Run의 Root 등).
            // 블렌드트리는 쿼터니언을 성분별로 보간하므로 반대 부호끼리 섞이면 몸이 한 프레임 뒤집힌다
            // (실사고 — 낙타가 걷다 순간 147° 틀어졌다 돌아옴). Idle을 기준으로 부호를 정렬한다.
            int fixedW = AlignQuaternionSigns(idle, walk);
            int fixedR = AlignQuaternionSigns(idle, run);
            if (fixedW + fixedR > 0)
                log.Append($"  [부호정렬] {rig}: Walk {fixedW}본 / Run {fixedR}본 교정\n");

            string path = $"{OutRoot}/Animal_{rig}.controller";
            BuildController(path, idle, walk, run);
            baked++;

            log.Append($"{rig,-18} ← Idle:{idle.name} / Walk:{walk.name} / Run:{run.name}")
               .Append(substituted ? "  ⚠대체사용" : "")
               .Append($"   (프리팹 {rigs[rig].Count}종: {string.Join(", ", rigs[rig])})\n");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[동물컨트롤러] 굽기 완료 — {baked}개 생성 / 실패 {missed}개 / 클립 대체 {fallbacks}종 / 루프 플래그 교정 {loopFixed}개\n"
                  + $"산출 위치: {OutRoot}\n\n{log}");
    }

    // ---------------- 리그 수집 ----------------

    /// <summary>프리팹의 Skeleton_* 자식 이름으로 리그를 판별한다 — 같은 리그면 애니를 공유한다.</summary>
    private static Dictionary<string, List<string>> FindRigs()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go == null) continue;

            string rig = null;
            foreach (Transform child in go.transform)
                if (child.name.StartsWith("Skeleton"))
                {
                    rig = child.name.StartsWith("Skeleton_") ? child.name.Substring("Skeleton_".Length) : child.name;
                    break;
                }
            if (string.IsNullOrEmpty(rig)) continue;

            if (!result.TryGetValue(rig, out var list)) result[rig] = list = new List<string>();
            list.Add(go.name);
        }
        return result;
    }

    // ---------------- 클립 수집 ----------------

    /// <summary>애니메이션 폴더를 훑어 Idle/Walk/Run/Jump를 뽑아둔다.</summary>
    private static List<ClipSet> IndexClipFolders()
    {
        var sets = new List<ClipSet>();
        foreach (var category in Directory.GetDirectories(AnimRoot))
            foreach (var dir in Directory.GetDirectories(category))
            {
                string folder = Path.GetFileName(dir);
                if (folder == "Controllers") continue;

                string unityPath = dir.Replace('\\', '/');
                var set = new ClipSet { folderName = folder };

                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { unityPath }))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (clip == null) continue;

                    string n = clip.name;
                    // 수중 동작은 육상 이동과 섞이면 안 된다 (Duck_Swim_Idle 같은 이름이 Idle로 잡히는 것 방지)
                    if (n.Contains("Swim") || n.Contains("Fly")) continue;

                    if (n.EndsWith("_Idle") && set.idle == null) { set.idle = clip; set.prefix = Strip(n, "_Idle"); }
                    else if (n.EndsWith("_Walk") && set.walk == null) { set.walk = clip; set.prefix ??= Strip(n, "_Walk"); }
                    else if (n.EndsWith("_Run") && set.run == null) { set.run = clip; set.prefix ??= Strip(n, "_Run"); }
                    else if (n.EndsWith("_Jump") && set.jump == null) { set.jump = clip; set.prefix ??= Strip(n, "_Jump"); }
                }

                if (set.idle != null || set.walk != null || set.run != null || set.jump != null) sets.Add(set);
            }
        return sets;
    }

    /// <summary>
    /// 리그 ↔ 클립 폴더 짝짓기. 폴더 이름이 1순위, 안 맞으면 클립 이름 앞머리로 2순위.
    /// (폴더명이 키릴 문자인 악어, 클립이 부모 종 이름을 쓰는 암사슴·암무스 같은 예외가 있다)
    /// </summary>
    private static ClipSet MatchClips(string rig, List<ClipSet> sets)
    {
        string key = Normalize(rig);

        foreach (var s in sets)
            if (!s.claimed && Normalize(s.folderName) == key) { s.claimed = true; return s; }

        foreach (var s in sets)
            if (!s.claimed && s.prefix != null && Normalize(s.prefix) == key) { s.claimed = true; return s; }

        return null;
    }

    // ---------------- 컨트롤러 조립 ----------------

    private static void BuildController(string path, AnimationClip idle, AnimationClip walk, AnimationClip run)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        else ResetController(ctrl);

        ctrl.AddParameter(ParamState, AnimatorControllerParameterType.Float);
        ctrl.AddParameter(ParamVert, AnimatorControllerParameterType.Float);

        ctrl.CreateBlendTreeInController("Blend Tree", out BlendTree idleMove, 0);
        idleMove.blendType = BlendTreeType.Simple1D;
        idleMove.blendParameter = ParamVert;
        idleMove.useAutomaticThresholds = false;
        idleMove.AddChild(idle, 0f);

        var moveTree = idleMove.CreateBlendTreeChild(1f);
        moveTree.name = "BlendTree";
        moveTree.blendType = BlendTreeType.Simple1D;
        moveTree.blendParameter = ParamState;
        moveTree.useAutomaticThresholds = false;
        moveTree.AddChild(walk, 0f);
        moveTree.AddChild(run, 1f);

        // AddChild가 자동 문턱값을 다시 계산해버리는 경우가 있어 마지막에 못 박는다
        SetThresholds(idleMove, 0f, 1f);
        SetThresholds(moveTree, 0f, 1f);

        EditorUtility.SetDirty(ctrl);
    }

    /// <summary>기존 에셋을 비운다 — 파일을 지웠다 만들면 GUID가 바뀌어 이미 배선한 프리팹이 끊긴다.</summary>
    private static void ResetController(AnimatorController ctrl)
    {
        while (ctrl.parameters.Length > 0) ctrl.RemoveParameter(0);
        ctrl.layers = new AnimatorControllerLayer[0];

        // 레이어를 떼면 옛 상태·블렌드트리가 하위 에셋으로 떠돈다 — 같이 치운다
        string path = AssetDatabase.GetAssetPath(ctrl);
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (obj == ctrl || obj == null) continue;
            if (obj is BlendTree || obj is AnimatorState || obj is AnimatorStateMachine || obj is AnimatorStateTransition)
                Object.DestroyImmediate(obj, true);
        }

        ctrl.AddLayer("Base Layer");
    }

    private static void SetThresholds(BlendTree tree, params float[] values)
    {
        var children = tree.children;
        for (int i = 0; i < children.Length && i < values.Length; i++) children[i].threshold = values[i];
        tree.children = children;
    }

    // ---------------- 잡일 ----------------

    /// <summary>
    /// 기준 클립(Idle)과 첫 키 쿼터니언 내적이 음수인 본의 회전 커브 4개(x/y/z/w)를 통째로 부호 반전.
    /// q와 -q는 같은 회전이라 단독 재생은 멀쩡하고, 블렌드에서만 뒤집혀서 눈치채기 어렵다.
    /// 반환 = 교정한 본 수. 같은 클립에 재실행해도 이미 정렬돼 있어 0 (멱등).
    /// </summary>
    private static int AlignQuaternionSigns(AnimationClip reference, AnimationClip clip)
    {
        if (reference == null || clip == null || reference == clip) return 0;

        // 본 경로 → 첫 키 쿼터니언 수집
        Dictionary<string, float[]> FirstKeys(AnimationClip c)
        {
            var m = new Dictionary<string, float[]>();
            foreach (var b in AnimationUtility.GetCurveBindings(c))
            {
                if (!b.propertyName.StartsWith("m_LocalRotation.")) continue;
                var curve = AnimationUtility.GetEditorCurve(c, b);
                if (curve.keys.Length == 0) continue;
                if (!m.TryGetValue(b.path, out var arr)) m[b.path] = arr = new float[4];
                int i = b.propertyName.EndsWith("x") ? 0 : b.propertyName.EndsWith("y") ? 1
                      : b.propertyName.EndsWith("z") ? 2 : 3;
                arr[i] = curve.keys[0].value;
            }
            return m;
        }

        var refQ = FirstKeys(reference);
        var clipQ = FirstKeys(clip);
        int fixedBones = 0;

        foreach (var kv in clipQ)
        {
            if (!refQ.TryGetValue(kv.Key, out var r)) continue;
            float dot = r[0] * kv.Value[0] + r[1] * kv.Value[1] + r[2] * kv.Value[2] + r[3] * kv.Value[3];
            if (dot >= 0f) continue;

            // 이 본의 회전 커브 4개 전 키 부호 반전 (탄젠트도 함께 — 곡선 모양 보존)
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.path != kv.Key || !b.propertyName.StartsWith("m_LocalRotation.")) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = -keys[i].value;
                    keys[i].inTangent = -keys[i].inTangent;
                    keys[i].outTangent = -keys[i].outTangent;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, b, curve);
            }
            fixedBones++;
        }

        if (fixedBones > 0) EditorUtility.SetDirty(clip);
        return fixedBones;
    }

    /// <summary>루프가 꺼진 이동 클립은 한 번 재생하고 얼어붙는다 (§11) — 켜준다.</summary>
    private static int EnsureLooping(AnimationClip clip)
    {
        if (clip == null || clip.isLooping) return 0;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return 1;
    }

    private static AnimationClip First(params AnimationClip[] candidates)
    {
        foreach (var c in candidates) if (c != null) return c;
        return null;
    }

    private static string Strip(string name, string suffix) => name.Substring(0, name.Length - suffix.Length);

    private static string Normalize(string s) => s.Replace("_", "").Replace(" ", "").ToLowerInvariant();

    private static List<string> SortedKeys(Dictionary<string, List<string>> dict)
    {
        var keys = new List<string>(dict.Keys);
        keys.Sort();
        return keys;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
