using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    public void DiscardCards(GameRoom room, Player player, List<int> handIndexes)
    {
        var sortedIndexes = handIndexes.Distinct().OrderByDescending(i => i).ToList();
        foreach (var index in sortedIndexes)
        {
            if (index < 0 || index >= player.Hand.Count) continue;
            var card = player.Hand[index];
            player.Hand.RemoveAt(index);
            room.DiscardPile.Add(card);
            room.LastDiscardedCard = card;
        }
    }

    public GameRoom DrawCard(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId) 
            return room;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null || room.Deck.Count == 0) return room;

        // 🔹 손패가 3장 또는 6장이면 더 이상 뽑을 수 없음
        if (player.Hand.Count == 3 || player.Hand.Count == 6) return room;

        // 플레이어 턴 횟수 확인하기(승리선언 하기위함)
        player.RoundTurnCount++;

        // [수정] 5장뿐만 아니라 2장일 때도 뽑을 수 있도록 조건 완화 (혹은 조건 삭제)
        // 1장을 뽑으면 3장 또는 6장이 됨
        var newCard = room.Deck[0];
        room.Deck.RemoveAt(0);
        player.Hand.Add(newCard);

        // 🔴 중요: 여기서 턴을 절대 넘기지 않음!
        // 턴은 오직 PlayCard(버리기)에서만 넘어감
        return room;
    }

    public GameRoom PlayCard(string roomId, string playerId, PlayingCard card)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId) 
            return room;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return room;

        var cardToPlay = player.Hand.FirstOrDefault(c =>
            (c.Suit == card.Suit && c.Rank == card.Rank) ||
            (IsJoker(c) && IsJoker(card)));
        
        if (cardToPlay != null)
        {
            player.Hand.Remove(cardToPlay);
            room.LastDiscardedCard = cardToPlay;
            room.DiscardPile.Add(cardToPlay);

            // 🚨 [수정] 바가지 체크 로직을 단일화하여 정확한 loserId를 전달합니다.
            var result = CheckBagajiWin(cardToPlay.Rank, playerId, room.Players);
            if (result.isBagajiWin)
            {
                var winner = room.Players.FirstOrDefault(p => p.PlayerId == result.winnerId);
                if (winner != null)
                {
                    // 프론트 모달 표시용 텍스트 설정
                    room.LastWinType = $"🔥 바가지 승리! ({winner.Name})";
                    // 점수 계산 실행 (result.loserId는 카드를 버린 playerId와 동일함)
                    ApplyBagajiWin(room, winner, result.loserId!);
                    return room;
                }
            }
            
            // 🛑 STOP 선언 처리
            if (room.IsStopDeclared && room.StopCallerId == playerId)
            {
                ApplyStopWin(room, player);
                return room;
            }

            room.LastActorPlayerId = playerId;
            // 턴 넘기기 로직은 허브의 Delay 이후 혹은 규칙에 따라 별도 처리
        }
        
        return room;
    }

    /// <summary>
    /// 방장이 클릭하여 버려진 카드들을 다시 덱으로 셔플 (Re-Shuffle Discard Pile)
    /// </summary>
    public bool ReshuffleDiscardPile(string roomId, string requesterId)
    {
        var room = GetRoom(roomId);
        
        // 방 존재 여부, 방장 권한, 버려진 카드가 있는지 확인
        if (room == null || room.HostPlayerId != requesterId || room.DiscardPile.Count == 0)
            return false;

        lock (room)
        {
            // 1. 버려진 카드 더미를 덱에 추가
            room.Deck.AddRange(room.DiscardPile);

            // 2. 덱을 무작위로 다시 셔플
            room.Deck = room.Deck.OrderBy(a => Guid.NewGuid()).ToList();

            // 3. 버려진 카드 더미 비우기 및 마지막 버린 카드 초기화
            room.DiscardPile.Clear();
            room.LastDiscardedCard = null;
        }

        return true;
    }

}
