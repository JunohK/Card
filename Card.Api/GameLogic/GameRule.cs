using Card.Api.Domain;

namespace Card.Api.GameLogic;

/// <summary>
/// 게임 규칙
/// 
/// 이 클래스는 판정과 점수 계산 담당
/// 턴 이동, 네트워크 전송, 상태 저장은 GameRoomService에서 담당
/// </summary>
public static class GameRule
{
    // 기본 규칙

    /// <summary>
    /// 게임 시작 시 모든 플레이어는 0점에서 시작한다.
    /// </summary>
    public static void InitializeScores(GameRoom room)
    {
        foreach (var player in room.Players)
        {
            player.Score = 0;
        }
    }

    // ===================================================
    // 내 턴에 카드 1장 드로우 후 즉시 종료 판정 (6장일 때)
    // ===================================================

    /// <summary>
    /// 카드 1장을 뽑아 손패가 6장이 되었을 때 즉시 게임 종료 조건 검사
    /// 종료 조건 : 1.같은 숫자 카드 4장 + 2장 => 본인 점수 -100
    ///            2. 같은 숫자 카드 3장 + 3장 => 본인 점수 변화 없음
    ///
    ///  공통 규칙 : 다른 플레이어들은 손에 남은 카드 숫자의 합만큼 점수에 더한다.
    ///  단 같은 숫자 3장이 있다면 그 숫자 x 3은 합산에서 제외한다.
    /// </summary>
    public static bool CheckSixCardImmediateFinish(
        GameRoom room,
        Player currentPlayer)
    {
        if(currentPlayer.Hand.Count != 6)
            return false;

        // 숫자별 그룹핑
        var groups = currentPlayer.Hand
            .GroupBy(c => c.Rank)
            .Select(g => g.Count())
            .OrderByDescending(c => c)
            .ToList();

        // 4장 + 2장
        if(groups.SequenceEqual(new[] {4,2}))
        {
            currentPlayer.Score -= 100;
            ApplyOtherPlayersScore(room,currentPlayer);
            return true;
        }

        // 3장 + 3장
        if(groups.SequenceEqual(new[] { 3, 3 }))
        {
            // 본인 점수변동 없음
            ApplyOtherPlayersScore(room, currentPlayer);
            return true;
        }

        return false;
    }
    
    // ===================================================
    // 4장 + 2장 & 총합 10 미만
    // ===================================================

    /// <summary>
    /// 손패가 6장이며
    /// 4장 + 2장 구조이고
    /// 카드 숫자의 총합이 10 미만이면
    /// 추가로 -200점
    /// </summary>
    public static void ApplyLowSum(Player player)
    {
        var sum = player.Hand.Sum(c => c.Value);

        if( sum < 10)
        {
            player.Score -= 200;
        }
    }

    // =========================================================
    // 4️⃣ 카드가 연속된 숫자일 경우 (스트레이트)
    // =========================================================

    /// <summary>
    /// 손패의 카드가 오름차순 연속 숫자일 경우
    /// (예: 3,4,5,6,7,8)
    /// → 해당 카드 숫자의 합만큼 점수 차감
    /// </summary>
    public static bool CheckStraight(Player player)
    {
        var ordered = player.Hand
            .Where(c => !c.IsJoker)
            .Select(c => c.Value!.Value)
            .OrderBy(v => v)
            .ToList();

        for(int i = 1; i < ordered.Count; i++)
        {
            if(ordered[i] != ordered [i - 1] + 1)
                return false;
        }

        int sum = ordered.Sum();
        player.Score -= sum;

        return true;
    }

    // =========================================================
    // 최종 대기 상태 인터럽트 승리
    // =========================================================

    /// <summary>
    /// 플레이어가 카드 2장만 남아 있고
    /// 두 카드가 같은 숫자일 때
    /// 
    /// 다른 플레이어가
    /// 같은 숫자의 카드를 버리면:
    /// - 즉시 게임 종료
    /// - 해당 플레이어 점수 +30
    /// </summary>
    public static bool CheckFinalWaitInterrupt(
        Player waitingPlayer,
        PlayingCard discardedCard)
    {
        if(waitingPlayer.Hand.Count != 2)
            return false;

        return waitingPlayer.Hand
            .All(c => c.Rank == discardedCard.Rank);
    }

