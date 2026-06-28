using Card.Api.Domain;

namespace Card.Api.Services;

public class BangActionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool GameEnded { get; set; }
    public string? Message { get; set; }
}

public partial class BangGameService
{
    private readonly GameRoomService _roomService;
    private readonly Random _random = new();

    public BangGameService(GameRoomService roomService)
    {
        _roomService = roomService;
    }

    // 4~7명 역할 분포표
    private static readonly Dictionary<int, BangRole[]> RoleDistribution = new()
    {
        [4] = new[] { BangRole.Sheriff, BangRole.Outlaw, BangRole.Outlaw, BangRole.Renegade },
        [5] = new[] { BangRole.Sheriff, BangRole.Deputy, BangRole.Outlaw, BangRole.Outlaw, BangRole.Renegade },
        [6] = new[] { BangRole.Sheriff, BangRole.Deputy, BangRole.Outlaw, BangRole.Outlaw, BangRole.Outlaw, BangRole.Renegade },
        [7] = new[] { BangRole.Sheriff, BangRole.Deputy, BangRole.Deputy, BangRole.Outlaw, BangRole.Outlaw, BangRole.Outlaw, BangRole.Renegade }
    };

    /// <summary>4~7명일 때만 뱅 게임 시작. 역할 배정·카드 분배.</summary>
    public bool StartBangGame(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room == null) return false;
        if (room.GameMode != GameMode.Bang) return false;
        if (room.IsStarted) return false;

        int count = room.Players.Count;
        if (!RoleDistribution.TryGetValue(count, out var roles)) return false;

        var bang = new BangState { UseDodgeCity = room.BangUseDodgeCity };

        // 1. 덱 구성 — 확장팩 옵션 반영
        var deck = BuildDeck(bang);
        Shuffle(deck);
        bang.Deck = deck;

        // 2. 역할 셔플 후 배정
        var shuffledRoles = roles.OrderBy(_ => _random.Next()).ToList();

        // 3. 캐릭터 풀 — 기본 16종, 확장팩이면 추가 8종까지 풀
        var allCharacters = Enum.GetValues<BangCharacter>()
            .Where(c => bang.UseDodgeCity || (int)c < 16)
            .ToList();
        var shuffledChars = allCharacters.OrderBy(_ => _random.Next()).Take(count).ToList();

        for (int i = 0; i < count; i++)
        {
            var roomPlayer = room.Players[i];
            var role = shuffledRoles[i];
            var character = shuffledChars[i];
            int baseHp = GetCharacterMaxHp(character);
            int maxHp = baseHp + (role == BangRole.Sheriff ? 1 : 0);  // 보안관은 +1 HP
            var bp = new BangPlayer
            {
                PlayerId = roomPlayer.PlayerId,
                Name = roomPlayer.Name,
                Role = role,
                RoleRevealed = role == BangRole.Sheriff,
                Character = character,
                Health = maxHp,
                MaxHealth = maxHp,
                SeatIndex = i,
                IsBot = roomPlayer.IsBot
            };
            // 시작 카드 = 체력만큼
            for (int k = 0; k < maxHp; k++) DrawToHand(bang, bp);
            bang.Players.Add(bp);
        }

        // 3. 보안관이 첫 턴
        var sheriff = bang.Players.First(p => p.Role == BangRole.Sheriff);
        bang.CurrentTurnPlayerId = sheriff.PlayerId;

        // 4. 턴 시작 — 보안관이 카드 2장 뽑기 (Dynamite/Jail 체크 포함)
        StartTurnFor(bang, sheriff);

