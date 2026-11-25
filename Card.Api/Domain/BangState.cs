namespace Card.Api.Domain;

public enum BangRole
{
    Sheriff = 0,    // 보안관 — 무법자 + 배신자 모두 제거하면 승리
    Deputy = 1,     // 부보안관 — 보안관과 함께 승리
    Outlaw = 2,     // 무법자 — 보안관 죽이면 승리
    Renegade = 3    // 배신자 — 최후의 1대1에서 보안관 죽이면 승리
}

public enum BangCharacter
{
    // 능력 활성 (이번 phase 구현)
    BartCassidy = 0,    // 4 HP. 데미지 받으면 카드 1장 드로우
    PaulRegret = 1,     // 3 HP. 항상 Mustang 효과 (받을 때 거리 +1)
    RoseDoolan = 2,     // 4 HP. 항상 Scope 효과 (보낼 때 거리 -1)
    WillyTheKid = 3,    // 4 HP. 턴당 BANG 무제한
    // 능력 비활성 (다음 phase) — enum만 등록
    BlackJack = 4,
    CalamityJanet = 5,
    ElGringo = 6,
    JesseJones = 7,
    Jourdonnais = 8,
    KitCarlson = 9,
    LuckyDuke = 10,
    PedroRamirez = 11,
    SidKetchum = 12,
    SlabTheKiller = 13,
    SuzyLafayette = 14,
    VultureSam = 15,
    // === Dodge City 확장팩 캐릭터 — 능력은 다음 phase ===
    ApacheKid = 16,
    BelleStar = 17,
    BillNoface = 18,
    ChuckWengam = 19,
    DocHolyday = 20,
    ElenaFuente = 21,
    GregDigger = 22,
    HerbHunter = 23
}

public enum BangCardType
{
    Bang = 0,            // 거리 내 1명 1데미지 (Missed로 회피 가능)
    Missed = 1,          // BANG 무효화
    Beer = 2,            // 체력 +1 (마지막 2명 이하일 땐 사용 불가)
    Stagecoach = 3,      // 카드 2장 뽑기
    GeneralStore = 4,    // 모든 플레이어 1장씩 (다음 phase)
    WellsFargo = 5,      // 카드 3장 뽑기
    Saloon = 6,          // 모든 살아있는 사람 체력 +1
    CatBalou = 7,        // 대상의 손패 1장 직접 선택 → 버림
    Panic = 8,           // 거리 1 이내 대상의 손패 1장 직접 선택 → 내 손
    Indians = 9,         // 본인 외 전원이 BANG 안 내면 1 데미지 (시계방향 순서)
    Gatling = 10,        // 본인 외 전원이 Missed 안 내면 1 데미지 (시계방향 순서)
    Duel = 11,           // A/B 번갈아 BANG. 못 내는 쪽 1 데미지
    // === 무기 (장착 — 한 명당 1개만, 새 무기 장착 시 기존 무기 버림) ===
    Volcanic = 12,       // 사거리 1 + 턴당 BANG 무제한
    Scofield = 13,       // 사거리 2
    Remington = 14,      // 사거리 3
    Carbine = 15,        // 사거리 4
    Winchester = 16,     // 사거리 5
    // === 장비 (장착 — 같은 종류 1장 한정) ===
    Mustang = 17,        // 본인이 받을 때 거리 +1
    Scope = 18,          // 본인이 보낼 때 거리 -1
    Barrel = 19,         // BANG 받을 때 펴기 ♥이면 자동 회피
    Dynamite = 20,       // 본인 턴 시작 시 펴기. ♠ 2~9이면 3 데미지 + 버림. 아니면 다음 사람으로
    Jail = 21,           // 대상에게 장착. 그 사람 턴 시작 시 펴기 ♥이면 탈옥, 아니면 턴 스킵
    // === Dodge City 확장팩 카드 ===
    Punch = 22,          // BANG 효과, 거리 1만
    Dodge = 23,          // Missed 효과 + 사용자가 카드 1장 드로우
    Whisky = 24,         // 2 HP 회복. 사용 시 손패 1장 추가 버림
    Tequila = 25,        // 대상 1명에게 1 HP 회복. 사용 시 손패 1장 추가 버림
    // 확장팩 추가 카드
    Springfield = 26,    // 손패 1장 추가 버림 + BANG 효과 (사거리 무한)
    BuffaloRifle = 27,   // BANG 효과 (사거리 무한)
    Brawl = 28,          // 손패 1장 추가 버림 + 다른 모두 손패 1장씩 강제 버림 (랜덤)
    RagTime = 29,        // 손패 1장 추가 버림 + 다른 사람 손에서 1장 가져옴 (블라인드, 거리 무한)
    CanCan = 30,         // 다른 사람의 카드(손/장비/무기) 1장 즉시 버림 (거리 무한)
    IronPlate = 31,      // 본인 장비. BANG 받기 전 자동 펴기 ♠이면 회피
    // 확장팩 추가 무기/장비
    Pepperbox = 32,      // 무기, 사거리 3
    Conestoga = 33,      // 무기, 사거리 1 (변형)
    RevCarabine = 34,    // 무기, 사거리 4
    TenGallonHat = 35,   // 장비. BANG 받기 전 자동 펴기 ♠이면 회피 (IronPlate와 별개 슬롯)
    Bibles = 36          // 장비. 본인 턴 시작 시 펴기 ♣이면 카드 1장 추가
}

