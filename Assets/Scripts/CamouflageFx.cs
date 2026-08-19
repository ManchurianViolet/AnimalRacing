using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// [얼룩말] 위장 반투명 연출 — 스킬 지속시간 동안 몸(스킨드메시+번호판)을 희미하게.
/// 발동 감지 = 전 클라로 중계되는 스킬 사건(OnSkillEvent — Camouflage + 내 RacerId). 통신 0.
/// 지속시간은 SkillTuning.CamouflageDuration이 단일 출처 — 밸런스가 바뀌면 연출도 자동 추종.
///
/// 구현: 발동 순간 렌더러의 머티리얼을 투명 모드 클론으로 갈아끼우고 알파를 내렸다가,
/// 끝나면 **원본 sharedMaterials를 되돌린다** (영구 오염 0 — 클론은 위장 중에만 산다).
/// TMP(번호판 숫자)는 tmp.alpha로 처리. RaceManager가 Camouflage 스킬 동물에만 부착.
/// </summary>
public class CamouflageFx : MonoBehaviour
{
    private Racer racer;
    private GameConfig config;

    private float timer = -1f;   // -1 = 대기
    private Renderer[] targets;
    private Material[][] originals;   // 렌더러별 원본 sharedMaterials
    private Material[][] clones;      // 렌더러별 투명 클론 (위장 중에만 할당)
    private TMPro.TMP_Text[] texts;

    public void Init(Racer racer, GameConfig config)
    {
        this.racer = racer;
        this.config = config;
    }

    private void OnEnable() => GameEvents.OnSkillEvent += HandleSkillEvent;
    private void OnDisable()
    {
        GameEvents.OnSkillEvent -= HandleSkillEvent;
        if (timer >= 0f) Restore();   // 파괴/라운드 전환 중이면 원본 복구
    }

    private void HandleSkillEvent(SkillFeedEvent evt, int rid)
    {
        if (racer == null) return;
        if (evt != SkillFeedEvent.Camouflage || rid != racer.RacerId) return;
        if (timer >= 0f) { timer = 0f; return; }   // 무전기 재발동 — 시간만 리셋
        BeginGhost();
    }

    private void LateUpdate()
    {
        if (timer < 0f) return;

        // 완주·탈락 시 즉시 정리 (죽은 얼룩말이 투명하면 정산 그림이 이상하다)
        if (racer == null || racer.HasFinished) { Restore(); return; }

        timer += Time.deltaTime;
        float dur = SkillTuning.CamouflageDuration;
        if (timer >= dur) { Restore(); return; }

        // 알파 엔벨로프: 페이드 인 → 유지 → 페이드 아웃
        float fade = Mathf.Max(0.05f, config != null ? config.camoFadeSeconds : 0.5f);
        float ghostA = config != null ? config.camoAlpha : 0.18f;
        float t = Mathf.Min(Mathf.Clamp01(timer / fade), Mathf.Clamp01((dur - timer) / fade));
        ApplyAlpha(Mathf.Lerp(1f, ghostA, t));
    }

    private void BeginGhost()
    {
        // 렌더러 수집은 발동 순간 (라운드마다 새 스폰이라 캐시 오염 없음). 트레일/파티클은 제외
        var all = GetComponentsInChildren<Renderer>(true);
        var list = new List<Renderer>();
        foreach (var r in all)
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
            if (r.GetComponent<TMPro.TMP_Text>() != null) continue;   // TMP는 alpha로 따로
            list.Add(r);
        }
        targets = list.ToArray();
        texts = GetComponentsInChildren<TMPro.TMP_Text>(true);

        originals = new Material[targets.Length][];
        clones = new Material[targets.Length][];
        for (int i = 0; i < targets.Length; i++)
        {
            originals[i] = targets[i].sharedMaterials;
            clones[i] = new Material[originals[i].Length];
            for (int m = 0; m < originals[i].Length; m++)
            {
                if (originals[i][m] == null) continue;
                var c = new Material(originals[i][m]);
                MakeTransparent(c);
                clones[i][m] = c;
            }
            targets[i].sharedMaterials = clones[i];
        }
        timer = 0f;
    }

    private void Restore()
    {
        timer = -1f;
        if (targets != null)
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].sharedMaterials = originals[i];   // 원본 에셋 참조 복귀 — 오염 0
                for (int m = 0; m < clones[i].Length; m++)
                    if (clones[i][m] != null) Destroy(clones[i][m]);
            }
        if (texts != null)
            foreach (var t in texts) if (t != null) t.alpha = 1f;
        targets = null; originals = null; clones = null; texts = null;
    }

    private void ApplyAlpha(float a)
    {
        if (clones != null)
            foreach (var mats in clones)
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_BaseColor"))
                    { var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c); }
                    else if (m.HasProperty("_Color"))
                    { var c = m.GetColor("_Color"); c.a = a; m.SetColor("_Color", c); }
                }
        if (texts != null)
            foreach (var t in texts) if (t != null) t.alpha = a;
    }

    /// <summary>URP Lit 계열을 런타임에 투명 모드로 전환 (알파 블렌딩 + ZWrite 끔).</summary>
    private static void MakeTransparent(Material m)
    {
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }
}
