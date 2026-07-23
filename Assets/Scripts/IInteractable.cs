/// <summary>상호작용 가능한 모든 것의 계약. 단말기, (추후) 문/상점 등.</summary>
public interface IInteractable
{
    string Prompt { get; }              // "E - 베팅하기"
    bool CanInteract();
    void Interact();
}
