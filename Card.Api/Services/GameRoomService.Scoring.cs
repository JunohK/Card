using Card.Api.Domain;
using System.Collections.Concurrent;

namespace Card.Api.Services;

public partial class GameRoomService
{
    // 승리 조건 체크 핵심 로직
    // 룰에 따른 승리 조건 체크 (패가 6장일 때 호출)
    // cardMode에 따라 max rank(13/12)와 HighSum 임계값(68/65)이 달라짐
    public (bool isValid, string winType, int scoreValue) CheckWinCondition(List<PlayingCard> hand, CardMode cardMode = CardMode.Trump)
    {
        int maxRank = cardMode == CardMode.Hwatu ? 12 : 13;
        int highSumThreshold = cardMode == CardMode.Hwatu ? 65 : 68;
        if (hand == null || hand.Count < 6) return (false, "None", 0);

        // 조커 판정 기준 통일 (Rank가 Joker, JK, Joker1, Joker2 중 하나면 조커로 인정)
        int jokerCount = hand.Count(c => IsJoker(c));
        var normalCards = hand.Where(c => !IsJoker(c)).ToList();
        var sortedRanks = normalCards.Select(c => GetRankValue(c.Rank)).OrderBy(n => n).ToList();

        // 스트레이트 체크
        var (isStraight, straightSum) = GetStraightResult(sortedRanks, jokerCount, maxRank);
        if (isStraight) return (true, "스트레이트", -straightSum);

        // HighSum 체크 (조커 = maxRank점)
        int totalHighSum = sortedRanks.Sum() + (jokerCount * maxRank);
        if (totalHighSum >= highSumThreshold) return (true, $"{highSumThreshold}-", -totalHighSum);

        // LowSum + (4+2)
        int LowSumGroup = sortedRanks.Sum() + (jokerCount * 1);
        if (LowSumGroup <= 10 && CanMakeGroups(hand, new[] { 4, 2 })) return (true, "200-", -200);
        
        // LowSum (조커 = 1점)
        int LowSum = sortedRanks.Sum() + (jokerCount * 1);
        if (LowSum <= 10) return ( true, "100-", -100);

        // 4장 + 2장 구성 (보상 -100점)
        if (CanMakeGroups(hand, new[] { 4, 2 })) return (true, "4 + 2", -100);

        // 3장 + 3장 구성 (보상 0점)
        if (CanMakeGroups(hand, new[] { 3, 3 })) return (true, "3 + 3", 0);

        // 2장 + 2장 + 2장 (보상 0점)
        if (CanMakeGroups(hand, new[] { 2, 2, 2 })) return (true, "2 + 2 + 2", 0);

        return (false, "None", 0);
    }

    // 스트레이트 여부와 "실제 완성된 숫자의 합"을 반환하는 보조 메서드
    // maxRank: Trump=13, Hwatu=12
    private (bool isStraight, int sum) GetStraightResult(List<int> ranks, int jokers, int maxRank = 13)
    {
        if (ranks.Count + jokers < 6) return (false, 0);

        var distinctRanks = ranks.Distinct().OrderBy(n => n).ToList();
        int maxStraightSum = 0;
        bool foundAnyStraight = false;

        // 가능한 모든 시작점 확인 (1부터 maxRank까지, 6장 연속 필요)
        for (int start = 1; start <= maxRank - 6 + 1; start++)
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

    // 조커 식별 단일 진입점 — Joker/JK/Joker1/Joker2 (case-insensitive)
    public static bool IsJoker(PlayingCard card)
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

    // 패배자 점수 계산 (3장 이상 같은 숫자 제외)
    public int CalculateLoserScore(List<PlayingCard> hand)
    {
        // 1. 조커 개수 확인 및 일반 카드 그룹화 (조커 판정은 IsJoker로 통일 — Joker1/Joker2도 인식)
        int jokerCount = hand.Count(c => IsJoker(c));
        var normalCards = hand.Where(c => !IsJoker(c)).ToList();
        
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

}
