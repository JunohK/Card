using Card.Api.Domain;
using Card.Api.GameLogic;
using System.Collections.Concurrent;

namespace Card.Api.Services;

/// <summary>
/// 게임 방 및 게임 상태 관리 서비스
/// </summary>
public class GameRoomService
{
    // 🔑 멀티스레드 안전한 방 저장소
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    /// <summary>
    /// 방 생성
    /// </summary>
    public GameRoom CreateRoom(
        string playerName,
        string title,
        string? password
    )
    {
        var room = new GameRoom
        {
            Title = title,
            Password = string.IsNullOrWhiteSpace(password) ? null : password
        };

        var host = new Player
        {
            Name = playerName
        };

        room.Players.Add(host);

        _rooms[room.RoomId] = room;
        return room;
    }

    /// <summary>
    /// 방 입장
    /// </summary>
    public GameRoom? JoinRoom(
        string roomId,
        string playerName,
        string? password = null)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
            return null;

        // 비밀번호 검사
        if (!string.IsNullOrWhiteSpace(room.Password))
        {
            if(room.Password != password)
                throw new Exception("비밀번호가 틀렸습니다.");
        }

        // 최대 7명 제한
        if (room.Players.Count >= 7)
            return null;

        // 중복 이름 방지
        if (room.Players.Any(p => p.Name == playerName))
            return null;

        room.Players.Add(new Player
        {
            Name = playerName
        });

