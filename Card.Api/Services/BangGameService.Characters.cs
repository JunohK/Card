using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    /// <summary>Kit Carlson — 펴진 3장 중 2장 선택 (지정한 1장은 덱 top으로 돌아감).</summary>
    public BangActionResult KitCarlsonChoice(string roomId, string playerId, int returnedCardId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.KitDraw) return Fail("Kit Carlson 진행 중이 아닙니다.");
        if (pending.SourcePlayerId != playerId) return Fail("당신이 결정할 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null) return Fail("플레이어 없음.");

        var returned = pending.RevealedCards.FirstOrDefault(c => c.Id == returnedCardId);
        if (returned == null) return Fail("선택한 카드가 풀에 없습니다.");

        pending.RevealedCards.Remove(returned);
        bang.Deck.Insert(0, returned);  // 덱 top으로
        foreach (var c in pending.RevealedCards) me.Hand.Add(c);
        bang.Pending = null;
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} Kit Carlson — 2장 획득, 1장 덱으로" };
    }

    /// <summary>Jesse Jones — fromDeck=true면 일반 드로우, false면 fromPlayerId의 무작위 카드 1장 + 덱 1장.</summary>
    public BangActionResult JesseJonesChoice(string roomId, string playerId, bool fromDeck, string? fromPlayerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.JesseDraw) return Fail("Jesse Jones 진행 중이 아닙니다.");
        if (pending.SourcePlayerId != playerId) return Fail("당신이 결정할 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null) return Fail("플레이어 없음.");

        if (fromDeck)
        {
            DrawToHand(bang, me);
            DrawToHand(bang, me);
        }
        else
        {
            if (string.IsNullOrEmpty(fromPlayerId)) return Fail("대상을 선택하세요.");
            var from = bang.Players.FirstOrDefault(p => p.PlayerId == fromPlayerId);
            if (from == null || !from.IsAlive || from.Hand.Count == 0) return Fail("대상이 유효하지 않습니다.");
            int idx = _random.Next(from.Hand.Count);
            var stolen = from.Hand[idx];
            from.Hand.RemoveAt(idx);
            me.Hand.Add(stolen);
            DrawToHand(bang, me);  // 덱에서 1장
        }
        bang.Pending = null;
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} Jesse Jones 드로우 완료" };
    }

    /// <summary>Pedro Ramirez — fromDeck=true면 일반 드로우, false면 버린 카드 top 1장 + 덱 1장.</summary>
    public BangActionResult PedroRamirezChoice(string roomId, string playerId, bool fromDeck)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.PedroDraw) return Fail("Pedro Ramirez 진행 중이 아닙니다.");
        if (pending.SourcePlayerId != playerId) return Fail("당신이 결정할 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null) return Fail("플레이어 없음.");

        if (fromDeck)
        {
            DrawToHand(bang, me);
            DrawToHand(bang, me);
        }
        else
        {
            if (bang.DiscardPile.Count == 0) return Fail("버린 카드 더미가 비어 있습니다.");
            var top = bang.DiscardPile[^1];
            bang.DiscardPile.RemoveAt(bang.DiscardPile.Count - 1);
            me.Hand.Add(top);
            DrawToHand(bang, me);
        }
        bang.Pending = null;
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} Pedro Ramirez 드로우 완료" };
    }

    /// <summary>Sid Ketchum — 손패 2장 버리면 HP 1 회복. 언제든 사용 가능 (자기 턴 아니라도).</summary>
    public BangActionResult UseSidKetchum(string roomId, string playerId, List<int> discardCardIds)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null || !me.IsAlive) return Fail("플레이어를 찾을 수 없습니다.");
        if (me.Character != BangCharacter.SidKetchum) return Fail("Sid Ketchum 능력입니다.");
        if (me.Health >= me.MaxHealth) return Fail("이미 최대 체력입니다.");
        if (discardCardIds == null || discardCardIds.Count != 2) return Fail("정확히 2장을 선택하세요.");
        if (discardCardIds.Distinct().Count() != 2) return Fail("같은 카드 ID를 두 번 선택할 수 없습니다.");

        var cards = new List<BangCard>();
        foreach (var id in discardCardIds)
        {
            var c = me.Hand.FirstOrDefault(x => x.Id == id);
            if (c == null) return Fail("선택한 카드가 손에 없습니다.");
            cards.Add(c);
        }
        foreach (var c in cards)
        {
            me.Hand.Remove(c);
            bang.DiscardPile.Add(c);
        }
        me.Health = Math.Min(me.MaxHealth, me.Health + 1);
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} Sid Ketchum 능력 — 손 2장 버림, HP {me.Health}/{me.MaxHealth}" };
    }

    /// <summary>Dodge City — Chuck Wengam: 본인 턴에 HP 1 잃고 카드 2장.</summary>
    /// <summary>Lucky Duke — 펴진 2장 중 1장 직접 선택. context별 후속 처리.</summary>
    public BangActionResult ChooseLuckyDuke(string roomId, string playerId, int chosenCardId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.LuckyDukeReveal)
            return Fail("Lucky Duke 선택 대기 상태가 아닙니다.");
        if (pending.TargetPlayerId != playerId)
            return Fail("당신이 선택할 차례가 아닙니다.");

        var chosen = pending.RevealedCards.FirstOrDefault(c => c.Id == chosenCardId);
        if (chosen == null) return Fail("선택한 카드가 펴진 카드에 없습니다.");

        // 두 펴진 카드 모두 버린 카드 더미로
        foreach (var c in pending.RevealedCards) bang.DiscardPile.Add(c);

        var target = bang.Players.FirstOrDefault(p => p.PlayerId == pending.TargetPlayerId);
        var source = bang.Players.FirstOrDefault(p => p.PlayerId == pending.SourcePlayerId);
        if (target == null || source == null) return Fail("플레이어를 찾을 수 없습니다.");

        string suitSym = chosen.Suit switch
        {
            CardSuit.Hearts => "♥", CardSuit.Diamonds => "♦",
            CardSuit.Clubs => "♣", CardSuit.Spades => "♠", _ => "?"
        };
        string rankStr = chosen.Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => chosen.Rank.ToString() };

        // Barrel 펴기 (모든 BANG 변형: barrel_bang, barrel_punch, barrel_springfield, barrel_buffalo, barrel_doc)
        if (pending.Context?.StartsWith("barrel_") == true)
        {
            int requiredMissed = pending.RequiredMissed;
            string ctxLabel = pending.Context switch
            {
                "barrel_bang" => "BANG",
                "barrel_punch" => "Punch",
                "barrel_springfield" => "Springfield",
                "barrel_buffalo" => "Buffalo Rifle",
                "barrel_doc" => "Doc 능력",
                _ => "BANG"
            };

            if (chosen.Suit == CardSuit.Hearts)
            {
                requiredMissed--;
                if (requiredMissed <= 0)
                {
                    bang.Pending = null;
                    TriggerSuzyLafayette(bang);
                    return new BangActionResult { Success = true, Message = $"🍀🛢 {target.Name} Lucky Duke + Barrel — {suitSym}{rankStr} 선택, {ctxLabel} 자동 회피!" };
                }
            }
            // 회피 안 됨 → BANG response pending으로 전환 (CardId는 원본 BANG 또는 0 for Doc)
            bang.Pending = new BangPending
            {
                Type = BangPendingType.BangResponse,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = source.PlayerId,
                CardId = pending.CardId,
                RequiredMissed = requiredMissed
            };
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name} Lucky Duke — {suitSym}{rankStr} 선택, {ctxLabel} 응답 대기" };
        }

        // Jail — Lucky Duke 본인 펴기. ♥이면 탈옥 후 드로우 페이즈, 아니면 턴 스킵 + 다음 사람으로
        if (pending.Context == "jail")
        {
            var jail = target.Equipment.FirstOrDefault(c => c.Type == BangCardType.Jail);
            if (jail == null) { bang.Pending = null; return new BangActionResult { Success = true, Message = "Jail 없음" }; }
            target.Equipment.Remove(jail);
            bang.DiscardPile.Add(jail);
            bang.Pending = null;
            var logs = new List<string>();
            if (chosen.Suit == CardSuit.Hearts)
            {
                logs.Add($"🔓 {target.Name} Lucky Duke 탈옥 ({suitSym}{rankStr})");
                var rest = ContinueAfterJail(bang, target, logs);
                return new BangActionResult { Success = true, Message = rest ?? string.Join(" / ", logs) };
            }
            logs.Add($"⛓ {target.Name} 감옥에 갇혀 턴 스킵 ({suitSym}{rankStr})");
            // 다음 살아있는 사람으로 자동 이동
            var next = NextAlive(bang, target);
            if (next != null && next.PlayerId != target.PlayerId)
            {
                bang.CurrentTurnPlayerId = next.PlayerId;
                var startLog = StartTurnFor(bang, next);
                logs.Add($"{next.Name}의 차례");
                if (!string.IsNullOrEmpty(startLog)) logs.Add(startLog);
            }
            return new BangActionResult { Success = true, Message = string.Join(" / ", logs) };
        }

        // Dynamite — Lucky Duke 본인 펴기. ♠ 2-9이면 폭발, 아니면 다음 사람 전달. 후속: Jail + 드로우
        if (pending.Context == "dynamite")
        {
            var dynamite = target.Equipment.FirstOrDefault(c => c.Type == BangCardType.Dynamite);
            if (dynamite == null)
            {
                bang.Pending = null;
                return new BangActionResult { Success = true, Message = "Dynamite 없음" };
            }
            var logs = new List<string>();
            ResolveDynamite(bang, target, dynamite, chosen, logs);
            bang.Pending = null;
            var rest = ContinueAfterDynamite(bang, target, logs);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name} Lucky Duke — {suitSym}{rankStr} 선택" + (rest != null ? $" / {rest}" : "") };
        }

        // IronPlate-only 펴기 (♠이면 회피, 아니면 BANG response)
        if (pending.Context?.StartsWith("ironplate_") == true)
        {
            string ctxLabel = pending.Context switch
            {
                "ironplate_bang" => "BANG",
                "ironplate_punch" => "Punch",
                "ironplate_springfield" => "Springfield",
                "ironplate_buffalo" => "Buffalo Rifle",
                _ => "BANG"
            };
            if (chosen.Suit == CardSuit.Spades)
            {
                bang.Pending = null;
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"🍀🛡 {target.Name} Lucky Duke + Iron Plate — {suitSym}{rankStr} 선택, {ctxLabel} 자동 회피!" };
            }
            bang.Pending = new BangPending
            {
                Type = BangPendingType.BangResponse,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = source.PlayerId,
                CardId = pending.CardId,
                RequiredMissed = pending.RequiredMissed
            };
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name} Lucky Duke — {suitSym}{rankStr} 선택, {ctxLabel} 응답 대기" };
        }

        // 알 수 없는 context → pending 닫고 안전 종료
        bang.Pending = null;
        return new BangActionResult { Success = true, Message = "Lucky Duke 선택 완료" };
    }

    public BangActionResult UseChuckWengam(string roomId, string playerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");
        if (bang.Pending != null) return Fail("응답 대기 중입니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null || !me.IsAlive) return Fail("플레이어를 찾을 수 없습니다.");
        if (me.Character != BangCharacter.ChuckWengam) return Fail("Chuck Wengam 능력입니다.");
        if (bang.CurrentTurnPlayerId != playerId) return Fail("본인 턴에만 사용 가능합니다.");
        if (me.Health <= 1) return Fail("HP가 1이면 사용할 수 없습니다 (사망 방지).");

        me.Health--;
        DrawToHand(bang, me);
        DrawToHand(bang, me);
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} Chuck Wengam 능력 — HP {me.Health}/{me.MaxHealth}, 카드 2장 획득" };
    }

    /// <summary>Dodge City — Doc Holyday: 본인 턴에 손패 2장 버려서 BANG 효과 발사. 턴당 1회.</summary>
    public BangActionResult UseDocHolyday(string roomId, string playerId, List<int> discardCardIds, string targetPlayerId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");
        if (bang.Pending != null) return Fail("응답 대기 중입니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null || !me.IsAlive) return Fail("플레이어를 찾을 수 없습니다.");
        if (me.Character != BangCharacter.DocHolyday) return Fail("Doc Holyday 능력입니다.");
        if (bang.CurrentTurnPlayerId != playerId) return Fail("본인 턴에만 사용 가능합니다.");
        if (bang.DocAbilityUsedThisTurn) return Fail("이번 턴 능력을 이미 사용했습니다.");
        if (discardCardIds == null || discardCardIds.Count != 2) return Fail("정확히 2장을 선택하세요.");
        if (discardCardIds.Distinct().Count() != 2) return Fail("같은 카드 ID를 두 번 선택할 수 없습니다.");

        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetPlayerId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        int dist = Distance(bang, me, target);
        int range = GetWeaponRange(me.Weapon);
        if (dist > range) return Fail($"거리({dist})가 사거리({range})보다 멀어 닿지 않습니다.");

        var cards = new List<BangCard>();
        foreach (var id in discardCardIds)
        {
            var c = me.Hand.FirstOrDefault(x => x.Id == id);
            if (c == null) return Fail("선택한 카드가 손에 없습니다.");
            cards.Add(c);
        }
        foreach (var c in cards)
        {
            me.Hand.Remove(c);
            bang.DiscardPile.Add(c);
        }
        bang.DocAbilityUsedThisTurn = true;

        // BANG 효과 발사 (BANG 카운트 증가 안 함 — 능력은 일반 BANG과 별개)
        int requiredMissed = me.Character == BangCharacter.SlabTheKiller ? 2 : 1;
        bool hasBarrel = target.Equipment.Any(c => c.Type == BangCardType.Barrel)
                         || target.Character == BangCharacter.Jourdonnais;

        if (hasBarrel && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = 0,
                RequiredMissed = requiredMissed,
                Context = "barrel_doc"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Doc Holyday Barrel 펴기 2장 중 선택 대기" };
        }

        if (hasBarrel)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Hearts);
            if (revealed != null && revealed.Suit == CardSuit.Hearts)
            {
                requiredMissed--;
                if (requiredMissed <= 0)
                {
                    TriggerSuzyLafayette(bang);
                    return new BangActionResult { Success = true, Message = $"🛢 {target.Name} Barrel — ♥ 자동 회피!" };
                }
            }
        }

        // Doc 능력 발사된 BANG의 카드 효과는 Apache Kid 면제 적용 X (카드 효과가 아닌 캐릭터 능력)
        bang.Pending = new BangPending
        {
            Type = BangPendingType.BangResponse,
            TargetPlayerId = target.PlayerId,
            SourcePlayerId = me.PlayerId,
            CardId = 0, // 능력 발사 — 카드 없음
            RequiredMissed = requiredMissed
        };
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"🩺 {me.Name} Doc Holyday 능력 → {target.Name}에게 BANG! (응답 대기)" };
    }

}
