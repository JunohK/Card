import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";
import "./GamePage.css";

const ENEMY_POSITIONS = [
    { top: '40%', left: '10%' }, { top: '20%', left: '25%' },
    { top: '15%', left: '50%' }, { top: '20%', left: '75%' },
    { top: '40%', left: '90%' }, { top: '70%', left: '85%' },
];

export default function GamePage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const [game, setGame] = useState<any>(null);
    const [showRoundResult, setShowRoundResult] = useState(false);
    const myId = connection.connectionId;
    
    // 중복 등록 방지를 위한 Ref
    const isSubscribed = useRef(false);

    // 카드 랭크 값을 숫자로 변환 (정렬 및 비교용)
    const getRankValue = (rank: string) => {
        if (rank === "Joker" || rank === "JK") return 99; 
        if (rank === "A") return 1;
        if (rank === "J") return 11;
        if (rank === "Q") return 12;
        if (rank === "K") return 13;
        return parseInt(rank);
    };

    // 카드 자동 정렬 함수
    const sortCards = (cards: any[]) => {
        if (!cards) return [];
        return [...cards].sort((a, b) => getRankValue(a.rank) - getRankValue(b.rank));
    };

    const getRankText = (rank: string) => rank === "Joker" || rank === "JK" ? "JK" : rank;

    useEffect(() => {
        const onUpdate = (data: any) => {
            console.log("📢 게임 데이터 수신:", data);
            if (!data) return;
            setGame({ ...data }); // 불변성 유지를 위해 새 객체로 설정
            setShowRoundResult(!!data.isRoundEnded);
        };

        const onGameTerminated = (targetRoomId: string) => {
            console.log("🚨 게임 종료 또는 기권 발생. 대기실로 이동:", targetRoomId);
            window.location.href = `/room/${targetRoomId}`;
        };

        // SignalR 리스너 등록 (중복 방지)
        if (!isSubscribed.current) {
            connection.on("RoomUpdated", onUpdate);
            connection.on("GameStarted", onUpdate);
            connection.on("ShowResultBoard", onUpdate);
            connection.on("GameTerminated", onGameTerminated);
            connection.on("ErrorMessage", (msg) => alert(msg));
            isSubscribed.current = true;
        }

        // 초기 데이터 로드 (새로고침 시 대응)
        connection.invoke("GetRoom", roomId)
            .then(data => {
                if (data) {
                    setGame(data);
                }
            })
            .catch(err => console.error("❌ GetRoom 에러:", err));

        // Cleanup: 컴포넌트 언마운트 시 리스너 제거
        return () => {
            connection.off("RoomUpdated");
            connection.off("GameStarted");
            connection.off("ShowResultBoard");
            connection.off("GameTerminated");
            connection.off("ErrorMessage");
            isSubscribed.current = false;
        };
    }, [roomId]);

    const handleNextRound = () => {
        connection.invoke("RequestNextRound", roomId)
            .catch(err => console.error("다음 라운드 요청 실패:", err));
    };

    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 전체 게임이 종료됩니다.")) {
            connection.invoke("GiveUp", roomId); 
        }
    };

    // 로딩 처리 (데이터가 완전히 로드될 때까지)
    if (!game || !game.players) {
        return (
            <div className="game-container" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', color: 'white' }}>
                <h2>데이터 동기화 중...</h2>
                <button onClick={() => navigate('/lobby')} style={{ marginTop: '20px', padding: '10px' }}>로비로 돌아가기</button>
            </div>
        );
    }

    // 플레이어 데이터 매핑
    const players = game.players || [];
    const me = players.find((p: any) => p.playerId === myId);
    const others = players.filter((p: any) => p.playerId !== myId);
    const isMyTurn = game.currentTurnPlayerId === myId;

    // 액션 가능 상태 계산
    const canDraw = isMyTurn && (me?.hand?.length === 2 || me?.hand?.length === 5);
    const canDiscardOrWin = isMyTurn && (me?.hand?.length === 3 || me?.hand?.length === 6);
    const canInterrupt = !isMyTurn && game.isInterruptWindowOpen;

    return (
        <div className="game-container">
            {/* 상단 헤더 영역 */}
            <div className="game-header">
                <div className="header-left">
                    <span className="set-info">ROUND {game.currentRound} / {game.maxRounds}</span>
                    <span className="room-info">ROOM: {roomId}</span>
                </div>
                <div className="player-scores">
                    {players.map((p: any) => (
                        <span key={p.playerId} className={`score-item ${p.playerId === myId ? "my-score" : ""} ${game.currentTurnPlayerId === p.playerId ? "turn-highlight" : ""}`}>
                            {p.name}: {p.totalScore || 0}점
                        </span>
                    ))}
                </div>
                <button className="exit-btn" onClick={handleExit}>기권</button>
            </div>

            {/* 게임 테이블 영역 */}
            <div className="game-table-area">
                <div className="table-oval" style={{ position: 'relative' }}>
                    
                    {/* 중앙 덱 및 버린 카드 영역 */}
                    <div style={{
                        position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)',
                        display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '20px'
                    }}>
                        <div style={{ display: 'flex', gap: '15px' }}>
                            {/* 덱 카드 */}
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} 
                                 onClick={() => canDraw && connection.invoke("DrawCard", roomId)}>
                                <span className="label">DECK</span>
                                <div className="count">{game.deckCount}</div>
                            </div>
                            {/* 버린 카드 소생 */}
                            <div className={`card-ui discard ${game.lastDiscardedCard?.color === 'Red' ? 'red' : 'black'}`}>
                                {game.lastDiscardedCard ? (
                                    <>
                                        <span className="rank">{getRankText(game.lastDiscardedCard.rank)}</span>
                                        <span className="suit">{game.lastDiscardedCard.suit === "Joker" || game.lastDiscardedCard.suit === "JK" ? "🃏" : game.lastDiscardedCard.suit}</span>
                                    </>
                                ) : <span className="empty-label">DROP</span>}
                            </div>
                        </div>

                        <button 
                            className={`interrupt-btn ${canInterrupt ? 'active' : ''}`}
                            onClick={() => canInterrupt && connection.invoke("InterruptDiscard", roomId)}
                            disabled={!canInterrupt}
                            style={{
                                padding: '10px 30px', borderRadius: '25px', fontSize: '1rem', fontWeight: 'bold', border: 'none',
                                backgroundColor: canInterrupt ? '#e74c3c' : '#bdc3c7', color: 'white', cursor: canInterrupt ? 'pointer' : 'default', transition: 'all 0.2s'
                            }}
                        >
                            가로채기
                        </button>
                    </div>

                    {/* 타 플레이어 위치 렌더링 */}
                    {others.map((player: any, idx: number) => (
                        <div key={player.playerId} className={`player-box ${game.currentTurnPlayerId === player.playerId ? 'active-turn' : ''}`}
                            style={{ position: 'absolute', top: ENEMY_POSITIONS[idx]?.top, left: ENEMY_POSITIONS[idx]?.left, transform: 'translate(-50%, -50%)' }}>
                            <div className="player-name">{player.name}</div>
                            <div className="opponent-card-back">{player.hand?.length || 0}</div>
                        </div>
                    ))}
                </div>
            </div>

            {/* 내 손패 영역 */}
            <div className="hand-area">
                <div className="turn-status-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px 0' }}>
                    <span className={`status-text ${isMyTurn ? "active-text" : ""}`} style={{ fontSize: '1.2rem', fontWeight: 'bold', marginRight: '15px' }}>
                        {isMyTurn 
                            ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") 
                            : `${players.find((p:any) => p.playerId === game.currentTurnPlayerId)?.name || '상대'}의 턴입니다...`}
                    </span>
                    
                    {isMyTurn && (
                        <button className="win-btn highlight" 
                            onClick={() => connection.invoke("DeclareWin", roomId)}
                            style={{ padding: '8px 16px', backgroundColor: '#f1c40f', border: 'none', borderRadius: '8px', fontWeight: 'bold', color: '#2c3e50', cursor: 'pointer' }}>
                            🏆 승리 선언
                        </button>
                    )}
                </div>

                <div className="cards-in-hand" style={{ display: 'flex', gap: '15px', justifyContent: 'center' }}>
                    {me?.hand && sortCards(me.hand).map((card: any, idx: number) => (
                        <div key={`${card.suit}-${card.rank}-${idx}`} 
                            className={`card-ui my-card ${card.color === 'Red' ? 'red' : 'black'}`}
                            style={{ 
                                width: '100px', height: '145px', fontSize: '1.4rem', 
                                background: (card.rank === "Joker" || card.rank === "JK") ? "#f1c40f" : "white", 
                                cursor: canDiscardOrWin ? 'pointer' : 'default',
                                borderRadius: '10px', display: 'flex', flexDirection: 'column', 
                                justifyContent: 'space-between', padding: '10px', boxShadow: '0 4px 8px rgba(0,0,0,0.3)', transition: 'transform 0.1s'
                            }}
                            onClick={() => canDiscardOrWin && connection.invoke("PlayCard", roomId, card)}>
                            <span className="rank" style={{ fontWeight: 'bold' }}>{getRankText(card.rank)}</span>
                            <span className="suit" style={{ fontSize: '2.5rem', textAlign: 'center' }}>
                                {card.suit === "Joker" || card.suit === "JK" ? "🃏" : card.suit}
                            </span>
                            <span className="rank" style={{ fontWeight: 'bold', textAlign: 'right', transform: 'rotate(180deg)' }}>
                                {getRankText(card.rank)}
                            </span>
                        </div>
                    ))}
                    {(!me?.hand || me.hand.length === 0) && <div style={{color:'rgba(255,255,255,0.5)'}}>패가 비어있습니다.</div>}
                </div>
            </div>

            {/* 라운드 결과 모달 */}
            {showRoundResult && !game.isFinished && (
                <div className="modal-overlay">
                    <div className="modal-content scoreboard">
                        <h2 className="result-title">ROUND {game.currentRound} RESULT</h2>
                        <p className="winner-announce">우승: <strong>{game.winnerName}</strong></p>
                        <table className="score-table">
                            <thead>
                                <tr><th>플레이어</th><th>이번 라운드</th><th>누적 점수</th></tr>
                            </thead>
                            <tbody>
                                {players.map((p: any) => (
                                    <tr key={p.playerId} className={p.playerId === myId ? "highlight-row" : ""}>
                                        <td>{p.name}</td>
                                        <td style={{ color: p.score <= 0 ? '#2ecc71' : '#e74c3c', fontWeight: 'bold' }}>
                                            {p.score > 0 ? `+${p.score}` : p.score}
                                        </td>
                                        <td>{p.totalScore}점</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                        <div className="modal-actions">
                            <button className="confirm-btn" onClick={handleNextRound}>다음 라운드 시작</button>
                        </div>
                    </div>
                </div>
            )}

            {/* 최종 게임 종료 모달 */}
            {game.isFinished && (
                <div className="modal-overlay">
                    <div className="modal-content scoreboard">
                        <h1 className="result-title">GAME OVER</h1>
                        <p className="winner-announce">🏆 최종 우승: <strong>{game.winnerName}</strong></p>
                        <table className="score-table">
                            <thead>
                                <tr><th>플레이어</th><th>최종 점수</th><th>결과</th></tr>
                            </thead>
                            <tbody>
                                {[...players].sort((a, b) => a.totalScore - b.totalScore).map((p: any) => (
                                    <tr key={p.playerId} className={p.playerId === myId ? "highlight-row" : ""}>
                                        <td>{p.name}</td>
                                        <td>{p.totalScore}점</td>
                                        <td>{p.totalScore === Math.min(...players.map((pl: any) => pl.totalScore)) ? "WINNER" : "LOSE"}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                        <div className="modal-actions">
                            <button className="confirm-btn" onClick={() => window.location.href = `/room/${roomId}`}>확인 (대기실로 복귀)</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}