        room.Bang = bang;
        room.IsStarted = true;
        room.IsFinished = false;
        return true;
    }

    // MVP 덱: BANG/Missed/Beer/Stagecoach/GeneralStore — 단순화된 비율
    private List<BangCard> BuildDeck(BangState bang)
    {
        var deck = new List<BangCard>();
        void Add(BangCardType type, int n)
        {
            for (int i = 0; i < n; i++)
                deck.Add(new BangCard
                {
                    Id = bang.NextCardId++,
                    Type = type,
                    Suit = (CardSuit)_random.Next(4),
                    Rank = _random.Next(1, 14)
                });
        }
        Add(BangCardType.Bang, 25);
        Add(BangCardType.Missed, 12);
        Add(BangCardType.Beer, 6);
        Add(BangCardType.Stagecoach, 2);
        Add(BangCardType.GeneralStore, 2);
        Add(BangCardType.WellsFargo, 1);
        Add(BangCardType.Saloon, 1);
        Add(BangCardType.CatBalou, 4);
        Add(BangCardType.Panic, 4);
        Add(BangCardType.Indians, 2);
        Add(BangCardType.Gatling, 1);
        Add(BangCardType.Duel, 3);
        // 무기
        Add(BangCardType.Volcanic, 2);
        Add(BangCardType.Scofield, 3);
        Add(BangCardType.Remington, 1);
        Add(BangCardType.Carbine, 1);
        Add(BangCardType.Winchester, 1);
        // 장비
        Add(BangCardType.Mustang, 2);
        Add(BangCardType.Scope, 1);
        Add(BangCardType.Barrel, 2);
        Add(BangCardType.Dynamite, 1);
        Add(BangCardType.Jail, 3);
        if (bang.UseDodgeCity)
        {
            Add(BangCardType.Punch, 4);
            Add(BangCardType.Dodge, 4);
            Add(BangCardType.Whisky, 1);
            Add(BangCardType.Tequila, 2);
            Add(BangCardType.Springfield, 3);
            Add(BangCardType.BuffaloRifle, 1);
            Add(BangCardType.Brawl, 2);
            Add(BangCardType.RagTime, 2);
            Add(BangCardType.CanCan, 2);
            Add(BangCardType.IronPlate, 1);
            Add(BangCardType.Pepperbox, 1);
            Add(BangCardType.Conestoga, 1);
            Add(BangCardType.RevCarabine, 1);
            Add(BangCardType.TenGallonHat, 1);
            Add(BangCardType.Bibles, 1);
        }
        return deck;
    }

    private void Shuffle(List<BangCard> deck)
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    // 덱이 비면 버린 카드 더미를 셔플해서 덱으로
    private BangCard? DrawCard(BangState bang)
    {
        if (bang.Deck.Count == 0)
        {
            if (bang.DiscardPile.Count == 0) return null;
            bang.Deck = bang.DiscardPile.ToList();
            bang.DiscardPile.Clear();
            Shuffle(bang.Deck);
        }
        var card = bang.Deck[0];
        bang.Deck.RemoveAt(0);
        return card;
    }

    private void DrawToHand(BangState bang, BangPlayer player)
    {
        var card = DrawCard(bang);
        if (card != null) player.Hand.Add(card);
    }

    private static string SuitSymbol(CardSuit s) => s switch
    {
        CardSuit.Hearts => "♥",
        CardSuit.Diamonds => "♦",
        CardSuit.Clubs => "♣",
        CardSuit.Spades => "♠",
        _ => "?"
    };

    // 덱에서 카드 1장 펴기 — 카드 자체는 그대로 버린 카드 더미로
    // Lucky Duke는 2장 펴고 favorWanted(예: Hearts면 true)를 만족하는 것 우선 선택
    private BangCard? DrawReveal(BangState bang, BangPlayer? player = null, Func<BangCard, bool>? favorWanted = null)
    {
        if (player != null && player.Character == BangCharacter.LuckyDuke && favorWanted != null)
        {
            var c1 = DrawCard(bang);
            var c2 = DrawCard(bang);
            BangCard? chosen = null, other = null;
            if (c1 != null && favorWanted(c1)) { chosen = c1; other = c2; }
            else if (c2 != null && favorWanted(c2)) { chosen = c2; other = c1; }
            else { chosen = c1; other = c2; }
            if (chosen != null) bang.DiscardPile.Add(chosen);
            if (other != null) bang.DiscardPile.Add(other);
            // 호출자가 메시지에 추가할 수 있도록 두 카드 임시 저장
            bang.LastLuckyDukeReveal = new List<BangCard>();
            if (c1 != null) bang.LastLuckyDukeReveal.Add(c1);
            if (c2 != null) bang.LastLuckyDukeReveal.Add(c2);
            return chosen;
        }
        var c = DrawCard(bang);
        if (c != null) bang.DiscardPile.Add(c);
        return c;
    }

    private static BangActionResult Fail(string msg) => new() { Success = false, Error = msg };
}
