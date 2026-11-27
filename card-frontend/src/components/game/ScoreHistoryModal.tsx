// 라운드별 점수 히스토리 — 헤더의 점수판을 누르면 열린다. 각 칸: 이번 라운드 점수 + (누적)
import type { PlayerView, RoundScoreEntry } from "../../types/cardGame";

type ScoreHistoryModalProps = {
    history: RoundScoreEntry[];
    players: PlayerView[];
    onClose: () => void;
};

const scoreClass = (score: number) => (score < 0 ? "score-minus" : score > 0 ? "score-plus" : "score-zero");

export default function ScoreHistoryModal({ history, players, onClose }: ScoreHistoryModalProps) {
    return (
        <div className="discard-modal-overlay" onClick={onClose}>
            <div className="discard-modal-content wide" onClick={e => e.stopPropagation()}>
                <div className="modal-header">
                    <h2 className="history-title">📊 라운드별 점수 히스토리</h2>
                    <button className="modal-close-btn" onClick={onClose}>닫기</button>
                </div>

                {history.length === 0 ? (
                    <p className="history-empty">아직 종료된 라운드가 없습니다.</p>
                ) : (
                    <table className="history-table">
                        <thead>
                            <tr>
                                <th>R</th>
                                <th>승자 / 족보</th>
                                {players.map(p => <th key={p.playerId} className="player-col">{p.name}</th>)}
                            </tr>
                        </thead>
                        <tbody>
                            {history.map((entry, idx) => (
                                <tr key={idx}>
                                    <td className="history-round">R{entry.round}</td>
                                    <td className="history-winner">
                                        <div className="history-winner-name">{entry.winnerName ?? "-"}</div>
                                        {entry.winType && <div className="history-win-type">{entry.winType}</div>}
                                    </td>
                                    {players.map(p => {
                                        const ps = entry.scores.find(s => s.playerId === p.playerId);
                                        const roundScore = ps?.score ?? 0;
                                        return (
                                            <td key={p.playerId} className="player-col">
                                                <div className={scoreClass(roundScore)}>{roundScore > 0 ? "+" : ""}{roundScore}</div>
                                                <div className="history-total">({ps?.totalScore ?? 0})</div>
                                            </td>
                                        );
                                    })}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}

                <div className="history-legend">
                    각 셀: <span className="score-minus">녹색=감점(좋음)</span> · <span className="score-plus">빨강=가점(나쁨)</span> · (괄호=누적)
                </div>
            </div>
        </div>
    );
}
