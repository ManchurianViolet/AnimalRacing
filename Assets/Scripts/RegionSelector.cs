using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [글로벌] 타이틀 좌하단 서버 표시 + 변경 팝업.
/// 선택지는 PhotonRegions.Choices에서 빌드 — 지역이 늘어도 여기는 무수정.
/// 실제 저장/재접속은 NetworkLauncher.ChangeRegion에 위임, 여기는 UI 흐름만 (TitleMenu와 같은 철학).
/// </summary>
public class RegionSelector : MonoBehaviour
{
    [SerializeField] private NetworkLauncher launcher;
    [SerializeField] private TMP_Text currentLabel;    // "서버: 자동 (한국)"
    [SerializeField] private Button btnChange;         // 팝업 토글
    [SerializeField] private GameObject popup;         // 선택 팝업 루트
    [SerializeField] private Transform optionParent;   // 선택 버튼 컨테이너 (VLG)
    [SerializeField] private Button optionTemplate;    // 복제용 템플릿 (비활성으로 두기)

    private void Start()
    {
        btnChange.onClick.AddListener(() => popup.SetActive(!popup.activeSelf));
        BuildOptions();
        popup.SetActive(false);
    }

    private void BuildOptions()
    {
        foreach (var (code, name) in PhotonRegions.Choices)
        {
            var btn = Instantiate(optionTemplate, optionParent);
            btn.gameObject.SetActive(true);
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = name;

            string captured = code;
            btn.onClick.AddListener(() =>
            {
                popup.SetActive(false);
                launcher.ChangeRegion(captured);
            });
        }
    }

    private void Update()
    {
        if (currentLabel == null) return;

        string saved = NetworkLauncher.SavedRegion;
        string text;
        if (PhotonNetwork.IsConnectedAndReady && !string.IsNullOrEmpty(PhotonNetwork.CloudRegion))
        {
            string live = PhotonRegions.Of(PhotonNetwork.CloudRegion);
            text = string.IsNullOrEmpty(saved) ? $"서버: 자동 ({live})" : $"서버: {live}";
        }
        else
        {
            text = string.IsNullOrEmpty(saved) ? "서버: 접속 중..." : $"서버: {PhotonRegions.Of(saved)} 접속 중...";
        }

        // 값이 같으면 TMP를 건드리지 않는다 (커마 패널과 같은 규칙)
        if (currentLabel.text != text) currentLabel.text = text;
    }
}
