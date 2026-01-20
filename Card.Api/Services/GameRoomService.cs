using Card.Api.Domain;
using Card.Api.GameLogic;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public class GameRoomService
{
    // 🔑 멀티스레드 환경에서 안전한 방 저장소
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    /// <summary>
    /// 방 생성: 방 객체만 생성하고 저장소에 등록합니다.
    /// </summary>
    public GameRoom CreateRoom(string playerName, string title, string? password)
    {
        var roomId = Guid.NewGuid().ToString().Substring(0, 8);
        var room = new GameRoom
        {
            RoomId = roomId,
            Title = title,
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            Players = new List<Player>(), // 빈 상태로 생성하여 JoinRoom에서 처리
            IsStarted = false,
            CreatedAt = DateTime.UtcNow
        };

        _rooms.TryAdd(roomId, room);
        return room;
    }

    /// <summary>
    /// 방 입장: 중복 입장을 방지하기 위해 기존 유령 세션을 제거한 후 추가합니다.
    /// </summary>
    public GameRoom? JoinRoom(string roomId, string playerId, string playerName, string? password = null)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
            return null;

        lock (room) // 여러 명이 동시에 입장할 때 리스트 꼬임 방지
        {
            var nickname = playerName.Trim();

            // [핵심 해결] 기존에 같은 PlayerId 혹은 같은 이름을 가진 플레이어가 있다면 모두 제거
            room.Players.RemoveAll(p => p.PlayerId == playerId || p.Name == nickname);

            // 비밀번호 체크
            if (!string.IsNullOrWhiteSpace(room.Password) && room.Players.Count > 0)
            {
                if (room.Password != password)
                {
                    throw new Exception("비밀번호가 틀렸습니다.");
                }
            }

            // 인원 제한 체크
            if (room.Players.Count >= 7)
                throw new Exception("방 인원이 가득 찼습니다.");

            // 새로운 플레이어 객체 생성 및 추가
            var newPlayer = new Player
            {
                PlayerId = playerId,
                Name = nickname,
                Hand = new List<PlayingCard>()
            };
            room.Players.Add(newPlayer);

            // 첫 번째 입장객(또는 방장)에게 권한 부여
            if (string.IsNullOrEmpty(room.HostPlayerId) || room.Players.Count == 1)
            {
                room.HostPlayerId = playerId;
            }
        }

        return room;
    }

    /// <summary>
    /// 방 나가기: 인원이 0명이 되면 방을 완전히 삭제합니다.
    /// </summary>
    public bool LeaveRoom(string roomId, string playerId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return false;

        lock (room)
        {
            room.Players.RemoveAll(p => p.PlayerId == playerId);

            if (room.Players.Count == 0)
            {
                _rooms.TryRemove(roomId, out _);
                return true; 
            }

            if (room.HostPlayerId == playerId && room.Players.Count > 0)
            {
                room.HostPlayerId = room.Players[0].PlayerId;
            }
        }

        return false;
    }

    /// <summary>
    /// 게임 시작 로직
    /// </summary>
    public void StartGame(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        if (room.IsStarted) return;

        var newDeck = CreateNewDeck();
        room.Deck = newDeck.OrderBy(a => Guid.NewGuid()).ToList(); // 셔플(섞기)

        CardDealer.DealInitialHands(room.Players, room.Deck, 5);
        
        if (room.Players.Count > 0)
        {
            room.CurrentTurnPlayerId = room.Players[0].PlayerId;
        }

        room.IsStarted = true;
    }

    public GameRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public IEnumerable<GameRoom> GetRooms()
    {
        return _rooms.Values;
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }

    // --- 게임 진행 관련 내부 로직 (생략 없이 모두 포함) ---

    public void StartTurn(GameRoom room)
    {
        if (room.IsFinished) return;
        var currentPlayer = room.Players.FirstOrDefault(p => p.PlayerId == room.CurrentTurnPlayerId);
        if (currentPlayer != null) DrawCard(room, currentPlayer);
    }

    private void DrawCard(GameRoom room, Player player)
    {
        if (room.Deck.Count == 0) return;
        var card = room.Deck[0];
        room.Deck.RemoveAt(0);
        player.Hand.Add(card);
    }

    public void ActingMyTurn(GameRoom room, string playerId, TurnActionType actionType, List<int>? discardIndexes = null)
    {
        if (room.IsFinished) return;
        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return;

        switch (actionType)
        {
            case TurnActionType.DiscardOne:
                if (discardIndexes != null) DiscardOne(room, player, discardIndexes);
                break;
            case TurnActionType.DiscardPairAndOne:
                if (discardIndexes != null) DiscardPairAndOne(room, player, discardIndexes);
                break;
            case TurnActionType.DeclareWin:
                if (GameRule.CheckSixCardImmediateFinish(room, player))
                    DeclareWin(room, player, WinReason.SixCardImmediate);
                break;
        }
    }

    private bool DiscardOne(GameRoom room, Player player, List<int> indexes)
    {
        if (indexes.Count != 1) return false;
        DiscardCards(room, player, indexes);
        EndTurn(room);
        return true;
    }

    private bool DiscardPairAndOne(GameRoom room, Player player, List<int> indexes)
    {
        if (indexes.Count != 3) return false;
        var cards = indexes.Select(i => player.Hand[i]).ToList();
        if (!cards.GroupBy(c => c.Rank).Any(g => g.Count() == 2)) return false;

        DiscardCards(room, player, indexes);
        if (player.Hand.Count == 2 && player.Hand[0].Rank == player.Hand[1].Rank)
            player.IsWaitingFinalWin = true;

        EndTurn(room);
        return true;
    }

    public void DeclareWin(GameRoom room, Player winner, WinReason reason)
    {
        if (room == null || room.IsFinished) return;

        // 1. 승리 조건 체크 (2.1 ~ 2.6 로직 실행)
        var (isValid, winType, scoreValue) = CheckWinCondition(winner.Hand);

        // [참고] SixCardImmediate(사구) 같은 특수 케이스는 reason을 통해 들어옵니다.
        if (!isValid && reason != WinReason.SixCardImmediate)
        {
            throw new Exception("승리 조건을 만족하지 않습니다.");
        }

        // 2. 게임 상태 업데이트
        room.IsFinished = true;
        room.IsGameOver = true; // 프론트엔드 알림용
        room.WinnerPlayerId = winner.PlayerId;
        room.WinnerName = winner.Name;
        room.WinReason = reason;

        // 3. 점수 계산 및 정산
        foreach (var player in room.Players)
        {
            if (player.PlayerId == winner.PlayerId)
            {
                // 승리자는 감점 (winType에 따른 점수 사용)
                // 예: SixOfAKind면 -200점 등
                player.TotalScore += scoreValue; 
            }
            else
            {
                // 패배자는 핸드 점수 합산 (3장 이상 동일 카드 제외)
                player.TotalScore += CalculateLoserScore(player.Hand);
            }
        }
    }

    // 승리 조건 체크 핵심 로직
    private (bool isValid, string winType, int scoreValue) CheckWinCondition(List<PlayingCard> hand)
    {
        int jokerCount = hand.Count(c => c.Rank == "Joker");
        var normalCards = hand.Where(c => c.Rank != "Joker").ToList();
        var groups = normalCards.GroupBy(c => c.Rank).Select(g => g.Count()).OrderByDescending(c => c).ToList();

        int maxGroup = (groups.FirstOrDefault() + jokerCount);

        // 2.1 사구 (6장)
        if (maxGroup >= 6) return (true, "SixOfAKind", -200);

        // 2.2 4장 + 2장
        if (CanMakeGroups(hand, new[] { 4, 2 })) return (true, "FourAndTwo", -100);

        // 2.3 3장 + 3장
        if (CanMakeGroups(hand, new[] { 3, 3 })) return (true, "ThreeAndThree", -150);

        // 2.4 2장 + 2장 + 2장
        if (CanMakeGroups(hand, new[] { 2, 2, 2 })) return (true, "ThreePairs", -80);

        // 2.5 5장 (패가 5장일 때)
        if (maxGroup >= 5 && hand.Count <= 5) return (true, "FiveOfAKind", -60);

        // 기본 승리 (3장 등)
        if (maxGroup >= 3 && hand.Count <= 3) return (true, "NormalWin", -30);

        return (false, "None", 0);
    }

    // 도우미: 특정 조합(예: 4장, 2장)을 조커를 사용하여 만들 수 있는지 판별
    private bool CanMakeGroups(List<PlayingCard> hand, int[] required)
    {
        int jokers = hand.Count(c => c.Rank == "Joker");
        var counts = hand.Where(c => c.Rank != "Joker")
                        .GroupBy(c => c.Rank)
                        .Select(g => g.Count())
                        .OrderByDescending(c => c).ToList();

        // 간단한 그리디 알고리즘으로 조커 배분하여 조합 확인
        // (실제로는 더 정교한 최적화가 필요할 수 있으나 기본 룰 대응 가능)
        foreach (var req in required)
        {
            bool matched = false;
            for (int i = 0; i < counts.Count; i++)
            {
                if (counts[i] >= req) { counts[i] -= req; matched = true; break; }
                if (counts[i] + jokers >= req) { jokers -= (req - counts[i]); counts[i] = 0; matched = true; break; }
            }
            if (!matched && jokers >= req) { jokers -= req; matched = true; }
            if (!matched) return false;
        }
        return true;
    }

    // 승리자 감점 액수 정의
    private int CalculateWinnerScore(string winType)
    {
        return winType switch
        {
            "SixOfAKind" => -200,    // 6장 동일
            "FourAndTwo" => -100,    // 4장, 2장
            "ThreeAndThree" => -150, // 3장, 3장
            "ThreePairs" => -80,     // 2+2+2
            "FiveOfAKind" => -60,    // 5장 동일
            _ => -30
        };
    }

    // 패배자 점수 계산 (3장 이상 같은 숫자 제외)
    private int CalculateLoserScore(List<PlayingCard> hand)
    {
        var normalCards = hand.Where(c => c.Rank != "Joker").ToList();
        var groupCounts = normalCards.GroupBy(c => c.Rank).ToDictionary(g => g.Key, g => g.Count());

        int score = 0;
        foreach (var card in normalCards)
        {
            if (groupCounts[card.Rank] < 3) // 3장 미만인 카드만 점수 합산
            {
                score += GetRankValue(card.Rank);
            }
        }
        return score;
    }

    public void EndTurn(GameRoom room)
    {
        if (room.IsFinished) return;
        var currentIndex = room.Players.FindIndex(p => p.PlayerId == room.CurrentTurnPlayerId);
        if (currentIndex == -1) currentIndex = 0;

        var nextIndex = (currentIndex + 1) % room.Players.Count;
        room.CurrentTurnPlayerId = room.Players[nextIndex].PlayerId;
        StartTurn(room);
    }

    private void DiscardCards(GameRoom room, Player player, List<int> handIndexes)
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

    private List<PlayingCard> CreateNewDeck()
    {
        var deck = new List<PlayingCard>();
        string[] suits = { "♠", "♥", "♦", "♣" };
        // A(1)부터 K(13)까지 정확히 정의
        string[] ranks = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

        // 1. 일반 카드 생성 (4문양 * 13장 = 52장)
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

        // 2. 조커 딱 1장만 추가 (합계 53장)
        // 기존에 Joker1, Joker2를 넣는 루프가 있었다면 모두 지우고 이것만 남기세요.
        deck.Add(new PlayingCard 
        { 
            Suit = "Joker", 
            Rank = "Joker", 
            Color = "Black" 
        });

        return deck;
    }

    // GameRoomService.cs

