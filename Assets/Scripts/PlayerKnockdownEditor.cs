#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// [에디터 전용] PlayerKnockdown 인스펙터에 "피격 재생" 버튼을 단다 — MPPM 2인 안 켜고도
/// 혼자서 쓰러짐→기상 사이클을 확인하기 위한 테스트 편의 장치 (빌드 미포함).
/// 사용법: 플레이 중 하이어라키에서 Player(나) 선택 → PlayerKnockdown 컴포넌트의 버튼 클릭.
/// </summary>
[CustomEditor(typeof(PlayerKnockdown))]
public class PlayerKnockdownEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("피격 재생 (쓰러짐 → 아무 키 기상)", GUILayout.Height(30f)))
                ((PlayerKnockdown)target).RequestKnockdown();   // 무적/이미 누움이면 내부에서 무시됨
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("플레이 중에만 사용 가능 — 실제 피격과 같은 경로(RequestKnockdown)로 재생된다.", MessageType.Info);
    }
}
#endif
