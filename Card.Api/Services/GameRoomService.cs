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

        lock (room)
        {
            var nickname = playerName.Trim();

            // 🔴 [수정 핵심] 기존에 '이름'이 같은 플레이어가 있는지 먼저 찾습니다.
            var existingPlayer = room.Players.FirstOrDefault(p => p.Name == nickname);

            if (existingPlayer != null)
            {
                // 1. 새로고침한 유저라면 기존 객체의 PlayerId(ConnectionId)만 새 것으로 바꿉니다.
                // 이렇게 하면 리스트 순서가 유지되어 내 위치가 아래로 밀리지 않습니다.
                string oldId = existingPlayer.PlayerId;
                existingPlayer.PlayerId = playerId;

                // 2. 만약 이 사람이 방장이었다면, 방장 ID도 새 ID로 즉시 갱신합니다.
                if (room.HostPlayerId == oldId)
                {
                    room.HostPlayerId = playerId;
                }
            }
            else
            {
                // 완전히 처음 들어오는 유저인 경우에만 새로 추가합니다.
                if (room.Players.Count >= 7)
                    throw new Exception("방 인원이 가득 찼습니다.");

                var newPlayer = new Player
                {
                    PlayerId = playerId,
                    Name = nickname,
                    Hand = new List<PlayingCard>()
                };
                room.Players.Add(newPlayer);

                // 첫 번째 입장객에게 방장 권한 부여
                if (string.IsNullOrEmpty(room.HostPlayerId))
                {
                    room.HostPlayerId = playerId;
                }
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

        // 전체 게임 시작 시에만 누적 점수 0으로 리셋
        foreach (var p in room.Players) {
            p.TotalScore = 0; 
        }

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
            p.RoundTurnCount = 0; // 각 플레이어가 몇번쨰 턴인지 확인

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

    public void DeclareStop(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.IsFinished || room.IsRoundEnded) return;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return;

        // 턴 및 카드 수 검증 (3장 또는 6장일 때만 가능)
        // 사용자님의 기존 로직 스타일: 현재 턴인 플레이어가 카드를 뽑은 상태인지 확인
        if (room.CurrentTurnPlayerId == player.PlayerId && (player.Hand.Count == 3))
        {
            room.IsStopDeclared = true;
            room.StopCallerId = player.PlayerId;
            
            // 서비스에서는 상태만 변경합니다. 
            // 알림 메시지는 Hub에서 처리하거나 room 상태를 통해 클라이언트에 전달됩니다.
        }
    }

    // STOP 전용 종료 처리
    private void ApplyStopWin(GameRoom room, Player stopPlayer)
    {
        room.IsRoundEnded = true;
        room.WinnerPlayerId = stopPlayer.PlayerId;
        room.WinnerHand = new List<PlayingCard>(stopPlayer.Hand);

        // 1. STOP 선언자의 현재 패 점수 계산 (버린 후의 남은 패 점수)
        int stopPlayerScore = CalculateFinalScore(stopPlayer.Hand, false);

        // 2. 선언자 제외, 현재 패가 2장인 다른 플레이어들의 최소 점수 찾기
        var otherTwoCardScores = room.Players
            .Where(p => p.PlayerId != stopPlayer.PlayerId && p.Hand.Count == 2)
            .Select(p => CalculateFinalScore(p.Hand, false))
            .ToList();

        // 비교 대상(2장인 사람)이 있고, 내 점수가 그들 중 최솟값보다 크거나 같다면 '독박'
        bool isDokbak = otherTwoCardScores.Any() && stopPlayerScore >= otherTwoCardScores.Min();

        room.WinnerName = isDokbak ? $"{stopPlayer.Name} (STOP 실패)" : $"{stopPlayer.Name} (STOP 성공)";
        room.LastWinType = isDokbak ? "STOP 독박 (+50점)" : "STOP 성공 (0점)";

        foreach (var p in room.Players)
        {
            if (p.PlayerId == stopPlayer.PlayerId)
            {
                // 독박이면 본인 카드 합 + 50점, 성공이면 0점
                p.Score = isDokbak ? (stopPlayerScore + 50) : 0;
            }
            else
            {
                // 나머지 인원은 손에 든 만큼 계산
                p.Score = CalculateFinalScore(p.Hand, false);
            }
            p.TotalScore += p.Score;
        }

        // 상태 초기화
        room.IsStopDeclared = false;
        room.StopCallerId = "";

        CheckAndEndFullGame(room);
    }

    private void StartNewRound(GameRoom room)
    {
        // 덱 다시 생성 및 셔플
        room.Deck = CreateNewDeck(); 
        room.DiscardPile.Clear();
        room.IsRoundEnded = false;

        // 플레이어들에게 카드 분배 (예: 2장씩)
        foreach (var player in room.Players)
        {
            player.Hand.AddRange(room.Deck.Take(2));
            room.Deck.RemoveRange(0, 2);
        }
        
        // 첫 번째 플레이어 결정 및 카드 1장 더 주기
        var firstPlayer = room.Players[0];
        firstPlayer.Hand.Add(room.Deck[0]);
        room.Deck.RemoveAt(0);
        room.CurrentTurnPlayerId = firstPlayer.PlayerId;
    }

    // 다음 라운드로 완전히 넘어가는 로직
    public void StartNextRound(GameRoom room)
    {
        if (room == null || !room.IsRoundEnded || room.IsFinished) return;

        // 턴 설정을 위해 이전 라운드 승리자를 저장
        string nextFirstPlayer = room.WinnerPlayerId;

        // 라운드 번호 증가 및 상태 초기화
        room.CurrentRound++;      
        room.IsRoundEnded = false; 
        room.WinnerPlayerId = string.Empty;
        room.LastDiscardedCard = null;
        room.DiscardPile.Clear();

        // 새 카드 분배 (CreateNewDeck은 덱을 섞어서 반환한다고 가정)
        var deck = CreateNewDeck(); 
        foreach (var player in room.Players)
        {
            player.Hand = deck.Take(5).ToList(); 
            deck.RemoveRange(0, 5);
            player.Score = 0; // 새 라운드를 위해 라운드 점수만 초기화
            player.RoundTurnCount = 0;
        }
        room.Deck = deck;
        
        // 4. 승리한 사람이 다음 라운드 첫 턴 시작
        // 만약 첫 라운드거나 승리자 정보가 없으면 방장으로 설정
        room.CurrentTurnPlayerId = string.IsNullOrEmpty(nextFirstPlayer) 
                                ? room.HostPlayerId 
                                : nextFirstPlayer;
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
        if (hand == null || hand.Count < 6) return (false, "None", 0);

        // 조커 판정 기준 통일 (Rank가 Joker, JK, Joker1, Joker2 중 하나면 조커로 인정)
        int jokerCount = hand.Count(c => IsJoker(c));
        var normalCards = hand.Where(c => !IsJoker(c)).ToList();
        var sortedRanks = normalCards.Select(c => GetRankValue(c.Rank)).OrderBy(n => n).ToList();

        // 스트레이트 체크
        var (isStraight, straightSum) = GetStraightResult(sortedRanks, jokerCount);
        if (isStraight) return (true, "스트레이트", -straightSum);

        // HighSum 체크 (조커 = 13점)
        int totalHighSum = sortedRanks.Sum() + (jokerCount * 13);
        if (totalHighSum >= 68) return (true, "68-", -totalHighSum);

        // LowSum + (4+2)
        int LowSumGroup = sortedRanks.Sum() + (jokerCount * 1);
        if (LowSumGroup <= 10 && CanMakeGroups(hand, new[] { 4, 2 })) return (true, "200-", -200);
        
        // LowSum (조커 = 1점)
        int LowSum = sortedRanks.Sum() + (jokerCount * 1);
        if (LowSum <= 10) return ( true, "10-", -100);

        // 4장 + 2장 구성 (보상 -100점)
        if (CanMakeGroups(hand, new[] { 4, 2 })) return (true, "4 + 2", -100);

        // 3장 + 3장 구성 (보상 0점)
        if (CanMakeGroups(hand, new[] { 3, 3 })) return (true, "3 + 3", 0);

        // 2장 + 2장 + 2장 (보상 0점)
        if (CanMakeGroups(hand, new[] { 2, 2, 2 })) return (true, "2 + 2 + 2", 0);

        return (false, "None", 0);
    }

    // 스트레이트 여부와 "실제 완성된 숫자의 합"을 반환하는 보조 메서드
    private (bool isStraight, int sum) GetStraightResult(List<int> ranks, int jokers)
    {
        if (ranks.Count + jokers < 6) return (false, 0);
        
        var distinctRanks = ranks.Distinct().OrderBy(n => n).ToList();
        int maxStraightSum = 0;
        bool foundAnyStraight = false;

        // 가능한 모든 시작점 확인 (A(1)부터 K(13)까지)
        for (int start = 1; start <= 13 - 6 + 1; start++)
        {
            int usedJokers = 0;
            int currentSum = 0;
            bool possible = true;

            // 해당 구간(start ~ start+5)이 스트레이트가 가능한지 확인
            for (int i = 0; i < 6; i++)
            {
                int targetCard = start + i;
                if (distinctRanks.Contains(targetCard))
                {
                    currentSum += targetCard;
                }
                else
                {
                    if (usedJokers < jokers)
                    {
                        usedJokers++;
                        currentSum += targetCard; // 조커를 빠진 숫자로 사용
                    }
                    else
                    {
                        possible = false;
                        break;
                    }
                }
            }

            // 스트레이트가 가능하다면, 남은 여분의 조커가 있는지 확인
            if (possible)
            {
                foundAnyStraight = true;
                int remainingJokers = jokers - usedJokers;
                int tempSum = currentSum;

                // 🟢 조커가 유리하게 작용하도록 하는 핵심 로직:
                // 이미 구간 내에 내 손패(distinctRanks)가 있어서 조커를 안 쓰고 통과한 자리가 있다면,
                // 내 손패의 낮은 숫자를 빼고 남은 조커를 그 구간의 가장 높은 숫자로 치환하여 합을 높임.
                // 하지만 여기서는 "6장 구간"이 고정되어 있으므로, 
                // 만약 손패에 같은 숫자가 여러장 있거나 구간 외의 숫자가 있어도 스트레이트 합은 해당 구간의 합(start ~ start+5)이 됩니다.
                // 따라서 여러 구간이 가능할 경우(예: 조커가 많아서 1-6도 되고 7-12도 될 때) 가장 큰 합을 선택합니다.
                
                if (tempSum > maxStraightSum)
                {
                    maxStraightSum = tempSum;
                }
            }
        }

        if (foundAnyStraight)
        {
            return (true, maxStraightSum);
        }
        
        return (false, 0);
    }

    // 2.1 & 3.1 가로채기 체크 (다른 유저가 카드를 냈을 때 호출)
    // void를 Task로 바꾸고 async를 추가합니다. (기존 호출부에서 await만 붙여주면 됩니다)
    public async Task CheckInterception(GameRoom room, string cardOwnerId, PlayingCard playedCard)
    {
        foreach (var player in room.Players.Where(p => p.PlayerId != cardOwnerId))
        {
            // 1. 카드 2장 상태 가로채기
            if (player.Hand.Count == 2)
            {
                bool isWaiting = player.Hand.Any(c => c.Rank == "Joker" || c.Rank == "JK") || 
                                (player.Hand[0].Rank == player.Hand[1].Rank);

                if (isWaiting)
                {
                    bool canIntercept = player.Hand.Any(c => c.Rank == playedCard.Rank) || 
                                        IsJoker(playedCard);

                    if (canIntercept)
                    {
                        // 바가지는 즉시 종료. 1초 대기 후 강제 승리 처리
                        await Task.Delay(1000); 
                        ApplyBagajiWin(room, player, cardOwnerId);
                        return;
                    }
                }
            }
            
            // 2. 카드 5장 상태 가로채기
            if (player.Hand.Count == 5 && CanMakeGroups(player.Hand, new[] { 3, 2 }))
            {
                var counts = player.Hand.Where(c => c.Rank != "Joker" && c.Rank != "JK")
                                        .GroupBy(c => c.Rank)
                                        .ToDictionary(g => g.Key, g => g.Count());
                
                int jokers = player.Hand.Count(c => c.Rank == "Joker" || c.Rank == "JK");

                foreach (var rank in counts.Keys)
                {
                    if ((playedCard.Rank == rank || playedCard.Rank == "Joker") && (counts[rank] + jokers >= 3))
                    {
                        await Task.Delay(1000);
                        return;
                    }
                }
            }
        }
    }

    public (bool isBagajiWin, string winnerId, string loserId) CheckBagajiWin(string discardedRank, string discarderId, List<Player> allPlayers)
    {
        // 조커는 바가지 대상 카드가 될 수 없음
        if (discardedRank == "Joker" || discardedRank == "JK" || discardedRank == "JOKER") 
            return (false, null, null);

        foreach (var player in allPlayers)
        {
            // 본인이 버린 카드로 본인이 승리할 수는 없음
            if (player.PlayerId == discarderId) continue;

            var hand = player.Hand;
            // 바가지는 무조건 카드가 2장일 때만 성립
            // if (hand.Count != 2) continue;

            // 일반 바가지
            if(hand.Count == 2){
                bool hasTargetRank = hand.Any(c => c.Rank == discardedRank);
                bool isBagajiStatus = false;

                // 조건 1: 동일 숫자 2장 (그 중 하나가 방금 버려진 카드와 일치)
                if (hand.Count(c => c.Rank == hand[0].Rank) == 2 && hasTargetRank)
                {
                    isBagajiStatus = true;
                }
                // 조건 2: 숫자 1장 + 조커 (그 숫자가 방금 버려진 카드와 일치)
                else if (hand.Any(c => c.Rank == "Joker" || c.Rank == "JK" || c.Rank == "JOKER") && hasTargetRank)
                {
                    isBagajiStatus = true;
                }

                if (isBagajiStatus)
                {
                    // 🟢 바가지 승리 발생! 
                    // winnerId: 바가지를 들고 있던 사람
                    // loserId: 카드를 버려서 바가지를 씌우게 된 사람
                    return (true, player.PlayerId, discarderId);
                }
            }

            // 자연바가지
            else if(hand.Count == 5)
            {
                // 조커 제외 일반 카드
                var normalCards = hand.Where(c => c.Rank != "Joker" && c.Rank != "JK" && c.Rank != "JOKER").ToList();

                // 조커 개수 파악
                int jokerCount = hand.Count - normalCards.Count;

                // 숫자별 그룹화(조커제외)
                var groups = normalCards.GroupBy(c => c.Rank).ToDictionary(g => g.Key, g => g.Count());

                // 상대가 버린 카드가 내 패에 존재하는지 확인
                if(groups.ContainsKey(discardedRank))
                {
                    bool isWin = false;

                    // 1. 조커가 없을 때 3장(A) + 2장(B) 구성이고 상대가 B를 냈을 때
                    if(jokerCount == 0 && groups.Count == 2)
                    {
                        if(groups[discardedRank] == 2) isWin = true;
                    }

                    // 2. 조커가 1장일 때 2장(A) + 2장(B)인 경우 상대가 A나 B 둘 중 하나를 냈을 때
                    else if(jokerCount == 1 && groups.Count == 2)
                    {
                        if(groups[discardedRank] == 2) isWin = true;
                    }

                    if(isWin) return (true, player.PlayerId, discarderId);
                }
            }
        }

        return (false, null, null);
    }

    public void ProcessBagajiGameOver(Player winner, Player loser, List<Player> allPlayers)
    {
        foreach (var player in allPlayers)
        {
            if (player.PlayerId == winner.PlayerId)
            {
                // 🟢 승자 (바가지를 들고 기다리던 사람)
                player.Score = 0; 
            }
            else if (player.PlayerId == loser.PlayerId)
            {
                // 🔴 패자 (카드를 버린 사람)
                // 본인 손패 점수 + 30점 벌점
                int handScore = CalculateLoserScore(player.Hand);
                player.Score = handScore + 30; 
            }
            else
            {
                // 그 외 나머지 인원: 본인 패 점수대로 벌점
                player.Score = CalculateLoserScore(player.Hand);
            }
        }
    }

    // 바가지 전용 종료 처리
    private void ApplyBagajiWin(GameRoom room, Player winner, string loserId)
    {
        room.IsRoundEnded = true;
        room.WinnerPlayerId = winner.PlayerId;
        room.WinnerName = $"{winner.Name} (바가지)";

        room.WinnerHand = new List<PlayingCard>(winner.Hand);

        foreach (var p in room.Players)
        {
            // 1. 승자 판별
            if (p.PlayerId == winner.PlayerId)
            {
                p.Score = 0;
            }
            // 2. 패자 판별 (자연바가지를 당한 사람: loserId)
            // ID 비교 시 공백이나 대소문자 이슈 방지를 위해 Trim() 사용
            else if (!string.IsNullOrEmpty(loserId) && p.PlayerId.Trim().Equals(loserId.Trim()))
            {
                // CalculateLoserScore를 사용하여 3장 세트 제외 후 점수 계산 + 독박 벌점 30점
                int handScore = CalculateLoserScore(p.Hand);
                p.Score = handScore + 30;
            }
            // 3. 나머지 인원
            else
            {
                p.Score = CalculateLoserScore(p.Hand);
            }

            // 🔴 중요: 실질적인 누적 점수에 합산하여 전광판에 반영
            p.TotalScore += p.Score;
        }

        CheckAndEndFullGame(room);
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

    // 플레이어의 패가 2장일 때 세트(Pair)인지 판별하는 로직
    public bool IsWaitingWinCondition(Player player)
    {
        if (player.Hand.Count != 2) return false;

        // 조커가 1장이라도 포함되어 있다면, 나머지 1장이 무엇이든 "같은 카드 2장"으로 간주
        bool hasJoker = player.Hand.Last().Rank == "Joker" || player.Hand.Last().Rank == "JK" || 
                        player.Hand.First().Rank == "Joker" || player.Hand.First().Rank == "JK";

        // 조커가 없더라도 두 카드의 숫자가 같으면 세트
        bool isSameRank = player.Hand[0].Rank == player.Hand[1].Rank;

        return hasJoker || isSameRank;
    }

    private bool IsJoker(PlayingCard card)
    {
        if (card == null || string.IsNullOrEmpty(card.Rank)) return false;
        string r = card.Rank.ToUpper();
        return r == "JOKER" || r == "JK" || r == "JOKER1" || r == "JOKER2";
    }

    // 도우미: 특정 조합(예: 4장, 2장)을 조커를 사용하여 만들 수 있는지 판별

    private bool CanMakeGroups(List<PlayingCard> hand, int[] required)
    {
        int totalJokers = hand.Count(c => IsJoker(c));
        // 숫별로 몇 장씩 있는지 카운트 (예: 2가 4장이면 counts는 [4])
        var counts = hand.Where(c => !IsJoker(c))
                        .GroupBy(c => c.Rank)
                        .Select(g => g.Count())
                        .ToList();

        // 조커 배분 최적화 로직 호출
        return CheckCombinationRecursive(counts, totalJokers, required.OrderByDescending(r => r).ToList());
    }

    private bool CheckCombinationRecursive(List<int> counts, int jokers, List<int> required)
    {
        // 모든 그룹을 다 만들었으면 성공
        if (required.Count == 0) return true;

        int target = required[0]; // 이번에 만들어야 할 목표 장수 (예: 4장)
        var remainingRequired = required.Skip(1).ToList();

        // 방법 1: 기존에 있는 숫자 그룹(counts) 중 하나를 선택해 조커를 보태서 target을 만듦
        for (int i = 0; i < counts.Count; i++)
        {
            int has = counts[i];
            int need = target - has;

            // 이미 가지고 있는 장수가 목표보다 많아도(예: 4장 필요한데 5장 있음) 
            // 족보 구성은 가능하므로 0개 필요로 처리
            int actualNeed = Math.Max(0, need);

            if (jokers >= actualNeed)
            {
                var nextCounts = new List<int>(counts);
                nextCounts.RemoveAt(i);
                if (CheckCombinationRecursive(nextCounts, jokers - actualNeed, remainingRequired))
                    return true;
            }
        }

        // 방법 2: 쌩판 새로운 그룹을 오직 조커만으로 만듦 (예: 조커가 2장일 때 '2장 그룹' 생성)
        if (jokers >= target)
        {
            if (CheckCombinationRecursive(new List<int>(counts), jokers - target, remainingRequired))
                return true;
        }

        return false;
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
    // private int CalculateWinnerScore(string winType)
    // {
    //     return winType switch
    //     {
    //         "SixOfAKind" => -200,    // 6장 동일
    //         "FourAndTwo" => -100,    // 4장, 2장
    //         "ThreeAndThree" => 0, // 3장, 3장
    //         "ThreePairs" => 0,     // 2+2+2
    //         "FiveOfAKind" => -60,    // 5장 동일
    //         _ => -30
    //     };
    // }

    // 패배자 점수 계산 (3장 이상 같은 숫자 제외)
    public int CalculateLoserScore(List<PlayingCard> hand)
    {
        // 1. 조커 개수 확인 및 일반 카드 그룹화
        int jokerCount = hand.Count(c => c.Rank == "Joker" || c.Rank == "JK" || c.Rank == "JOKER");
        var normalCards = hand.Where(c => !(c.Rank == "Joker" || c.Rank == "JK" || c.Rank == "JOKER")).ToList();
        
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

            // 1장인데 조커가 2장 있다면? (조커가 여러장이 될 경우)
            // if (count == 1 && remainingJokers >= 2)
            // {
            //     remainingJokers -= 2;
            //     continue;
            // }

            // 세트를 만들지 못한 나머지 카드들만 점수 합산
            totalScore += (group.Value * count);
        }

        // 사용되지 않고 남은 조커가 있다면? (조커 단독 점수)
        // 룰에 따라 0점 혹은 특정 점수 가산 (현재는 0점 처리)
        if (remainingJokers > 0)
        {
            totalScore += (remainingJokers * 1); // 패에 2장 남은 경우 조커 1점으로 계산
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

        // 덱 셔플 로직
        Random rnd = new Random();
        deck = deck.OrderBy(x => rnd.Next()).ToList();

        return deck;
    }

    public GameRoom DrawCard(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || !room.IsStarted || room.IsFinished || room.CurrentTurnPlayerId != playerId) 
            return room;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null || room.Deck.Count == 0) return room;

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
            (c.Rank == "Joker" && card.Rank == "Joker"));
        
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
                    ApplyBagajiWin(room, winner, result.loserId);
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

    public void CompleteGame(string roomId)
    {
        var room = GetRoom(roomId);
        if (room == null) return;

        // 🔴 핵심: IsStarted를 false로 바꿔야 클라이언트가 RoomPage로 전환될 근거가 생깁니다.
        room.IsStarted = false; 
        room.IsFinished = false; 
        
        // 게임 데이터 초기화 (대기실로 돌아가기 위해 패와 라운드 점수 비움)
        foreach (var p in room.Players)
        {
            p.Hand.Clear();
            p.Score = 0; 
        }
        
        room.CurrentRound = 1; // 라운드 초기화
        room.WinnerName = string.Empty;
    }

    public void GiveUpGame(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.IsFinished) return;

        var surrenderPlayer = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        
        // 기권 시 전광판만 띄움 (IsStarted는 유지하여 아직 GamePage에 머물게 함)
        room.IsFinished = true;
        room.WinnerName = surrenderPlayer != null ? $"{surrenderPlayer.Name} 기권" : "게임 종료";
        
        // 요청하신 대로 패 점수 계산 없이 현재 TotalScore 유지
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

    // 전체 게임 종료 체크 공통 메서드
    private void CheckAndEndFullGame(GameRoom room)
    {
        if (room.CurrentRound >= room.MaxRounds)
        {
            room.IsFinished = true;
            room.IsStarted = false;
        }
    }

    // 플레이어 점수 업데이트 로직
    private void UpdatePlayerStats(GameRoom room)
    {
        // 이번 라운드 승자 - 점수가 가장 낮은 사람
        var roundWinner = room.Players.OrderBy(p => p.Score).First();

        foreach(var p in room.Players)
        {
            p.TotalGames += 1; // 판수 증가
            if(p.PlayerId == roundWinner.PlayerId) p.Wins += 1; // 승리 횟수 증가

            // 최고 점수 기록 업데이트 (기존보다 높으면 갱신)
            if (p.TotalScore > p.MaxScore) p.MaxScore = p.TotalScore;

            // 최저 점수 기록 업데이트 (기존보다 낮으면 갱신)
            if (p.TotalScore < p.MinScore) p.MinScore = p.TotalScore;
        }
    }
}