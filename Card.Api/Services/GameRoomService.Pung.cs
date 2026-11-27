using Card.Api.Domain;

namespace Card.Api.Services;

/// <summary>뻥 성공 결과 — 뻥한 사람과, 대기 시간 중 뽑았다가 덱으로 돌아간 뽑기 기록(없으면 null).</summary>
public sealed record PungResult(Player Puncher, PungWindowDraw? RevertedDraw);

// 뻥 — 상대가 버린 뒤 2초 대기 시간 안에 같은 숫자 2장(부족하면 조커)을 내고 턴을 가져온다.
// 대기 시간 토큰(PungWindowFlag)을 열고 닫는 타이밍은 허브(PlayCard)가 맡고, 규칙은 여기서 판단한다.
public partial class GameRoomService
{
    /// <summary>
    /// 뻥 시도. 성공하면 결과, 자격이 없거나 대기 시간이 이미 닫혔으면 null.
    /// 성공 시: 대기 시간 닫기 → (그 사이 뽑은 카드 되돌리기) → 2장 버림 → 턴을 뻥한 사람에게 (STOP 불가 턴).
    /// </summary>
    public PungResult? TryPung(GameRoom room, string playerId)
    {
        if (room.IsRoundEnded) return null;

        var targetCard = room.LastDiscardedCard;
        if (targetCard == null) return null;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return null;

        // 자격 검증을 먼저 한 뒤 대기 시간을 원자적으로 닫는다 (한 명만 통과). 자기가 버린 카드는 뻥 불가
        if (room.LastActorPlayerId == player.PlayerId) return null;
        if (!CanPung(player, targetCard)) return null;
        int openToken = Volatile.Read(ref room.PungWindowFlag);
        if (openToken == 0 || Interlocked.CompareExchange(ref room.PungWindowFlag, 0, openToken) != openToken) return null;

        // 2장을 못 채우면 아무것도 하지 않는다 (대기 시간은 이미 닫힌 상태 — 기존 동작 유지)
        var handIndexes = PickPungCards(player.Hand, targetCard.Rank);
        if (handIndexes.Count < 2) return null;

        // 대기 시간 중에 다음 사람이 이미 뽑았으면 그 카드는 덱으로 되돌린다 (뽑은 사람이 봤으므로 섞어서 넣음)
        var reverted = RevertPungWindowDraw(room, openToken);

        DiscardCards(room, player, handIndexes);

        // 원래 카드를 버린 사람(LastActor) 다음부터 나 직전까지는 건너뛴 것 → 첫 바퀴 턴을 마친 것으로 기록
        PassTurn(room, room.LastActorPlayerId, player, byPung: true);
        room.LastActorPlayerId = player.PlayerId;   // 바닥의 마지막 카드는 뻥한 사람이 버린 것

        return new PungResult(player, reverted);
    }

    /// <summary>
    /// 뻥(가로채기) 자격.
    /// 손패 2장: 같은 숫자 쌍(또는 조커 포함)이고 버린 카드가 그 숫자이거나 조커일 때.
    /// 손패 5장: 버린 카드와 같은 숫자 + 조커가 합쳐 2장 이상일 때.
    /// </summary>
    public static bool CanPung(Player player, PlayingCard playedCard)
    {
        if (player.Hand.Count == 2)
        {
            bool isPair = player.Hand[0].Rank == player.Hand[1].Rank || player.Hand.Any(IsJoker);
            bool matchesPlayed = player.Hand.Any(c => c.Rank == playedCard.Rank) || IsJoker(playedCard);
            if (isPair && matchesPlayed) return true;
        }

        if (player.Hand.Count == 5)
        {
            int matchingRank = player.Hand.Count(c => c.Rank == playedCard.Rank);
            int jokers = player.Hand.Count(IsJoker);
            if (matchingRank + jokers >= 2) return true;
        }

        return false;
    }

    /// <summary>뻥으로 낼 손패 위치 2개 — 같은 숫자를 먼저, 모자라면 조커로 채운다. 못 채우면 2개 미만.</summary>
    private static List<int> PickPungCards(List<PlayingCard> hand, string rank)
    {
        var sameRank = hand.Select((card, index) => (card, index)).Where(x => x.card.Rank == rank).Select(x => x.index);
        var jokers = hand.Select((card, index) => (card, index)).Where(x => IsJoker(x.card)).Select(x => x.index);

        var picked = sameRank.Take(2).ToList();
        picked.AddRange(jokers.Take(2 - picked.Count));
        return picked;
    }

    /// <summary>
    /// 뻥 대기 시간 중에 뽑았던 카드를 되돌린다 — 그 사이에 뻥이 성립하면 그 뽑기는 무효.
    /// 뽑은 사람이 이미 본 카드라 덱 맨 위가 아니라 섞어서 넣는다. 되돌렸으면 그 기록, 해당 없으면 null.
    /// </summary>
    public PungWindowDraw? RevertPungWindowDraw(GameRoom room, int windowToken)
    {
        var draw = room.PungWindowDraw;
        if (draw == null || draw.Token != windowToken) return null;
        room.PungWindowDraw = null;

        var drawer = room.Players.FirstOrDefault(p => p.PlayerId == draw.PlayerId);
        if (drawer == null || !drawer.Hand.Remove(draw.Card)) return null;

        room.Deck.Add(draw.Card);
        room.Deck = room.Deck.OrderBy(_ => Guid.NewGuid()).ToList();
        return draw;
    }
}
