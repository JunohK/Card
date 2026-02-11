import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";
import "../css/GamePage.css";

const ENEMY_POSITIONS = [
    { top: '40%', left: '10%' }, { top: '20%', left: '25%' },
    { top: '15%', left: '50%' }, { top: '20%', left: '75%' },
    { top: '40%', left: '90%' }, { top: '70%', left: '85%' },
];

type MyProfile = {
    name: string;
    wins: number;
    totalGames: number;
    maxScore: number;
    minScore: number;
}

interface GameState {
    isNaturalBagajiEnabled: boolean;
    players: any[];
    currentTurnPlayerId: string;
}


export default function GamePage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const [game, setGame] = useState<any>(null);
    const [showRoundResult, setShowRoundResult] = useState(false);
    const [showDiscardModal, setShowDiscardModal] = useState(false);
    const [lastDrawnCardKey, setLastDrawnCardKey] = useState<string | null>(null);
    const [input, setInput] = useState("");
    const [connected, setConnected] = useState(connection.state === "Connected");
    const [messages, setMessages] = useState<string[]>([]);
    const [myProfile, setMyProfile] = useState<MyProfile>({ 
        name: "", 
        wins: 0, 
        totalGames: 0, 
        maxScore: 0, 
        minScore: 0 
    });
    const [isChatMinimized, setIsChatMinimized] = useState(true); // false로 하면 최소화가 기본값
    const [hasNewMessage, setHasNewMessage] = useState(false);
    const [winnerHand, setWinnerHand] = useState<any[]>([]);
    const [winnerName, setWinnerName] = useState<string | null>(null);
    
    // // 메시지가 새로 추가되면 자동으로 채팅창을 펼침
    // useEffect(() => {
    //     if (messages.length > 0) {
    //         setIsChatMinimized(false);
    //     }
    // }, [messages]);
    const chatRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        // chatRef.current가 존재하는지(null이 아닌지) 체크 후 호출
        if (chatRef.current) {
            chatRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages]);

    useEffect(() => {
        connection.on("NaturalBagajiToggled", (isEnabled: boolean) => {
            // game 상태 업데이트
            setGame((prev: GameState) => ({
                ...prev,
                isNaturalBagajiEnabled: isEnabled
            }));
        });

        return () => {
            connection.off("NaturalBagajiToggled");
        };
    }, [connection]);

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

    const sendMessage = async () => {
        if (!input.trim() || connection.state !== "Connected") return;
        await connection.invoke("SendChatMessage", input);
        setInput("");
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
        const onUpdate = async (data: any) => {
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
            
            const roundEnded = data.isRoundEnded || data.IsRoundEnded;
            const gameFinished = data.isFinished || data.IsFinished;

            if (gameFinished) {
                setShowRoundResult(false); 
                try {
                    // 서버에 게임 결과 반영 요청
                    await connection.invoke("UpdateGameResult", roomId);
                } catch (err) {
                    console.error("DB 업데이트 요청 실패:", err);
                }
            } else if (roundEnded) {
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

        const onReceiveMessage = (user: string, message: string) => {
            setMessages(prev => [...prev, `${user} : ${message}`]);
            if (isChatMinimized) {
                setHasNewMessage(true);
            }
        };

        const onConnectedUser = (data: any) => {
            if (typeof data === "string") {
                setMyProfile(prev => ({ ...prev, name: data }));
            } else {
                setMyProfile({
                    name: data.nickname || data.name || "",
                    wins: data.wins || 0,
                    totalGames: data.totalGames || 0,
                    maxScore: data.maxScore || 0,
                    minScore: data.minScore || 0
                });
            }
        };

        if (!isSubscribed.current) {
            connection.on("RoomUpdated", onUpdate);
            connection.on("ReshuffleDeck", onUpdate);
            connection.on("ReceiveMessage", onReceiveMessage);
            connection.on("ConnectedUser", onConnectedUser);

            connection.on("GameStarted", (data) => {
                setGame((prev: any) => ({
                    ...prev,
                    winnerName: null,
                    winnerHand: [],
                    isBagaji: false,
                    showResult: false 
                }));
                onUpdate(data);
            });

            connection.on("ShowResultBoard", (data: any) => {
                console.log("SHOW RESULT BOARD", data.WinnerHand);
                setWinnerHand(data.WinnerHand || []);
                setWinnerName(data.WinnerName || null);
                setShowRoundResult(true);
            });

            connection.on("HideResultBoard", onHideResultBoard);
            connection.on("GameTerminated", onGameTerminated);
            connection.on("ExitToRoom", onExitToRoom);

            connection.on("ReshuffleDeck", (msg) => {
                console.log(msg);
            });

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

        // 프로필 정보 로드
        connection.invoke("GetMyProfile").then(data => {
            if (data) setMyProfile(data);
        }).catch(err => console.error("프로필 로드 실패:", err));

        return () => {
            connection.off("ReceiveMessage", onReceiveMessage);
            connection.off("ConnectedUser", onConnectedUser);
            unsubscribeAll();
        };
    }, [roomId, myId, navigate, isChatMinimized]);

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
    const checkCanDeclareWin = () => {
        if (!isMyTurn) return false;

        // 'me' 대신 'myInfo'로 이름을 바꾸어 중복 선언 에러 방지
        const myInfo = players.find((p: any) => (p.playerId || p.PlayerId) === myId);
        const currentTurnCount = myInfo?.roundTurnCount || myInfo?.RoundTurnCount || 0;

        // 내가 2번째 턴 이상일 때만 버튼 활성화
        return currentTurnCount >= 2;
    };

    const canDeclareWin = checkCanDeclareWin();

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

        // 4. 내 턴 조건 완화 (턴이 넘어온 직후에도 뻥 버튼이 유지되도록)
        // 상대가 버린 직후 턴이 나에게 왔더라도, 뻥을 칠 수 있는 기회를 주기 위해 
        // 내 손패가 아직 5장(뽑기 전)이라면 뻥 체크를 허용합니다.
        if (isMyTurn && currentHandCount !== 5) return false; 

        // 5. 비교 대상(상대가 버린 카드)의 숫자 추출
        const discardedRank = (lastDiscarded.rank || lastDiscarded.Rank)?.toString().toUpperCase();
        // 조커는 뻥의 대상(버려진 카드)이 될 수 없음
        if (!discardedRank || discardedRank === "JK" || discardedRank === "JOKER") return false;

        // 6. 내 손패 확인 (동일 숫자 카드와 조커 분리 추출)
        const sameRankCards = myHand.filter((c: any) => {
            const myCardRank = (c.rank || c.Rank)?.toString().toUpperCase();
            return myCardRank === discardedRank;
        });

        const jokerCards = myHand.filter((c: any) => {
            const myCardRank = (c.rank || c.Rank)?.toString().toUpperCase();
            return myCardRank === "JK" || myCardRank === "JOKER";
        });

        // 7. 뻥 구성 우선순위 결정 (7, 7, 조커 상황 대응)
        let finalPungCards: any[] = [];
        
        // 숫자 카드를 우선순위로 먼저 채움 (최대 2장)
        finalPungCards.push(...sameRankCards.slice(0, 2));

        // 숫자 카드가 2장이 안 될 때만 조커를 사용함
        if (finalPungCards.length < 2) {
            const needed = 2 - finalPungCards.length;
            finalPungCards.push(...jokerCards.slice(0, needed));
        }

        // 최종 결과: 우선순위에 따라 구성된 카드가 2장이면 뻥 가능(true)
        return finalPungCards.length === 2;
    };

    const canPung = checkCanPung();

    /** 🛑 STOP 버튼 활성화 조건 (본인 포함 2장인 사람 2명 이상) */
    const checkCanStop = () => {
        const currentHandCount = myHand.length;
        
        // 1. 내 턴이고, 내가 카드를 한 장 뽑아서 3장인 상태여야 함 (뽑기 전 2장)
        const isActionPhase = isMyTurn && currentHandCount === 3;
        if (!isActionPhase) return false;

        // 2. 패가 2장인 사람 카운트 (본인 포함)
        const playersWithTwoCards = players.filter((p: any) => {
            const pid = p.playerId || p.PlayerId;
            const handCount = p.hand?.length || p.Hand?.length || 0;

            if (pid === myId) {
                // 나는 현재 3장이지만 뽑기 전에는 2장이었으므로 조건 충족
                return true; 
            }
            
            // 다른 플레이어들은 현재 손에 든 카드가 정확히 2장이어야 함
            return handCount === 2;
        });

        // 본인(2장 상태에서 뽑은 자)을 포함하여 2장인 사람이 최소 2명 이상일 때
        return playersWithTwoCards.length >= 2;
    };

    /** 🛑 STOP 버튼 클릭 핸들러 */
    const handleStop = () => {
        if (!canStop) return;
        
        if (window.confirm("STOP을 선언하시겠습니까? 이번에 카드를 버리면 게임이 종료됩니다.")) {
            connection.invoke("DeclareStop", roomId)
                .catch(err => console.error("STOP 호출 실패:", err));
        }
    };

    const canStop = checkCanStop();

    /** 🔥 뻥 버튼 클릭 핸들러 */
    const handlePung = () => {
        // 뻥 버튼을 누를 수 있는 조건이 있다면 체크 (예: 내 차례가 아닐 때도 가능한지 등)
        // if (!canPung) return; 

        if (window.confirm("뻥을 선언하시겠습니까? 성공하면 상대의 차례를 뺏어옵니다!")) {
            connection.invoke("DeclarePung", roomId)
                .catch(err => console.error("뻥 호출 실패:", err));
        }
    };

return (
        <div className={`game-container ${isMyTurn ? "my-turn-flash" : ""}`} style={{ position: 'relative', minHeight: '100vh', overflow: 'hidden' }}>
            <style>{`
                @keyframes fadeInModal {
                    0% { opacity: 0; }
                    100% { opacity: 1; }
                }

                .fade-in-2s {
                    animation: fadeInModal 2s ease-in-out forwards;
                }
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
                /* ✨ 내 턴일 때 화면 테두리 금색 광채 애니메이션 */
                @keyframes goldGlow {
                    0% { box-shadow: inset 0 0 10px #f1c40f; }
                    50% { box-shadow: inset 0 0 40px #f39c12; }
                    100% { box-shadow: inset 0 0 10px #f1c40f; }
                }
                /* ✨ 뻥/알림 메시지 애니메이션 */
                @keyframes notifyPop {
                    0% { transform: translate(-50%, -60%); opacity: 0; }
                    10% { transform: translate(-50%, -50%); opacity: 1; }
                    90% { transform: translate(-50%, -50%); opacity: 1; }
                    100% { transform: translate(-50%, -40%); opacity: 0; }
                }

                .my-turn-flash { animation: goldGlow 1.5s infinite ease-in-out; }
                .new-card-highlight { animation: blueGlow 0.8s ease-in-out infinite !important; z-index: 100 !important; }
                .pung-active { animation: redPulse 0.5s infinite !important; background-color: #e74c3c !important; color: white !important; border: 2px solid white !important; cursor: pointer !important; opacity: 1 !important; z-index: 1000; }
                .rule-btn-fixed { position: fixed; bottom: 20px; left: 20px; padding: 10px 20px; background: #f1c40f; color: #2c3e50; border: none; border-radius: 50px; font-weight: bold; cursor: pointer; z-index: 1000; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }
                .reshuffle-badge { cursor: pointer; background: #2980b9; padding: 4px 8px; border-radius: 4px; font-size: 11px; margin-top: 8px; border: none; color: white; transition: 0.2s; }
                .reshuffle-badge:hover { background: #3498db; }
                .discard-modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: flex; justify-content: center; align-items: center; z-index: 9999; }
                .discard-modal-content { background: #2c3e50; width: 80%; max-width: 600px; max-height: 80vh; overflow-y: auto; padding: 20px; border-radius: 12px; border: 1px solid #34495e; }
                
                /* 알림 오버레이 스타일 */
                .turn-notify-overlay {
                    position: fixed; top: 35%; left: 50%; transform: translate(-50%, -50%);
                    background: rgba(0, 0, 0, 0.85); color: #f1c40f; padding: 20px 50px;
                    border-radius: 60px; z-index: 11000; font-weight: bold; font-size: 2.2rem;
                    border: 4px solid #f1c40f; pointer-events: none; animation: notifyPop 2.5s forwards;
                    box-shadow: 0 0 30px rgba(241, 196, 15, 0.4); text-shadow: 2px 2px 4px rgba(0,0,0,0.5);
                }
            `}</style>

            {/* 📢 뻥 성공 및 턴 시작 알림 (alertMsg 상태가 있을 때 노출) */}
            {/* {alertMsg && <div className="turn-notify-overlay">📢 {alertMsg}</div>} */}

            {/* 🔴 조그만 에러 알림 팝업 UI */}
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
                        {/* 덱과 버린 카드 레이아웃 */}
                        <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} 
                                style={{
                                    width: '100px',
                                    height: '140px',
                                    cursor: canDraw ? 'pointer' : 'default'
                                }}
                                onClick={() => {
                                    if(canDraw) connection.invoke("DrawCard", roomId);
                                }}>
                                <span className="label">DECK</span>
                                <div className="count">{deckCount}</div>
                                {isHost && deckCount === 0 && <button className="reshuffle-badge" onClick={(e) => { e.stopPropagation(); handleReshuffle(); }}>🔄 셔플</button>}
                            </div>
                            
                            <div className={`card-ui discard ${(lastDiscarded?.color || lastDiscarded?.Color) === 'Red' ? 'red' : 'black'}`}
                                onClick={() => setShowDiscardModal(true)} 
                                style={{ 
                                    width: '100px',   // 동일하게 확장
                                    height: '140px',  // 동일하게 확장
                                    cursor: 'pointer',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    justifyContent: 'center',
                                    alignItems: 'center',
                                    border: '2px solid #333',
                                    borderRadius: '10px'
                                }}
                            >
                                {lastDiscarded ? (
                                    <>
                                        <span className="rank">{getRankText(lastDiscarded.rank || lastDiscarded.Rank)}</span>
                                        <span className="suit">{(lastDiscarded.suit || lastDiscarded.Suit) === "Joker" ? "🃏" : (lastDiscarded.suit || lastDiscarded.Suit)}</span>
                                    </>
                                ) : <span className="empty-label">DROP</span>}
                            </div>
                        </div>

                        {/* 액션 버튼 영역 (뻥과 STOP을 가로로 배치) */}
                        <div style={{ display: 'flex', flexDirection: 'row', gap: '15px', alignItems: 'center' }}>
                            {/* 🔥 뻥 버튼 */}
                            <button 
                                className={`interrupt-btn ${canPung ? 'pung-active' : ''}`} 
                                onClick={() => { if(canPung) connection.invoke("InterruptDiscard", roomId); }} 
                                disabled={!canPung}
                                style={{ 
                                    padding: '12px 25px', 
                                    borderRadius: '10px', 
                                    fontWeight: 'bold', 
                                    fontSize: '1.1rem', 
                                    transition: '0.3s',
                                    minWidth: '100px'
                                }}
                            >
                                {canPung ? "🔥 뻥!!" : "뻥"}
                            </button>

                            {/* STOP 버튼 추가 (뻥 버튼 오른쪽에 배치됨) */}
                            <button 
                                className={`interrupt-btn ${canStop ? 'stop-active' : ''}`} 
                                onClick={handleStop}
                                disabled={!canStop}
                                style={{ 
                                    padding: '12px 25px', 
                                    borderRadius: '10px', 
                                    fontWeight: 'bold', 
                                    fontSize: '1.1rem',
                                    opacity: canStop ? 1 : 0.5, 
                                    cursor: canStop ? 'pointer' : 'default',
                                    backgroundColor: canStop ? '#f1c40f' : '#7f8c8d', 
                                    color: canStop ? '#2c3e50' : 'white', 
                                    border: 'none',
                                    minWidth: '100px',
                                    transition: '0.3s'
                                }}
                            >
                                {canStop ? "🛑 STOP" : "STOP"}
                            </button>
                            <button
                                className="win-btn highlight"
                                onClick={() => {
                                    connection.invoke("ToggleNaturalBagaji", roomId);
                                }}
                            >
                                자연바가지 {game.isNaturalBagajiEnabled ? "ON" : "OFF"}
                            </button>
                        </div>
                    </div>

                    {others.map((player: any, idx: number) => {
                        // 🟢 바가지 체크 로직 (카드 2장 중 조커 포함 혹은 동일 숫자)
                        const opponentHand = player.hand || player.Hand || [];
                        let isBagaji = false;

                        if (opponentHand.length === 2) {
                            const card1 = opponentHand[0]?.rank || opponentHand[0]?.Rank;
                            const card2 = opponentHand[1]?.rank || opponentHand[1]?.Rank;

                            const isCard1Joker = card1 === "JK" || card1 === "JOKER" || card1 === "Joker";
                            const isCard2Joker = card2 === "JK" || card2 === "JOKER" || card2 === "Joker";

                            if (isCard1Joker || isCard2Joker) {
                                // 조건 1: 한 장이라도 조커면 바가지
                                isBagaji = true;
                            } else if (getRankText(card1) === getRankText(card2)) {
                                // 조건 2: 조커는 없지만 두 카드의 숫자가 같으면 바가지
                                isBagaji = true;
                            }
                        }

                        return (
                            <div key={player.playerId || player.PlayerId} className={`player-box ${currentTurnId === (player.playerId || player.PlayerId) ? 'active-turn' : ''}`} style={{ position: 'absolute', top: ENEMY_POSITIONS[idx]?.top, left: ENEMY_POSITIONS[idx]?.left, transform: 'translate(-50%, -50%)' }}>
                                <div className="player-name">{player.name || player.Name}</div>
                                <div className="opponent-card-back">{(player.hand || player.Hand)?.length || 0}</div>
                                
                                {/* 🔴 바가지 표시 추가 */}
                                {isBagaji && (
                                    <div className="bagaji-label" style={{ 
                                        marginTop: '5px', 
                                        color: '#e74c3c', 
                                        fontWeight: 'bold', 
                                        fontSize: '0.9rem',
                                        textShadow: '0 0 5px rgba(255,255,255,0.5)',
                                        textAlign: 'center',
                                        animation: 'pulse 1s infinite'
                                    }}>
                                        🔥 바가지
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>

            <div className="hand-area">
                <div className="turn-status-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '15px 0' }}>
                    <span className={`status-text ${isMyTurn || canPung ? "active-text" : ""}`} style={{ fontSize: '1.2rem', fontWeight: 'bold', marginRight: '15px', color: canPung ? '#e74c3c' : 'inherit' }}>
                        {isMyTurn ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") : (canPung ? "🔥 지금 바로 '뻥'이 가능합니다!" : "상대방의 턴입니다...")}
                    </span>
                    {/* 🏆 승리 선언 버튼: 내 턴 + 선언 가능할 때만 */}
                    {isMyTurn && canDeclareWin && (
                        <button
                            className="win-btn highlight"
                            onClick={() => connection.invoke("DeclareWin", roomId)}
                        >
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
                                <span className="rank" style={{ fontWeight: 'bold', fontSize: '1.1rem' }}>{rankText}</span>
                                <span className="suit" style={{ fontSize: '2.2rem', textAlign: 'center' }}>{suitText}</span>
                                <span className="rank" style={{ fontWeight: 'bold', textAlign: 'right', transform: 'rotate(180deg)', fontSize: '1.1rem' }}>{rankText}</span>
                            </div>
                        );
                    })}
                </div>
            </div>

            {/* 1. 라운드 결과창: showRoundResult가 true이고 게임이 완전히 끝나지 않았을 때 표시 */}
            {showRoundResult && !game.isFinished && (
                <div className="discard-modal-overlay fade-in-2s">
                    <div className="discard-modal-content" style={{ textAlign: 'center' }}>
                        <h2 style={{ color: '#f1c40f', marginBottom: '20px' }}>ROUND RESULT</h2>
                        
                        {/* 🏆 우승자 패 표시 영역 (로그 포함) */}
                        <div style={{ marginBottom: '25px', padding: '15px', background: 'rgba(255,255,255,0.05)', borderRadius: '12px' }}>
                            <p style={{ color: '#aaa', fontSize: '0.9rem', marginBottom: '10px' }}>우승자 카드 구성</p>
                            <div style={{ 
                                display: 'flex', 
                                gap: '8px', 
                                justifyContent: 'center', 
                                flexWrap: 'wrap', 
                                minHeight: '70px' 
                            }}> 
                                {(() => {
                                    const winnerCards = game.WinnerHand || game.winnerHand;

                                    if (!winnerCards || winnerCards.length === 0) {
                                        return (
                                            <div style={{ color: '#e74c3c' }}>
                                                <p>승리 카드를 불러올 수 없습니다.</p>
                                            </div>
                                        );
                                    }

                                    return sortCards(winnerCards).map((card: any, i: number) => {
                                        // 카드 개별 데이터 확인용 로그
                                        if (i === 0) console.log("4. Sample Card Object:", card);

                                        return (
                                            <div key={i} style={{ 
                                                width: '50px', height: '70px', background: 'white', borderRadius: '5px', 
                                                display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center',
                                                color: (card.Color || card.color) === 'Red' ? '#e74c3c' : '#2c3e50',
                                                border: '1px solid #ddd', fontSize: '0.8rem',
                                                boxShadow: '0 2px 4px rgba(0,0,0,0.2)'
                                            }}>
                                                <span style={{ fontWeight: 'bold' }}>{getRankText(card.Rank || card.rank)}</span>
                                                <span style={{ fontSize: '1.2rem' }}>
                                                    {(card.Suit || card.suit) === "Joker" ? "🃏" : (card.Suit || card.suit)}
                                                </span>
                                            </div>
                                        );
                                    });
                                })()}
                            </div>
                        </div>

                        <div style={{ marginBottom: '20px', padding: '12px', background: 'rgba(241, 196, 15, 0.1)', borderRadius: '8px', border: '1px solid #f1c40f' }}>
                            <span style={{ color: '#f1c40f', fontWeight: 'bold' }}>판정 결과: </span>
                            <span style={{ color: '#ffffff', fontSize: '1.2rem', fontWeight: 'bold', marginLeft: '8px' }}>
                                {game.LastWinType || game.lastWinType || "족보 확인 중..."}
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
                                    {(game.Players || game.players || []).map((p: any) => {
                                        const currentScore = p.Score !== undefined ? p.Score : (p.score ?? 0);
                                        return (
                                            <tr key={p.PlayerId || p.playerId} style={{ borderBottom: '1px solid #444' }}>
                                                <td style={{ padding: '10px' }}>{p.Name || p.name}</td>
                                                <td style={{ 
                                                    padding: '10px', 
                                                    color: currentScore <= 0 ? '#2ecc71' : '#e74c3c',
                                                    fontWeight: 'bold'
                                                }}>
                                                    {currentScore > 0 ? `+${currentScore}` : currentScore}
                                                </td>
                                                <td style={{ padding: '10px' }}>
                                                    {p.TotalScore !== undefined ? p.TotalScore : (p.totalScore ?? 0)} 점
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
                <div className="discard-modal-overlay fade-in-2s" style={{ backgroundColor: 'rgba(0, 0, 0, 0.9)', zIndex: 2000 }}>
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

                            {/* 🏆 최종 우승자의 카드 노출 */}
                            <div style={{ display: 'flex', gap: '10px', justifyContent: 'center', marginTop: '20px' }}>
                                {sortCards(game.winnerHand || []).map((card: any, i: number) => (
                                    <div key={i} style={{ 
                                        width: '60px', height: '85px', background: 'white', borderRadius: '6px', 
                                        display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center',
                                        color: (card.color || card.Color) === 'Red' ? '#e74c3c' : '#2c3e50',
                                        boxShadow: '0 0 15px rgba(241, 196, 15, 0.5)'
                                    }}>
                                        <span style={{ fontWeight: 'bold', fontSize: '1rem' }}>{getRankText(card.rank || card.Rank)}</span>
                                        <span style={{ fontSize: '1.8rem' }}>{(card.suit || card.Suit) === "Joker" ? "🃏" : (card.suit || card.Suit)}</span>
                                    </div>
                                ))}
                            </div>

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
            <div className="game-mini-chat" style={{
                position: 'fixed',
                bottom: '20px',
                right: '20px',
                width: '260px',
                // 최소화 상태일 때 높이를 45px(헤더+경계)로 고정
                height: isChatMinimized ? '45px' : '320px',
                backgroundColor: 'rgba(15, 23, 42, 0.95)',
                borderRadius: '12px',
                display: 'flex',
                flexDirection: 'column',
                border: '1px solid #334155',
                zIndex: 10002,
                fontSize: '0.85rem',
                boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
                overflow: 'hidden',
                transition: 'height 0.3s ease' // 높이 변경 시 부드러운 효과
            }}>
                {/* 헤더: 클릭 시 최소화/최대화 토글 및 알림 초기화 */}
                <div 
                    onClick={() => {
                        setIsChatMinimized(!isChatMinimized);
                        setHasNewMessage(false); // ✅ 클릭 시 알림 상태 초기화
                    }}
                    style={{ 
                        padding: '10px 12px', 
                        // ✅ 새 메시지가 있고 최소화 상태일 때 배경색을 노란색(#eab308)으로 변경
                        background: (hasNewMessage && isChatMinimized) ? '#eab308' : '#1e293b', 
                        borderBottom: isChatMinimized ? 'none' : '1px solid #334155', 
                        display: 'flex', 
                        alignItems: 'center', 
                        justifyContent: 'space-between', 
                        cursor: 'pointer',
                        userSelect: 'none',
                        transition: 'background-color 0.3s ease'
                    }}
                >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <div style={{ 
                            width: '8px', 
                            height: '8px', 
                            borderRadius: '50%', 
                            // ✅ 알림 중일 때는 상태 표시등도 대비를 위해 어둡게 표시 가능
                            backgroundColor: connected ? '#22c55e' : '#ef4444' 
                        }}></div>
                        <span style={{ 
                            fontWeight: 'bold', 
                            // ✅ 노란 배경일 때 글자색을 어두운 남색(#0f172a)으로 변경하여 가독성 확보
                            color: (hasNewMessage && isChatMinimized) ? '#0f172a' : '#cbd5e1', 
                            fontSize: '0.75rem', 
                            letterSpacing: '0.05em' 
                        }}>
                            {(hasNewMessage && isChatMinimized) ? 'NEW MESSAGE!' : 'LIVE CHAT'}
                        </span>
                    </div>
                    {/* 최소화 상태 표시 아이콘 */}
                    <span style={{ 
                        color: (hasNewMessage && isChatMinimized) ? '#0f172a' : '#94a3b8', 
                        fontSize: '0.7rem' 
                    }}>
                        {isChatMinimized ? '▲' : '▼'}
                    </span>
                </div>

                {/* 메시지 리스트: 최소화 상태가 아닐 때만 렌더링 */}
                {!isChatMinimized && (
                    <div className="chat-messages" style={{
                        flex: 1,
                        overflowY: 'auto',
                        padding: '12px',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '8px'
                    }}>
                        {messages.map((m, i) => {
                            const splitIdx = m.indexOf(" : ");
                            if (splitIdx === -1) return null;

                            const user = m.substring(0, splitIdx).trim(); // 공백 제거
                            const msg = m.substring(splitIdx + 3);
                            const isMe = myProfile.name && user === myProfile.name.trim(); // 내 이름과 비교

                            return (
                                <div key={i} style={{
                                    alignSelf: isMe ? 'flex-end' : 'flex-start',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: isMe ? 'flex-end' : 'flex-start',
                                    maxWidth: '85%'
                                }}>
                                    {!isMe && <div style={{ fontSize: '0.65rem', color: '#94a3b8', marginBottom: '2px' }}>{user}</div>}
                                    <div style={{
                                        backgroundColor: isMe ? '#2563eb' : '#334155',
                                        color: 'white',
                                        padding: '6px 12px',
                                        borderRadius: isMe ? '12px 12px 2px 12px' : '12px 12px 12px 2px',
                                        fontSize: '0.8rem'
                                    }}>
                                        {msg}
                                    </div>
                                </div>
                            );
                        })}
                        {/* 자동 스크롤을 위한 하단 지점 */}
                        <div ref={chatRef} />
                    </div>
                )}

                {/* 입력창: 최소화 상태가 아닐 때만 하단에 고정 */}
                {!isChatMinimized && (
                    <div style={{ padding: '10px', background: '#0f172a', borderTop: '1px solid #334155', display: 'flex', gap: '6px' }}>
                        <input 
                            className="chat-input"
                            style={{
                                flex: 1,
                                backgroundColor: '#1e293b',
                                border: '1px solid #475569',
                                color: 'white',
                                borderRadius: '6px',
                                padding: '6px 10px',
                                fontSize: '0.8rem',
                                outline: 'none'
                            }}
                            placeholder="메시지 입력..." 
                            value={input} 
                            onChange={e => setInput(e.target.value)}
                            onKeyDown={e => e.key === 'Enter' && sendMessage()}
                        />
                        <button 
                            onClick={sendMessage}
                            disabled={!connected}
                            style={{
                                backgroundColor: '#3b82f6',
                                color: 'white',
                                border: 'none',
                                borderRadius: '6px',
                                padding: '6px 12px',
                                fontSize: '0.75rem',
                                fontWeight: 'bold',
                                cursor: 'pointer',
                                opacity: connected ? 1 : 0.5
                            }}
                        >
                            전송
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
}