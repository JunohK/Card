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
    const [showDiscardModal, setShowDiscardModal] = useState(false);
    const [lastDrawnCardKey, setLastDrawnCardKey] = useState<string | null>(null);
    
    const myId = connection.connectionId;
    const prevHandRef = useRef<string[]>([]);
    const isSubscribed = useRef(false);

    // 1. 카드 랭크/텍스트 처리 (대소문자 통합 대응)
    const getRankValue = (rank: string) => {
        if (!rank) return 0;
        const r = rank.toString().toUpperCase();
        if (r === "JOKER" || r === "JK") return 99; 
        if (r === "A") return 1;
        if (r === "J") return 11;
        if (r === "Q") return 12;
        if (r === "K") return 13;
        return parseInt(r);
    };

    const getRankText = (rank: string) => {
        if (!rank) return "";
        const r = rank.toString().toUpperCase();
        return (r === "JOKER" || r === "JK") ? "JK" : r;
    };

    const sortCards = (cards: any[]) => {
        if (!cards) return [];
        return [...cards].sort((a, b) => 
            getRankValue(a.rank || a.Rank) - getRankValue(b.rank || b.Rank)
        );
    };

    useEffect(() => {
        const onUpdate = (data: any) => {
            if (!data) return;
            console.log("📢 게임 데이터 수신:", data);

            // 2. 카드 하이라이트(Glow) 감지 로직
            const playersArr = data.players || data.Players || [];
            const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
            const currentHand = me?.hand || me?.Hand || [];
            const currentHandKeys = currentHand.map((c: any, i: number) => 
                `${c.rank || c.Rank}-${c.suit || c.Suit}-${i}`
            );

            if (currentHandKeys.length > prevHandRef.current.length) {
                const newKey = currentHandKeys.find((key: string) => !prevHandRef.current.includes(key));
                if (newKey) {
                    setLastDrawnCardKey(newKey);
                    setTimeout(() => setLastDrawnCardKey(null), 4000);
                }
            }
            prevHandRef.current = currentHandKeys;

            setGame({ ...data });
            setShowRoundResult(!!(data.isRoundEnded || data.IsRoundEnded));
        };

        const onGameTerminated = (targetRoomId: string) => {
            navigate(`/room/${targetRoomId || roomId}`);
        };

        if (!isSubscribed.current) {
            connection.on("RoomUpdated", onUpdate);
            connection.on("GameStarted", onUpdate);
            connection.on("ShowResultBoard", onUpdate);
            connection.on("GameTerminated", onGameTerminated);
            connection.on("ErrorMessage", (msg) => alert(msg));
            isSubscribed.current = true;
        }

        connection.invoke("GetRoom", roomId).then(data => {
            if (data) {
                setGame(data);
                const playersArr = data.players || data.Players || [];
                const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
                prevHandRef.current = (me?.hand || me?.Hand || []).map((c: any, i: number) => 
                    `${c.rank || c.Rank}-${c.suit || c.Suit}-${i}`
                );
            }
        });

        return () => {
            connection.off("RoomUpdated");
            connection.off("GameStarted");
            connection.off("ShowResultBoard");
            connection.off("GameTerminated");
            connection.off("ErrorMessage");
            isSubscribed.current = false;
        };
    }, [roomId, myId, navigate]);

    // 기능 함수들
    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 전체 게임이 종료되며 대기실로 이동합니다.")) {
            connection.invoke("GiveUp", roomId)
                .finally(() => navigate(`/room/${roomId}`)); 
        }
    };

    const handleReturnToRoom = () => {
        connection.invoke("GiveUp", roomId).catch(() => {
            navigate(`/room/${roomId}`);
        });
    };

    const handleReshuffle = () => {
        if (window.confirm("버려진 카드들을 다시 섞어서 덱으로 만드시겠습니까?")) {
            connection.invoke("ReshuffleDeck", roomId).catch(err => alert("셔플 실패: " + err));
        }
    };

    const openRules = () => {
        window.open('/rules', '_blank', 'width=600,height=800,noopener,noreferrer');
    };

    // 로딩 처리
    if (!game || (!game.players && !game.Players)) {
        return (
            <div className="game-container" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', color: 'white' }}>
                <h2 style={{ marginBottom: '20px' }}>데이터 동기화 중...</h2>
                <button onClick={() => navigate('/lobby')} style={{ padding: '12px 24px', backgroundColor: '#34495e', color: 'white', border: 'none', borderRadius: '8px', cursor: 'pointer', fontWeight: 'bold' }}>로비로 돌아가기</button>
            </div>
        );
    }

    const players = game.players || game.Players || [];
    const me = players.find((p: any) => (p.playerId || p.PlayerId) === myId);
    const others = players.filter((p: any) => (p.playerId || p.PlayerId) !== myId);
    const currentTurnId = game.currentTurnPlayerId || game.CurrentTurnPlayerId;
    const isMyTurn = currentTurnId === myId;
    const isHost = (game.hostPlayerId || game.HostPlayerId) === myId;
    const deckCount = game.deckCount ?? game.DeckCount ?? 0;
    const lastDiscarded = game.lastDiscardedCard || game.LastDiscardedCard;
    const discardPile = game.discardPile || game.DiscardPile || [];

    const canDraw = isMyTurn && (me?.hand?.length === 2 || me?.hand?.length === 5 || me?.Hand?.length === 2 || me?.Hand?.length === 5);
    const canDiscardOrWin = isMyTurn && (me?.hand?.length === 3 || me?.hand?.length === 6 || me?.Hand?.length === 3 || me?.Hand?.length === 6);
    const isFinished = game.isFinished ?? game.IsFinished;
    const canInterrupt = !isMyTurn && (game.isInterruptWindowOpen || game.IsInterruptWindowOpen);

    return (
        <div className="game-container">
            <style>{`
                @keyframes blueGlow {
                    0% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; transform: scale(1); }
                    50% { box-shadow: 0 0 25px #3498db; border: 3px solid #5dade2; transform: scale(1.05); }
                    100% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; transform: scale(1); }
                }
                .new-card-highlight { animation: blueGlow 0.8s ease-in-out infinite !important; z-index: 100 !important; }
                .rule-btn-fixed { position: fixed; bottom: 20px; left: 20px; padding: 10px 20px; background: #f1c40f; color: #2c3e50; border: none; border-radius: 50px; font-weight: bold; cursor: pointer; z-index: 1000; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }
                .reshuffle-badge { cursor: pointer; background: #2980b9; padding: 4px 8px; border-radius: 4px; font-size: 11px; margin-top: 8px; border: none; color: white; transition: 0.2s; }
                .reshuffle-badge:hover { background: #3498db; }
                .discard-modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: flex; justifyContent: center; alignItems: center; z-index: 9999; }
                .discard-modal-content { background: #2c3e50; width: 80%; max-width: 600px; max-height: 80vh; overflow-y: auto; padding: 20px; borderRadius: 12px; border: 1px solid #34495e; }
            `}</style>

            <button className="rule-btn-fixed" onClick={openRules}>📜 게임 족보</button>

            <div className="game-header">
                <div className="header-left">
                    <span className="set-info">ROUND {game.currentRound || game.CurrentRound}</span>
                    <span className="room-info">ROOM: {roomId}</span>
                </div>
                <div className="player-scores">
                    {players.map((p: any) => (
                        <span key={p.playerId || p.PlayerId} className={`score-item ${(p.playerId || p.PlayerId) === myId ? "my-score" : ""} ${currentTurnId === (p.playerId || p.PlayerId) ? "turn-highlight" : ""}`}>
                            {p.name || p.Name}: {p.totalScore ?? p.TotalScore ?? 0}점
                        </span>
                    ))}
                </div>
                <button className="exit-btn" onClick={handleExit}>기권</button>
            </div>

            <div className="game-table-area">
                <div className="table-oval" style={{ position: 'relative' }}>
                    <div style={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '20px' }}>
                        <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>
                            {/* DECK 클릭 시 모달 열기 추가 */}
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} onClick={() => {
                                if(canDraw) connection.invoke("DrawCard", roomId);
                                else setShowDiscardModal(true); // 드로우 불가능할 때 덱 누르면 버려진 카드 보기
                            }}>
                                <span className="label">DECK</span>
                                <div className="count">{deckCount}</div>
                                {isHost && deckCount === 0 && <button className="reshuffle-badge" onClick={(e) => { e.stopPropagation(); handleReshuffle(); }}>🔄 셔플</button>}
                            </div>
                            {/* DROP 영역 클릭 시 모달 열기 */}
                            <div className={`card-ui discard ${(lastDiscarded?.color || lastDiscarded?.Color) === 'Red' ? 'red' : 'black'}`} onClick={() => setShowDiscardModal(true)} style={{ cursor: 'pointer' }}>
                                {lastDiscarded ? (
                                    <>
                                        <span className="rank">{getRankText(lastDiscarded.rank || lastDiscarded.Rank)}</span>
                                        <span className="suit">{(lastDiscarded.suit || lastDiscarded.Suit) === "Joker" ? "🃏" : (lastDiscarded.suit || lastDiscarded.Suit)}</span>
                                    </>
                                ) : <span className="empty-label">DROP</span>}
                            </div>
                        </div>
                        <button className={`interrupt-btn ${canInterrupt ? 'active' : ''}`} onClick={() => canInterrupt && connection.invoke("InterruptDiscard", roomId)} disabled={!canInterrupt}>가로채기</button>
                    </div>

                    {others.map((player: any, idx: number) => (
                        <div key={player.playerId || player.PlayerId} className={`player-box ${currentTurnId === (player.playerId || player.PlayerId) ? 'active-turn' : ''}`} style={{ position: 'absolute', top: ENEMY_POSITIONS[idx]?.top, left: ENEMY_POSITIONS[idx]?.left, transform: 'translate(-50%, -50%)' }}>
                            <div className="player-name">{player.name || player.Name}</div>
                            <div className="opponent-card-back">{(player.hand || player.Hand)?.length || 0}</div>
                        </div>
                    ))}
                </div>
            </div>

            <div className="hand-area">
                <div className="turn-status-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '20px 0' }}>
                    <span className={`status-text ${isMyTurn ? "active-text" : ""}`} style={{ fontSize: '1.2rem', fontWeight: 'bold', marginRight: '15px' }}>
                        {isMyTurn ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") : "상대방의 턴입니다..."}
                    </span>
                    {isMyTurn && <button className="win-btn highlight" onClick={() => connection.invoke("DeclareWin", roomId)}>🏆 승리 선언</button>}
                </div>

                <div className="cards-in-hand" style={{ display: 'flex', gap: '15px', justifyContent: 'center' }}>
                    {(me?.hand || me?.Hand) && sortCards(me.hand || me.Hand).map((card: any, idx: number) => {
                        const rankText = getRankText(card.rank || card.Rank);
                        const suitText = (card.suit === "Joker" || card.Suit === "Joker") ? "🃏" : (card.suit || card.Suit);
                        const cardKey = `${card.rank || card.Rank}-${card.suit || card.Suit}-${idx}`;
                        const isNew = cardKey === lastDrawnCardKey;

                        return (
                            <div key={cardKey} className={`card-ui my-card ${(card.color || card.Color) === 'Red' ? 'red' : 'black'} ${isNew ? "new-card-highlight" : ""}`}
                                style={{ width: '100px', height: '145px', background: rankText === "JK" ? "#f1c40f" : "white", cursor: canDiscardOrWin ? 'pointer' : 'default', borderRadius: '10px', display: 'flex', flexDirection: 'column', justifyContent: 'space-between', padding: '10px', boxShadow: '0 4px 8px rgba(0,0,0,0.3)', transition: 'transform 0.1s' }}
                                onClick={() => canDiscardOrWin && connection.invoke("PlayCard", roomId, card)}>
                                <span className="rank" style={{ fontWeight: 'bold' }}>{rankText}</span>
                                <span className="suit" style={{ fontSize: '2.5rem', textAlign: 'center' }}>{suitText}</span>
                                <span className="rank" style={{ fontWeight: 'bold', textAlign: 'right', transform: 'rotate(180deg)' }}>{rankText}</span>
                            </div>
                        );
                    })}
                </div>
            </div>

            {/* 버려진 카드 확인 모달 (오름차순 정렬 적용) */}
            {showDiscardModal && (
                <div className="discard-modal-overlay" onClick={() => setShowDiscardModal(false)}>
                    <div className="discard-modal-content" onClick={e => e.stopPropagation()}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px', borderBottom: '2px solid #34495e', paddingBottom: '10px' }}>
                            <h2 style={{ margin: 0, color: 'white' }}>버려진 카드 기록 ({discardPile.length})</h2>
                            <button onClick={() => setShowDiscardModal(false)} style={{ background: '#e74c3c', color: 'white', border: 'none', padding: '8px 15px', borderRadius: '4px', cursor: 'pointer', fontWeight: 'bold' }}>닫기</button>
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(70px, 1fr))', gap: '12px', padding: '10px' }}>
                            {discardPile.length === 0 ? (
                                <p style={{ textAlign: 'center', gridColumn: '1/-1', color: '#bdc3c7' }}>버려진 카드가 없습니다.</p>
                            ) : (
                                // sortCards를 사용하여 오름차순으로 정렬하여 표시
                                sortCards(discardPile).map((card: any, idx: number) => (
                                    <div key={idx} style={{ background: 'white', color: (card.color ?? card.Color) === 'Red' ? 'red' : 'black', borderRadius: '8px', padding: '10px', textAlign: 'center', boxShadow: '0 2px 5px rgba(0,0,0,0.2)' }}>
                                        <div style={{ fontWeight: 'bold', fontSize: '14px' }}>{getRankText(card.rank ?? card.Rank)}</div>
                                        <div style={{ fontSize: '24px' }}>{(card.suit ?? card.Suit) === "Joker" ? "🃏" : (card.suit ?? card.Suit)}</div>
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </div>
            )}

            {isFinished && (
                <div style={{ position: 'fixed', top: 0, left: 0, width: '100%', height: '100%', background: 'rgba(0,0,0,0.9)', display: 'flex', justifyContent: 'center', alignItems: 'center', zIndex: 10000 }}>
                    <div style={{ background: 'white', padding: '30px', borderRadius: '15px', textAlign: 'center', color: 'black', minWidth: '320px' }}>
                        <h1>GAME OVER</h1>
                        <h2 style={{ color: '#e67e22' }}>우승: {game.winnerName ?? game.WinnerName}</h2>
                        <hr />
                        {players.map((p: any) => (
                            <div key={p.playerId ?? p.PlayerId} style={{ display: 'flex', justifyContent: 'space-between', padding: '5px 10px' }}>
                                <span>{p.name ?? p.Name}</span>
                                <strong>{p.totalScore ?? p.TotalScore}점</strong>
                            </div>
                        ))}
                        <button onClick={handleReturnToRoom} style={{ marginTop: '20px', width: '100%', padding: '12px', background: '#27ae60', color: 'white', border: 'none', borderRadius: '5px', fontWeight: 'bold', cursor: 'pointer' }}>확인 (대기실로 복귀)</button>
                    </div>
                </div>
            )}
        </div>
    );
}