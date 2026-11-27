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

    public GameRoom? DrawCard(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId)
            return room;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return room;

        // 🔹 손패가 3장 또는 6장이면 더 이상 뽑을 수 없음
        if (player.Hand.Count == 3 || player.Hand.Count == 6) return room;

        // 🔹 덱이 비면 버림더미(바닥 마지막 1장 제외)를 섞어 자동 보충
        if (room.Deck.Count == 0) RefillDeckFromDiscard(room);
        if (room.Deck.Count == 0) return room;

        // 1장을 뽑으면 3장 또는 6장이 됨
        var newCard = room.Deck[0];
        room.Deck.RemoveAt(0);
        player.Hand.Add(newCard);

        // 🔹 뻥 대기 시간 중의 뽑기는 기록 — 그 시간 안에 뻥이 성립하면 RevertPungWindowDraw로 되돌린다
        int windowToken = Volatile.Read(ref room.PungWindowFlag);
        if (windowToken != 0) room.PungWindowDraw = new PungWindowDraw(windowToken, playerId, newCard);

        // 🔴 중요: 여기서 턴을 절대 넘기지 않음!
        // 턴은 오직 PlayCard(버리기)에서만 넘어감
        return room;
    }

    /// <summary>
    /// 턴을 to에게 넘긴다 (일반 턴 이동·뻥 공용). fromPlayerId는 직전에 카드를 버린 사람.
    /// 첫 바퀴 기록: 버린 사람 + 그 다음부터 to 직전까지(뻥으로 건너뛴 사람)를 턴을 마친 것으로 친다.
    /// </summary>
    public void PassTurn(GameRoom room, string fromPlayerId, Player to, bool byPung)
    {
        int n = room.Players.Count;
        int from = room.Players.FindIndex(p => p.PlayerId == fromPlayerId);
        int target = room.Players.IndexOf(to);
        if (from >= 0 && target >= 0)
        {
            for (int k = from; k != target; k = (k + 1) % n)
                room.FirstLapDoneIds.Add(room.Players[k].PlayerId);
        }
        UpdateFirstLap(room);

        room.CurrentTurnPlayerId = to.PlayerId;
        room.TurnStartedAt = DateTime.UtcNow;
        room.IsPungTurn = byPung;
    }

    /// <summary>순서상 다음 사람에게 턴을 넘긴다 (일반 턴 이동 — 버림 후·타임아웃).</summary>
    public void PassToNextPlayer(GameRoom room, string currentPlayerId)
    {
        int currentIndex = room.Players.FindIndex(p => p.PlayerId == currentPlayerId);
        var next = room.Players[(currentIndex + 1) % room.Players.Count];
        PassTurn(room, currentPlayerId, next, byPung: false);
    }

    private static void UpdateFirstLap(GameRoom room) =>
        room.IsFirstLapDone = room.IsFirstLapDone || room.Players.All(p => room.FirstLapDoneIds.Contains(p.PlayerId));

    private static void ResetTurnState(GameRoom room)
    {
        room.IsFirstLapDone = false;
        room.FirstLapDoneIds.Clear();
        room.IsPungTurn = false;
    }

    /// <summary>카드 1장 버림. 실제로 버렸으면 true, 내 턴이 아니거나 손에 없는 카드면 false(상태 변경 없음). 턴 이동은 하지 않는다.</summary>
    public bool PlayCard(string roomId, string playerId, PlayingCard card)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId)
            return false;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return false;

        var cardToPlay = player.Hand.FirstOrDefault(c =>
            (c.Suit == card.Suit && c.Rank == card.Rank) ||
            (IsJoker(c) && IsJoker(card)));
        if (cardToPlay == null) return false;

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
                return true;
            }
        }

        // 🛑 STOP 선언 처리
        if (room.IsStopDeclared && room.StopCallerId == playerId)
        {
            ApplyStopWin(room, player);
            return true;
        }

        room.LastActorPlayerId = playerId;
        // 턴 넘기기 로직은 허브의 Delay 이후 혹은 규칙에 따라 별도 처리
        return true;
    }

    /// <summary>버림더미를 덱으로 되돌린다. 바닥의 마지막 카드(뻥·바가지 기준)는 남긴다. 되돌린 장수 반환.</summary>
    private static int RefillDeckFromDiscard(GameRoom room)
    {
        if (room.DiscardPile.Count <= 1) return 0;
        var keep = room.DiscardPile[^1];
        var recycled = room.DiscardPile.Take(room.DiscardPile.Count - 1).ToList();
        room.Deck.AddRange(recycled.OrderBy(_ => Guid.NewGuid()));
        room.DiscardPile.Clear();
        room.DiscardPile.Add(keep);
        room.LastDiscardedCard = keep;
        return recycled.Count;
    }

    /// <summary>
    /// 방장이 수동으로 버림더미를 덱에 되돌림 — 자동 보충(DrawCard)의 예비 수단. 덱이 비어 있을 때만.
    /// </summary>
    public bool ReshuffleDiscardPile(string roomId, string requesterId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.HostPlayerId != requesterId || room.Deck.Count > 0)
            return false;
        return RefillDeckFromDiscard(room) > 0;
    }

}