        return room;
    }

    /// <summary>
    /// 게임 시작
    /// </summary>
    public void StartGame(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
            return;

        if (room.IsStarted)
            return;

        // 덱 생성 + 셔플
        room.Deck = DeckFactory.CreateShuffledDeck();

        // 카드 분배 (각자 5장)
        CardDealer.DealInitialHands(
            room.Players,
            room.Deck,
            // 각자 손에 들어가는 패
            cardsPerPlayer: 5
        );

        room.IsStarted = true;
    }

    /// <summary>
    /// 방 조회
    /// </summary>
    public GameRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    /// <summary>
    /// 카드 뽑기
    /// </summary>
    public void StartTurn(GameRoom room)
    {
        if (room.IsFinished)
            return;

        var currentPlayer = room.Players
            .First(p => p.PlayerId == room.CurrentTurnPlayerId);

        // 턴 시작 시 카드 1장 지급
        DrawCard(room, currentPlayer);
    }

    /// <summary>
    /// 카드 뽑기
    /// </summary>
    private void DrawCard(GameRoom room, Player player)
    {
        // 덱이 비어있으면 아무것도 하지 않음
        if (room.Deck.Count == 0)
            return;

        var card = room.Deck[0];
        room.Deck.RemoveAt(0);

        player.Hand.Add(card);
    }

    /// <summary>
    /// 내 턴에 수행하는 행동
    /// UI 버튼에서 ActionType을 명확히 전달한다.
    /// </summary>
    public void ActingMyTurn(
        GameRoom room,
        string playerId,
        TurnActionType actionType,
        List<int>? discardIndexes = null)
    {
        // 게임 종료 상태면 아무 행동 불가
        if (room.IsFinished)
            return;

        var player = room.Players
            .First(p => p.PlayerId == playerId);

        switch (actionType)
        {
            // =====================================
            // 1️⃣ 카드 1장 버리기
            // =====================================
            case TurnActionType.DiscardOne:
                if (discardIndexes == null)
                    return;

                DiscardOne(room, player, discardIndexes);
                break;

            // =====================================
            // 2️⃣ 같은 카드 2장 + 1장 버리기
            // =====================================
            case TurnActionType.DiscardPairAndOne:
                if (discardIndexes == null)
                    return;

                DiscardPairAndOne(room, player, discardIndexes);
                break;

            // =====================================
            // 3️⃣ 게임 종료 선언 버튼
            // (6장 즉시 종료 전용)
            // =====================================
            case TurnActionType.DeclareWin:

                // Rule에서 점수 계산 + 종료 가능 여부 판정
                if (GameRule.CheckSixCardImmediateFinish(room, player))
                {
                    DeclareWin(
                        room,
                        player,
                        WinReason.SixCardImmediate
                    );
                }
                break;
        }
    }

    /// <summary>
    /// 카드 1장 버리기
    /// </summary>
    private bool DiscardOne(GameRoom room, Player player, List<int> indexes)
    {
        if(indexes.Count != 1)
            return false;

        DiscardCards(room, player, indexes);

        EndTurn(room);
        return true;
    }

    /// <summary>
    /// 같은 카드 2장 + 1장 버리기
    /// </summary>
    private bool DiscardPairAndOne(GameRoom room, Player player, List<int> indexes)
    {
        if(indexes.Count != 3)
            return false;

        var cards = indexes.Select(i => player.Hand[i]).ToList();

        // 같은 Rank 2장 검증
        var groups = cards.GroupBy(c => c.Rank).ToList();
        if (!groups.Any(g => g.Count() == 2))
            return false;

        DiscardCards(room, player, indexes);

        // 손에 2장만 남고 같은 카드
        if (player.Hand.Count == 2 &&
            player.Hand[0].Rank == player.Hand[1].Rank)
        {
            player.IsWaitingFinalWin = true;
        }

        EndTurn(room);
        return true;
    }

    /// <summary>
    /// 게임 종료 및 승자 확정
    /// </summary>
    private void DeclareWin(
        GameRoom room,
        Player winner,
        WinReason reason)
    {
        if (room.IsFinished)
            return;

        room.IsFinished = true;
        room.WinnerPlayerId = winner.PlayerId;
        room.WinReason = reason;
    }

    /// <summary>
    /// 다른 플레이어의 턴 중
    /// 방금 버려진 카드에 대해 인터럽트 행동 처리
    /// </summary>
    public bool ReactToDiscard(
        string roomId,
        string reactingPlayerId,
        List<int> handIndexes)
    {
        // 방 조회
        if (!_rooms.TryGetValue(roomId, out var room))
            return false;

        // 플레이어 조회
        var player = room.Players
            .FirstOrDefault(p => p.PlayerId == reactingPlayerId);

        if (player == null)
            return false;

        // 마지막 버려진 카드
        var discardedCard = room.LastDiscardedCard;
        if (discardedCard == null)
            return false;

        // 이미 종료된 게임이면 무시
        if (room.IsFinished)
            return false;

        // =====================================
        // 1️⃣ Rule: 인터럽트 가능 여부 검사
        // =====================================
        if (!GameRule.CanReactToDiscard(
                player,
                discardedCard,
                handIndexes))
        {
            return false;
        }

        // =====================================
        // 2️⃣ 카드 실제로 버리기
        // =====================================
        DiscardCards(room, player, handIndexes);

        // =====================================
        // 3️⃣ Final Wait 상태 진입 여부
        // =====================================
        if (player.Hand.Count == 2 &&
            player.Hand[0].Rank == player.Hand[1].Rank)
        {
            player.IsWaitingFinalWin = true;
        }

        // =====================================
        // 4️⃣ 즉시 승리 판정 (모든 플레이어 대상)
        // =====================================
        foreach (var p in room.Players)
        {
            // 4-1 Final Wait 인터럽트
            if (GameRule.CheckFinalWaitInterrupt(p, discardedCard))
            {
                p.Score += 30;

                DeclareWin(
                    room,
                    p,
                    WinReason.FinalWaitInterrupt
                );
                return true;
            }

            // 4-2 Triple 인터럽트
            if (GameRule.CheckTripleInterrupt(p, discardedCard))
            {
                p.Score += 30;

                DeclareWin(
                    room,
                    p,
                    WinReason.TripleInterrupt
                );
                return true;
            }
        }

        return true;
    }


    /// <summary>
    /// 게임 승리 조건
    /// </summary>
    // public bool CheckFinalWin(
    //     GameRoom room,
    //     PlayingCard discardedCard)
    // {
    //     // 참여중인 플레이어 순회 - 본인 턴이 아닌 경우에도 끝낼 수 있어서
    //     foreach(var player in room.Players)
    //     {
    //         // 아직 2장만 남은 상태가 아닌 플레이어 스킵
    //         if(!player.IsWaitingFinalWin)
    //             continue;

    //         // 손에 2장만 남아있고, 그 2장의 rank가 방금 버려진 카드의 rank와 같다면 - 바가지
    //         if(player.Hand.Count == 2 && 
    //             player.Hand.All(c => c.Rank == discardedCard.Rank))
    //         {
    //             // 승리 조건 충족 -> 게임 종료
    //             // room.IsFinished = true;
    //             DeclareWin(room, player);

    //             // 승자 정보 저장  
    //             room.WinnerPlayerId = player.PlayerId;
    //         }
    //     }
    //     // 아무도 승리 조건을 만족하지 않음
    //     return false;
    // }

    /// <summary>
    /// 턴 넘기기
    /// </summary>
    public void EndTurn(GameRoom room)
    {
        if(room.IsFinished)
            return;

        var currentIndex = room.Players
            .FindIndex(p => p.PlayerId == room.CurrentTurnPlayerId);

        // 다음 플에이어 인덱스 계산 (원형)
        var nextIndex = (currentIndex + 1) % room.Players.Count;

        room.CurrentTurnPlayerId = room.Players[nextIndex].PlayerId;

        // 다음 턴 시작
        StartTurn(room);
    }

    /// <summary>
    /// 플레이어 손에서 카드 여러 장을 버린다
    /// - handIndexes는 플레이어 Hand 기준 인덱스
    /// </summary>
    private void DiscardCards(
        GameRoom room,
        Player player,
        List<int> handIndexes)
    {
        // 인덱스 내림차순 정렬
        // → 앞에서 지우면 인덱스가 밀림
        var sortedIndexes = handIndexes
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();

        foreach (var index in sortedIndexes)
        {
            // 인덱스 범위 검사
            if (index < 0 || index >= player.Hand.Count)
                continue;

            var card = player.Hand[index];

            // 손에서 제거
            player.Hand.RemoveAt(index);

            // 버린 카드 더미에 추가
            room.DiscardPile.Add(card);

            // 마지막으로 버려진 카드 갱신
            room.LastDiscardedCard = card;
        }
    }

    /// <summary>
    /// 게임 종료 시 플레이어 점수 계산
    /// </summary>
    private void CalculateScores(GameRoom room)
    {
        foreach (var player in room.Players)
        {
            if(player.PlayerId == room.WinnerPlayerId)
            {
                player.Score += 10;
                continue;
            }
        }
    }

    /// <summary>
    /// 다음 라운드 진행 여부 판단
    /// </summary>
    public bool CanStartNextRound(GameRoom room)
    {
        return room.CurrentRound < room.TotalRounds;
    }

    /// <summary>
    /// 다음 라운드 시작
    /// </summary>
    public void StartNextRound(GameRoom room)
    {
        room.CurrentRound++;
        room.IsFinished = false;
        room.WinnerPlayerId = null;

        // 플레이어 상태 초기화
        foreach(var player in room.Players)
        {
            player.Hand.Clear();
            player.IsWaitingFinalWin = false;
        }
        
        room.Deck.Clear();
        room.DiscardPile.Clear();
        room.LastDiscardedCard = null;

        // 덱 생성 및 셔플
        room.Deck = DeckFactory.CreateShuffledDeck();

        // 첫 턴 지정
        room.CurrentTurnPlayerId = room.Players[0].PlayerId;

        // 초기 패 지급 5장
        foreach(var player in room.Players)
        {
            for(int i = 0; i < 5; i ++)
            {
                DrawCard(room, player);
            }
        }
    }

    // 방 목록 조회(로비)
    public IEnumerable<GameRoom> GetRooms()
    {
        return _rooms.Values;
    }

    // 방 삭제(호스트 나가면)
    public void RemoveRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }

    internal bool TryInterrupt(GameRoom room, string playerId, List<int> handIndexes)
    {
        throw new NotImplementedException();
    }
}


