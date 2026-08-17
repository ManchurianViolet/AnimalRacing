using System.Text;
using UnityEngine;

/// <summary>
/// 캐릭터 외형 조립기. 슬롯별로 선택된 부위 메시를 렌더러에 꽂는다 (-1 = 안 씀).
/// 부위 프리팹 전부가 동일한 44개 본에 스킨돼 있어 메시 교체만으로 애니메이션이 그대로 따라온다.
/// 타이틀 화면의 전시용 캐릭터와 인게임 아바타가 같은 컴포넌트를 쓴다.
/// </summary>
public class CharacterCustomization : MonoBehaviour
{
    public const string PrefsKey = "characterLook";

    [SerializeField] private CharacterPartLibrary library;

    [Tooltip("저장된 외형이 없을 때의 기본 차림 (슬롯 순서대로 인덱스, -1=안 씀). 비우면 알몸")]
    [SerializeField] private string defaultCode = "";

    [Tooltip("Awake에서 이 컴퓨터의 저장 외형을 입을지. 타이틀 전시용=켬 / 네트워크 아바타=끔 — 남의 아바타가 내 옷장(PlayerPrefs)을 열면 안 되고, 착용은 NetworkPlayerSetup이 전담한다")]
    [SerializeField] private bool loadSavedOnAwake = true;

    private SkinnedMeshRenderer[] slotRenderers;
    private int[] selection;

    public CharacterPartLibrary Library => library;
    public int SlotCount => library != null ? library.SlotCount : 0;

    private void Awake()
    {
        Bind();
        if (loadSavedOnAwake)
        {
            if (!PlayerPrefs.HasKey(PrefsKey))
            {
                // 신규 유저: 랜덤 차림을 뽑아 즉시 저장 — "빈 외형 코드"인 사람을 세상에서 없앤다
                Randomize();
                SaveToPrefs();
            }
            else
            {
                Decode(PlayerPrefs.GetString(PrefsKey, defaultCode));
            }
        }
        else
        {
            Decode(defaultCode);
        }
        ApplyAll();
    }

    /// <summary>슬롯 이름으로 렌더러를 찾아둔다 (자식 어디에 있든 이름으로 매칭).</summary>
    private void Bind()
    {
        if (library == null || library.slots == null) return;
        if (slotRenderers != null) return;

        slotRenderers = new SkinnedMeshRenderer[library.slots.Length];
        selection = new int[library.slots.Length];

        var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < library.slots.Length; i++)
        {
            string want = library.slots[i].rendererName;
            foreach (var r in all)
            {
                if (r.name != want) continue;
                slotRenderers[i] = r;
                // 컬링 박스를 매 프레임 실제 뼈 위치로 재계산 — 고정 박스는 서 있는 자세 기준이라
                // 쓰러짐 같은 큰 자세 변화에서 부위(머리카락 등)가 화면 밖 판정으로 사라진다
                r.updateWhenOffscreen = true;
                break;
            }
            if (slotRenderers[i] == null)
                Debug.LogWarning($"[커마] 슬롯 렌더러 '{want}' 를 못 찾음 — 캐릭터 프리팹 확인 필요");

            selection[i] = library.slots[i].optional ? -1 : 0;
        }
    }

    // ---- 선택 ----

    public int GetSelection(int slot)
    {
        Bind();
        return (selection != null && slot >= 0 && slot < selection.Length) ? selection[slot] : -1;
    }

    public void SetSelection(int slot, int index)
    {
        Bind();
        if (selection == null || slot < 0 || slot >= selection.Length) return;

        var def = library.slots[slot];
        int min = def.optional ? -1 : 0;
        int max = def.parts.Length - 1;
        selection[slot] = Mathf.Clamp(index, min, Mathf.Max(min, max));
        ApplySlot(slot);
    }

    /// <summary>다음/이전 부위로 순환 (끄기 포함).</summary>
    public void Cycle(int slot, int dir)
    {
        Bind();
        var def = library.slots[slot];
        int count = def.parts.Length;
        if (count == 0) return;

        int min = def.optional ? -1 : 0;
        int span = count - min;                       // 선택지 개수
        int cur = selection[slot] - min;              // 0 기준으로
        int next = ((cur + dir) % span + span) % span;
        SetSelection(slot, next + min);
    }

    public string GetSelectedName(int slot)
    {
        Bind();
        var def = library.slots[slot];
        int i = selection[slot];
        if (i < 0 || i >= def.parts.Length) return Loc.Get("custom.none");
        return library.PartLabel(slot, i);   // SO의 한글 displayName이 아니라 현재 언어로
    }

    public void Randomize()
    {
        Bind();
        for (int i = 0; i < library.slots.Length; i++)
        {
            var def = library.slots[i];
            if (def.parts.Length == 0) continue;
            int min = def.optional ? -1 : 0;
            SetSelection(i, Random.Range(min, def.parts.Length));
        }
    }

    // ---- 적용 ----

    public void ApplyAll()
    {
        Bind();
        if (slotRenderers == null) return;
        for (int i = 0; i < slotRenderers.Length; i++) ApplySlot(i);
    }

    private void ApplySlot(int slot)
    {
        var r = slotRenderers[slot];
        if (r == null) return;

        var def = library.slots[slot];
        int i = selection[slot];
        Mesh mesh = (i >= 0 && i < def.parts.Length) ? def.parts[i].mesh : def.emptyMesh;
        if (mesh == null) return;

        r.sharedMesh = mesh;
        // 바운즈는 메시마다 다르므로 갱신 — 안 하면 카메라 밖 판정이 틀어져 옷이 사라진다
        r.localBounds = mesh.bounds;
    }

    // ---- 저장/복원 ----

    /// <summary>"3,0,-1,2,..." 형태 — PlayerPrefs 저장 및 네트워크 전송(추후)에 사용.</summary>
    public string Encode()
    {
        Bind();
        if (selection == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < selection.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(selection[i]);
        }
        return sb.ToString();
    }

    public void Decode(string code)
    {
        Bind();
        if (selection == null || string.IsNullOrEmpty(code)) return;

        var parts = code.Split(',');
        for (int i = 0; i < selection.Length && i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int v)) continue;
            var def = library.slots[i];
            int min = def.optional ? -1 : 0;
            selection[i] = Mathf.Clamp(v, min, Mathf.Max(min, def.parts.Length - 1));
        }
    }

    /// <summary>외형 코드를 입힌다. 코드가 비면 기본 차림(defaultCode) — 이전에 입고 있던 옷이 남지 않게.</summary>
    public void ApplyCode(string code)
    {
        Decode(string.IsNullOrEmpty(code) ? defaultCode : code);
        ApplyAll();
    }

    public void SaveToPrefs()
    {
        PlayerPrefs.SetString(PrefsKey, Encode());
        PlayerPrefs.Save();
    }

    public void LoadFromPrefs()
    {
        Decode(PlayerPrefs.GetString(PrefsKey, ""));
        ApplyAll();
    }
}
