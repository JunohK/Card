namespace Card.Api.Domain;

public enum TurnPhase
{
    Draw,       // 카드 드로우 직후
    Action,     // 행동 선택 대기
    Finished
}

public class TurnState
{
    public string CurrentTurnPlayerId { get; set; } = "";
    public TurnPhase Phase { get; set; } = TurnPhase.Draw;
}