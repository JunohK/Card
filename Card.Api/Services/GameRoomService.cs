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
    /// 방 입장: 중복 입장을 방지하기 위해 기존 유령 세션을 제거한 후 추가합 니다.
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
    public void StartGame(string roomId, int maxRounds) 
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        if (room.IsStarted) return;

        room.MaxRounds = maxRounds;
        room.CurrentRound = 1;
        room.IsStarted = true;
        room.IsFinished = false;

        SetupRound(room); // 라운드 세팅 호출
    }

    // 라운드 초기화 (덱 생성 -> 셔플 -> 5장씩 분배하며 덱에서 제거)
    private void SetupRound(GameRoom room)
    {
        var newDeck = CreateNewDeck();
        room.Deck = newDeck.OrderBy(a => Guid.NewGuid()).ToList();
        
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
        
        room.IsRoundEnded = false; // 전광판 닫기
        room.DiscardPile.Clear();
        room.LastDiscardedCard = null;
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

    // 수정된 승리 선언 (족보 검증 포함)
    public bool DeclareWin(GameRoom room, Player winner, WinReason reason)
    {
        // 이미 종료 처리 중이면 중복 실행 방지
        if (room == null || room.IsFinished || room.IsRoundEnded) return false;

        var check = CheckWinCondition(winner.Hand);
        if (reason == WinReason.ManualDeclare && !check.isValid) return false;

        // 1. 라운드 종료 상태로 변경 (클라이언트가 이 값을 보고 결과창을 띄워야 함)
        room.IsRoundEnded = true; 
        room.WinnerPlayerId = winner.PlayerId;
        room.WinnerName = winner.Name;
        room.LastWinType = check.winType;

        // 2. 점수 계산 및 누계 반영
        foreach (var player in room.Players)
        {
            int roundScore = (player.PlayerId == winner.PlayerId) 
                ? check.scoreValue 
                : CalculateLoserScore(player.Hand);
                
            player.Score = roundScore;
            player.TotalScore += roundScore;
        }

        // 3. 전체 게임 종료 체크 (MaxRounds 도달 시)
        if (room.CurrentRound >= room.MaxRounds)
        {
            room.IsFinished = true;
            room.IsStarted = false;
        }

        return true; 
    }

    // 다음 라운드로 완전히 넘어가는 로직
    public void StartNextRound(GameRoom room)
    {
        if (room == null || !room.IsRoundEnded || room.IsFinished) return;

        // 4. 라운드 번호 증가 및 상태 초기화
        room.CurrentRound++;      
        room.IsRoundEnded = false; 
        room.WinnerPlayerId = string.Empty;
        room.LastDiscardedCard = null;
        room.DiscardPile.Clear();

        // 5. 새 카드 분배 (CreateNewDeck은 덱을 섞어서 반환한다고 가정)
        var deck = CreateNewDeck(); 
        foreach (var player in room.Players)
        {
            player.Hand = deck.Take(5).ToList(); 
            deck.RemoveRange(0, 5);
            player.Score = 0; // 새 라운드를 위해 라운드 점수만 초기화
        }
        room.Deck = deck;
        
        // 승리한 사람이 다음 라운드 첫 턴을 시작 (선택 사항)
        room.CurrentTurnPlayerId = room.WinnerPlayerId; 
    }

// 다음 라운드를 위해 카드와 덱만 초기화하는 메서드
private void PrepareNextRound(GameRoom room)
{
    var newDeck = CreateNewDeck();
    room.Deck = newDeck.OrderBy(a => Guid.NewGuid()).ToList();
    
    foreach (var p in room.Players)
    {
        p.Hand.Clear();
        p.IsWaitingFinalWin = false;
    }

    CardDealer.DealInitialHands(room.Players, room.Deck, 5);
    room.CurrentTurnPlayerId = room.Players[0].PlayerId;
    room.LastDiscardedCard = null;
    room.DiscardPile.Clear();
    // IsStarted는 true 유지, IsFinished는 false 유지
}

    // 승리 조건 체크 핵심 로직
    // 룰에 따른 승리 조건 체크 (패가 6장일 때 호출)
    public (bool isValid, string winType, int scoreValue) CheckWinCondition(List<PlayingCard> hand)
    {
        if (hand.Count < 6) return (false, "None", 0);

        // 조커(JK) 포함 개수 파악
        int jokerCount = hand.Count(c => c.Rank == "Joker" || c.Rank == "JK");
        var normalCards = hand.Where(c => c.Rank != "Joker" && c.Rank != "JK").ToList();
        var sortedRanks = normalCards.Select(c => GetRankValue(c.Rank)).OrderBy(n => n).ToList();
        
        int totalSum = sortedRanks.Sum();

        // 1. 합계 65점 이상: 보상으로 합계만큼 감점
        if (totalSum >= 65) return (true, "HighSum", -totalSum);

        // 2. 스트레이트: 보상으로 합계만큼 감점
        if (IsStraight(sortedRanks, jokerCount)) return (true, "Straight", -totalSum);

        // 3. 4장 + 2장: 요청하신 대로 -100점 보상 (점수가 크게 낮아짐)
        if (CanMakeGroups(hand, new[] { 4, 2 })) return (true, "FourAndTwo", -100);

        // 4. 3장 + 3장: 0점 (유지)
        if (CanMakeGroups(hand, new[] { 3, 3 })) return (true, "ThreeAndThree", 0);

        // 5. 2장 + 2장 + 2장: 0점 (유지)
        if (CanMakeGroups(hand, new[] { 2, 2, 2 })) return (true, "ThreePairs", 0);

        return (false, "None", 0);
    }

    // 스트레이트 판정 보조 (조커 포함)
    private bool IsStraight(List<int> ranks, int jokers)
    {
        if (ranks.Count + jokers < 6) return false;
        var distinctRanks = ranks.Distinct().ToList();
        if (distinctRanks.Count + jokers < 6) return false;

        for (int start = distinctRanks.Min(); start <= distinctRanks.Max() - 5 + jokers; start++)
        {
            int matchCount = 0;
            for (int i = 0; i < 6; i++)
            {
                if (distinctRanks.Contains(start + i)) matchCount++;
            }
            if (matchCount + jokers >= 6) return true;
        }
        return false;
    }

    // 2.1 & 3.1 가로채기 체크 (다른 유저가 카드를 냈을 때 호출)
    public void CheckInterception(GameRoom room, string cardOwnerId, PlayingCard playedCard)
    {
        foreach (var player in room.Players.Where(p => p.PlayerId != cardOwnerId))
        {
            // 3.1 카드 2장 상태에서 가로채기 (같은 카드 2장 대기 중일 때)
            if (player.Hand.Count == 2 && player.Hand.All(c => c.Rank == player.Hand[0].Rank || c.Rank == "Joker"))
            {
                if (player.Hand.Any(c => c.Rank == playedCard.Rank) || playedCard.Rank == "Joker")
                {
                    ApplyInterceptionWin(room, player, cardOwnerId, 30);
                    return;
                }
            }
            
            // 2.1 카드 5장 상태 (3장+2장 보유 중일 때)
            if (player.Hand.Count == 5 && CanMakeGroups(player.Hand, new[] { 3, 2 }))
            {
                // 내가 3장 가진 카드와 같은 카드를 남이 냈을 때
                var threeRank = player.Hand.GroupBy(c => c.Rank).FirstOrDefault(g => g.Count() >= 3)?.Key;
                if (playedCard.Rank == threeRank)
                {
                    ApplyInterceptionWin(room, player, cardOwnerId, 30);
                    return;
                }
            }
        }
    }

    private void ApplyInterceptionWin(GameRoom room, Player winner, string loserId, int penalty)
    {
        room.IsRoundEnded = true;
        room.WinnerName = winner.Name;
        winner.Score = 0;
        
        var loser = room.Players.First(p => p.PlayerId == loserId);
        loser.Score = CalculateLoserScore(loser.Hand) + penalty;
        
        // 나머지 인원 점수 계산 후 라운드 종료
    }

    // 도우미: 특정 조합(예: 4장, 2장)을 조커를 사용하여 만들 수 있는지 판별
    private bool CanMakeGroups(List<PlayingCard> hand, int[] required)
    {
        int totalJokers = hand.Count(c => c.Rank == "Joker");
        var counts = hand.Where(c => c.Rank != "Joker")
                        .GroupBy(c => c.Rank)
                        .Select(g => g.Count())
                        .OrderByDescending(c => c)
                        .ToList();

        // 가능한 모든 조합을 시도해보기 위해 재귀적으로 체크하거나, 
        // 현재 룰(최대 6장)에 맞춰 최적화된 로직을 사용합니다.
        return CheckCombination(counts, totalJokers, required.ToList());
    }

    private bool CheckCombination(List<int> counts, int jokers, List<int> required)
    {
        if (required.Count == 0) return true;

        int target = required[0];
        var remainingRequired = required.Skip(1).ToList();

        // 1. 기존 숫자에 조커를 보태서 타겟 그룹을 만드는 경우
        for (int i = 0; i < counts.Count; i++)
        {
            int need = Math.Max(0, target - counts[i]);
            if (jokers >= need)
            {
                var nextCounts = new List<int>(counts);
                nextCounts.RemoveAt(i);
                if (CheckCombination(nextCounts, jokers - need, remainingRequired))
                    return true;
            }
        }

        // 2. 조커만으로 타겟 그룹을 만드는 경우
        if (jokers >= target)
        {
            if (CheckCombination(new List<int>(counts), jokers - target, remainingRequired))
                return true;
        }

        return false;
    }

    // 승리자 감점 액수 정의
    private int CalculateWinnerScore(string winType)
    {
        return winType switch
        {
            "SixOfAKind" => -200,    // 6장 동일
            "FourAndTwo" => -100,    // 4장, 2장
            "ThreeAndThree" => 0, // 3장, 3장
            "ThreePairs" => 0,     // 2+2+2
            "FiveOfAKind" => -60,    // 5장 동일
            _ => -30
        };
    }

    // 패배자 점수 계산 (3장 이상 같은 숫자 제외)
    public int CalculateLoserScore(List<PlayingCard> hand)
    {
        // 1. 조커 개수 확인 및 일반 카드 그룹화
        int jokerCount = hand.Count(c => c.Rank == "Joker");
        var normalCards = hand.Where(c => c.Rank != "Joker").ToList();
        
        // 숫별로 장수 카운트
        var groups = normalCards.GroupBy(c => c.Rank)
                                .Select(g => new { 
                                    Rank = g.Key, 
                                    Count = g.Count(), 
                                    Value = GetRankValue(g.Key) 
                                })
                                .OrderByDescending(g => g.Value) // 높은 점수부터 지우기 위해 정렬
                                .ToList();

        int totalScore = 0;
        int remainingJokers = jokerCount;

        foreach (var group in groups)
        {
            int count = group.Count;

            // 이미 3장 이상인 경우 (3장 또는 4장) -> 자동으로 0점 처리 (패스)
            if (count >= 3) continue;

            // 2장인데 조커가 있다면? -> 조커 1장을 써서 3장 세트로 만듦 (0점 처리)
            if (count == 2 && remainingJokers >= 1)
            {
                remainingJokers -= 1;
                continue; 
            }

            // 1장인데 조커가 2장 있다면? (이 게임은 조커가 1장이므로 실제로는 불가능하지만 로직상 추가)
            if (count == 1 && remainingJokers >= 2)
            {
                remainingJokers -= 2;
                continue;
            }

            // 세트를 만들지 못한 나머지 카드들만 점수 합산
            totalScore += (group.Value * count);
        }

        // 사용되지 않고 남은 조커가 있다면? (조커 단독 점수)
        // 룰에 따라 0점 혹은 특정 점수 가산 (현재는 0점 처리)
        if (remainingJokers > 0)
        {
            // totalScore += (remainingJokers * 15); // 예: 조커 장당 15점 벌칙 시
        }

        return totalScore;
    }

    // 바가지
    public void DeclareInterceptionWin(GameRoom room, Player winner, string loserId)
    {
        if (room == null || room.IsFinished || room.IsRoundEnded) return;

        room.IsRoundEnded = true;
        room.WinnerPlayerId = winner.PlayerId;
        room.WinnerName = winner.Name;
        room.WinReason = WinReason.ManualDeclare; // 가로채기용 Enum이 있다면 그것 사용

        foreach (var player in room.Players)
        {
            if (player.PlayerId == winner.PlayerId)
            {
                // 가로채기 성공한 사람 (2.1, 3.1 룰 공통)
                player.Score = 0; 
            }
            else if (player.PlayerId == loserId)
            {
                // 카드를 내서 가로채기 당한 사람 (방출자)
                // 기본 패 점수 + 벌점 30점
                int handScore = CalculateLoserScore(player.Hand);
                player.Score = handScore + 30;
            }
            else
            {
                // 나머지 일반 패배자들
                player.Score = CalculateLoserScore(player.Hand);
            }

            player.TotalScore += player.Score;
        }

        // 라운드 종료 및 전체 게임 종료 체크
        if (room.CurrentRound >= room.MaxRounds)
        {
            room.IsFinished = true;
            room.IsStarted = false;
        }
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
            
            // 마지막 카드 확인 - 바가지 확인
            room.LastActorPlayerId = playerId;

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