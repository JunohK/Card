// 카드 앞면 — 손패·바닥·결과창·버린 카드 목록에서 같은 규칙으로 그린다.
// 화투 모드는 그림(SVG)을 배경으로 깔고 글자를 겹쳐 그리지 않는다. 크기·색은 GamePage.css의 변형별 클래스.
import type { CardView } from "../../types/cardGame";
import { getRankText, getRankValue, getSuitText, hwatuImageFor, isJokerRank } from "../../util/cardRank";

export type CardFaceVariant =
    | "hand"     // 내 손패 — 네 모서리 숫자 + 가운데 무늬
    | "floor"    // 테이블 가운데 바닥 카드 (card가 null이면 DROP)
    | "result"   // 라운드 결과창 승자 카드
    | "final"    // 최종 결과창 승자 카드
    | "list";    // 버린 카드 목록 — 그림 없이 글자만 (화투는 달 숫자만)

const BASE_CLASS: Record<CardFaceVariant, string> = {
    hand: "card-ui my-card",
    floor: "card-ui discard",
    result: "result-card",
    final: "result-card final",
    list: "discard-list-card"
};

type CardFaceProps = {
    card: CardView | null;
    variant: CardFaceVariant;
    hwatu: boolean;
    className?: string;
    onClick?: () => void;
};

export default function CardFace({ card, variant, hwatu, className = "", onClick }: CardFaceProps) {
    const imageSrc = card && variant !== "list" ? hwatuImageFor(card.rank, hwatu) : null;
    const isJoker = card?.suit === "Joker";
    const numberOnly = variant === "list" && hwatu && !isJoker;
    const classes = [
        BASE_CLASS[variant],
        card?.color === "Red" ? "red" : "black",
        variant === "hand" && isJokerRank(card?.rank) ? "joker" : "",
        imageSrc ? "has-image" : "",
        numberOnly ? "number-only" : "",
        className
    ].filter(Boolean).join(" ");

    return (
        <div className={classes} style={imageSrc ? { backgroundImage: `url(${imageSrc})` } : undefined} onClick={onClick}>
            {cardContent(card, variant, !!imageSrc, numberOnly)}
        </div>
    );
}

function cardContent(card: CardView | null, variant: CardFaceVariant, hasImage: boolean, numberOnly: boolean) {
    if (!card) return <span className="empty-label">DROP</span>;
    if (hasImage) return null;
    if (numberOnly) return <div className="month">{getRankValue(card.rank)}</div>;

    const rankText = getRankText(card.rank);
    if (variant === "hand") {
        return (
            <>
                <div className="card-corners">
                    <span className="rank">{rankText}</span>
                    <span className="rank">{rankText}</span>
                </div>
                <span className="suit">{getSuitText(card)}</span>
                <div className="card-corners bottom">
                    <span className="rank">{rankText}</span>
                    <span className="rank">{rankText}</span>
                </div>
            </>
        );
    }
    return (
        <>
            <span className="rank">{rankText}</span>
            <span className="suit">{getSuitText(card)}</span>
        </>
    );
}
