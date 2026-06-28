using Card.Api.Domain;

namespace Card.Api.Services;

public partial class BangGameService
{
    // Dodge City — Apache Kid: 다이아 카드의 효과는 본인에게 영향 X
    private static bool ApacheKidImmune(BangCard card, BangPlayer target) =>
        target.Character == BangCharacter.ApacheKid && card.Suit == CardSuit.Diamonds;

    // Dodge City — ♠ 펴서 회피하는 방어 장비 (IronPlate, TenGallonHat 둘 다)
    private static bool HasSpadeShield(BangPlayer target) =>
        target.Equipment.Any(c => c.Type == BangCardType.IronPlate || c.Type == BangCardType.TenGallonHat);

    // Calamity Janet — BANG ↔ Missed 호환. 확장팩 Dodge는 Missed로, Punch는 BANG으로도 호환
    private static bool CanActAsBang(BangCard c, BangPlayer owner) =>
        c.Type == BangCardType.Bang
        || c.Type == BangCardType.Punch
        || (owner.Character == BangCharacter.CalamityJanet
            && (c.Type == BangCardType.Missed || c.Type == BangCardType.Dodge));

    private static bool CanActAsMissed(BangCard c, BangPlayer owner) =>
        c.Type == BangCardType.Missed
        || c.Type == BangCardType.Dodge
        || (owner.Character == BangCharacter.CalamityJanet
            && (c.Type == BangCardType.Bang || c.Type == BangCardType.Punch))
        // Dodge City — Elena Fuente: 어떤 카드든 Missed로 사용 가능
        || owner.Character == BangCharacter.ElenaFuente;

    // 캐릭터 기본 최대 HP (보안관 +1 보너스는 별도)
    private static int GetCharacterMaxHp(BangCharacter c) => c switch
    {
        BangCharacter.PaulRegret => 3,
        BangCharacter.ElGringo => 3,
        _ => 4
    };

    // 무기 사거리 — 무기 없으면 default 1 (Colt45)
    private static int GetWeaponRange(BangCard? weapon) => weapon?.Type switch
    {
        BangCardType.Volcanic => 1,
        BangCardType.Scofield => 2,
        BangCardType.Remington => 3,
        BangCardType.Carbine => 4,
        BangCardType.Winchester => 5,
        BangCardType.Pepperbox => 3,
        BangCardType.Conestoga => 1,
        BangCardType.RevCarabine => 4,
        _ => 1
    };

    // 턴당 BANG 무제한 — Volcanic 또는 Willy the Kid 캐릭터
    private static bool HasUnlimitedBang(BangPlayer player) =>
        player.Weapon?.Type == BangCardType.Volcanic ||
        player.Character == BangCharacter.WillyTheKid;

    // from → to 거리. Mustang(받는 쪽 +1), Scope(보내는 쪽 -1), Paul Regret(받을 때 +1), Rose Doolan(보낼 때 -1)
    // Dodge City — Belle Star: 본인 턴 동안 다른 사람 장착 카드 효과 무효 (Mustang 등 무시)
    private int Distance(BangState bang, BangPlayer from, BangPlayer to)
    {
        int baseDist = SeatDistance(bang, from, to);
        if (baseDist == int.MaxValue) return baseDist;
        int modified = baseDist;
        bool belleTurn = from.Character == BangCharacter.BelleStar
                         && bang.CurrentTurnPlayerId == from.PlayerId;
        if (!belleTurn && to.Equipment.Any(c => c.Type == BangCardType.Mustang)) modified++;
        if (from.Equipment.Any(c => c.Type == BangCardType.Scope)) modified--;
        if (to.Character == BangCharacter.PaulRegret) modified++;
        if (from.Character == BangCharacter.RoseDoolan) modified--;
        return Math.Max(1, modified);
    }

    private int SeatDistance(BangState bang, BangPlayer a, BangPlayer b)
    {
        var aliveSeats = bang.Players.Where(p => p.IsAlive).Select(p => p.SeatIndex).ToList();
        if (!aliveSeats.Contains(a.SeatIndex) || !aliveSeats.Contains(b.SeatIndex)) return int.MaxValue;
        // 살아있는 사람만 둘러앉은 원형 거리
        int total = aliveSeats.Count;
        int posA = aliveSeats.IndexOf(a.SeatIndex);
        int posB = aliveSeats.IndexOf(b.SeatIndex);
        int diff = Math.Abs(posA - posB);
        return Math.Min(diff, total - diff);
    }

}
