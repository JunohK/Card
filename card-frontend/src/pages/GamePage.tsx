import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";
import "../css/GamePage.css";

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
    
    // ✅ 에러 메시지 처리를 위한 상태 추가
    const [errorMsg, setErrorMsg] = useState<string | null>(null);

    const myId = connection.connectionId;
    const prevHandRef = useRef<string[]>([]);
    const isSubscribed = useRef(false);

    // 🟢 카드 고유 키 생성
    const getCardKey = (card: any) => {
        if (!card) return "";
        return `${card.rank || card.Rank}-${card.suit || card.Suit}-${card.id || card.Id || ""}`;
    };

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

    const unsubscribeAll = () => {
        connection.off("RoomUpdated");
        connection.off("GameStarted");
        connection.off("ShowResultBoard");
        connection.off("GameTerminated");
        connection.off("ErrorMessage");
        connection.off("HideResultBoard");
        connection.off("ExitToRoom");
        isSubscribed.current = false;
        console.log("🚫 모든 게임 리스너 구독 해제됨");
    };

    useEffect(() => {
        const onUpdate = (data: any) => {
            if (!data) return;

            const playersArr = data.players || data.Players || [];
            const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
            const currentHand = me?.hand || me?.Hand || [];
            
            const currentHandKeys = currentHand.map((c: any) => getCardKey(c));

            if (currentHandKeys.length > prevHandRef.current.length) {
                const newKey = currentHandKeys.find((key: string) => !prevHandRef.current.includes(key));
                if (newKey) {
                    setLastDrawnCardKey(newKey);
                    setTimeout(() => setLastDrawnCardKey(null), 1000);
                }
            }
            prevHandRef.current = currentHandKeys;

            setGame({ ...data });
            
            if (data.isRoundEnded || data.IsRoundEnded) {
                setShowRoundResult(true);
            }
        };

        const onHideResultBoard = () => setShowRoundResult(false);

        const onGameTerminated = (data: any) => {
            setGame((prev: any) => ({ ...prev, ...data, isFinished: true }));
        };

        const onExitToRoom = (targetRoomId: string) => {
            unsubscribeAll();
            navigate(`/room/${targetRoomId || roomId}`, { replace: true });
        };

        if (!isSubscribed.current) {
            connection.on("RoomUpdated", onUpdate);
            connection.on("GameStarted", onUpdate);
            connection.on("ShowResultBoard", onUpdate);
            connection.on("HideResultBoard", onHideResultBoard);
            connection.on("GameTerminated", onGameTerminated);
            connection.on("ExitToRoom", onExitToRoom);
            
            // ✅ 기존 alert(msg) 대신 커스텀 팝업 상태 업데이트로 변경
            connection.on("ErrorMessage", (msg) => {
                setErrorMsg(msg);
                setTimeout(() => setErrorMsg(null), 3000); 
            });
            
            isSubscribed.current = true;
        }

        connection.invoke("GetRoom", roomId).then(data => {
            if (data) {
                setGame(data);
                const playersArr = data.players || data.Players || [];
                const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
                prevHandRef.current = (me?.hand || me?.Hand || []).map((c: any) => getCardKey(c));
            }
        });

        return () => unsubscribeAll();
    }, [roomId, myId, navigate]);

    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 전체 게임이 종료되며 대기실로 이동합니다.")) {
            connection.invoke("GiveUp", roomId).catch(err => console.error("기권 처리 중 오류:", err));
        }
    };

    const handleReturnToRoom = () => {
        const winnerName = game?.winnerName || game?.WinnerName || "";
        const isGiveUp = winnerName.includes("(기권)");
        unsubscribeAll();
        if (isGiveUp) {
            navigate('/lobby', { replace: true });
        } else {
            navigate(`/room/${roomId}`, { replace: true });
        }
    };

    const handleNextRoundRequest = () => {
        connection.invoke("RequestNextRound", roomId).catch(err => alert("다음 라운드 시작 실패: " + err));
    };

    const handleReshuffle = () => {
        if (window.confirm("버려진 카드들을 다시 섞어서 덱으로 만드시겠습니까?")) {
            connection.invoke("ReshuffleDeck", roomId).catch(err => alert("셔플 실패: " + err));
        }
    };

    const openRules = () => {
        window.open('/rules', '_blank', 'width=600,height=800,noopener,noreferrer');
    };

    if (!game || (!game.players && !game.Players)) {
        return (
            <div className="game-container loading-state" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', color: 'white', height: '100vh' }}>
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

    const myHand = me?.hand || me?.Hand || [];
    const canDraw = isMyTurn && (myHand.length === 2 || myHand.length === 5);
    const canDiscardOrWin = isMyTurn && (myHand.length === 3 || myHand.length === 6);
    const isFinished = game.isFinished ?? game.IsFinished;

    /** * 🏆 승리 선언 활성화 조건 (최종 보강 버전)
     * 1. 내 턴이어야 함
     * 2. 내 턴 횟수가 2회 이상이거나, 모든 플레이어가 최소 1회 이상 행동했어야 함
     * 3. 만약 서버 데이터가 아직 연동되지 않았다면(0일 경우), 최소 10장 이상의 카드가 버려진 후에 활성화 (안전 장치)
     */
    const myTurnCount = me?.roundTurnCount ?? me?.RoundTurnCount ?? 0;
    const allPlayersActed = players.every((p: any) => (p.roundTurnCount ?? p.RoundTurnCount ?? 0) >= 1);
    
    // 최종 판단 로직
    const canDeclareWin = isMyTurn && (
        myTurnCount >= 2 || 
        allPlayersActed || 
        (discardPile.length > (players.length * 2)) // 서버 카운트 장애 대비 백업 조건
    );

    const checkCanPung = () => {
        // 1. 기본 조건: 내 손에 카드가 5장일 때만 가능
        const currentHandCount = myHand.length;
        if (currentHandCount !== 5) return false;

        // 2. 바닥에 버려진 카드가 있어야 함
        if (!lastDiscarded) return false; 

        // 3. 자가 뻥 방지: 내가 버린 카드는 내가 뻥 할 수 없음
        const lastActorId = game?.lastActorPlayerId || game?.LastActorPlayerId;
        if (lastActorId && String(lastActorId) === String(myId)) {
            return false; 
        }

        // 4. 내 턴이 아닐 때만 가능
        if (isMyTurn) return false; 

        // 5. 비교 대상(상대가 버린 카드)의 숫자 추출 (대소문자 처리 강화)
        const discardedRank = (lastDiscarded.rank || lastDiscarded.Rank)?.toString().toUpperCase();
        if (!discardedRank) return false;

        // 6. 내 손패 필터링 (일반 숫자 매칭 + 조커 포함)
        const sameRankCards = myHand.filter((c: any) => {
            const myCardRank = (c.rank || c.Rank)?.toString().toUpperCase();
            
            // 조건 1: 상대가 버린 카드 숫자와 내 카드의 숫자가 정확히 일치
            const isMatch = myCardRank === discardedRank;
            
            // 조건 2: 내 카드가 조커(JK 또는 JOKER)인 경우 (숫자 상관없이 뻥 재료가 됨)
            const isJoker = myCardRank === "JK" || myCardRank === "JOKER";
            
            return isMatch || isJoker;
        });

        // 7. 위 조건에 부합하는 카드가 내 손에 2장 이상 있으면 '뻥' 가능
        return sameRankCards.length >= 2; 
    };

    const canPung = checkCanPung();

    return (
        <div className="game-container" style={{ position: 'relative', minHeight: '100vh', overflow: 'hidden' }}>
            <style>{`
                @keyframes blueGlow {
                    0% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; }
                    50% { box-shadow: 0 0 25px #3498db; border: 3px solid #5dade2; }
                    100% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; }
                }
                @keyframes redPulse {
                    0% { box-shadow: 0 0 5px #e74c3c; }
                    50% { box-shadow: 0 0 30px #e74c3c; background-color: #ff5e4d; }
                    100% { box-shadow: 0 0 5px #e74c3c; }
                }
                .new-card-highlight { animation: blueGlow 0.8s ease-in-out infinite !important; z-index: 100 !important; }
                .pung-active { animation: redPulse 0.5s infinite !important; background-color: #e74c3c !important; color: white !important; border: 2px solid white !important; cursor: pointer !important; opacity: 1 !important; z-index: 1000; }
                .rule-btn-fixed { position: fixed; bottom: 20px; left: 20px; padding: 10px 20px; background: #f1c40f; color: #2c3e50; border: none; border-radius: 50px; font-weight: bold; cursor: pointer; z-index: 1000; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }
                .reshuffle-badge { cursor: pointer; background: #2980b9; padding: 4px 8px; border-radius: 4px; font-size: 11px; margin-top: 8px; border: none; color: white; transition: 0.2s; }
                .reshuffle-badge:hover { background: #3498db; }
                .discard-modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: flex; justify-content: center; align-items: center; z-index: 9999; }
                .discard-modal-content { background: #2c3e50; width: 80%; max-width: 600px; max-height: 80vh; overflow-y: auto; padding: 20px; border-radius: 12px; border: 1px solid #34495e; }
            `}</style>

            {/* 🔴 조그만 에러 알림 팝업 UI 추가 */}
            {errorMsg && (
                <div style={{
                    position: 'fixed', top: '15%', left: '50%', transform: 'translateX(-50%)',
                    backgroundColor: 'rgba(231, 76, 60, 0.95)', color: 'white', padding: '12px 25px',
                    borderRadius: '50px', zIndex: 10001, fontWeight: 'bold', boxShadow: '0 4px 15px rgba(0,0,0,0.4)',
                    fontSize: '1rem', border: '2px solid rgba(255,255,255,0.2)'
                }}>
                    ⚠️ {errorMsg}
                </div>
            )}

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
                    <div style={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '20px', zIndex: 10 }}>
                        <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} onClick={() => {
                                if(canDraw) connection.invoke("DrawCard", roomId);
                            }}>
                                <span className="label">DECK</span>
                                <div className="count">{deckCount}</div>
                                {isHost && deckCount === 0 && <button className="reshuffle-badge" onClick={(e) => { e.stopPropagation(); handleReshuffle(); }}>🔄 셔플</button>}
                            </div>
                            <div className={`card-ui discard ${(lastDiscarded?.color || lastDiscarded?.Color) === 'Red' ? 'red' : 'black'}`} onClick={() => setShowDiscardModal(true)} style={{ cursor: 'pointer' }}>
                                {lastDiscarded ? (
                                    <>
                                        <span className="rank">{getRankText(lastDiscarded.rank || lastDiscarded.Rank)}</span>
                                        <span className="suit">{(lastDiscarded.suit || lastDiscarded.Suit) === "Joker" ? "🃏" : (lastDiscarded.suit || lastDiscarded.Suit)}</span>
                                    </>
                                ) : <span className="empty-label">DROP</span>}
                            </div>
                        </div>
                        <button 
                            className={`interrupt-btn ${canPung ? 'pung-active' : ''}`} 
                            onClick={() => { if(canPung) connection.invoke("InterruptDiscard", roomId); }} 
                            disabled={!canPung}
                            style={{ padding: '12px 25px', borderRadius: '10px', fontWeight: 'bold', fontSize: '1.1rem', transition: '0.3s' }}
                        >
                            {canPung ? "🔥 뻥!!" : "뻥"}
                        </button>
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
                <div className="turn-status-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '15px 0' }}>
                    <span className={`status-text ${isMyTurn || canPung ? "active-text" : ""}`} style={{ fontSize: '1.2rem', fontWeight: 'bold', marginRight: '15px', color: canPung ? '#e74c3c' : 'inherit' }}>
                        {isMyTurn ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") : (canPung ? "🔥 지금 바로 '뻥'이 가능합니다!" : "상대방의 턴입니다...")}
                    </span>
                    
                    {/* 🏆 승리 선언 버튼: 조건 충족 시에만 렌더링 */}
                    {canDeclareWin && (
                        <button className="win-btn highlight" onClick={() => connection.invoke("DeclareWin", roomId)}>
                            🏆 승리 선언
                        </button>
                    )}
                </div>

                <div className="cards-in-hand" style={{ display: 'flex', gap: '10px', justifyContent: 'center', flexWrap: 'nowrap', paddingBottom: '20px' }}>
                    {sortCards(myHand).map((card: any) => {
                        const rankText = getRankText(card.rank || card.Rank);
                        const suitText = (card.suit === "Joker" || card.Suit === "Joker") ? "🃏" : (card.suit || card.Suit);
                        const cardKey = getCardKey(card);
                        const isNew = cardKey === lastDrawnCardKey;

                        return (
                            <div key={cardKey} 
                                className={`card-ui my-card ${(card.color || card.Color) === 'Red' ? 'red' : 'black'} ${isNew ? "new-card-highlight" : ""}`}
                                style={{ 
                                    width: '90px', height: '130px', 
                                    background: rankText === "JK" ? "#f1c40f" : "white", 
                                    cursor: canDiscardOrWin ? 'pointer' : 'default',
                                    borderRadius: '10px', display: 'flex', flexDirection: 'column', 
                                    justifyContent: 'space-between', padding: '10px', 
                                    boxShadow: '0 4px 8px rgba(0,0,0,0.3)', transition: 'transform 0.1s' 
                                }}
                                onClick={() => canDiscardOrWin && connection.invoke("PlayCard", roomId, card)}>
                                <span className="rank" style={{ fontWeight: 'bold', fontSize: '1rem' }}>{rankText}</span>
                                <span className="suit" style={{ fontSize: '2.2rem', textAlign: 'center' }}>{suitText}</span>
                                <span className="rank" style={{ fontWeight: 'bold', textAlign: 'right', transform: 'rotate(180deg)', fontSize: '1rem' }}>{rankText}</span>
                            </div>
                        );
                    })}
                </div>
            </div>

            {showRoundResult && !game.isFinished && (
                <div className="discard-modal-overlay">
                    <div className="discard-modal-content" style={{ textAlign: 'center' }}>
                        <h2 style={{ color: '#f1c40f', marginBottom: '20px' }}>ROUND RESULT</h2>
                        
                        <div style={{ marginBottom: '20px', padding: '12px', background: 'rgba(241, 196, 15, 0.1)', borderRadius: '8px', border: '1px solid #f1c40f' }}>
                            <span style={{ color: '#f1c40f', fontWeight: 'bold' }}>판정 결과: </span>
                            <span style={{ color: '#ffffff', fontSize: '1.2rem', fontWeight: 'bold', marginLeft: '8px' }}>
                                {game.lastWinType || game.LastWinType || "족보 확인 중..."}
                            </span>
                        </div>

                        <div style={{ margin: '20px 0', color: 'white' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '1px solid #555' }}>
                                        <th style={{ padding: '10px' }}>플레이어</th>
                                        <th style={{ padding: '10px' }}>획득 점수</th>
                                        <th style={{ padding: '10px' }}>누적 점수</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {players.map((p: any) => {
                                        const currentScore = p.score !== undefined ? p.score : (p.Score ?? 0);
                                        return (
                                            <tr key={p.playerId || p.PlayerId} style={{ borderBottom: '1px solid #444' }}>
                                                <td style={{ padding: '10px' }}>{p.name || p.Name}</td>
                                                <td style={{ 
                                                    padding: '10px', 
                                                    color: currentScore <= 0 ? '#2ecc71' : '#e74c3c',
                                                    fontWeight: 'bold'
                                                }}>
                                                    {currentScore > 0 ? `+${currentScore}` : currentScore}
                                                </td>
                                                <td style={{ padding: '10px' }}>
                                                    {p.totalScore !== undefined ? p.totalScore : (p.TotalScore ?? 0)} 점
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                        {isHost ? (
                            <button onClick={handleNextRoundRequest} style={{ padding: '15px 30px', background: '#27ae60', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}>
                                다음 라운드 시작
                            </button>
                        ) : (
                            <p style={{ color: '#bdc3c7' }}>방장이 다음 라운드를 준비 중입니다...</p>
                        )}
                    </div>
                </div>
            )}

            {/* 2. 최종 결과창: game.isFinished가 true일 때만 표시 (1라운드 게임인 경우 바로 이 창이 뜸) */}
            {game.isFinished && (
                <div className="discard-modal-overlay" style={{ backgroundColor: 'rgba(0, 0, 0, 0.9)', zIndex: 2000 }}>
                    <div className="discard-modal-content" style={{ textAlign: 'center', border: '2px solid #f1c40f', padding: '40px' }}>
                        <h1 style={{ color: '#f1c40f', fontSize: '2.5rem', marginBottom: '10px' }}>
                            {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "GIVE UP" : "GAME OVER"}
                        </h1>
                        
                        <div style={{ marginBottom: '30px', padding: '20px', background: 'rgba(241, 196, 15, 0.1)', borderRadius: '12px' }}>
                            <span style={{ color: '#bdc3c7', display: 'block', marginBottom: '5px' }}>
                                {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "기권 승리자" : "최종 우승자"}
                            </span>
                            <span style={{ color: '#ffffff', fontSize: '2rem', fontWeight: 'bold' }}>
                                👑 {game.winnerName || game.WinnerName || "-"}
                            </span>
                            <div style={{ color: '#f1c40f', marginTop: '10px' }}>
                                판정 족보: {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "상대방 기권" : (game.lastWinType || game.LastWinType || "게임 종료")}
                            </div>
                        </div>

                        <div style={{ margin: '20px 0', maxHeight: '300px', overflowY: 'auto' }}>
                            <table style={{ width: '100%', color: 'white', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '2px solid #f1c40f', height: '40px' }}>
                                        <th>순위</th>
                                        <th>플레이어</th>
                                        <th>최종 총점</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {[...(game.players || [])]
                                        .sort((a, b) => (a.totalScore ?? a.TotalScore ?? 0) - (b.totalScore ?? b.TotalScore ?? 0))
                                        .map((p, index) => (
                                            <tr key={p.playerId || p.PlayerId} style={{ height: '45px', borderBottom: '1px solid #333' }}>
                                                <td>{index + 1}위</td>
                                                <td>{p.name || p.Name}</td>
                                                <td>{p.totalScore ?? p.TotalScore} 점</td>
                                            </tr>
                                        ))}
                                </tbody>
                            </table>
                        </div>

                        <button 
                            onClick={handleReturnToRoom} 
                            style={{ padding: '15px 50px', background: '#f1c40f', color: '#000', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}
                        >
                            확인
                        </button>
                    </div>
                </div>
            )}
            
            {showDiscardModal && (
                <div className="discard-modal-overlay" onClick={() => setShowDiscardModal(false)}>
                    <div className="discard-modal-content" onClick={e => e.stopPropagation()}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
                            <h2 style={{ color: 'white' }}>버려진 카드 기록 ({discardPile.length})</h2>
                            <button onClick={() => setShowDiscardModal(false)} style={{ background: '#e74c3c', color: 'white', border: 'none', padding: '8px 15px', borderRadius: '4px' }}>닫기</button>
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(65px, 1fr))', gap: '10px' }}>
                            {sortCards(discardPile).map((card: any, idx: number) => (
                                <div key={idx} style={{ background: 'white', color: (card.color ?? card.Color) === 'Red' ? 'red' : 'black', borderRadius: '8px', padding: '8px', textAlign: 'center' }}>
                                    <div style={{ fontWeight: 'bold', fontSize: '12px' }}>{getRankText(card.rank ?? card.Rank)}</div>
                                    <div style={{ fontSize: '20px' }}>{(card.suit ?? card.Suit) === "Joker" ? "🃏" : (card.suit ?? card.Suit)}</div>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}