import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";
import "./GamePage.css";

// 상대방들이 배치될 고정 위치 (좌측 상단 기준 % 좌표)
const ENEMY_POSITIONS = [
    { top: '40%', left: '10%' },  // 왼쪽 중앙
    { top: '20%', left: '25%' },  // 왼쪽 상단
    { top: '15%', left: '50%' },  // 상단 정중앙
    { top: '20%', left: '75%' },  // 오른쪽 상단
    { top: '40%', left: '90%' },  // 오른쪽 중앙
    { top: '70%', left: '85%' },  // 오른쪽 하단
];

export default function GamePage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const [game, setGame] = useState<any>(null);
    const myId = connection.connectionId;

    useEffect(() => {
        // 방 상태 업데이트 수신
        connection.on("RoomUpdated", (data) => setGame(data));
        
        // 에러 메시지 수신 (승리 조건 미달 등)
        connection.on("ErrorMessage", (msg) => {
            alert(msg);
        });

        // 초기 방 데이터 로드
        connection.invoke("GetRoom", roomId).then(data => setGame(data));

        return () => { 
            connection.off("RoomUpdated"); 
            connection.off("ErrorMessage");
        };
    }, [roomId]);

    // 기권 핸들러: 서버에 기권 요청
    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 모든 플레이어의 게임이 종료됩니다.")) {
            connection.invoke("GiveUp", roomId); 
        }
    };

    if (!game) return <div className="game-container">Loading...</div>;

    const me = game.players.find((p: any) => p.playerId === myId);
    const others = game.players.filter((p: any) => p.playerId !== myId);
    const isMyTurn = game.currentTurnPlayerId === myId;

    const getCardValue = (rank: string) => {
        if (rank === "A") return 1;
        if (rank === "J") return 11;
        if (rank === "Q") return 12;
        if (rank === "K") return 13;
        if (rank === "Joker") return "★";
        return parseInt(rank);
    };

    const getRankText = (rank: string) => {
        if (rank === "Joker") return "JK";
        return rank;
    };

    const canDraw = isMyTurn && (me?.hand.length === 2 || me?.hand.length === 5);
    const canDiscardOrWin = isMyTurn && (me?.hand.length === 3 || me?.hand.length === 6);

    return (
        <div className="game-container">
            {/* 상단 정보바 */}
            <div className="game-header">
                <div className="header-left">
                    <span className="set-info">SET {game.currentSet || 1} / 10</span>
                    <span className="room-info">ROOM: {roomId}</span>
                </div>
                <div className="player-scores">
                    {game.players.map((p: any) => (
                        <span key={p.playerId} className={p.playerId === myId ? "my-score" : ""}>
                            {p.name}: {p.totalScore || 0}점
                        </span>
                    ))}
                </div>
                <button className="exit-btn" onClick={handleExit}>기권</button>
            </div>

            {/* 메인 게임 테이블 영역 */}
            <div className="game-table-area">
                <div className="table-oval">
                    {/* 중앙 덱 및 버린 카드 구역 */}
                    <div className="table-center">
                        <div 
                            className={`card-ui deck ${canDraw ? 'can-action' : ''}`} 
                            onClick={() => canDraw && connection.invoke("DrawCard", roomId)}
                        >
                            <span className="label">DECK</span>
                            <div className="count">{game.deckCount}</div>
                            {canDraw && <div className="pick-hint">PICK!</div>}
                        </div>

                        <div className={`card-ui discard ${game.lastDiscardedCard?.color === 'Red' ? 'red' : 'black'}`}>
                            {game.lastDiscardedCard ? (
                                <>
                                    <span className="rank">{getRankText(game.lastDiscardedCard.rank)}</span>
                                    <span className="suit">{game.lastDiscardedCard.suit === "Joker" ? "🃏" : game.lastDiscardedCard.suit}</span>
                                </>
                            ) : <span className="empty-label">DROP</span>}
                        </div>
                    </div>

                    {/* 상대방들 배치 */}
                    {others.map((player: any, idx: number) => (
                        <div 
                            key={player.playerId} 
                            className={`player-box ${game.currentTurnPlayerId === player.playerId ? 'active-turn' : ''}`}
                            style={{
                                position: 'absolute',
                                top: ENEMY_POSITIONS[idx]?.top || '0%',
                                left: ENEMY_POSITIONS[idx]?.left || '0%',
                                transform: 'translate(-50%, -50%)'
                            }}
                        >
                            <div className="player-name">
                                {game.currentTurnPlayerId === player.playerId ? "▶ " : ""}{player.name}
                            </div>
                            <div className="opponent-card-back">{player.hand.length}</div>
                        </div>
                    ))}
                </div>
            </div>

            {/* 하단 내 영역 */}
            <div className="hand-area">
                <div className="turn-status-container">
                    <div className="turn-status-row">
                        <span className={`status-text ${isMyTurn ? "active-text" : ""}`}>
                            {canDraw && `▲ 카드를 한 장 뽑으세요`}
                            {canDiscardOrWin && `▼ 버릴 카드를 선택하세요`}
                            {!isMyTurn && "상대방 턴 대기 중..."}
                        </span>
                        
                        {canDiscardOrWin && (
                            <button className="win-btn highlight" onClick={() => connection.invoke("DeclareWin", roomId)}>
                                🏆 승리 선언
                            </button>
                        )}
                    </div>
                </div>

                <div className="cards-in-hand">
                    {me?.hand.map((card: any, idx: number) => (
                        <div 
                            key={idx} 
                            className={`card-ui my-card ${card.color === 'Red' ? 'red' : 'black'}`}
                            style={{ 
                                background: card.rank === "Joker" ? "#f1c40f" : "white",
                                cursor: canDiscardOrWin ? 'pointer' : 'default',
                                transform: canDiscardOrWin ? 'translateY(-20px)' : 'none'
                            }}
                            onClick={() => canDiscardOrWin && connection.invoke("PlayCard", roomId, card)}
                        >
                            <span className="rank">{getRankText(card.rank)}</span>
                            <span className="suit">{card.suit === "Joker" ? "🃏" : card.suit}</span>
                            <div className="card-value-hint">{getCardValue(card.rank)}</div>
                        </div>
                    ))}
                </div>
            </div>

            {/* 게임 종료 결과 점수판 모달 */}
            {game.isFinished && (
                <div className="modal-overlay">
                    <div className="modal-content scoreboard">
                        <h1 className="result-title">GAME RESULT</h1>
                        <p className="winner-announce">🏆 승리: <strong>{game.winnerName}</strong></p>
                        
                        <table className="score-table">
                            <thead>
                                <tr>
                                    <th>플레이어</th>
                                    <th>누적 점수</th>
                                    <th>결과</th>
                                </tr>
                            </thead>
                            <tbody>
                                {/* 점수가 낮은 순서대로 정렬하여 출력 */}
                                {[...game.players].sort((a, b) => (a.totalScore || 0) - (b.totalScore || 0)).map((p: any) => (
                                    <tr key={p.playerId} className={p.playerId === myId ? "highlight-row" : ""}>
                                        <td>{p.name} {p.playerId === myId && "(나)"}</td>
                                        <td className="score-val">{p.totalScore || 0}점</td>
                                        <td className={p.playerId === game.winnerPlayerId ? "win-text" : ""}>
                                            {p.playerId === game.winnerPlayerId ? "WINNER" : "LOSE"}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>

                        <div className="modal-actions">
                            <button 
                                className="confirm-btn" 
                                onClick={() => navigate(`/waiting/${roomId}`)}
                            >
                                확인 (대기실로 복귀)
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}