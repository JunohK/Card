// 라운드 결과창 — 승자 카드, 판정 족보, 이번 라운드/누적 점수. 방장만 다음 라운드를 시작할 수 있다.
import type { CardGameState } from "../../types/cardGame";
import { sortCards } from "../../util/cardRank";
import CardFace from "./CardFace";

type RoundResultModalProps = {
    game: CardGameState;
    hwatu: boolean;
    isHost: boolean;
    onNextRound: () => void;
};

export default function RoundResultModal({ game, hwatu, isHost, onNextRound }: RoundResultModalProps) {
    const winnerCards = game.winnerHand ?? [];

    return (
        <div className="discard-modal-overlay fade-in-2s">
            <div className="discard-modal-content round-result">
                <h2>ROUND RESULT</h2>

                <div className="result-section">
                    <p className="result-section-label">우승자 카드 구성</p>
                    <div className="result-cards">
                        {winnerCards.length === 0 ? (
                            <div className="result-cards-empty">
                                <p>승리 카드를 불러올 수 없습니다.</p>
                            </div>
                        ) : (
                            sortCards(winnerCards).map((card, i) => <CardFace key={i} card={card} variant="result" hwatu={hwatu} />)
                        )}
                    </div>
                </div>

                <div className="result-verdict">
                    <span className="result-verdict-label">판정 결과: </span>
                    <span className="result-verdict-value">{game.lastWinType || "족보 확인 중..."}</span>
                </div>

                <div className="result-table-wrap">
                    <table className="result-table">
                        <thead>
                            <tr>
                                <th>플레이어</th>
                                <th>획득 점수</th>
                                <th>누적 점수</th>
                            </tr>
                        </thead>
                        <tbody>
                            {game.players.map(p => {
                                const score = p.score ?? 0;
                                return (
                                    <tr key={p.playerId}>
                                        <td>{p.name}</td>
                                        <td className={score <= 0 ? "score-good" : "score-bad"}>{score > 0 ? `+${score}` : score}</td>
                                        <td>{p.totalScore ?? 0} 점</td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>

                {isHost ? (
                    <button className="next-round-btn" onClick={onNextRound}>다음 라운드 시작</button>
                ) : (
                    <p className="waiting-text">방장이 다음 라운드를 준비 중입니다...</p>
                )}
            </div>
        </div>
    );
}
