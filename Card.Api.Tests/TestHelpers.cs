using Card.Api.Domain;
using Card.Api.Services;

namespace Card.Api.Tests;

/// <summary>Cards 모드 테스트용 카드 팩토리. 서버 CreateNewDeck과 동일한 필드 규칙.</summary>
internal static class Cards
{
    public static PlayingCard C(string rank, string suit = "♠") => new()
    {
        Suit = suit,
        Rank = rank,
        Color = (suit == "♥" || suit == "♦") ? "Red" : "Black"
    };

    public static PlayingCard Joker() => new() { Type = CardType.Joker, Suit = "Joker", Rank = "Joker", Color = "Black" };

    public static List<PlayingCard> Hand(params string[] ranks) =>
        ranks.Select(r => r == "Joker" ? Joker() : C(r)).ToList();
}

internal static class Rooms
{
    /// <summary>Cards 모드 방 생성 + n명 입장. PlayerId = 닉네임 (서버 규칙과 동일).</summary>
    public static (GameRoomService svc, GameRoom room) Cards(int players, int jokerCount = 1, CardMode mode = CardMode.Trump)
    {
        var svc = new GameRoomService();
        var room = svc.CreateRoom("p1", "t", null, GameMode.Cards);
        room.JokerCount = jokerCount;
        room.CardMode = mode;
        for (int i = 1; i <= players; i++) svc.JoinRoom(room.RoomId, $"p{i}", $"p{i}");
        return (svc, room);
    }

    public static Player P(GameRoom room, string id) => room.Players.Single(p => p.PlayerId == id);
}
