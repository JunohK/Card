using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    /// <summary>General Store 순서 시계방향으로 1장씩 선택.</summary>
    public BangActionResult PickFromGeneralStore(string roomId, string playerId, int cardId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.GeneralStorePick)
            return Fail("General Store 진행 중이 아닙니다.");
        if (pending.ResponderQueue.Count == 0 || pending.ResponderQueue[0] != playerId)
            return Fail("당신 차례가 아닙니다.");

        var picked = pending.RevealedCards.FirstOrDefault(c => c.Id == cardId);
        if (picked == null) return Fail("선택한 카드가 펴진 풀에 없습니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        if (me == null) return Fail("플레이어 없음.");

        // 카드 이동
        pending.RevealedCards.Remove(picked);
        me.Hand.Add(picked);
        pending.ResponderQueue.RemoveAt(0);

        // 마지막 1장은 자동으로 남은 사람에게 (정통 룰)
        if (pending.RevealedCards.Count == 1 && pending.ResponderQueue.Count == 1)
        {
            var last = pending.RevealedCards[0];
            var lastPlayer = bang.Players.First(p => p.PlayerId == pending.ResponderQueue[0]);
            lastPlayer.Hand.Add(last);
            pending.RevealedCards.Clear();
            pending.ResponderQueue.Clear();
        }

        if (pending.ResponderQueue.Count == 0 || pending.RevealedCards.Count == 0)
        {
            bang.Pending = null;
            return new BangActionResult { Success = true, Message = $"{me.Name} 카드 획득. General Store 종료" };
        }
        return new BangActionResult { Success = true, Message = $"{me.Name} 카드 획득. 다음 선택" };
    }

    /// <summary>Indians 응답. useBang=true면 BANG 카드 소비, false면 데미지 수용.</summary>
    public BangActionResult RespondIndians(string roomId, string playerId, bool useBang)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.IndiansResponse)
            return Fail("Indians 진행 중이 아닙니다.");
        if (pending.ResponderQueue.Count == 0 || pending.ResponderQueue[0] != playerId)
            return Fail("당신 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var source = bang.Players.FirstOrDefault(p => p.PlayerId == pending.SourcePlayerId);
        if (me == null || source == null) return Fail("플레이어 없음.");

        string msg;
        if (useBang)
        {
            var bangCard = me.Hand.FirstOrDefault(c => CanActAsBang(c, me));
            if (bangCard == null) return Fail("BANG 카드가 없습니다.");
            me.Hand.Remove(bangCard);
            bang.DiscardPile.Add(bangCard);
            msg = $"{me.Name}이(가) BANG으로 응답!";
        }
        else
        {
            ApplyDamage(bang, me, source);
            msg = $"{me.Name} Indians 피격 (HP {me.Health}/{me.MaxHealth})";
        }

        pending.ResponderQueue.RemoveAt(0);
        bool ended = CheckGameEnd(bang);
        if (ended || pending.ResponderQueue.Count == 0)
            bang.Pending = null;
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, GameEnded = ended, Message = msg };
    }

    /// <summary>Gatling 응답. useMissed=true면 Missed 소비, false면 데미지 수용.</summary>
    public BangActionResult RespondGatling(string roomId, string playerId, bool useMissed)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.GatlingResponse)
            return Fail("Gatling 진행 중이 아닙니다.");
        if (pending.ResponderQueue.Count == 0 || pending.ResponderQueue[0] != playerId)
            return Fail("당신 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var source = bang.Players.FirstOrDefault(p => p.PlayerId == pending.SourcePlayerId);
        if (me == null || source == null) return Fail("플레이어 없음.");

        string msg;
        if (useMissed)
        {
            var missed = me.Hand.FirstOrDefault(c => CanActAsMissed(c, me));
            if (missed == null) return Fail("Missed 카드가 없습니다.");
            me.Hand.Remove(missed);
            bang.DiscardPile.Add(missed);
            msg = $"{me.Name}이(가) Missed로 회피!";
        }
        else
        {
            ApplyDamage(bang, me, source);
            msg = $"{me.Name} Gatling 피격 (HP {me.Health}/{me.MaxHealth})";
        }

        pending.ResponderQueue.RemoveAt(0);
        bool ended = CheckGameEnd(bang);
        if (ended || pending.ResponderQueue.Count == 0)
            bang.Pending = null;
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, GameEnded = ended, Message = msg };
    }

    /// <summary>Duel 응답 — BANG 던지면 상대가 다음 응답자, 못 내면 1 데미지 + Duel 종료.</summary>
    public BangActionResult RespondDuel(string roomId, string playerId, bool useBang)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.DuelResponse)
            return Fail("Duel 진행 중이 아닙니다.");
        if (pending.TargetPlayerId != playerId)
            return Fail("당신 차례가 아닙니다.");

        var me = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var opponentId = pending.SourcePlayerId;
        var opponent = bang.Players.FirstOrDefault(p => p.PlayerId == opponentId);
        if (me == null || opponent == null) return Fail("플레이어 없음.");

        if (useBang)
        {
            var bangCard = me.Hand.FirstOrDefault(c => CanActAsBang(c, me));
            if (bangCard == null) return Fail("BANG 카드가 없습니다.");
            me.Hand.Remove(bangCard);
            bang.DiscardPile.Add(bangCard);

            // 응답자 교체: 상대가 다음 차례
            pending.SourcePlayerId = me.PlayerId;
            pending.TargetPlayerId = opponent.PlayerId;
            return new BangActionResult { Success = true, Message = $"{me.Name} BANG → {opponent.Name} 응답 차례" };
        }
        else
        {
            // BANG 못 냄 → 1 데미지 + Duel 종료
            ApplyDamage(bang, me, opponent);
            bang.Pending = null;
            bool ended = CheckGameEnd(bang);
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, GameEnded = ended, Message = $"{me.Name} Duel 패배 (HP {me.Health}/{me.MaxHealth})" };
        }
    }

    /// <summary>CatBalou/Panic — 사용자가 대상 손패의 카드 ID를 직접 선택.</summary>
    public BangActionResult PickCardFromTarget(string roomId, string playerId, int targetCardId)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.CardPickFromTarget)
            return Fail("카드 선택할 상태가 아닙니다.");
        if (pending.SourcePlayerId != playerId)
            return Fail("당신이 선택할 차례가 아닙니다.");

        var source = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var target = bang.Players.FirstOrDefault(p => p.PlayerId == pending.TargetPlayerId);
        if (source == null || target == null) return Fail("플레이어를 찾을 수 없습니다.");

        // 손/장비/무기 어디서 왔는지 찾고 출처 표시
        string source_zone;
        var picked = target.Hand.FirstOrDefault(c => c.Id == targetCardId);
        if (picked != null)
        {
            target.Hand.Remove(picked);
            source_zone = "손패";
        }
        else if (target.Weapon != null && target.Weapon.Id == targetCardId)
        {
            picked = target.Weapon;
            target.Weapon = null;
            source_zone = "무기";
        }
        else
        {
            picked = target.Equipment.FirstOrDefault(c => c.Id == targetCardId);
            if (picked == null) return Fail("선택한 카드가 대상에게 없습니다.");
            target.Equipment.Remove(picked);
            source_zone = "장비";
        }

        if (pending.PickToHand)
        {
            source.Hand.Add(picked);
        }
        else
        {
            bang.DiscardPile.Add(picked);
        }
        bang.Pending = null;

        string action = pending.PickToHand ? "가져옴" : "버림";
        TriggerSuzyLafayette(bang);
        return new BangActionResult { Success = true, Message = $"{source.Name}이(가) {target.Name}의 {source_zone} 카드 1장을 {action}" };
    }

    /// <summary>BANG 응답 처리. useMissed=true면 Missed 카드 자동 소비, false면 데미지 수용.</summary>
    public BangActionResult RespondBang(string roomId, string playerId, bool useMissed)
    {
        var room = _roomService.GetRoom(roomId);
        if (room?.Bang == null) return Fail("뱅 게임 방이 아닙니다.");
        var bang = room.Bang;
        if (bang.IsFinished) return Fail("게임이 종료됐습니다.");

        var pending = bang.Pending;
        if (pending == null || pending.Type != BangPendingType.BangResponse)
            return Fail("응답할 BANG이 없습니다.");
        if (pending.TargetPlayerId != playerId)
            return Fail("당신이 응답할 차례가 아닙니다.");

        var target = bang.Players.FirstOrDefault(p => p.PlayerId == playerId);
        var source = bang.Players.FirstOrDefault(p => p.PlayerId == pending.SourcePlayerId);
        if (target == null || source == null) return Fail("플레이어를 찾을 수 없습니다.");

        if (useMissed)
        {
            var missed = target.Hand.FirstOrDefault(c => CanActAsMissed(c, target));
            if (missed == null) return Fail("Missed 카드가 없습니다.");
            bool wasDodge = missed.Type == BangCardType.Dodge;
            target.Hand.Remove(missed);
            bang.DiscardPile.Add(missed);
            // Dodge는 응답 시 카드 1장 보너스 드로우
            if (wasDodge) DrawToHand(bang, target);
            pending.RequiredMissed--;
            if (pending.RequiredMissed > 0)
            {
                TriggerSuzyLafayette(bang);
                return new BangActionResult { Success = true, Message = $"{target.Name} {(wasDodge ? "Dodge" : "Missed")} 1장 사용 — 추가 {pending.RequiredMissed}장 필요" };
            }
            bang.Pending = null;
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, Message = $"{target.Name}이(가) {(wasDodge ? "Dodge" : "Missed")}로 회피!" };
        }
        else
        {
            ApplyDamage(bang, target, source);
            bang.Pending = null;
            var ended = CheckGameEnd(bang);
            TriggerSuzyLafayette(bang);
            return new BangActionResult { Success = true, GameEnded = ended, Message = $"{target.Name} BANG 피격 (HP {target.Health}/{target.MaxHealth})" };
        }
    }

    private void ApplyDamage(BangState bang, BangPlayer target, BangPlayer? source)
    {
        target.Health = Math.Max(0, target.Health - 1);

        // Bart Cassidy — 데미지 받은 직후 살아있으면 카드 1장 드로우
        if (target.Health > 0 && target.Character == BangCharacter.BartCassidy)
        {
            DrawToHand(bang, target);
        }

        // El Gringo — 데미지 받았고 공격자가 있으며 공격자 손패가 있으면 그 손에서 1장 무작위로 가져옴
        if (target.Health > 0 && target.Character == BangCharacter.ElGringo
            && source != null && source.PlayerId != target.PlayerId && source.Hand.Count > 0)
        {
            int idx = _random.Next(source.Hand.Count);
            var stolen = source.Hand[idx];
            source.Hand.RemoveAt(idx);
            target.Hand.Add(stolen);
        }

        if (target.Health == 0)
        {
            // Death save — 마지막 2명 이하가 아니고, 손에 Beer가 있다면 자동으로 1장 소비 + 1 HP 회복
            int aliveBefore = bang.Players.Count(p => p.IsAlive) + 1; // 본인 곧 사망 가정 전 alive (본인은 health 0이지만 IsAlive false 이미)
            // IsAlive는 Health > 0 — 이미 false. 살아있는 다른 사람 + 본인(사망 직전)
            int otherAlive = bang.Players.Count(p => p.IsAlive && p.PlayerId != target.PlayerId);
            bool isLastDuel = otherAlive <= 1;  // 본인 외 1명만 남으면 = 결투 끝 — Beer 효과 없음 (정통 룰)

            if (!isLastDuel)
            {
                var beer = target.Hand.FirstOrDefault(c => c.Type == BangCardType.Beer);
                if (beer != null)
                {
                    target.Hand.Remove(beer);
                    bang.DiscardPile.Add(beer);
                    target.Health = 1;
                    return;  // 부활 — 사망 처리 X
                }
            }

            target.RoleRevealed = true;

            // Vulture Sam — 사망자의 손/장착 모두 가져옴 (살아있는 Vulture Sam만)
            var vulture = bang.Players.FirstOrDefault(p =>
                p.IsAlive && p.PlayerId != target.PlayerId && p.Character == BangCharacter.VultureSam);
            if (vulture != null)
            {
                vulture.Hand.AddRange(target.Hand);
                if (target.Weapon != null) vulture.Hand.Add(target.Weapon);
                vulture.Hand.AddRange(target.Equipment);
            }
            else
            {
                foreach (var c in target.Hand) bang.DiscardPile.Add(c);
                if (target.Weapon != null) bang.DiscardPile.Add(target.Weapon);
                foreach (var e in target.Equipment) bang.DiscardPile.Add(e);
            }
            target.Hand.Clear();
            target.Weapon = null;
            target.Equipment.Clear();

            // 공격자에게 무법자 보상 (정통 룰: 무법자 사망 시 공격자가 카드 3장)
            if (source != null && source.IsAlive && target.Role == BangRole.Outlaw)
            {
                for (int i = 0; i < 3; i++) DrawToHand(bang, source);
            }

            // Dodge City — Greg Digger: 다른 사람 사망 시 HP +2
            foreach (var greg in bang.Players.Where(p => p.IsAlive
                && p.PlayerId != target.PlayerId && p.Character == BangCharacter.GregDigger))
            {
                greg.Health = Math.Min(greg.MaxHealth, greg.Health + 2);
            }
            // Dodge City — Herb Hunter: 다른 사람 사망 시 카드 2장
            foreach (var herb in bang.Players.Where(p => p.IsAlive
                && p.PlayerId != target.PlayerId && p.Character == BangCharacter.HerbHunter))
            {
                DrawToHand(bang, herb);
                DrawToHand(bang, herb);
            }
            // 보안관이 부보안관 죽이면 보안관 손/장착 모두 버림 (정통 룰 — 처벌)
            if (source != null && source.IsAlive && source.Role == BangRole.Sheriff && target.Role == BangRole.Deputy)
            {
                foreach (var c in source.Hand) bang.DiscardPile.Add(c);
                source.Hand.Clear();
                if (source.Weapon != null) { bang.DiscardPile.Add(source.Weapon); source.Weapon = null; }
                foreach (var e in source.Equipment) bang.DiscardPile.Add(e);
                source.Equipment.Clear();
            }
        }
    }

    // 승리 조건 체크
    private bool CheckGameEnd(BangState bang)
    {
        var alive = bang.Players.Where(p => p.IsAlive).ToList();
        var sheriff = bang.Players.FirstOrDefault(p => p.Role == BangRole.Sheriff);
        bool sheriffDead = sheriff == null || !sheriff.IsAlive;

        if (sheriffDead)
        {
            // 마지막 1인이 배신자라면 배신자 단독 승리
            if (alive.Count == 1 && alive[0].Role == BangRole.Renegade)
            {
                EndGame(bang, BangRole.Renegade, new[] { alive[0].PlayerId }, "배신자 단독 승리");
                return true;
            }
            // 그 외 보안관 사망 = 무법자 승리
            var outlawIds = bang.Players.Where(p => p.Role == BangRole.Outlaw).Select(p => p.PlayerId).ToList();
            EndGame(bang, BangRole.Outlaw, outlawIds, "보안관 사망 — 무법자 승리");
            return true;
        }

        // 보안관 살아있고, 무법자·배신자 모두 사망 → 보안관 팀 승리
        bool anyEnemyAlive = bang.Players.Any(p => p.IsAlive && (p.Role == BangRole.Outlaw || p.Role == BangRole.Renegade));
        if (!anyEnemyAlive)
        {
            var sheriffTeam = bang.Players.Where(p => p.Role == BangRole.Sheriff || p.Role == BangRole.Deputy)
                                          .Select(p => p.PlayerId).ToList();
            EndGame(bang, BangRole.Sheriff, sheriffTeam, "보안관 팀 승리");
            return true;
        }
        return false;
    }

    private void EndGame(BangState bang, BangRole winnerRole, IEnumerable<string> winnerIds, string reason)
    {
        bang.IsFinished = true;
        bang.WinnerRole = winnerRole;
        bang.WinnerPlayerIds = winnerIds.ToList();
        bang.EndReason = reason;
        // 모든 역할 공개
        foreach (var p in bang.Players) p.RoleRevealed = true;
    }

}
