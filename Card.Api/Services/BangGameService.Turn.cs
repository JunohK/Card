using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    /// <summary>턴 시작 — Dynamite/Jail 체크 후 카드 2장 자동 드로우. 반환: 턴 스킵 메시지 (있으면).</summary>
    private string? StartTurnFor(BangState bang, BangPlayer player)
    {
        bang.BangsPlayedThisTurn = 0;
        bang.DocAbilityUsedThisTurn = false;
        bang.TurnStartedAt = DateTime.UtcNow;  // 턴 타임아웃 기준 시각
        var logs = new List<string>();

        // 1) Dynamite — 본인 장비에 있으면 펴기. ♠ 2~9이면 폭발 (3 데미지 + 버림), 아니면 시계방향 다음 사람에게 전달
        var dynamite = player.Equipment.FirstOrDefault(c => c.Type == BangCardType.Dynamite);
        if (dynamite != null)
        {
            // Lucky Duke — 두 카드 펴서 본인이 선택 (수동)
            if (player.Character == BangCharacter.LuckyDuke)
            {
                var c1 = DrawCard(bang);
                var c2 = DrawCard(bang);
                var pending = new BangPending
                {
                    Type = BangPendingType.LuckyDukeReveal,
                    TargetPlayerId = player.PlayerId,
                    SourcePlayerId = player.PlayerId,
                    CardId = dynamite.Id,
                    Context = "dynamite"
                };
                if (c1 != null) pending.RevealedCards.Add(c1);
                if (c2 != null) pending.RevealedCards.Add(c2);
                bang.Pending = pending;
                return $"🍀 {player.Name} Lucky Duke — Dynamite 펴기 2장 중 선택 대기";
            }

            // 일반: 자동 펴기
            var revealed = DrawReveal(bang, player, c => !(c.Suit == CardSuit.Spades && c.Rank >= 2 && c.Rank <= 9));
            ResolveDynamite(bang, player, dynamite, revealed, logs);
        }

        return ContinueAfterDynamite(bang, player, logs);
    }

    // Dynamite 처리 후 — Jail 검사 + 드로우 페이즈 (Lucky Duke Dynamite 수동 흐름에서도 재사용)
    private string? ContinueAfterDynamite(BangState bang, BangPlayer player, List<string> logs)
    {
        // 2) Jail — 본인 장비에 있으면 펴기. ♥이면 탈옥 + 정상 진행, 아니면 턴 스킵
        var jail = player.Equipment.FirstOrDefault(c => c.Type == BangCardType.Jail);
        if (jail != null)
        {
            // Lucky Duke — 두 카드 펴서 본인이 선택 (수동)
            if (player.Character == BangCharacter.LuckyDuke)
            {
                var c1 = DrawCard(bang);
                var c2 = DrawCard(bang);
                var pending = new BangPending
                {
                    Type = BangPendingType.LuckyDukeReveal,
                    TargetPlayerId = player.PlayerId,
                    SourcePlayerId = player.PlayerId,
                    CardId = jail.Id,
                    Context = "jail"
                };
                if (c1 != null) pending.RevealedCards.Add(c1);
                if (c2 != null) pending.RevealedCards.Add(c2);
                bang.Pending = pending;
                var head = logs.Count > 0 ? string.Join(" / ", logs) + " / " : "";
                return $"{head}🍀 {player.Name} Lucky Duke — Jail 펴기 2장 중 선택 대기";
            }

            // 자동
            var revealed = DrawReveal(bang, player, c => c.Suit == CardSuit.Hearts);
            player.Equipment.Remove(jail);
            bang.DiscardPile.Add(jail);
            if (revealed != null && revealed.Suit == CardSuit.Hearts)
            {
                logs.Add($"🔓 {player.Name} 탈옥 성공 (♥ {revealed.Rank})");
            }
            else
            {
                logs.Add($"⛓ {player.Name} 감옥에 갇혀 턴 스킵 ({(revealed != null ? $"{SuitSymbol(revealed.Suit)}{revealed.Rank}" : "-")})");
                return string.Join(" / ", logs);  // 드로우 페이즈 X — 턴 스킵
            }
        }

        // 죽었으면 더 진행 X
        if (!player.IsAlive) return string.Join(" / ", logs);

        return ContinueAfterJail(bang, player, logs);
    }

    // Jail 처리 후 — 드로우 페이즈 (Lucky Duke Jail 수동 흐름에서도 재사용)
    private string? ContinueAfterJail(BangState bang, BangPlayer player, List<string> logs)
    {
        // 3) 드로우 페이즈 — 캐릭터 능력에 따라 분기
        //    Kit Carlson: 3장 펴서 2장 선택 (pending 설정 후 사용자 선택 대기)
        //    Jesse Jones: 옵션 모달 (덱 vs 다른 사람 손에서)
        //    Pedro Ramirez: 옵션 모달 (덱 vs 버린 카드 top)
        //    Black Jack: 1장 + 두 번째 카드 펴기 ♥/♦이면 1장 더
        //    그 외: 단순 2장 드로우

        if (player.Character == BangCharacter.KitCarlson)
        {
            var revealed = new List<BangCard>();
            for (int i = 0; i < 3; i++)
            {
                var c = DrawCard(bang);
                if (c != null) revealed.Add(c);
            }
            if (revealed.Count > 0)
            {
                bang.Pending = new BangPending
                {
                    Type = BangPendingType.KitDraw,
                    SourcePlayerId = player.PlayerId,
                    RevealedCards = revealed
                };
                return logs.Count > 0 ? string.Join(" / ", logs) : null;
            }
        }
        else if (player.Character == BangCharacter.JesseJones)
        {
            // 다른 사람 중 손패 있는 사람이 있으면 옵션 모달, 아니면 일반 드로우
            bool anyHand = bang.Players.Any(p => p.IsAlive && p.PlayerId != player.PlayerId && p.Hand.Count > 0);
            if (anyHand)
            {
                bang.Pending = new BangPending
                {
                    Type = BangPendingType.JesseDraw,
                    SourcePlayerId = player.PlayerId
                };
                return logs.Count > 0 ? string.Join(" / ", logs) : null;
            }
        }
        else if (player.Character == BangCharacter.PedroRamirez)
        {
            if (bang.DiscardPile.Count > 0)
            {
                bang.Pending = new BangPending
                {
                    Type = BangPendingType.PedroDraw,
                    SourcePlayerId = player.PlayerId
                };
                return logs.Count > 0 ? string.Join(" / ", logs) : null;
            }
        }

        // Dodge City — Bill Noface: 1장 + (MaxHealth - Health)장
        if (player.Character == BangCharacter.BillNoface)
        {
            int n = 1 + Math.Max(0, player.MaxHealth - player.Health);
            for (int i = 0; i < n; i++) DrawToHand(bang, player);
            TriggerSuzyLafayette(bang);
            return logs.Count > 0 ? string.Join(" / ", logs) : null;
        }

        // 일반 드로우 (Black Jack 포함)
        DrawToHand(bang, player);
        if (player.Character == BangCharacter.BlackJack)
        {
            var second = DrawCard(bang);
            if (second != null)
            {
                player.Hand.Add(second);
                if (second.Suit == CardSuit.Hearts || second.Suit == CardSuit.Diamonds)
                {
                    logs.Add($"🃏 {player.Name} Black Jack — {SuitSymbol(second.Suit)}{second.Rank} 펴서 1장 추가");
                    DrawToHand(bang, player);
                }
            }
        }
        else
        {
            DrawToHand(bang, player);
        }

        // Dodge City — Bibles: 본인 턴 시작 시 펴기 ♣이면 카드 1장 추가
        if (player.Equipment.Any(c => c.Type == BangCardType.Bibles))
        {
            var revealed = DrawReveal(bang, player, c => c.Suit == CardSuit.Clubs);
            if (revealed != null && revealed.Suit == CardSuit.Clubs)
            {
                logs.Add($"📖 {player.Name} Bibles — ♣{revealed.Rank} 펴서 카드 1장 추가");
                DrawToHand(bang, player);
            }
        }

        TriggerSuzyLafayette(bang);
        return logs.Count > 0 ? string.Join(" / ", logs) : null;
    }

    // Dynamite 펴기 결과 처리 — 폭발/전달 결정 (LuckyDuke 수동 선택 후에도 호출)
    private void ResolveDynamite(BangState bang, BangPlayer player, BangCard dynamite, BangCard? revealed, List<string> logs)
    {
        if (revealed != null && revealed.Suit == CardSuit.Spades && revealed.Rank >= 2 && revealed.Rank <= 9)
        {
            logs.Add($"💥 {player.Name} Dynamite 폭발! (♠{revealed.Rank}) 3 데미지");
            for (int i = 0; i < 3 && player.IsAlive; i++) ApplyDamage(bang, player, null);
            player.Equipment.Remove(dynamite);
            bang.DiscardPile.Add(dynamite);
        }
        else
        {
            var next = NextAlive(bang, player);
            if (next != null && next.PlayerId != player.PlayerId)
            {
                player.Equipment.Remove(dynamite);
                next.Equipment.Add(dynamite);
                logs.Add($"💣 {player.Name} → {next.Name}에게 Dynamite 전달 ({(revealed != null ? $"{SuitSymbol(revealed.Suit)}{revealed.Rank}" : "-")})");
            }
        }
    }

    // Suzy Lafayette — 손패 0장이면 즉시 1장. 어떤 hand 변경 후라도 호출 가능
    private void TriggerSuzyLafayette(BangState bang)
    {
        foreach (var p in bang.Players)
        {
            if (p.IsAlive && p.Character == BangCharacter.SuzyLafayette && p.Hand.Count == 0)
            {
                DrawToHand(bang, p);
            }
        }
    }

    /// <summary>턴 종료. 손패가 체력 초과면 cardIds로 명시 버림.</summary>
    public BangActionResult EndTurn(string roomId, string playerId, List<int>? discardCardIds = null)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");
        if (bang.Pending != null) return Fail("응답 대기 중입니다.");
        if (bang.CurrentTurnPlayerId != playerId) return Fail("내 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null) return Fail("플레이어 없음.");

        // 손패 ≤ 체력 검증
        int excess = me.Hand.Count - me.Health;
        if (excess > 0)
        {
            if (discardCardIds == null || discardCardIds.Count != excess)
                return Fail($"손패 {me.Hand.Count}장, 체력 {me.Health} — {excess}장을 버려야 합니다.");
            foreach (var cid in discardCardIds)
            {
                var c = me.Hand.FirstOrDefault(x => x.Id == cid);
                if (c == null) return Fail("버릴 카드를 찾을 수 없습니다.");
                me.Hand.Remove(c);
                bang.DiscardPile.Add(c);
            }
        }

        // 다음 살아있는 플레이어로 턴 이동 — Dynamite/Jail 처리 후 카드 스킵된 경우 자동으로 그 다음 사람까지 이동
        var next = NextAlive(bang, me);
        if (next == null) return Fail("플레이어가 모두 사망했습니다.");
        bang.CurrentTurnPlayerId = next.PlayerId;
        var startLog = StartTurnFor(bang, next);

        // 폭발로 사망했거나 감옥으로 스킵된 경우 — 다음 살아있는 사람으로 한 번 더 이동
        // (재귀 호출은 피하고, 한 번의 스킵만 자동 처리)
        if (!next.IsAlive || (startLog != null && startLog.Contains("감옥")))
        {
            var nextNext = NextAlive(bang, next);
            if (nextNext != null)
            {
                bang.CurrentTurnPlayerId = nextNext.PlayerId;
                var log2 = StartTurnFor(bang, nextNext);
                var combined = string.Join(" / ", new[] { startLog, $"{nextNext.Name}의 차례", log2 }.Where(s => !string.IsNullOrEmpty(s)));
                return new BangActionResult { Success = true, Message = combined };
            }
        }

        var msg = startLog != null ? $"{startLog} / {next.Name}의 차례" : $"{next.Name}의 차례";
        return new BangActionResult { Success = true, Message = msg };
    }

    private BangPlayer? NextAlive(BangState bang, BangPlayer current)
    {
        int n = bang.Players.Count;
        for (int step = 1; step <= n; step++)
        {
            int idx = (current.SeatIndex + step) % n;
            var p = bang.Players[idx];
            if (p.IsAlive) return p;
        }
        return null;
    }

}