// GameRoomService.cs 클래스 내부

    public GameRoom DrawCard(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId) 
            return room;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null || room.Deck.Count == 0) return room;

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

        var cardToPlay = player.Hand.FirstOrDefault(c => c.Suit == card.Suit && c.Rank == card.Rank);
        
        if (cardToPlay != null)
        {
            player.Hand.Remove(cardToPlay);
            room.LastDiscardedCard = cardToPlay;
            room.DiscardPile.Add(cardToPlay);
            
            // [수정] 카드 버린 후 턴 넘기기
            // 패가 2장(뽑기 전) -> 3장(뽑은 후) -> 2장(버린 후) 인 경우도 다음 턴으로
            int currentIndex = room.Players.FindIndex(p => p.PlayerId == playerId);
            int nextIndex = (currentIndex + 1) % room.Players.Count;
            room.CurrentTurnPlayerId = room.Players[nextIndex].PlayerId;
        }
        return room;
    }

    // 족보 체크 및 점수 계산 메서드 (GameRoomService 내부)
    public int CalculateFinalScore(List<PlayingCard> hand, bool isWinner)
    {
        // 조커를 제외한 숫자 리스트 (Joker는 어떤 숫자로든 변신 가능)
        var jokers = hand.Count(c => c.Rank == "Joker1" || c.Rank == "Joker2");
        var numbers = hand.Where(c => c.Rank != "Joker1" && c.Rank != "Joker2")
                        .Select(c => int.Parse(c.Rank)).OrderBy(n => n).ToList();

        if (isWinner) {
            // 승리자 감점 로직 (여기에 2.1 ~ 2.6 로직 구현)
            // 예: 2.2 (4장, 2장 같은 카드인 경우) -100점
            // 이 부분은 복잡한 조합 최적화 로직이 들어가야 하므로 승리 선언 시 별도 체크
            return 0; // 기본 반환값 (실제 로직은 승리 선언 시점에 처리)
        } else {
            // 패배자 점수 합산 로직
            // 같은 카드 3장 이상 제외 로직 포함
            var groups = numbers.GroupBy(n => n).Where(g => g.Count() < 3);
            int score = 0;
            foreach(var g in groups) score += g.Key * g.Count();
            return score;
        }
    }

    private int GetRankValue(string rank)
    {
        return rank switch
        {
            "A" => 1,
            "J" => 11,
            "Q" => 12,
            "K" => 13,
            "Joker" => 0, // 조커는 이미 위에서 제외했지만 안전을 위해 추가
            _ => int.TryParse(rank, out int val) ? val : 0
        };
    }

    public void GiveUpGame(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.IsFinished) return;

        var surrenderPlayer = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        
        // 게임 종료 상태로 변경
        room.IsFinished = true;
        room.IsStarted = false; // 게임 중 아님 상태로 변경
        
        // 기권자 정보를 기록하거나 승자를 임의 지정 (예: 남은 인원 중 첫 번째)
        room.WinnerName = $"{surrenderPlayer?.Name} 기권";
        room.WinReason = WinReason.ManualDeclare; // 기권 관련 Enum이 있다면 그것을 사용
    }
}