public enum CardSuit
{
    Hearts = 0,    // ♥
    Diamonds = 1,  // ♦
    Clubs = 2,     // ♣
    Spades = 3     // ♠
}

public class BangCard
{
    public int Id { get; set; }                  // 게임 내 고유 ID (덱 내 인스턴스 구분)
    public BangCardType Type { get; set; }
    public CardSuit Suit { get; set; }
    public int Rank { get; set; }                // 1(A) ~ 13(K) — 펴기(draw!) 메커니즘용
}

public class BangPlayer
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BangRole Role { get; set; }
    public bool RoleRevealed { get; set; }       // 보안관만 공개, 나머지는 죽을 때 공개
    public BangCharacter Character { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public List<BangCard> Hand { get; set; } = new();
    public bool IsAlive => Health > 0;
    public int SeatIndex { get; set; }           // 좌석 인덱스 (거리 계산용)

    // 장착 — 무기 1개 (없으면 default 사거리 1, Colt45 취급), 장비 N개 (같은 종류 1장)
    public BangCard? Weapon { get; set; }
    public List<BangCard> Equipment { get; set; } = new();

    // AI/봇 플레이어 여부 — true면 서버가 자동으로 액션 처리
    public bool IsBot { get; set; }

    // 손패 장수 — 직렬화 뷰 전용 (다른 플레이어에게는 Hand 대신 이 값만 공개)
    public int HandCount { get; set; }

    /// <summary>viewer 관점의 직렬화용 뷰 — 본인이 아니면 손패 내용과 비공개 역할을 숨긴다.</summary>
    public BangPlayer CreateViewFor(string viewerPlayerId, bool gameFinished)
    {
        var view = (BangPlayer)MemberwiseClone();
        var hand = Hand ?? new List<BangCard>();
        view.HandCount = hand.Count;
        if (PlayerId != viewerPlayerId)
        {
            view.Hand = new List<BangCard>();
            // 역할은 비밀 정보 — 공개(보안관/사망) 전이면 상수로 마스킹. 게임 종료 시 전체 공개.
            if (!RoleRevealed && !gameFinished)
                view.Role = BangRole.Outlaw;
        }
        return view;
    }
}

public enum BangPendingType
{
    None = 0,
    BangResponse = 1,        // BANG 대상이 Missed 응답 대기
    GeneralStorePick = 2,    // 잡화점 카드 선택 대기 (시계방향 차례)
    EndTurnDiscard = 3,      // 턴 종료 시 손패 ≤ 체력 만들기
    CardPickFromTarget = 4,  // CatBalou/Panic 사용자가 대상 손패에서 1장 선택
    IndiansResponse = 5,     // Indians — 큐의 첫 사람이 BANG 응답 대기
    GatlingResponse = 6,     // Gatling — 큐의 첫 사람이 Missed 응답 대기
    DuelResponse = 7,        // Duel — TargetPlayerId가 BANG 응답 대기 (번갈아 던지기)
    PedroDraw = 8,           // Pedro Ramirez — 첫 드로우 옵션 선택
    JesseDraw = 9,           // Jesse Jones — 첫 드로우 옵션 선택 (다른 사람 손에서)
    KitDraw = 10,            // Kit Carlson — 3장 펴고 2장 선택
    LuckyDukeReveal = 11     // Lucky Duke — 2장 펴고 1장 직접 선택 (현재 BANG-Barrel만 지원)
}

