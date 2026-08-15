using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 피규어 HUD 아이콘용 런타임 썸네일 — 동물 프리팹을 씬 밖 지하(y -500)에서 투명 배경으로
/// 한 번 렌더해 스프라이트로 만들고 캐시한다 (동물×번호당 1회, 256px).
/// AnimalDefinition.icon(수제 초상화 — 백로그)이 비어 있는 동안의 자동 대체품이며,
/// icon이 채워지면 PlayerHUD가 그쪽을 우선한다.
/// [멀티] 순수 로컬 연출 — 네트워크 통신 0.
/// </summary>
public static class FigurineThumbs
{
    private const int Size = 256;

    // 실패(null)도 캐시한다 — 매 프레임 재렌더 시도를 막는다
    private static readonly Dictionary<long, Sprite> cache = new Dictionary<long, Sprite>();

    public static Sprite Get(AnimalDefinition def, int postNumber)
    {
        if (def == null || def.prefab == null) return null;
        long key = (long)def.GetInstanceID() * 100 + postNumber;
        if (cache.TryGetValue(key, out var s)) return s;
        s = Render(def, postNumber);
        cache[key] = s;
        return s;
    }

    private static Sprite Render(AnimalDefinition def, int postNumber)
    {
        // 씬에서 멀리 떨어진 지하에 임시 조립 — 레이어 분리 없이도 배경에 아무것도 안 찍힌다.
        // (Directional Light는 무한 광원이라 지하에서도 지상과 같은 조명을 받는다)
        var holder = new GameObject("__figThumb");
        holder.transform.position = new Vector3(0f, -500f, 0f);
        holder.SetActive(false);                       // Awake 전에 게임플레이 컴포넌트 제거 (피규어와 동일 패턴)

        GameObject camGo = null;
        try
        {
            var body = Object.Instantiate(def.prefab, holder.transform);
            // ⚠ Instantiate(prefab, parent)는 프리팹에 저장된 position을 로컬로 쓴다 (§11 실사고)
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            BettingRoomManager.StripGameplay(body);
            holder.SetActive(true);

            var anim = holder.GetComponentInChildren<Animator>(true);
            if (anim != null) anim.enabled = false;    // 서 있는 기본 포즈 고정

            var plate = holder.GetComponentInChildren<RacerNumberPlate>(true);
            if (plate != null) plate.Apply(postNumber);

            var rs = holder.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return null;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);

            camGo = new GameObject("__figThumbCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;                       // 수동 Render 1회만 — 매 프레임 그리지 않는다
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 투명 배경
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 20f;

            // 3/4 정면 — 얼굴과 옆구리 번호판이 같이 보이는 각 (동물 프리팹은 +Z가 앞)
            Vector3 dir = new Vector3(0.55f, 0.35f, 1f).normalized;
            cam.transform.position = b.center + dir * 5f;
            cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position);

            // 프레이밍: 바운즈 최장변이 아니라 8코너를 카메라 공간에 투영한 실점유로 잰다 —
            // 3/4 각도에선 최장변 기준이 크게 헐거워져 아이콘에서 동물이 조그맣게 나온다
            float maxX = 0f, maxY = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var lp = cam.transform.InverseTransformPoint(corner);
                maxX = Mathf.Max(maxX, Mathf.Abs(lp.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(lp.y));
            }
            cam.orthographicSize = Mathf.Max(maxY, maxX) * 1.04f;   // RT가 정사각이라 x도 그대로 (여백 4%)

            var rt = RenderTexture.GetTemporary(Size, Size, 16, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // 알파 픽셀 실측으로 타이트 크롭 — 프레이밍이 얼마나 헐겁든 아이콘에선 동물이 최대 크기.
            // (바운즈 코너 투영만으론 3/4 각도에서 AABB가 실루엣보다 크게 잡혀 여백이 남는다)
            var px = tex.GetPixels32();
            int minX = Size, minY = Size, maxPx = -1, maxPy = -1;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (px[y * Size + x].a > 16)
                    {
                        if (x < minX) minX = x;
                        if (x > maxPx) maxPx = x;
                        if (y < minY) minY = y;
                        if (y > maxPy) maxPy = y;
                    }
            if (maxPx < 0) return null;                // 아무것도 안 찍힘 — 렌더 실패
            const int pad = 3;
            minX = Mathf.Max(0, minX - pad);
            minY = Mathf.Max(0, minY - pad);
            maxPx = Mathf.Min(Size - 1, maxPx + pad);
            maxPy = Mathf.Min(Size - 1, maxPy + pad);
            var crop = new Rect(minX, minY, maxPx - minX + 1, maxPy - minY + 1);

            return Sprite.Create(tex, crop, new Vector2(0.5f, 0.5f));
        }
        finally
        {
            // ⚠ Destroy는 프레임 끝까지 지연된다 — 같은 프레임에 여러 마리를 연달아 렌더하면
            //    앞서 만든 홀더가 아직 살아 있어 남의 썸네일에 같이 찍힌다 (실사고: 호랑이 뒤에 말).
            //    파괴 전 즉시 비활성화로 렌더에서 제외한다.
            holder.SetActive(false);
            Object.Destroy(holder);
            if (camGo != null) Object.Destroy(camGo);
        }
    }
}
