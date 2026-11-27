// 버려진 카드 기록 — 바닥 카드를 누르면 열린다. 숫자순 정렬, 화투 모드는 달 숫자만
import type { CardView } from "../../types/cardGame";
import { sortCards } from "../../util/cardRank";
import CardFace from "./CardFace";

type DiscardPileModalProps = {
    cards: CardView[];
    hwatu: boolean;
    onClose: () => void;
};

export default function DiscardPileModal({ cards, hwatu, onClose }: DiscardPileModalProps) {
    return (
        <div className="discard-modal-overlay" onClick={onClose}>
            <div className="discard-modal-content" onClick={e => e.stopPropagation()}>
                <div className="modal-header">
                    <h2 className="discard-title">버려진 카드 기록 ({cards.length})</h2>
                    <button className="modal-close-btn" onClick={onClose}>닫기</button>
                </div>
                <div className="discard-grid">
                    {sortCards(cards).map((card, idx) => <CardFace key={idx} card={card} variant="list" hwatu={hwatu} />)}
                </div>
            </div>
        </div>
    );
}
