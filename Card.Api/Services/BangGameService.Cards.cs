using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    /// <summary>현재 턴 플레이어가 카드 사용. MVP — 5종만 처리.</summary>
    public BangActionResult PlayCard(string roomId, string playerId, int cardId, string? targetPlayerId = null)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");
        if (bang.Pending != null) return Fail("응답 대기 중입니다.");
        if (bang.CurrentTurnPlayerId != playerId) return Fail("내 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null || !me.IsAlive) return Fail("플레이어를 찾을 수 없습니다.");

        var card = me.Hand.FirstOrDefault(c => c.Id == cardId);
        if (card == null) return Fail("손에 없는 카드입니다.");

        switch (card.Type)
        {
            case BangCardType.Bang:
                return PlayBang(bang, me, card, targetPlayerId);
            case BangCardType.Missed:
                // Calamity Janet은 Missed를 BANG으로 사용 가능
                if (me.Character == BangCharacter.CalamityJanet)
                    return PlayBang(bang, me, card, targetPlayerId);
                return Fail("Missed는 BANG 응답으로만 사용합니다.");
            case BangCardType.Beer:
                return PlayBeer(bang, me, card);
            case BangCardType.Stagecoach:
                return PlayStagecoach(bang, me, card);
            case BangCardType.WellsFargo:
                return PlayWellsFargo(bang, me, card);
            case BangCardType.Saloon:
                return PlaySaloon(bang, me, card);
            case BangCardType.CatBalou:
                return PlayCardPick(bang, me, card, targetPlayerId, pickToHand: false, requireDistance1: false);
            case BangCardType.Panic:
                return PlayCardPick(bang, me, card, targetPlayerId, pickToHand: true, requireDistance1: true);
            case BangCardType.GeneralStore:
                return PlayGeneralStore(bang, me, card);
            case BangCardType.Indians:
                return PlayIndians(bang, me, card);
            case BangCardType.Gatling:
                return PlayGatling(bang, me, card);
            case BangCardType.Duel:
                return PlayDuel(bang, me, card, targetPlayerId);
            // 무기 — 장착 (기존 무기 교체)
            case BangCardType.Volcanic:
            case BangCardType.Scofield:
            case BangCardType.Remington:
            case BangCardType.Carbine:
            case BangCardType.Winchester:
            case BangCardType.Pepperbox:
            case BangCardType.Conestoga:
            case BangCardType.RevCarabine:
                return EquipWeapon(bang, me, card);
            // 본인 장비
            case BangCardType.Mustang:
            case BangCardType.Scope:
            case BangCardType.Barrel:
            case BangCardType.Dynamite:
                return EquipGear(bang, me, card);
            // 대상 장비 (Jail은 다른 사람에게 장착)
            case BangCardType.Jail:
                return PlayJail(bang, me, card, targetPlayerId);
            // === Dodge City 확장팩 ===
            case BangCardType.Punch:
                return PlayPunch(bang, me, card, targetPlayerId);
            case BangCardType.Dodge:
                return Fail("Dodge는 BANG 응답으로만 사용합니다.");
            case BangCardType.Whisky:
                return PlayWhisky(bang, me, card);
            case BangCardType.Tequila:
                return PlayTequila(bang, me, card, targetPlayerId);
            case BangCardType.Springfield:
                return PlaySpringfield(bang, me, card, targetPlayerId);
            case BangCardType.BuffaloRifle:
                return PlayBuffaloRifle(bang, me, card, targetPlayerId);
            case BangCardType.Brawl:
                return PlayBrawl(bang, me, card);
            case BangCardType.RagTime:
                return PlayRagTime(bang, me, card, targetPlayerId);
            case BangCardType.CanCan:
                return PlayCanCan(bang, me, card, targetPlayerId);
            case BangCardType.IronPlate:
            case BangCardType.TenGallonHat:
            case BangCardType.Bibles:
                return EquipGear(bang, me, card);
            default:
                return Fail("알 수 없는 카드입니다.");
        }
    }

    // Dodge City: Punch — BANG 효과지만 거리 1 강제
    private BangActionResult PlayPunch(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (bang.BangsPlayedThisTurn >= 1 && !HasUnlimitedBang(me))
            return Fail("턴당 BANG! 1회 제한입니다.");
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        // Punch는 거리 1 강제 (사거리 무관)
        int dist = Distance(bang, me, target);
        if (dist > 1) return Fail($"Punch는 거리 1에서만 사용 가능합니다 (현재 {dist}).");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        bang.BangsPlayedThisTurn++;

        // Dodge City — Apache Kid: ♦ 카드 효과 무시
        if (ApacheKidImmune(card, target))
        {
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        int requiredMissed = me.Character == BangCharacter.SlabTheKiller ? 2 : 1;
        // Belle Star — 본인 턴이면 장착 Barrel은 무효, Jourdonnais 능력은 유지
        bool belleTurn = me.Character == BangCharacter.BelleStar
                         && bang.CurrentTurnPlayerId == me.PlayerId;
        bool hasBarrel = (!belleTurn && target.Equipment.Any(c => c.Type == BangCardType.Barrel))
                         || target.Character == BangCharacter.Jourdonnais;
        bool hasIronPlate = !belleTurn && HasSpadeShield(target);

        // Lucky Duke + Barrel → 수동 선택 pending
        if (hasBarrel && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "barrel_punch"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Punch Barrel 펴기 2장 중 선택 대기" };
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
                    return new BangActionResult { Success = true, Message = $"🛢 {target.Name}의 Barrel — Punch 회피!" };
                }
            }
        }
        if (!hasBarrel && hasIronPlate && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "ironplate_punch"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Punch Iron Plate 펴기 2장 중 선택 대기" };
        }
        if (hasIronPlate)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Spades);
            if (revealed != null && revealed.Suit == CardSuit.Spades)
            {
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"🛡 {target.Name}의 Iron Plate — Punch 회피!" };
            }
        }

        bang.Pending = new BangPending
        {
            Type = BangPendingType.BangResponse,
            TargetPlayerId = target.PlayerId,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            RequiredMissed = requiredMissed
        };
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 👊 Punch → {target.Name} (응답 대기)" };
    }

    // Dodge City: Whisky — 2 HP 회복 + 손패 1장 추가 버림 (총 2장 소비)
    private BangActionResult PlayWhisky(BangState bang, BangPlayer me, BangCard card)
    {
        int aliveCount = bang.Players.Count(p => p.IsAlive);
        if (aliveCount <= 2) return Fail("최후의 결전에서는 Whisky를 사용할 수 없습니다.");
        if (me.Health >= me.MaxHealth) return Fail("이미 최대 체력입니다.");
        // 추가로 버릴 카드가 있어야 함 (Whisky 자체 + 1장 = 손에 최소 2장)
        if (me.Hand.Count < 2) return Fail("Whisky 사용엔 추가로 버릴 카드 1장이 더 필요합니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        // 추가 1장 무작위 버림 (단순화 — 사용자 선택 모달 없이)
        var extra = me.Hand[_random.Next(me.Hand.Count)];
        me.Hand.Remove(extra);
        bang.DiscardPile.Add(extra);

        me.Health = Math.Min(me.MaxHealth, me.Health + 2);
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🥃 Whisky — HP {me.Health}/{me.MaxHealth}" };
    }

    // Dodge City: Tequila — 대상 1명(본인 가능)에게 1 HP 회복 + 손패 1장 추가 버림
    private BangActionResult PlayTequila(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.Health >= target.MaxHealth) return Fail($"{target.Name}은(는) 이미 최대 체력입니다.");
        if (me.Hand.Count < 2) return Fail("Tequila 사용엔 추가로 버릴 카드 1장이 더 필요합니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        var extra = me.Hand[_random.Next(me.Hand.Count)];
        me.Hand.Remove(extra);
        bang.DiscardPile.Add(extra);

        target.Health = Math.Min(target.MaxHealth, target.Health + 1);
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🍹 Tequila → {target.Name} HP {target.Health}/{target.MaxHealth}" };
    }

    // Dodge City — Springfield: 손패 1장 추가 버림 + BANG 효과 (사거리 무한, BANG 카운트 증가)
    private BangActionResult PlaySpringfield(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (bang.BangsPlayedThisTurn >= 1 && !HasUnlimitedBang(me))
            return Fail("턴당 BANG! 1회 제한입니다.");
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        if (me.Hand.Count < 2) return Fail("Springfield 사용엔 추가로 버릴 카드 1장이 더 필요합니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        var extra = me.Hand[_random.Next(me.Hand.Count)];
        me.Hand.Remove(extra);
        bang.DiscardPile.Add(extra);
        bang.BangsPlayedThisTurn++;

        if (ApacheKidImmune(card, target))
        {
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        int requiredMissed = me.Character == BangCharacter.SlabTheKiller ? 2 : 1;
        bool belleTurn = me.Character == BangCharacter.BelleStar
                         && bang.CurrentTurnPlayerId == me.PlayerId;
        bool hasBarrel = (!belleTurn && target.Equipment.Any(c => c.Type == BangCardType.Barrel))
                         || target.Character == BangCharacter.Jourdonnais;
        bool hasIronPlate = !belleTurn && HasSpadeShield(target);

        if (hasBarrel && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "barrel_springfield"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Springfield Barrel 펴기 2장 중 선택 대기" };
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
                    return new BangActionResult { Success = true, Message = $"🛢 {target.Name}의 Barrel — ♥ 자동 회피!" };
                }
            }
        }
        if (!hasBarrel && hasIronPlate && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "ironplate_springfield"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Springfield Iron Plate 펴기 2장 중 선택 대기" };
        }
        if (hasIronPlate)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Spades);
            if (revealed != null && revealed.Suit == CardSuit.Spades)
            {
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"🛡 {target.Name}의 Iron Plate — ♠ 자동 회피!" };
            }
        }

        bang.Pending = new BangPending
        {
            Type = BangPendingType.BangResponse,
            TargetPlayerId = target.PlayerId,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            RequiredMissed = requiredMissed
        };
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🎯 Springfield → {target.Name} (사거리 무한, 응답 대기)" };
    }

    // Dodge City — Buffalo Rifle: BANG 효과 (사거리 무한, BANG 카운트 X — 카드 1장으로 1회용)
    private BangActionResult PlayBuffaloRifle(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        if (ApacheKidImmune(card, target))
        {
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        int requiredMissed = 1;
        bool belleTurn = me.Character == BangCharacter.BelleStar
                         && bang.CurrentTurnPlayerId == me.PlayerId;
        bool hasBarrel = (!belleTurn && target.Equipment.Any(c => c.Type == BangCardType.Barrel))
                         || target.Character == BangCharacter.Jourdonnais;
        bool hasIronPlate = !belleTurn && HasSpadeShield(target);

        if (hasBarrel && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "barrel_buffalo"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Buffalo Rifle Barrel 펴기 2장 중 선택 대기" };
        }

        if (hasBarrel)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Hearts);
            if (revealed != null && revealed.Suit == CardSuit.Hearts)
            {
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"🛢 {target.Name}의 Barrel — ♥ 자동 회피!" };
            }
        }
        if (!hasBarrel && hasIronPlate && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "ironplate_buffalo"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Buffalo Rifle Iron Plate 펴기 2장 중 선택 대기" };
        }
        if (hasIronPlate)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Spades);
            if (revealed != null && revealed.Suit == CardSuit.Spades)
            {
                return new BangActionResult { Success = true, Message = $"🛡 {target.Name}의 Iron Plate — ♠ 자동 회피!" };
            }
        }

        bang.Pending = new BangPending
        {
            Type = BangPendingType.BangResponse,
            TargetPlayerId = target.PlayerId,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            RequiredMissed = requiredMissed
        };
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🦬 Buffalo Rifle → {target.Name} (사거리 무한, 응답 대기)" };
    }

    // Dodge City — Brawl: 손패 1장 추가 버림 + 다른 모두가 손패 1장씩 강제 버림 (랜덤)
    private BangActionResult PlayBrawl(BangState bang, BangPlayer me, BangCard card)
    {
        if (me.Hand.Count < 2) return Fail("Brawl 사용엔 추가로 버릴 카드 1장이 더 필요합니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        var extra = me.Hand[_random.Next(me.Hand.Count)];
        me.Hand.Remove(extra);
        bang.DiscardPile.Add(extra);

        var lost = new List<string>();
        foreach (var p in bang.Players.Where(p => p.IsAlive && p.PlayerId != me.PlayerId))
        {
            if (p.Hand.Count == 0) continue;
            var dropped = p.Hand[_random.Next(p.Hand.Count)];
            p.Hand.Remove(dropped);
            bang.DiscardPile.Add(dropped);
            lost.Add(p.Name);
        }
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🥊 Brawl — {string.Join(", ", lost)} 손패 1장 강제 버림" };
    }

    // Dodge City — Rag Time: 손패 1장 추가 버림 + 다른 사람 손에서 1장 가져옴 (랜덤, 거리 무한)
    private BangActionResult PlayRagTime(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        if (target.Hand.Count == 0) return Fail("대상의 손패가 비어 있습니다.");
        if (me.Hand.Count < 2) return Fail("Rag Time 사용엔 추가로 버릴 카드 1장이 더 필요합니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        var extra = me.Hand[_random.Next(me.Hand.Count)];
        me.Hand.Remove(extra);
        bang.DiscardPile.Add(extra);

        var taken = target.Hand[_random.Next(target.Hand.Count)];
        target.Hand.Remove(taken);
        me.Hand.Add(taken);
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{me.Name} 🎼 Rag Time → {target.Name} 손에서 1장 가져옴" };
    }

    // Dodge City — Can Can: 대상의 손/장비/무기 카드 1장 즉시 버림 (거리 무한, Cat Balou + 장비 가능)
    private BangActionResult PlayCanCan(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        if (target.Hand.Count == 0 && target.Weapon == null && target.Equipment.Count == 0)
            return Fail("대상에게 버릴 카드가 없습니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        bang.Pending = new BangPending
        {
            Type = BangPendingType.CardPickFromTarget,
            SourcePlayerId = me.PlayerId,
            TargetPlayerId = target.PlayerId,
            CardId = card.Id,
            PickToHand = false // Cat Balou 동작 (버리기)
        };
        return new BangActionResult { Success = true, Message = $"{me.Name} 💃 Can Can → {target.Name}의 카드 선택 중 (장비 포함)" };
    }

    private BangActionResult EquipWeapon(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        if (me.Weapon != null) bang.DiscardPile.Add(me.Weapon);
        me.Weapon = card;
        return new BangActionResult { Success = true, Message = $"{me.Name} {card.Type} 장착 (사거리 {GetWeaponRange(card)})" };
    }

    private BangActionResult EquipGear(BangState bang, BangPlayer me, BangCard card)
    {
        if (me.Equipment.Any(c => c.Type == card.Type))
            return Fail($"{card.Type}을(를) 이미 장착했습니다.");
        me.Hand.Remove(card);
        me.Equipment.Add(card);
        return new BangActionResult { Success = true, Message = $"{me.Name} {card.Type} 장착" };
    }

    private BangActionResult PlayJail(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        if (target.Role == BangRole.Sheriff) return Fail("보안관에게는 Jail을 사용할 수 없습니다.");
        if (target.Equipment.Any(c => c.Type == BangCardType.Jail)) return Fail("이미 감옥에 갇혀 있습니다.");

        me.Hand.Remove(card);

        // Dodge City — Apache Kid: ♦ 카드 효과 무시 (Jail이 ♦면 그냥 버림)
        if (ApacheKidImmune(card, target))
        {
            bang.DiscardPile.Add(card);
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ Jail 무시" };
        }

        target.Equipment.Add(card);
        return new BangActionResult { Success = true, Message = $"{me.Name} → {target.Name} 감옥행" };
    }

    private BangActionResult PlayBang(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (bang.BangsPlayedThisTurn >= 1 && !HasUnlimitedBang(me))
            return Fail("턴당 BANG! 1회 제한입니다.");
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        int dist = Distance(bang, me, target);
        int range = GetWeaponRange(me.Weapon);
        if (dist > range) return Fail($"거리({dist})가 사거리({range})보다 멀어 BANG 닿지 않습니다.");

        // 카드 소비 + BANG 횟수 카운트
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        bang.BangsPlayedThisTurn++;

        // Dodge City — Apache Kid: ♦ 카드 효과 무시
        if (ApacheKidImmune(card, target))
        {
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        // Slab the Killer — BANG에 Missed 2장 필요
        int requiredMissed = me.Character == BangCharacter.SlabTheKiller ? 2 : 1;

        // Barrel 자동 펴기 — ♥이면 Missed 1장 차감. Jourdonnais는 Barrel 기본 보유
        // Belle Star — 본인 턴이면 장착 Barrel은 무효, Jourdonnais 능력은 유지
        bool belleTurn = me.Character == BangCharacter.BelleStar
                         && bang.CurrentTurnPlayerId == me.PlayerId;
        bool hasBarrel = (!belleTurn && target.Equipment.Any(c => c.Type == BangCardType.Barrel))
                         || target.Character == BangCharacter.Jourdonnais;
        bool hasIronPlate = !belleTurn && HasSpadeShield(target);

        // Lucky Duke + Barrel — 두 카드 펴서 사용자에게 직접 선택하도록 pending 설정
        if (hasBarrel && target.Character == BangCharacter.LuckyDuke)
        {
            var c1 = DrawCard(bang);
            var c2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "barrel_bang"
            };
            if (c1 != null) pending.RevealedCards.Add(c1);
            if (c2 != null) pending.RevealedCards.Add(c2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Barrel 펴기 2장 중 선택 대기" };
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
                    return new BangActionResult { Success = true, Message = $"🛢 {target.Name}의 Barrel — ♥ {revealed.Rank} 자동 회피!" };
                }
            }
        }
        // IronPlate-only LuckyDuke 모달 (Barrel 없을 때만)
        if (!hasBarrel && hasIronPlate && target.Character == BangCharacter.LuckyDuke)
        {
            var lc1 = DrawCard(bang);
            var lc2 = DrawCard(bang);
            var pending = new BangPending
            {
                Type = BangPendingType.LuckyDukeReveal,
                TargetPlayerId = target.PlayerId,
                SourcePlayerId = me.PlayerId,
                CardId = card.Id,
                RequiredMissed = requiredMissed,
                Context = "ironplate_bang"
            };
            if (lc1 != null) pending.RevealedCards.Add(lc1);
            if (lc2 != null) pending.RevealedCards.Add(lc2);
            bang.Pending = pending;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🍀 {target.Name}의 Lucky Duke — Iron Plate 펴기 2장 중 선택 대기" };
        }

        if (hasIronPlate)
        {
            var revealed = DrawReveal(bang, target, c => c.Suit == CardSuit.Spades);
            if (revealed != null && revealed.Suit == CardSuit.Spades)
            {
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"🛡 {target.Name}의 Iron Plate — ♠ {revealed.Rank} 자동 회피!" };
            }
        }

        bang.Pending = new BangPending
        {
            Type = BangPendingType.BangResponse,
            TargetPlayerId = target.PlayerId,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            RequiredMissed = requiredMissed
        };
        TriggerSuzyLafayette(bang);
        string slabHint = requiredMissed > 1 ? $" (Slab의 BANG — Missed {requiredMissed}장 필요)" : "";
        return new BangActionResult { Success = true, Message = $"{me.Name} → {target.Name}에게 BANG!{slabHint} (응답 대기)" };
    }

    private BangActionResult PlayWellsFargo(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        DrawToHand(bang, me);
        DrawToHand(bang, me);
        DrawToHand(bang, me);
        return new BangActionResult { Success = true, Message = $"{me.Name} Wells Fargo — 카드 3장 획득" };
    }

    private BangActionResult PlaySaloon(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        var healed = new List<string>();
        foreach (var p in bang.Players.Where(p => p.IsAlive && p.Health < p.MaxHealth))
        {
            p.Health += 1;
            healed.Add(p.Name);
        }
        return new BangActionResult { Success = true, Message = $"{me.Name} Saloon — 모두 회복 ({string.Join(", ", healed)})" };
    }

    // CatBalou(버리기) / Panic(가져오기) 공통 처리 — 대상 검증 후 카드 선택 pending 설정
    private BangActionResult PlayCardPick(BangState bang, BangPlayer me, BangCard card, string? targetId, bool pickToHand, bool requireDistance1)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        if (target.Hand.Count == 0 && target.Weapon == null && target.Equipment.Count == 0)
            return Fail("대상에게 가져갈/버릴 카드가 없습니다.");
        if (requireDistance1)
        {
            int dist = Distance(bang, me, target);
            if (dist > 1) return Fail($"거리({dist})가 멀어 사용할 수 없습니다 (Panic은 거리 1).");
        }

        // 카드 소비 + pending 설정
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        // Dodge City — Apache Kid: ♦ 카드 효과 무시
        if (ApacheKidImmune(card, target))
        {
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        bang.Pending = new BangPending
        {
            Type = BangPendingType.CardPickFromTarget,
            SourcePlayerId = me.PlayerId,
            TargetPlayerId = target.PlayerId,
            CardId = card.Id,
            PickToHand = pickToHand
        };
        string action = pickToHand ? "Panic" : "Cat Balou";
        return new BangActionResult { Success = true, Message = $"{me.Name} {action} → {target.Name}의 카드 선택 중" };
    }

    private BangActionResult PlayGeneralStore(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        // 살아있는 플레이어 수만큼 카드 펴기
        int alive = bang.Players.Count(p => p.IsAlive);
        var revealed = new List<BangCard>();
        for (int i = 0; i < alive; i++)
        {
            var c = DrawCard(bang);
            if (c != null) revealed.Add(c);
        }
        if (revealed.Count == 0) return Fail("덱이 비어 General Store를 사용할 수 없습니다.");

        // 시계방향 응답 큐: 사용자부터 시작
        var queue = ClockwiseAliveStartingAt(bang, me.PlayerId, includeSelf: true);
        bang.Pending = new BangPending
        {
            Type = BangPendingType.GeneralStorePick,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            RevealedCards = revealed,
            ResponderQueue = queue
        };
        return new BangActionResult { Success = true, Message = $"{me.Name} General Store — {revealed.Count}장 펴짐. {me.Name}부터 선택" };
    }

    private BangActionResult PlayIndians(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        // 사용자 제외 살아있는 사람 시계방향 응답 큐
        var queue = ClockwiseAliveStartingAt(bang, me.PlayerId, includeSelf: false);
        // Dodge City — Apache Kid: ♦ 카드는 본인에게 영향 X
        if (card.Suit == CardSuit.Diamonds)
        {
            queue.RemoveAll(pid => bang.Players.FirstOrDefault(p => p.PlayerId == pid)?.Character == BangCharacter.ApacheKid);
        }
        if (queue.Count == 0) return new BangActionResult { Success = true, Message = "Indians — 대상이 없습니다." };

        bang.Pending = new BangPending
        {
            Type = BangPendingType.IndiansResponse,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            ResponderQueue = queue
        };
        return new BangActionResult { Success = true, Message = $"{me.Name} Indians — 각자 BANG으로 응답해야 함" };
    }

    private BangActionResult PlayGatling(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        var queue = ClockwiseAliveStartingAt(bang, me.PlayerId, includeSelf: false);
        // Dodge City — Apache Kid: ♦ 카드는 본인에게 영향 X
        if (card.Suit == CardSuit.Diamonds)
        {
            queue.RemoveAll(pid => bang.Players.FirstOrDefault(p => p.PlayerId == pid)?.Character == BangCharacter.ApacheKid);
        }
        if (queue.Count == 0) return new BangActionResult { Success = true, Message = "Gatling — 대상이 없습니다." };

        bang.Pending = new BangPending
        {
            Type = BangPendingType.GatlingResponse,
            SourcePlayerId = me.PlayerId,
            CardId = card.Id,
            ResponderQueue = queue
        };
        return new BangActionResult { Success = true, Message = $"{me.Name} Gatling — 각자 Missed로 응답해야 함" };
    }

    private BangActionResult PlayDuel(BangState bang, BangPlayer me, BangCard card, string? targetId)
    {
        if (string.IsNullOrEmpty(targetId)) return Fail("대상이 필요합니다.");
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == targetId);
        if (target == null || !target.IsAlive) return Fail("대상이 유효하지 않습니다.");
        if (target.PlayerId == me.PlayerId) return Fail("자신에게는 사용할 수 없습니다.");
        // Duel은 거리 무관

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);

        // Dodge City — Apache Kid: ♦ 카드 효과 무시
        if (ApacheKidImmune(card, target))
        {
            return new BangActionResult { Success = true, Message = $"🪶 {target.Name} (Apache Kid) — ♦ 카드 효과 무시" };
        }

        // 대상부터 응답 — TargetPlayerId가 현재 응답자, SourcePlayerId가 다음 응답자
        bang.Pending = new BangPending
        {
            Type = BangPendingType.DuelResponse,
            SourcePlayerId = me.PlayerId,
            TargetPlayerId = target.PlayerId,
            CardId = card.Id
        };
        return new BangActionResult { Success = true, Message = $"{me.Name} → {target.Name}에게 Duel! ({target.Name} BANG 응답)" };
    }

    // 시계방향 살아있는 플레이어 목록 (start 다음부터). includeSelf=true면 start 본인부터.
    private List<string> ClockwiseAliveStartingAt(BangState bang, string startId, bool includeSelf)
    {
        var start = bang.Players.FirstOrDefault(p => p.PlayerId == startId);
        if (start == null) return new List<string>();
        int n = bang.Players.Count;
        var result = new List<string>();
        for (int step = includeSelf ? 0 : 1; step < n; step++)
        {
            int idx = (start.SeatIndex + step) % n;
            var p = bang.Players[idx];
            if (p.IsAlive) result.Add(p.PlayerId);
        }
        return result;
    }

    private BangActionResult PlayBeer(BangState bang, BangPlayer me, BangCard card)
    {
        // 마지막 2명일 때는 Beer 사용 불가 (원룰)
        int alive = bang.Players.Count(p => p.IsAlive);
        if (alive <= 2) return Fail("최후의 결전에서는 Beer를 사용할 수 없습니다.");
        if (me.Health >= me.MaxHealth) return Fail("이미 최대 체력입니다.");

        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        me.Health = Math.Min(me.MaxHealth, me.Health + 1);
        return new BangActionResult { Success = true, Message = $"{me.Name} 체력 회복 → {me.Health}/{me.MaxHealth}" };
    }

    private BangActionResult PlayStagecoach(BangState bang, BangPlayer me, BangCard card)
    {
        me.Hand.Remove(card);
        bang.DiscardPile.Add(card);
        DrawToHand(bang, me);
        DrawToHand(bang, me);
        return new BangActionResult { Success = true, Message = $"{me.Name} 카드 2장 획득" };
    }

}
