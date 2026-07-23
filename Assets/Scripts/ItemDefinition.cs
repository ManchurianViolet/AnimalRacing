using UnityEngine;

public enum ItemKind { Boost, Slow }

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