public class BangPending
{
    public BangPendingType Type { get; set; }
    public string? TargetPlayerId { get; set; }  // 단일 응답해야 할 사람 (BangResponse 등)
    public string? SourcePlayerId { get; set; }  // 액션 발동자
    public int? CardId { get; set; }             // 관련 카드

    // CardPickFromTarget 전용 — 카드를 가져갈 것인지 (Panic) vs 버릴 것인지 (CatBalou)
    public bool PickToHand { get; set; }

    // Indians/Gatling 같은 다중 응답 — 응답해야 할 사람 큐 (시계방향)
    public List<string> ResponderQueue { get; set; } = new();

    // GeneralStorePick — 펴진 카드 풀
    public List<BangCard> RevealedCards { get; set; } = new();

    // BangResponse 전용 — Slab the Killer는 2장 필요 (기본 1)
    public int RequiredMissed { get; set; } = 1;

    // LuckyDukeReveal 전용 — "barrel_bang" 등 어떤 펴기인지
    public string? Context { get; set; }
}

// BangPlayer.IsBot은 BangPlayer 클래스에 추가 (별도 파일)

public class BangState
{
    public List<BangPlayer> Players { get; set; } = new();

    // 🔒 덱 순서는 비공개 (펴기/드로우 예측 방지) — 클라이언트는 DeckCount만 사용
    [System.Text.Json.Serialization.JsonIgnore]
    public List<BangCard> Deck { get; set; } = new();
    public int DeckCount => Deck.Count;

    public List<BangCard> DiscardPile { get; set; } = new();
    public string CurrentTurnPlayerId { get; set; } = string.Empty;

    // 현 턴 BANG 사용 횟수 (캐릭터 능력 없으면 1회 제한)
    public int BangsPlayedThisTurn { get; set; }

    // Dodge City — Doc Holyday: 본인 턴에 1회만 능력 사용 가능
    public bool DocAbilityUsedThisTurn { get; set; }

    // Lucky Duke이 직전 펴기 시 표시할 두 카드 (action 결과 메시지에 표시 후 클리어)
    [System.Text.Json.Serialization.JsonIgnore]
    public List<BangCard>? LastLuckyDukeReveal { get; set; }

    // 응답 대기 액션 (수동 응답 모달용 — MVP는 None 상태로만 작동, 다음 phase 활성화)
    public BangPending? Pending { get; set; }

    // 종료 상태
    public bool IsFinished { get; set; }
    public BangRole? WinnerRole { get; set; }
    public List<string> WinnerPlayerIds { get; set; } = new();
    public string? EndReason { get; set; }

    // 다음 카드 인스턴스 ID 발급용 카운터
    public int NextCardId { get; set; } = 1;

    // Dodge City 확장팩 사용 여부 — 방장이 게임 시작 전 토글, 시작 후 변경 불가
    public bool UseDodgeCity { get; set; } = false;

    // 현재 턴 시작 시각(UTC) — 턴 타임아웃 서버 검증용. StartTurnFor에서 갱신.
    public DateTime TurnStartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>viewer 관점의 직렬화용 뷰 — 다른 플레이어의 손패/비공개 역할을 마스킹.</summary>
    public BangState CreateViewFor(string viewerPlayerId)
    {
        var view = (BangState)MemberwiseClone();
        view.Players = Players.Select(p => p.CreateViewFor(viewerPlayerId, IsFinished)).ToList();

        // CatBalou/Panic/CanCan — 선택자는 대상 손패를 뒷면으로 보고 블라인드 선택한다.
        // Id만 실제 값으로 남기고 내용(Type/Suit/Rank)은 상수로 익명화 → 선택 가능 + 내용 비공개.
        if (Pending?.Type == BangPendingType.CardPickFromTarget
            && Pending.SourcePlayerId == viewerPlayerId
            && Pending.TargetPlayerId is string targetId && targetId != viewerPlayerId)
        {
            var maskedTarget = view.Players.FirstOrDefault(p => p.PlayerId == targetId);
            var realTarget = Players.FirstOrDefault(p => p.PlayerId == targetId);
            if (maskedTarget != null && realTarget != null)
                maskedTarget.Hand = realTarget.Hand
                    .Select(c => new BangCard { Id = c.Id, Type = BangCardType.Bang, Suit = CardSuit.Hearts, Rank = 0 })
                    .ToList();
        }
        return view;
    }
}
