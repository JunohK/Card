// Cards 모드 방 상태 — 서버 GameRoom.CreateViewFor 뷰의 직렬화 모양 (SignalR JSON은 camelCase)
// 서버 원본: Card.Api/Domain/GameRoom.cs, Player.cs, PlayingCard.cs

export type CardView = {
    id: string;
    suit: string;      // ♠ ♥ ♦ ♣ / 조커는 "Joker"
    rank: string;      // A, 2~10, J, Q, K / 조커는 "Joker"
    color: string;     // "Red" | "Black"
    type: number;      // 0 = Normal, 1 = Joker
    value?: number | null;
};

export type WinOut = { rank: string; winType: string };

// 족보 힌트 — 서버가 본인 뷰에만 실어 준다 (6장: 지금 승리 가능 / 5장: 어떤 카드가 오면 승리)
export type WinHint = {
    isWinning: boolean;
    winType: string | null;
    score: number;
    outs: WinOut[];
};

export type PlayerView = {
    playerId: string;
    name: string;
    hand: CardView[];          // 상대 손패는 빈 배열 (마스킹)
    handCount: number;
    isBagaji: boolean;
    score: number;             // 이번 라운드 점수
    totalScore: number;        // 누적 점수
    allowNaturalBagaji: boolean;
    isBot: boolean;
    winHint: WinHint | null;
};

export type PlayerRoundScore = { playerId: string; name: string; score: number; totalScore: number };

export type RoundScoreEntry = {
    round: number;
    winnerName: string | null;
    winType: string | null;
    scores: PlayerRoundScore[];
};

export type CardRoomView = {
    roomId: string;
    hostPlayerId: string | null;
    players: PlayerView[];
    isStarted: boolean;
    isFinished: boolean;
    isRoundEnded: boolean;
    currentRound: number;
    maxRounds: number;
    cardMode: number;          // 0 = Trump, 1 = Hwatu
    deckCount: number;
    discardPile: CardView[];
    lastDiscardedCard: CardView | null;
    lastActorPlayerId: string;
    currentTurnPlayerId: string | null;
    turnTimeoutSec: number;
    isPungWindowOpen: boolean;
    isPungTurn: boolean;
    isFirstLapDone: boolean;
    winnerPlayerId: string | null;
    winnerName: string | null;
    winnerHand: CardView[];
    lastWinType: string | null;
    gameWinnerIds: string[];
    gameWinnerName: string | null;
    roundScoreHistory: RoundScoreEntry[];
};

// 화면 상태 — 서버 뷰 + 내 자연바가지 설정(NaturalBagajiToggled로 즉시 반영)
export type CardGameState = CardRoomView & { isNaturalBagajiEnabled: boolean };

export type MyProfile = {
    name: string;
    wins: number;
    totalGames: number;
    maxScore: number;
    minScore: number;
};
