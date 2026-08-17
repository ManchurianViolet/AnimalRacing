using UnityEngine;

/// <summary>
/// 캐릭터 커스터마이징 부위 목록 (에셋팩의 SlotLibrary에서 자동 생성 — Tools 메뉴 참조).
/// 부위 프리팹들이 전부 같은 44개 본에 스킨돼 있어서, 슬롯 렌더러의 메시만 갈아끼우면 된다.
/// (부위 프리팹을 통째로 붙이면 자기 뼈대 복사본이 따라와서 옷만 T포즈로 굳는다 — 메시만 쓴다)
/// </summary>
[CreateAssetMenu(fileName = "CharacterPartLibrary", menuName = "HorseRace/CharacterPartLibrary")]
public class CharacterPartLibrary : ScriptableObject
{
    [System.Serializable]
    public class Part
    {
        public string displayName;
        public Mesh mesh;
    }

    [System.Serializable]
    public class SlotDef
    {
        [Tooltip("Base_Mesh 아래 슬롯 오브젝트 이름 — 이 이름으로 렌더러를 찾는다")]
        public string rendererName;
        [Tooltip("UI에 보일 이름")]
        public string displayName;
        [Tooltip("끄기(안 씀) 허용 — 몸/표정은 false")]
        public bool optional = true;
        [Tooltip("끄기용 빈 껍데기 메시 (정점 4개짜리). Base_Mesh의 기본 메시")]
        public Mesh emptyMesh;
        public Part[] parts;
    }

    public SlotDef[] slots;

    public int SlotCount => slots != null ? slots.Length : 0;

    // ---- 표시 이름 (로컬라이제이션) ----
    // SO에 nameKey를 넣지 않고 rendererName + 인덱스로 키를 조립한다 — 이 SO는 에셋팩에서 자동 생성되는 물건이라
    // 재생성될 때마다 손으로 키를 다시 박아야 하는 걸 피하려는 것. 키가 없으면 SO의 한글 displayName으로 폴백한다.
    // 키 형식: custom.slot.<rendererName 소문자> / custom.part.<rendererName 소문자>.<부위 인덱스>

    private static string SlotKeyBase(SlotDef def) =>
        string.IsNullOrEmpty(def.rendererName) ? "unknown" : def.rendererName.ToLowerInvariant();

    /// <summary>슬롯 이름 (몸/상의/모자…) — 현재 언어로.</summary>
    public string SlotLabel(int slot)
    {
        if (slots == null || slot < 0 || slot >= slots.Length) return "";
        var def = slots[slot];
        return Loc.Get("custom.slot." + SlotKeyBase(def), def.displayName);
    }

    /// <summary>부위 이름 (정장/헤드폰/운동화…) — 현재 언어로.</summary>
    public string PartLabel(int slot, int part)
    {
        if (slots == null || slot < 0 || slot >= slots.Length) return "";
        var def = slots[slot];
        if (def.parts == null || part < 0 || part >= def.parts.Length) return "";
        return Loc.Get($"custom.part.{SlotKeyBase(def)}.{part}", def.parts[part].displayName);
    }
}
