using UnityEngine;

// SkillTrigger = 발동 무전기 (5초 후 지정 동물 스킬 강제 발동)
// Execute     = 처형 무전기 (5초 후 그 시점의 꼴등 탈락)
public enum ItemKind { Boost, Slow, SkillTrigger, Execute }

[CreateAssetMenu(fileName = "Item_", menuName = "HorseRace/Item")]
public class ItemDefinition : ScriptableObject
{
    public string itemName;
    public ItemKind kind;
    [TextArea] public string description;
    public Sprite icon;

    [Header("효과 수치")]
    public float duration = 3f;
    public float magnitude = 1.5f;   // Boost 1.6 / Slow 0.5
}
