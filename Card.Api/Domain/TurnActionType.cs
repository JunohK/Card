namespace Card.Api.Domain;

/// <summary>
/// 플레이어가 선택할 수 있는 턴 행동 타입
/// </summary>
public enum TurnActionType
{
    DiscardOne,        // 카드 1장 버리기
    DiscardPairAndOne, // 같은 카드 2장 + 추가 1장
    DeclareWin         // 게임 종료 선언
}