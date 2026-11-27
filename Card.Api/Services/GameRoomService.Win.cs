using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    // 수정된 승리 선언 (족보 검증 포함)
    public bool DeclareWin(GameRoom room, Player winner, WinReason reason)
    {
        if (room == null || room.IsFinished || room.IsRoundEnded) return false;

        // 🔴 [가장 중요] 그 어떤 로직도 타기 전에 현재 패를 완전히 새로운 리스트로 복제합니다.
        // .ToList()만으로는 부족할 수 있으니 새 객체를 생성하여 복사하십시오.
        var savedHand = winner.Hand.Select(c => new PlayingCard { 
            Suit = c.Suit, 
            Rank = c.Rank, 
            Color = c.Color,
            Type = c.Type,
            Id = c.Id
        }).ToList();

        // 2️⃣ 족보 판정용 패 (게임 상태와 완전히 분리)
        var handForCheck = winner.Hand
            .Select(c => new PlayingCard
            {
                Suit = c.Suit,
                Rank = c.Rank,
                Color = c.Color,
                Type = c.Type,
                Id = c.Id
            })
            .ToList();

        // 🔴 절대 winner.Hand를 넘기지 말 것
        var check = CheckWinCondition(handForCheck, room.CardMode);
        if (reason == WinReason.ManualDeclare && !check.isValid) return false;

        // 2. 라운드 종료 정보 설정
        room.IsRoundEnded = true; 
        room.WinnerPlayerId = winner.PlayerId;
        room.WinnerName = winner.Name;
        room.LastWinType = check.winType;

        // 🔴 [수정] 서버에서 클라이언트로 보낼 최종 패 정보에 복제본을 할당`
        room.WinnerHand = savedHand;

        // 3. 점수 계산
        foreach (var player in room.Players)
        {
            int roundScore = (player.PlayerId == winner.PlayerId)
                ? check.scoreValue
                : CalculateLoserScore(player.Hand);

            player.Score = roundScore;
            player.TotalScore += roundScore;
        }

        // 4. 라운드 점수 히스토리 기록
        RecordRoundScore(room);

        // 5. 전체 게임 종료 체크 (게임 전체 승자 확정 포함)
        CheckAndEndFullGame(room);

        return true;
    }

    public void DeclareStop(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.IsFinished || room.IsRoundEnded) return;

        var player = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null) return;

        // 내 턴 + 카드를 뽑아 3장인 상태 + 패가 2장인 사람이 본인 포함 2명 이상 (클라이언트 STOP 버튼 조건과 동일)
        // 뻥으로 가져온 턴(뻥 후 3장)에는 STOP 불가
        bool isActionPhase = room.CurrentTurnPlayerId == player.PlayerId && player.Hand.Count == 3 && !room.IsPungTurn;
        bool othersWithTwo = room.Players.Any(p => p.PlayerId != player.PlayerId && p.Hand.Count == 2);
        if (isActionPhase && othersWithTwo)
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

        // 1. STOP 선언자의 현재 패 점수 계산 (바가지/독박 체크용)
        // STOP 선언자도 점수 계산 로직은 동일하게 CalculateLoserScore를 사용합니다.
        int stopPlayerScore = CalculateLoserScore(stopPlayer.Hand);

        // 2. 선언자 제외, 현재 패가 2장인 다른 플레이어들의 최소 점수 찾기
        var otherTwoCardScores = room.Players
            .Where(p => p.PlayerId != stopPlayer.PlayerId && p.Hand.Count == 2)
            .Select(p => CalculateLoserScore(p.Hand))
            .ToList();

        // 비교 대상(2장인 사람)이 있고, 내 점수가 그들 중 최솟값보다 크거나 같다면 '독박'
        bool isDokbak = otherTwoCardScores.Any() && stopPlayerScore >= otherTwoCardScores.Min();

        room.WinnerName = isDokbak ? $"{stopPlayer.Name} (STOP 실패)" : $"{stopPlayer.Name} (STOP 성공)";
        room.LastWinType = isDokbak ? "STOP 실패 (+50점)" : "STOP 성공 (0점)";

        // 3. 모든 플레이어 점수 계산 (패배자 로직 적용)
        foreach (var p in room.Players)
        {
            if (p.PlayerId == stopPlayer.PlayerId)
            {
                // 독박이면 본인 카드 합(세트 제외 점수) + 50점, 성공이면 0점
                p.Score = isDokbak ? (stopPlayerScore + 50) : 0;
            }
            else
            {
                // ✅ 핵심: 모든 패배자들은 사용자님이 만든 CalculateLoserScore 로직을 타게 함
                p.Score = CalculateLoserScore(p.Hand);
            }
            
            // 누적 점수 반영
            p.TotalScore += p.Score;
            
            // 디버깅용 로그
            Console.WriteLine($"[STOP 결과] {p.Name}: 이번 라운드 {p.Score}점, 총점 {p.TotalScore}점");
        }

        // 4. 라운드 점수 히스토리 기록
        RecordRoundScore(room);

        // 5. 상태 초기화 (다음 판을 위해)
        room.IsStopDeclared = false;
        room.StopCallerId = "";

        // 전체 게임 종료 조건 체크 (점수 초과 등)
        CheckAndEndFullGame(room);
    }

    public (bool isBagajiWin, string? winnerId, string? loserId) CheckBagajiWin(string discardedRank, string discarderId, List<Player> allPlayers)
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
                // 자연바가지 비활성화 버튼
                if (!player.AllowNaturalBagaji)
                    continue;

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

        // 라운드 점수 히스토리 기록
        RecordRoundScore(room);

        CheckAndEndFullGame(room);
    }

    public void GiveUpGame(string roomId, string playerId)
    {
        var room = GetRoom(roomId);
        if (room == null || room.IsFinished) return;

        var surrenderPlayer = room.Players.FirstOrDefault(p => p.PlayerId == playerId);
        
        // 기권 시 전광판만 띄움 (IsStarted는 유지하여 아직 GamePage에 머물게 함)
        room.IsFinished = true;
        room.WinnerName = surrenderPlayer != null ? $"{surrenderPlayer.Name} (기권)" : "게임 종료";
        // 기권자는 제외하고 총점 최저가 게임 승자
        SetGameWinners(room, excludePlayerId: playerId);

        // 요청하신 대로 패 점수 계산 없이 현재 TotalScore 유지
    }

    public bool CanDeclareWin(GameRoom room, Player player)
    {
        if (room.CurrentTurnPlayerId != player.PlayerId)
            return false;

        // 첫 바퀴(모두 한 번씩 턴, 뻥으로 건너뛴 사람 포함)가 끝나야 가능 — 클라이언트 승리선언 버튼 조건과 동일
        return room.IsFirstLapDone;
    }
}
