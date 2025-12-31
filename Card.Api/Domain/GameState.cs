// 게임 상태 여부

namespace Card.Api.Domain;

public class GameState
{
    public string CurrentTurnPlayerId { get; set; } = "";
    public bool IsStarted { get; set; } = false;
}