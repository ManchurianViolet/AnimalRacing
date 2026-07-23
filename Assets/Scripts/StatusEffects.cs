public enum StatusEffectType { Boost, Slow }

[System.Serializable]
public class StatusEffect
{
    public StatusEffectType type;
    public float remaining;
    public float magnitude;

    public StatusEffect(StatusEffectType type, float duration, float magnitude = 1f)
    {
        this.type = type;
        this.remaining = duration;
        this.magnitude = magnitude;
    }
}
