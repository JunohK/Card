using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    /// <summary>
    /// 게임 시작 로직
    /// </summary>
    public void StartGame(string roomId, int maxRounds) 
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        if (room.IsStarted) return;
        if (room.Players.Count < 2)
            throw new InvalidOperationException("2명 이상이어야 게임을 시작할 수 있습니다.");

        room.MaxRounds = maxRounds;
        room.CurrentRound = 1;
        room.IsStarted = true;
        room.IsFinished = false;
        room.WinnerHand.Clear(); // 이전 우승자의 카드리스트 비움
        room.RoundScoreHistory.Clear(); // 라운드 점수 히스토리 초기화

        // STOP 관련 상태 초기화
        room.IsStopDeclared = false;
        room.StopCallerId = "";
        room.WinnerName = null;
        room.WinnerPlayerId = string.Empty;
        room.GameWinnerIds.Clear();
        room.GameWinnerName = null;
        room.StatsRecorded = false;

        // 전체 게임 시작 시에만 누적 점수 0으로 리셋
        foreach (var p in room.Players) {
            p.TotalScore = 0; 
        }

        SetupRound(room); // 라운드 세팅 호출
    }

    // 라운드 초기화 (덱 생성 -> 셔플 -> 5장씩 분배하며 덱에서 제거)
    private void SetupRound(GameRoom room)
    {
        var newDeck = CreateNewDeck(room.JokerCount, room.CardMode);
        room.Deck = newDeck.OrderBy(a => Guid.NewGuid()).ToList();
        
        // ✅ 라운드 시작 시 우승자 정보 리셋
        room.IsRoundEnded = false;
        room.IsStopDeclared = false;
        room.StopCallerId = "";
        ResetTurnState(room);
        room.WinnerName = null;
        room.WinnerPlayerId = null;
        room.WinnerHand.Clear(); // 라운드 시작 시 카드 리스트 초기화

        foreach (var p in room.Players)
        {
            p.Hand.Clear();
            p.IsWaitingFinalWin = false;
            p.Score = 0; // 이번 라운드 점수 초기화

            // 덱에서 직접 꺼내서 분배 (덱 수량 실시간 차감 반영)
            for (int i = 0; i < 5; i++)
            {
                if (room.Deck.Count > 0)
                {
                    var card = room.Deck[0];
                    p.Hand.Add(card);
                    room.Deck.RemoveAt(0); 
                }
            }
        }

        if (room.Players.Count > 0) room.CurrentTurnPlayerId = room.Players[0].PlayerId;
        room.TurnStartedAt = DateTime.UtcNow;  // 턴 타임아웃 기준 시각

        room.IsRoundEnded = false; // 전광판 닫기
        room.DiscardPile.Clear();
        room.LastDiscardedCard = null;
    }

    // 라운드 종료 시점에 각 플레이어의 이번 라운드 점수와 누적 점수를 히스토리에 기록
    private static void RecordRoundScore(GameRoom room)
    {
        room.RoundScoreHistory.Add(new RoundScoreEntry
        {
            Round = room.CurrentRound,
            WinnerName = room.WinnerName,
            WinType = room.LastWinType,
            Scores = room.Players.Select(p => new PlayerRoundScore
            {
                PlayerId = p.PlayerId,
                Name = p.Name,
                Score = p.Score,
                TotalScore = p.TotalScore
            }).ToList()
        });
    }

    // 다음 라운드로 완전히 넘어가는 로직
    public void StartNextRound(GameRoom room)
    {
        if (room == null || !room.IsRoundEnded || room.IsFinished) return;

        // 턴 설정을 위해 이전 라운드 승리자를 저장. 결과창 중에 나가서 방에 없으면 방장이 선
        string? nextFirstPlayer = room.WinnerPlayerId;
        if (!room.Players.Any(p => p.PlayerId == nextFirstPlayer))
            nextFirstPlayer = room.HostPlayerId;

        // 2. 라운드 번호 증가 및 '핵심' 상태 초기화
        room.CurrentRound++;      
        room.IsRoundEnded = false; 
        room.IsFinished = false;     // 혹시 true였다면 리셋
        
        // ✅ [가장 중요] STOP 선언 상태 초기화
        room.IsStopDeclared = false;
        room.StopCallerId = string.Empty;
        ResetTurnState(room);

        // ✅ 우승 데이터 초기화
        room.WinnerPlayerId = string.Empty;
        room.WinnerName = null;
        // room.WinnerHand.Clear(); // 이전 판 우승자 카드 리스트 비움
        
        room.LastDiscardedCard = null;
        room.DiscardPile.Clear();

        // 새 카드 분배 (CreateNewDeck은 덱을 섞어서 반환한다고 가정)
        var deck = CreateNewDeck(room.JokerCount, room.CardMode);
        foreach (var player in room.Players)
        {
            player.Hand = deck.Take(5).ToList(); 
            deck.RemoveRange(0, 5);
            player.Score = 0; // 새 라운드를 위해 라운드 점수만 초기화
        }
        room.Deck = deck;
        
        // 4. 승리한 사람이 다음 라운드 첫 턴 시작
        // 만약 첫 라운드거나 승리자 정보가 없으면 방장으로 설정
        room.CurrentTurnPlayerId = string.IsNullOrEmpty(nextFirstPlayer)
                                ? room.HostPlayerId
                                : nextFirstPlayer;
        room.TurnStartedAt = DateTime.UtcNow;  // 턴 타임아웃 기준 시각
    }

    private List<PlayingCard> CreateNewDeck(int jokerCount = 1, CardMode cardMode = CardMode.Trump)
    {
        var deck = new List<PlayingCard>();
        string[] suits = { "♠", "♥", "♦", "♣" };
        // Trump: 1~13 (A~K), Hwatu: 1~12 (K 제외)
        string[] ranks = cardMode == CardMode.Hwatu
            ? new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q" }
            : new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

        // 1. 일반 카드 생성 (Trump: 52장, Hwatu: 48장)
        foreach (var suit in suits)
        {
            foreach (var rank in ranks)
            {
                deck.Add(new PlayingCard
                {
                    Suit = suit,
                    Rank = rank,
                    Color = (suit == "♥" || suit == "♦") ? "Red" : "Black"
                });
            }
        }

        // 2. 조커는 방 옵션(0/1/2)에 따라 추가
        int clampedJokers = Math.Clamp(jokerCount, 0, 2);
        for (int i = 0; i < clampedJokers; i++)
        {
            deck.Add(new PlayingCard
            {
                Type = CardType.Joker,
                Suit = "Joker",
                Rank = "Joker",
                Color = "Black"
            });
        }

        // 덱 셔플 로직
        Random rnd = new Random();
        deck = deck.OrderBy(x => rnd.Next()).ToList();

        return deck;
    }

    // 전체 게임 종료 체크 공통 메서드
    private void CheckAndEndFullGame(GameRoom room)
    {
        if (room.CurrentRound >= room.MaxRounds)
        {
            room.IsFinished = true;
            room.IsStarted = false;
            SetGameWinners(room);
        }
    }

    // 게임 전체 승자 확정 — 총점 최저(동점이면 전원). excludePlayerId(기권자)는 후보에서 제외
    private static void SetGameWinners(GameRoom room, string? excludePlayerId = null)
    {
        var candidates = room.Players.Where(p => p.PlayerId != excludePlayerId).ToList();
        if (candidates.Count == 0) return;
        int best = candidates.Min(p => p.TotalScore);
        var winners = candidates.Where(p => p.TotalScore == best).ToList();
        room.GameWinnerIds = winners.Select(p => p.PlayerId).ToList();
        room.GameWinnerName = string.Join(", ", winners.Select(p => p.Name));
    }

}
