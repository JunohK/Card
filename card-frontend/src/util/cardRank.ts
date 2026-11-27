// 카드 숫자(랭크) 보조 함수 — Cards 게임 화면 공용
import type { CardView } from "../types/cardGame";
import { hwatuCardDataUri } from "./hwatuCard";

export const isJokerRank = (rank: string | null | undefined): boolean => {
    const r = (rank ?? "").toUpperCase();
    return r === "JOKER" || r === "JK";
};

/** 정렬·화투 달 표시용 숫자. A=1, J=11, Q=12, K=13, 조커는 맨 뒤(99) */
export const getRankValue = (rank: string): number => {
    if (!rank) return 0;
    const r = rank.toUpperCase();
    if (isJokerRank(r)) return 99;
    if (r === "A") return 1;
    if (r === "J") return 11;
    if (r === "Q") return 12;
    if (r === "K") return 13;
    return parseInt(r);
};

/** 카드에 쓰는 글자. 조커는 "JK" */
export const getRankText = (rank: string): string => {
    if (!rank) return "";
    const r = rank.toUpperCase();
    return isJokerRank(r) ? "JK" : r;
};

export const getSuitText = (card: CardView): string => (card.suit === "Joker" ? "🃏" : card.suit);

export const sortCards = (cards: CardView[]): CardView[] =>
    [...cards].sort((a, b) => getRankValue(a.rank) - getRankValue(b.rank));

export const getCardKey = (card: CardView): string => `${card.rank}-${card.suit}-${card.id ?? ""}`;

/**
 * 화투 모드일 때 카드 앞면 그림(SVG data URI). 숫자·그림이 들어 있어 글자는 겹쳐 그리지 않는다.
 * 화투에는 무늬(♠♥♦♣)가 없으므로 같은 달 4장은 같은 그림. 트럼프 모드·조커는 null → 글자 카드
 */
export const hwatuImageFor = (rank: string, hwatu: boolean): string | null => {
    if (!hwatu || !rank || isJokerRank(rank)) return null;
    const r = rank.toUpperCase();
    const rankMap: Record<string, number> = { A: 1, J: 11, Q: 12 };
    const month = rankMap[r] ?? parseInt(r, 10);
    if (isNaN(month)) return null;
    return hwatuCardDataUri(month);
};
