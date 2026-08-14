using TMPro;
using UnityEngine;

/// <summary>
/// [로컬라이제이션] 씬/프리팹의 정적 TMP 라벨용 — 키 하나를 들고, 켜질 때와 언어가 바뀔 때 갈아끼운다.
/// 코드가 매 프레임 다시 쓰는 텍스트(HUD 지갑·타이머 등)에는 붙이지 말 것 — 그쪽은 호출부가 Loc를 직접 쓴다.
/// 4단계(씬 일괄 부착)에서 MCP로 대량 배선 예정.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedLabel : MonoBehaviour
{
    [Tooltip("strings.csv의 키")]
    [SerializeField] private string key;

    private TMP_Text label;

    private void Awake() => label = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        Apply();
        Loc.OnLanguageChanged += Apply;
    }

    private void OnDisable() => Loc.OnLanguageChanged -= Apply;

    private void Apply()
    {
        if (label != null && !string.IsNullOrEmpty(key)) label.text = Loc.Get(key);
    }

    /// <summary>배선 도구용 — 키를 넣고 즉시 적용.</summary>
    public void SetKey(string newKey)
    {
        key = newKey;
        Apply();
    }
}