    // =========================================================
    // 6️⃣ 3장 + 2장 상태에서의 인터럽트 종료
    // =========================================================

    /// <summary>
    /// 손패가 3장 + 2장 구조일 때
    /// (같은 숫자 3장이 존재)
    ///
    /// 누군가 해당 숫자의 카드를 버리면:
    /// - 즉시 게임 종료
    /// - 그 카드를 낸 사람에게 +30점
    /// </summary>
    public static bool CheckTripleInterrupt(
        Player player,
        PlayingCard discardedCard)
    {
        var tripleRank = player.Hand
            .GroupBy(c => c.Rank)
            .Where(g => g.Count() == 3)
            .Select(g => g.Key)
            .FirstOrDefault();

        if(tripleRank == null)
            return false;

        return discardedCard.Rank == tripleRank;
    }

    // =========================================================
    // 내부 공통 처리
    // =========================================================

    /// <summary>
    /// 즉시 종료 시
    /// 현재 플레이어를 제외한
    /// 다른 플레이어들의 점수를 계산한다
    /// </summary>
    public static void ApplyOtherPlayersScore(
        GameRoom room,
        Player winner)
    {
        foreach(var player in room.Players)
        {
            if(player.PlayerId == winner.PlayerId)
                continue;

            int sum = 0;

            var groups = player.Hand
                .Where(c => !c.IsJoker) // Joker는 점수 합산 제외
                .GroupBy(c => c.Value!.Value);

            foreach(var group in groups)
            {
                // 같은 숫자 3장은 접수 합산 제외
                if(group.Count() == 3)
                    continue;

                sum += group.Sum(c => c.Value!.Value);
            }

            player.Score += sum;
        }
    }

    /// <summary>
    /// Joker를 포함한 카드 Value 목록 반환
    /// Joker는 계산에 필요한 값으로 치환됨
    /// </summary>
    public static List<int> resolveValues(
        List<PlayingCard> hand,
        Func<List<int>, int> chooseBestValue)
    {
        var fixedValues = hand
            .Where(c => !c.IsJoker)
            .Select(c => c.Value!.GetValueOrDefault())
            .ToList();

        int jokerCount = hand.Count(c => c.IsJoker);

        for(int i = 0; i <jokerCount; i++)
        {
            int best = chooseBestValue(fixedValues);
            fixedValues.Add(best);
        }
        return fixedValues;
    }

    /// <summary>
    /// 다른 사람이 버린 카드에 대해
    /// 내가 인터럽트 행동을 할 수 있는지 검사
    /// 
    /// 규칙:
    /// - 총 3장을 버려야 함
    /// - 그 중 2장은 버려진 카드와 같은 Rank여야 함
    /// - 나머지 1장은 아무 카드 가능
    /// </summary>
    public static bool CanReactToDiscard(
        Player player,
        PlayingCard discardedCard,
        List<int> handIndexes)
    {
        // 반드시 3장을 버려야 함
        if (handIndexes.Count != 3)
            return false;

        // 손패 범위 체크
        if (handIndexes.Any(i => i < 0 || i >= player.Hand.Count))
            return false;

        // 실제 선택한 카드들
        var selectedCards = handIndexes
            .Select(i => player.Hand[i])
            .ToList();

        // 버려진 카드와 같은 Rank 카드가 정확히 2장 이상 있어야 함
        var sameRankCount = selectedCards
            .Count(c => c.Rank == discardedCard.Rank);

        return sameRankCount >= 2;
    }

    /// <summary>
    /// Final Wait 상태의 플레이어가
    /// 방금 버려진 카드로 즉시 승리 가능한지 판정
    /// </summary>
    public static bool IsFinalWin(
        Player player,
        PlayingCard discardedCard)
    {
        // 대기 상태가 아니면 불가
        if (!player.IsWaitingFinalWin)
            return false;

        // 반드시 카드 2장만 남아 있어야 함
        if (player.Hand.Count != 2)
            return false;

        // 남은 두 장의 Rank가 버려진 카드와 같으면 승리
        return player.Hand.All(c => c.Rank == discardedCard.Rank);
    }
}