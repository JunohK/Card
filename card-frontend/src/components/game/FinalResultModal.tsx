// 최종 결과창 — 게임 종료(마지막 라운드·기권·퇴장). 총점 순위와 우승자, 확인 시 대기실로 돌아간다.
import type { CardGameState } from "../../types/cardGame";
import { sortCards } from "../../util/cardRank";
import CardFace from "./CardFace";

type FinalResultModalProps = {
    game: CardGameState;
    hwatu: boolean;
    onConfirm: () => void;
    onShowStats: () => void;
};

export default function FinalResultModal({ game, hwatu, onConfirm, onShowStats }: FinalResultModalProps) {
    const isGiveUp = (game.winnerName ?? "").includes("(기권)");
    // 마지막 라운드 승자의 카드 — 그 사람이 게임 승자일 때만 노출 (다른 사람 패를 우승자 것처럼 보이지 않도록)
    const showWinnerCards = (game.gameWinnerIds ?? []).includes(game.winnerPlayerId ?? "");
    const ranking = [...game.players].sort((a, b) => (a.totalScore ?? 0) - (b.totalScore ?? 0));

    return (
        <div className="discard-modal-overlay final fade-in-2s">
            <div className="discard-modal-content final-result">
                <h1>{isGiveUp ? "기권" : "게임 종료"}</h1>

                <div className="final-winner-box">
                    <span className="final-winner-label">{isGiveUp ? "기권 승리자" : "최종 우승자"}</span>
                    <span className="final-winner-name">👑 {game.gameWinnerName || game.winnerName || "-"}</span>

                    {showWinnerCards && (
                        <div className="final-winner-cards">
                            {sortCards(game.winnerHand ?? []).map((card, i) => <CardFace key={i} card={card} variant="final" hwatu={hwatu} />)}
                        </div>
                    )}

                    <div className="final-win-type">
                        판정 족보: {isGiveUp ? "상대방 기권" : (game.lastWinType || "게임 종료")}
                    </div>
                </div>

                <div className="final-ranking">
                    <table>
                        <thead>
                            <tr>
                                <th>순위</th>
                                <th>플레이어</th>
                                <th>최종 총점</th>
                            </tr>
                        </thead>
                        <tbody>
                            {ranking.map((p, index) => (
                                <tr key={p.playerId}>
                                    <td>{index + 1}위</td>
                                    <td>{p.name}</td>
                                    <td>{p.totalScore} 점</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>

                <div className="final-actions">
                    <button className="final-confirm-btn" onClick={onConfirm}>확인</button>
                    <button className="final-stats-btn" onClick={onShowStats}>📊 통계</button>
                </div>
            </div>
        </div>
    );
}
