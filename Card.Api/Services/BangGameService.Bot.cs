using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    /// <summary>봇이 멈춘 이유 진단 — 사람 응답 대기 / 사람 차례 / 봇 EndTurn 실패 등.
    /// ProcessOneBotAction이 didAction=false 반환 시 호출. 디버깅용.</summary>
    public string? GetBotStallReason(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return "방 없음";
        var bang = room.Bang;
        if (bang.IsFinished) return "게임 종료";

        if (bang.Pending != null)
        {
            var pending = bang.Pending;
            string responderId = pending.Type switch
            {
                BangPendingType.BangResponse => pending.TargetPlayerId ?? "?",
                BangPendingType.DuelResponse => pending.TargetPlayerId ?? "?",
                BangPendingType.LuckyDukeReveal => pending.TargetPlayerId ?? "?",
                BangPendingType.IndiansResponse => pending.ResponderQueue.FirstOrDefault() ?? "?",
                BangPendingType.GatlingResponse => pending.ResponderQueue.FirstOrDefault() ?? "?",
                BangPendingType.GeneralStorePick => pending.ResponderQueue.FirstOrDefault() ?? "?",
                _ => pending.SourcePlayerId ?? "?"
            };
            var responder = bang.Players.FirstOrDefault(p => p.PlayerId == responderId);
            return $"pending={pending.Type}, 응답자={responder?.Name ?? "?"} (봇={responder?.IsBot})";
        }

        var current = bang.Players.FirstOrDefault(p => p.PlayerId == bang.CurrentTurnPlayerId);
        if (current == null) return "currentTurn 플레이어 없음";
        if (!current.IsAlive) return $"{current.Name} 사망 (다음 이동 실패)";
        if (!current.IsBot) return $"사람 차례 ({current.Name})";
        return $"봇 {current.Name} 액션 불가 (손패 {current.Hand.Count}장, BANG 사용 {bang.BangsPlayedThisTurn})";
    }

    /// <summary>한 가지 봇 액션만 시도. 봇이 액션했으면 (didAction=true, log), 더 액션 없으면 (false, null).
    /// Hub에서 await Task.Delay와 함께 루프 호출하여 봇 사고 지연 시뮬레이션.</summary>
    public (bool didAction, string? log) ProcessOneBotAction(string roomId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null || room.Bang.IsFinished) return (false, null);
        var bang = room.Bang;

        // 1) Pending 응답이 봇 대상이면 자동 처리
        if (bang.Pending != null)
        {
            var pending = bang.Pending;
            BangPlayer? botResponder = null;
            if (pending.Type == BangPendingType.BangResponse || pending.Type == BangPendingType.DuelResponse)
                botResponder = bang.Players.FirstOrDefault(p => p.PlayerId == pending.TargetPlayerId && p.IsBot && p.IsAlive);
            else if (pending.Type == BangPendingType.IndiansResponse || pending.Type == BangPendingType.GatlingResponse || pending.Type == BangPendingType.GeneralStorePick)
            {
                if (pending.ResponderQueue.Count > 0)
                    botResponder = bang.Players.FirstOrDefault(p => p.PlayerId == pending.ResponderQueue[0] && p.IsBot && p.IsAlive);
            }
            else if (pending.Type == BangPendingType.CardPickFromTarget || pending.Type == BangPendingType.PedroDraw || pending.Type == BangPendingType.JesseDraw || pending.Type == BangPendingType.KitDraw)
                botResponder = bang.Players.FirstOrDefault(p => p.PlayerId == pending.SourcePlayerId && p.IsBot && p.IsAlive);
            else if (pending.Type == BangPendingType.LuckyDukeReveal)
                botResponder = bang.Players.FirstOrDefault(p => p.PlayerId == pending.TargetPlayerId && p.IsBot && p.IsAlive);

            if (botResponder == null) return (false, null);
            var r = HandleBotResponse(roomId, bang, botResponder, pending);
            return (true, r);
        }

        // 2) 현재 턴 플레이어 사망 시 다음 사람으로 자동 이동
        var current = bang.Players.FirstOrDefault(p => p.PlayerId == bang.CurrentTurnPlayerId);
        if (current == null) return (false, null);
        if (!current.IsAlive)
        {
            var nx = NextAlive(bang, current);
            if (nx == null) return (false, null);
            bang.CurrentTurnPlayerId = nx.PlayerId;
            var nxLog = StartTurnFor(bang, nx);
            return (true, $"({current.Name} 사망) {nx.Name}의 차례" + (!string.IsNullOrEmpty(nxLog) ? $" / {nxLog}" : ""));
        }
        if (!current.IsBot) return (false, null);

        // 3) 봇 본인 턴 — 카드 사용 또는 턴 종료
        var actionLog = BotPlayOneAction(roomId, bang, current);
        if (actionLog != null) return (true, actionLog);

        // 더 할 액션 없음 → 턴 종료
        var excess = current.Hand.Count - current.Health;
        List<int>? discardIds = null;
        if (excess > 0)
            discardIds = current.Hand.OrderBy(_ => _random.Next()).Take(excess).Select(c => c.Id).ToList();
        var er = EndTurn(roomId, current.PlayerId, discardIds);
        if (!er.Success)
        {
            // EndTurn fail 시 진단 + 다음 사람으로 강제 이동 (데드락 방지)
            var failReason = er.Error ?? "원인 불명";
            var nx2 = NextAlive(bang, current);
            if (nx2 != null && nx2.PlayerId != current.PlayerId)
            {
                bang.CurrentTurnPlayerId = nx2.PlayerId;
                var nxLog2 = StartTurnFor(bang, nx2);
                return (true, $"⚠️ 봇 {current.Name} EndTurn 실패({failReason}) → {nx2.Name}로 강제 이동" + (!string.IsNullOrEmpty(nxLog2) ? $" / {nxLog2}" : ""));
            }
            return (false, $"⚠️ 봇 EndTurn 실패: {failReason}");
        }
        return (true, $"🤖 {current.Name}: 턴 종료" + (!string.IsNullOrEmpty(er.Message) ? $" / {er.Message}" : ""));
    }

    // 봇 타겟 선정 — 역할 기반.
    //  Outlaw: Sheriff 최우선, 그 다음 Deputy, 그 다음 Renegade, 동률은 HP 낮은 순
    //  Deputy: Sheriff 제외하고 가장 약한 적 (Outlaw 우선)
    //  Sheriff: Outlaw/Renegade 우선 (Deputy 제외), 그 다음 HP 낮은 순
    //  Renegade: 가장 약한 적 (모두 균등 대상)
    private BangPlayer? PickEnemyByRole(IEnumerable<BangPlayer> enemies, BangPlayer me)
    {
        if (enemies == null || !enemies.Any()) return null;
        int Priority(BangPlayer p)
        {
            return me.Role switch
            {
                BangRole.Outlaw => p.Role switch { BangRole.Sheriff => 0, BangRole.Deputy => 1, BangRole.Renegade => 2, _ => 3 },
                BangRole.Deputy => p.Role switch { BangRole.Outlaw => 0, BangRole.Renegade => 1, BangRole.Sheriff => 99, _ => 2 },
                BangRole.Sheriff => p.Role switch { BangRole.Outlaw => 0, BangRole.Renegade => 1, BangRole.Deputy => 99, _ => 2 },
                BangRole.Renegade => 0,
                _ => 0
            };
        }
        return enemies
            .OrderBy(Priority)
            .ThenBy(p => p.Health)
            .FirstOrDefault();
    }

    /// <summary>봇 본인 턴 한 가지 액션 시도. 성공 시 메시지 반환, 더 할 액션 없으면 null.</summary>
    private string? BotPlayOneAction(string roomId, BangState bang, BangPlayer bot)
    {
        if (bang.Pending != null) return null; // pending 있으면 액션 X

        // 0. 캐릭터 능력 자동 사용 (가장 안전한 것부터)
        // Sid Ketchum — HP 부족하고 손 2장 이상이면 HP +1 (BANG 카드 보존)
        if (bot.Character == BangCharacter.SidKetchum && bot.Health < bot.MaxHealth && bot.Hand.Count >= 4)
        {
            // BANG/Missed가 아닌 카드 2장 우선 버림
            var discardable = bot.Hand
                .Where(c => c.Type != BangCardType.Bang && c.Type != BangCardType.Missed)
                .Take(2).Select(c => c.Id).ToList();
            if (discardable.Count < 2)
            {
                // 부족하면 일반 손패에서 보충
                var rest = bot.Hand.Select(c => c.Id).Except(discardable).Take(2 - discardable.Count);
                discardable.AddRange(rest);
            }
            if (discardable.Count == 2)
            {
                var r = UseSidKetchum(roomId, bot.PlayerId, discardable);
                if (r.Success) return $"🤖 {bot.Name} 🎭 Sid Ketchum 능력 사용";
            }
        }
        // Chuck Wengam — HP > 2 + 손패 부족하면 사용
        if (bot.Character == BangCharacter.ChuckWengam && bot.Health > 2 && bot.Hand.Count < 2 && bang.CurrentTurnPlayerId == bot.PlayerId)
        {
            var r = UseChuckWengam(roomId, bot.PlayerId);
            if (r.Success) return $"🤖 {bot.Name} 🎲 Chuck Wengam 능력 사용 (HP -1, 카드 +2)";
        }
        // Doc Holyday — 본인 턴 + 능력 미사용 + 손패 3장 이상 + 사거리 내 적 있으면 능력 BANG
        if (bot.Character == BangCharacter.DocHolyday
            && bang.CurrentTurnPlayerId == bot.PlayerId
            && !bang.DocAbilityUsedThisTurn
            && bot.Hand.Count >= 3)
        {
            int range = GetWeaponRange(bot.Weapon);
            var target = bang.Players
                .Where(p => p.IsAlive && p.PlayerId != bot.PlayerId)
                .Where(p => Distance(bang, bot, p) <= range)
                .OrderBy(p => p.Health)
                .FirstOrDefault();
            if (target != null)
            {
                // BANG/Missed가 아닌 카드 2장 우선 버림
                var discardable = bot.Hand
                    .Where(c => c.Type != BangCardType.Bang && c.Type != BangCardType.Missed)
                    .Take(2).Select(c => c.Id).ToList();
                if (discardable.Count < 2)
                {
                    var rest = bot.Hand.Select(c => c.Id).Except(discardable).Take(2 - discardable.Count);
                    discardable.AddRange(rest);
                }
                if (discardable.Count == 2)
                {
                    var r = UseDocHolyday(roomId, bot.PlayerId, discardable, target.PlayerId);
                    if (r.Success) return $"🤖 {bot.Name} 🩺 Doc Holyday 능력 → {target.Name}";
                }
            }
        }

        // 1. 거리 내 적이 있고 BANG 가능하면 사용 (역할 기반 타겟팅)
        bool canBang = (bang.BangsPlayedThisTurn < 1) || HasUnlimitedBang(bot);
        if (canBang)
        {
            var bangCard = bot.Hand.FirstOrDefault(c => CanActAsBang(c, bot));
            if (bangCard != null)
            {
                int range = GetWeaponRange(bot.Weapon);
                var enemies = bang.Players
                    .Where(p => p.IsAlive && p.PlayerId != bot.PlayerId)
                    .Where(p => Distance(bang, bot, p) <= range);
                var target = PickEnemyByRole(enemies, bot);
                if (target != null)
                {
                    var r = PlayCard(roomId, bot.PlayerId, bangCard.Id, target.PlayerId);
                    if (r.Success) return $"🤖 {bot.Name} BANG → {target.Name}";
                }
            }
        }

        // 2. Beer — 응급 비축 우선. HP가 위험(1)이거나 손패 풍부할 때만 사용
        var beer = bot.Hand.FirstOrDefault(c => c.Type == BangCardType.Beer);
        if (beer != null && bot.Health < bot.MaxHealth)
        {
            int aliveCount = bang.Players.Count(p => p.IsAlive);
            if (aliveCount > 2)
            {
                // HP가 1이면 즉시 사용 (사망 방지). HP > 1이면 손패가 체력의 1.5배 이상일 때만 (다음 턴 비축)
                bool urgent = bot.Health == 1;
                bool safe = bot.Hand.Count >= bot.Health * 2;  // 손패 충분히 많을 때만 회복에 투자
                if (urgent || safe)
                {
                    var r = PlayCard(roomId, bot.PlayerId, beer.Id, null);
                    if (r.Success) return $"🤖 {bot.Name} 🍺 맥주";
                }
            }
        }

        // 3. Saloon (체력 부족 + 보안관팀이면 우선)
        var saloon = bot.Hand.FirstOrDefault(c => c.Type == BangCardType.Saloon);
        if (saloon != null && bot.Health < bot.MaxHealth)
        {
            var r = PlayCard(roomId, bot.PlayerId, saloon.Id, null);
            if (r.Success) return $"🤖 {bot.Name} 🍻 Saloon";
        }

        // 4. 무기 장착 (사거리 더 긴 무기로 교체) — 현재 없거나 사거리 < 카드 사거리
        var weaponCard = bot.Hand.FirstOrDefault(c =>
            c.Type == BangCardType.Volcanic || c.Type == BangCardType.Scofield ||
            c.Type == BangCardType.Remington || c.Type == BangCardType.Carbine ||
            c.Type == BangCardType.Winchester || c.Type == BangCardType.Pepperbox ||
            c.Type == BangCardType.RevCarabine);
        if (weaponCard != null)
        {
            int currentRange = GetWeaponRange(bot.Weapon);
            int newRange = GetWeaponRange(weaponCard);
            if (bot.Weapon == null || newRange > currentRange)
            {
                var r = PlayCard(roomId, bot.PlayerId, weaponCard.Id, null);
                if (r.Success) return $"🤖 {bot.Name} 🔫 {weaponCard.Type} 장착";
            }
        }

        // 5. 본인 장비 (Mustang/Scope/Barrel/IronPlate) — 같은 종류 없을 때만
        var gear = bot.Hand.FirstOrDefault(c =>
            (c.Type == BangCardType.Mustang || c.Type == BangCardType.Scope ||
             c.Type == BangCardType.Barrel || c.Type == BangCardType.IronPlate ||
             c.Type == BangCardType.TenGallonHat || c.Type == BangCardType.Bibles)
            && !bot.Equipment.Any(e => e.Type == c.Type));
        if (gear != null)
        {
            var r = PlayCard(roomId, bot.PlayerId, gear.Id, null);
            if (r.Success) return $"🤖 {bot.Name} 🛡 {gear.Type} 장착";
        }

        // 6. Stagecoach / Wells Fargo — 손패 충원
        var draw = bot.Hand.FirstOrDefault(c => c.Type == BangCardType.Stagecoach || c.Type == BangCardType.WellsFargo);
        if (draw != null)
        {
            var r = PlayCard(roomId, bot.PlayerId, draw.Id, null);
            if (r.Success) return $"🤖 {bot.Name} 카드 드로우";
        }

        // 7. Cat Balou / Panic — 가장 위협적인 적 (역할 기반 + 손패/장비 양 고려)
        var catBalou = bot.Hand.FirstOrDefault(c => c.Type == BangCardType.CatBalou || c.Type == BangCardType.CanCan);
        if (catBalou != null)
        {
            var enemies = bang.Players
                .Where(p => p.IsAlive && p.PlayerId != bot.PlayerId)
                .Where(p => p.Hand.Count > 0 || p.Weapon != null || p.Equipment.Count > 0);
            var target = PickEnemyByRole(enemies, bot)
                ?? enemies.OrderByDescending(p => p.Hand.Count + p.Equipment.Count + (p.Weapon != null ? 1 : 0)).FirstOrDefault();
            if (target != null)
            {
                var r = PlayCard(roomId, bot.PlayerId, catBalou.Id, target.PlayerId);
                if (r.Success) return $"🤖 {bot.Name} Cat Balou → {target.Name}";
            }
        }

        // 8. Indians / Gatling (살아있는 적이 많을 때)
        var aoe = bot.Hand.FirstOrDefault(c => c.Type == BangCardType.Indians || c.Type == BangCardType.Gatling);
        if (aoe != null && bang.Players.Count(p => p.IsAlive) >= 3)
        {
            var r = PlayCard(roomId, bot.PlayerId, aoe.Id, null);
            if (r.Success) return $"🤖 {bot.Name} {aoe.Type}";
        }

        return null; // 더 할 액션 없음 — 턴 종료
    }

    private string? HandleBotResponse(string roomId, BangState bang, BangPlayer bot, BangPending pending)
    {
        // 단순 휴리스틱:
        //  BangResponse — Missed 가지고 있으면 사용, 아니면 데미지 수용
        //  DuelResponse — BANG 가지고 있으면 사용, 아니면 데미지 수용
        //  IndiansResponse — BANG 가지고 있으면 사용, 아니면 데미지 수용
        //  GatlingResponse — Missed 가지고 있으면 사용, 아니면 데미지 수용
        //  CardPickFromTarget — 대상 첫 손패 또는 첫 장비 자동 선택
        //  GeneralStorePick — 첫 카드 자동 선택
        //  Pedro/Jesse/Kit — 덱에서 / 첫 카드
        //  LuckyDukeReveal — 첫 카드 선택
        switch (pending.Type)
        {
            case BangPendingType.BangResponse:
            {
                bool hasMissed = bot.Hand.Any(c => CanActAsMissed(c, bot));
                int missedCount = bot.Hand.Count(c => CanActAsMissed(c, bot));
                // 위험 평가: HP 1이거나 (HP 낮고 Missed 여유) → 회피, 아니면 데미지 수용 (카드 보존)
                bool shouldDodge;
                if (!hasMissed) shouldDodge = false;
                else if (bot.Health <= 1) shouldDodge = true;
                else if (missedCount >= 2) shouldDodge = true;
                else if (bot.Health <= 2) shouldDodge = true;  // 낮은 HP는 보호
                else shouldDodge = false;  // HP 충분 + Missed 1장 = 보존
                var r = RespondBang(roomId, bot.PlayerId, shouldDodge);
                return $"🤖 {bot.Name} 뱅! 응답 ({(shouldDodge ? "빗나감" : "수용")})";
            }
            case BangPendingType.DuelResponse:
            {
                bool hasBang = bot.Hand.Any(c => CanActAsBang(c, bot));
                var r = RespondDuel(roomId, bot.PlayerId, hasBang);
                return $"🤖 {bot.Name} Duel 응답 ({(hasBang ? "BANG" : "수용")})";
            }
            case BangPendingType.IndiansResponse:
            {
                bool hasBang = bot.Hand.Any(c => CanActAsBang(c, bot));
                var r = RespondIndians(roomId, bot.PlayerId, hasBang);
                return $"🤖 {bot.Name} Indians ({(hasBang ? "BANG" : "수용")})";
            }
            case BangPendingType.GatlingResponse:
            {
                bool hasMissed = bot.Hand.Any(c => CanActAsMissed(c, bot));
                var r = RespondGatling(roomId, bot.PlayerId, hasMissed);
                return $"🤖 {bot.Name} Gatling ({(hasMissed ? "Missed" : "수용")})";
            }
            case BangPendingType.CardPickFromTarget:
            {
                var target = bang.Players.FirstOrDefault(p => p.PlayerId == pending.TargetPlayerId);
                int cardId;
                if (target?.Hand.Count > 0) cardId = target.Hand[0].Id;
                else if (target?.Weapon != null) cardId = target.Weapon.Id;
                else if (target?.Equipment.Count > 0) cardId = target.Equipment[0].Id;
                else { bang.Pending = null; return null; }
                var r = PickCardFromTarget(roomId, bot.PlayerId, cardId);
                return $"🤖 {bot.Name} Cat Balou/Panic 첫 카드 선택";
            }
            case BangPendingType.GeneralStorePick:
            {
                var first = pending.RevealedCards.FirstOrDefault();
                if (first == null) { bang.Pending = null; return null; }
                var r = PickFromGeneralStore(roomId, bot.PlayerId, first.Id);
                return $"🤖 {bot.Name} General Store 첫 카드";
            }
            case BangPendingType.PedroDraw:
            {
                var r = PedroRamirezChoice(roomId, bot.PlayerId, true);
                return $"🤖 {bot.Name} Pedro 덱에서";
            }
            case BangPendingType.JesseDraw:
            {
                var r = JesseJonesChoice(roomId, bot.PlayerId, true, null);
                return $"🤖 {bot.Name} Jesse 덱에서";
            }
            case BangPendingType.KitDraw:
            {
                var first = pending.RevealedCards.FirstOrDefault();
                if (first == null) { bang.Pending = null; return null; }
                var r = KitCarlsonChoice(roomId, bot.PlayerId, first.Id);
                return $"🤖 {bot.Name} Kit 첫 카드 덱으로";
            }
            case BangPendingType.LuckyDukeReveal:
            {
                var first = pending.RevealedCards.FirstOrDefault();
                if (first == null) { bang.Pending = null; return null; }
                var r = ChooseLuckyDuke(roomId, bot.PlayerId, first.Id);
                return $"🤖 {bot.Name} Lucky Duke 첫 카드";
            }
        }
        return null;
    }

}
