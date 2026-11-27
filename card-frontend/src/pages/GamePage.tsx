import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection, ensureConnection, setActiveRoom } from "../signalr/connection";
import { getMyPlayerId } from "../auth/authStorage";
import { useTurnTimeout } from "../util/useTurnTimeout";
import { playSound } from "../sound/sound";
import { pushHistory } from "../util/gameHistory";
import { getCardKey, getRankText, getRankValue, isJokerRank, sortCards } from "../util/cardRank";
import type { CardGameState, CardRoomView, CardView, MyProfile, WinOut } from "../types/cardGame";
import StatsModal from "../components/StatsModal";
import CardFace from "../components/game/CardFace";
import GameChat from "../components/game/GameChat";
import RoundResultModal from "../components/game/RoundResultModal";
import FinalResultModal from "../components/game/FinalResultModal";
import ScoreHistoryModal from "../components/game/ScoreHistoryModal";
import DiscardPileModal from "../components/game/DiscardPileModal";
import "../css/GamePage.css";

// 이 페이지가 구독하는 서버 이벤트 — 나갈 때 한 번에 해제
const GAME_EVENTS = ["RoomUpdated", "GameStarted", "ShowResultBoard", "GameTerminated", "ErrorMessage", "HideResultBoard", "ExitToRoom"];
const offGameEvents = () => GAME_EVENTS.forEach(name => connection.off(name));

// 서버 뷰 → 화면 상태. 자연바가지 설정은 내 플레이어 값 (없으면 이전 값, 기본 ON)
const toGameState = (data: CardRoomView, myId: string | null, prev: CardGameState | null): CardGameState => {
    const me = data.players.find(p => p.playerId === myId);
    return { ...data, isNaturalBagajiEnabled: me?.allowNaturalBagaji ?? prev?.isNaturalBagajiEnabled ?? true };
};

// 손패에서 2장 이상인 숫자마다 테두리 색 클래스를 배정 (숫자 오름차순, 6색 순환). 조커는 제외 — CSS에서 별도 금색
const buildRankGroups = (hand: CardView[]): Record<string, string> => {
    const counts: Record<string, number> = {};
    for (const c of hand) {
        const r = c.rank.toUpperCase();
        counts[r] = (counts[r] ?? 0) + 1;
    }
    const groups: Record<string, string> = {};
    Object.keys(counts)
        .filter(r => counts[r] >= 2 && !isJokerRank(r))
        .sort((a, b) => getRankValue(a) - getRankValue(b))
        .forEach((r, i) => { groups[r] = `rank-group-${i % 6}`; });
    return groups;
};

// 상대 좌석 — 본인(6시)을 포함한 전원을 테이블 타원 위에 균등 배치.
// 2명이면 12시, 3명이면 10시·2시, 4명이면 9·12·3시 … 최대 7명까지 같은 비율.
// k = 내 다음 턴부터 시계 방향 순서(1..n), totalPlayers = 본인 포함 인원
const seatPosition = (k: number, totalPlayers: number) => {
    const theta = Math.PI + (2 * Math.PI * k) / totalPlayers; // 12시 기준 시계 방향 각도, 본인은 π(6시)
    return { top: `${50 - 35 * Math.cos(theta)}%`, left: `${50 + 40 * Math.sin(theta)}%` };
};

// 뻥 버튼 표시용 판단 — 서버 GameRoomService.CanPung의 5장 규칙과 같음 (최종 판단은 서버)
// 버린 카드와 같은 숫자 + 조커가 합쳐 2장 이상. 조커는 뻥의 대상(버려진 카드)이 될 수 없음
const canPungWith = (hand: CardView[], discarded: CardView): boolean => {
    if (hand.length !== 5 || isJokerRank(discarded.rank)) return false;
    const target = discarded.rank.toUpperCase();
    const sameRank = hand.filter(c => c.rank.toUpperCase() === target).length;
    const jokers = hand.filter(c => isJokerRank(c.rank)).length;
    return sameRank + jokers >= 2;
};

// 족보 힌트 문구 — "이 카드가 오면 승리: 4, 10 → 스트레이트 · 조커 → 스트레이트"
const formatOuts = (outs: WinOut[], hwatu: boolean) => {
    const byType: Record<string, string[]> = {};
    for (const o of outs) {
        const label = o.rank === "Joker" ? "조커" : (hwatu ? String(getRankValue(o.rank)) : getRankText(o.rank));
        (byType[o.winType] ??= []).push(label);
    }
    return "이 카드가 오면 승리: " + Object.entries(byType).map(([type, ranks]) => `${ranks.join(", ")} → ${type}`).join(" · ");
};

type ConnectedUserPayload = string | (Partial<MyProfile> & { nickname?: string });

export default function GamePage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const myId = getMyPlayerId();

    // 재접속(네트워크 끊김 자동 재연결) 시 자동 재입장할 방 등록
    useEffect(() => {
        setActiveRoom(roomId ?? null);
        return () => setActiveRoom(null);
    }, [roomId]);

    const [game, setGame] = useState<CardGameState | null>(null);
    const [showRoundResult, setShowRoundResult] = useState(false);
    const [showDiscardModal, setShowDiscardModal] = useState(false);
    const [showScoreHistory, setShowScoreHistory] = useState(false);
    const [showStats, setShowStats] = useState(false); // 결과창에서 방을 떠나지 않고 띄우는 게임 기록 모달
    const [lastDrawnCardKey, setLastDrawnCardKey] = useState<string | null>(null);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);
    const [myProfile, setMyProfile] = useState<MyProfile>({ name: "", wins: 0, totalGames: 0, maxScore: 0, minScore: 0 });

    const prevHandRef = useRef<string[]>([]);
    const isSubscribed = useRef(false);
    const historyPushedRef = useRef(false);   // 게임당 1회만 전적 기록 (종료 후 RoomUpdated가 반복돼도 중복 방지)

    const leaveGameEvents = () => {
        offGameEvents();
        isSubscribed.current = false;
    };

    // 자연바가지 토글 결과 — 본인에게만 오는 이벤트
    useEffect(() => {
        const onToggled = (isEnabled: boolean) => setGame(prev => prev && { ...prev, isNaturalBagajiEnabled: isEnabled });
        connection.on("NaturalBagajiToggled", onToggled);
        return () => connection.off("NaturalBagajiToggled", onToggled);
    }, []);

    useEffect(() => {
        const onUpdate = (data: CardRoomView) => {
            if (!data) return;

            // 새로 들어온 카드 강조 (손패가 늘었을 때 이전에 없던 카드)
            const me = data.players.find(p => p.playerId === myId);
            const handKeys = (me?.hand ?? []).map(getCardKey);
            if (handKeys.length > prevHandRef.current.length) {
                const newKey = handKeys.find(key => !prevHandRef.current.includes(key));
                if (newKey) {
                    setLastDrawnCardKey(newKey);
                    setTimeout(() => setLastDrawnCardKey(null), 1000);
                }
            }
            prevHandRef.current = handKeys;

            setGame(prev => toGameState(data, myId, prev));

            if (data.isFinished) {
                setShowRoundResult(false);
                // 전적 DB 반영은 서버가 게임 종료 시 1회 수행 (클라이언트 호출 없음 — 중복 집계 방지)
                // 게임 결과 통계 — localStorage (게임 전체 승자 = 총점 최저 기준). 종료 후 RoomUpdated가 반복돼도 1회만
                if (!historyPushedRef.current) {
                    historyPushedRef.current = true;
                    pushHistory({
                        mode: "Cards",
                        winnerName: data.gameWinnerName || data.winnerName || "승자 미확정",
                        detail: data.lastWinType || "",
                        isMyWin: (data.gameWinnerIds ?? []).includes(myId ?? "")
                    });
                }
            } else if (data.isRoundEnded) {
                setShowRoundResult(true);
            }
        };

        const onConnectedUser = (data: ConnectedUserPayload) => {
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
            connection.on("ConnectedUser", onConnectedUser);
            connection.on("GameStarted", (data: CardRoomView) => {
                historyPushedRef.current = false;
                onUpdate(data);
            });
            // 결과 내용은 RoomUpdated의 방 상태로 그린다 — 이 이벤트는 결과창을 여는 신호
            connection.on("ShowResultBoard", () => {
                setShowRoundResult(true);
                playSound("win");
            });
            connection.on("HideResultBoard", () => setShowRoundResult(false));
            connection.on("GameTerminated", () => setGame(prev => prev && { ...prev, isFinished: true }));
            connection.on("ExitToRoom", (targetRoomId: string) => {
                offGameEvents();
                isSubscribed.current = false;
                navigate(`/room/${targetRoomId || roomId}`, { replace: true });
            });
            connection.on("ErrorMessage", (msg: string) => {
                setErrorMsg(msg);
                setTimeout(() => setErrorMsg(null), 3000);
            });
            isSubscribed.current = true;
        }

        // 새로고침 복구: 연결 보장 → 방 재입장(그룹 재가입) → 현재 상태 로드
        const loadRoom = async () => {
            try {
                const ok = await ensureConnection();
                if (!ok) throw new Error("서버에 연결할 수 없습니다.");
                const savedPwd = sessionStorage.getItem(`room_pwd_${roomId}`) || "";
                await connection.invoke("JoinRoom", roomId, savedPwd === "" ? null : savedPwd);
                const data = await connection.invoke<CardRoomView | null>("GetRoom", roomId);
                if (data) {
                    setGame(prev => toGameState(data, myId, prev));
                    const me = data.players.find(p => p.playerId === myId);
                    prevHandRef.current = (me?.hand ?? []).map(getCardKey);
                }
            } catch (err) {
                console.error("방 상태 로드 실패:", err);
                setErrorMsg("방 정보를 불러오지 못했습니다. 로비로 돌아갑니다.");
                setTimeout(() => navigate('/lobby', { replace: true }), 1500);
            }
        };
        loadRoom();

        // 프로필 정보 로드
        connection.invoke<MyProfile | null>("GetMyProfile").then(data => {
            if (data) setMyProfile(data);
        }).catch(err => console.error("프로필 로드 실패:", err));

        return () => {
            connection.off("ConnectedUser", onConnectedUser);
            offGameEvents();
            isSubscribed.current = false;
        };
    }, [roomId, myId, navigate]);

    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 전체 게임이 종료되며 대기실로 이동합니다.")) {
            playSound("resign");
            connection.invoke("GiveUp", roomId).catch(err => console.error("기권 처리 중 오류:", err));
        }
    };

    const handleReturnToRoom = () => {
        leaveGameEvents();
        // 기권 종료도 일반 종료와 같이 대기실로 복귀 — 전원이 로비로 나가면 빈 방이 삭제되기 때문
        navigate(`/room/${roomId}`, { replace: true });
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

    // 턴 타임아웃 — 현재 턴 플레이어에 대해 카운트다운 (라운드 진행 중일 때만)
    const turnSecLeft = useTurnTimeout(
        roomId,
        game?.currentTurnPlayerId ?? null,
        !!game && game.isStarted && !game.isFinished && !game.isRoundEnded,
        game?.turnTimeoutSec ?? 60
    );

    // 본인 차례 시작 알림 사운드 (false → true 전환)
    // 아래 로딩 중 early return보다 앞에 있어야 함 — 훅은 매 렌더 같은 순서로 호출돼야 함
    const currentTurnId = game?.currentTurnPlayerId ?? null;
    const isMyTurn = !!game && currentTurnId === myId;
    const prevIsMyTurnRef = useRef(false);
    useEffect(() => {
        if (isMyTurn && !prevIsMyTurnRef.current) {
            playSound("my-turn");
        }
        prevIsMyTurnRef.current = isMyTurn;
    }, [isMyTurn]);

    // 카드 버림·뽑기·뻥 효과음 — 내 클릭이 아니라 서버 상태 변화로 판단해 모든 플레이어의 동작에 재생
    // 뻥: 서버가 뻥 턴(isPungTurn)으로 바꾼 순간 — 이때 바닥에 깔린 2장은 버림 소리 대신 뻥 소리
    // 버림: 버려진 카드 더미가 늘어남. 뽑기: 누군가의 손패가 정확히 1장 늘어남 (2→3, 5→6)
    // 라운드가 바뀌면 손패가 5장으로 다시 채워지고 더미가 비므로 버림·뽑기 둘 다 해당하지 않는다
    const prevCardCountsRef = useRef<{ pile: number; hands: Record<string, number>; pungTurnOf: string | null } | null>(null);
    useEffect(() => {
        if (!game) return;
        const pile = (game.discardPile ?? []).length;
        const hands: Record<string, number> = {};
        for (const p of game.players ?? []) hands[p.playerId] = p.handCount ?? 0;
        const pungTurnOf = game.isPungTurn ? game.currentTurnPlayerId : null;
        const prev = prevCardCountsRef.current;
        prevCardCountsRef.current = { pile, hands, pungTurnOf };
        if (!prev) return;
        if (pungTurnOf && pungTurnOf !== prev.pungTurnOf) playSound("pung", 0.9);
        else if (pile > prev.pile) playSound("play-card");
        if (Object.keys(hands).some(id => prev.hands[id] !== undefined && hands[id] === prev.hands[id] + 1)) playSound("draw-card");
    }, [game]);

    if (!game || !game.players) {
        return (
            <div className="game-container loading-state">
                <h2>데이터 동기화 중...</h2>
                <button className="loading-back-btn" onClick={() => navigate('/lobby')}>로비로 돌아가기</button>
            </div>
        );
    }

    const players = game.players;
    const me = players.find(p => p.playerId === myId);
    // 내 다음 턴 순서부터 나열 — 좌석을 시계 방향 턴 순서대로 앉히기 위해 (턴 순서 = players 배열 순서)
    const myIndex = me ? players.indexOf(me) : -1; // -1이면 players에 내가 없는 경우(관전 등)
    const others = myIndex >= 0
        ? [...players.slice(myIndex + 1), ...players.slice(0, myIndex)]
        : players.filter(p => p.playerId !== myId);
    const isHost = game.hostPlayerId === myId;
    const hwatu = game.cardMode === 1;
    const deckCount = game.deckCount ?? 0;
    const lastDiscarded = game.lastDiscardedCard;

    const myHand = me?.hand ?? [];
    // 서버가 본인 뷰에만 실어 주는 족보 힌트 (6장: 지금 승리 가능 / 5장: 어떤 카드가 오면 승리)
    const winHint = me?.winHint ?? null;
    const rankGroups = buildRankGroups(myHand);

    const canDraw = isMyTurn && (myHand.length === 2 || myHand.length === 5);
    const canDiscardOrWin = isMyTurn && (myHand.length === 3 || myHand.length === 6);
    // 첫 바퀴(모두 한 번씩 턴, 뻥으로 건너뛴 사람 포함)가 끝나야 승리 선언 가능 — 서버 CanDeclareWin과 동일
    const isFirstLapDone = !!game.isFirstLapDone;
    const canDeclareWin = isMyTurn && isFirstLapDone;
    // 뻥: 뻥 대기 시간(상대가 버린 뒤 2초) 동안만, 내가 버린 카드는 불가. 뽑기는 막지 않는다
    // (그 사이 뻥이 성립하면 서버가 뽑은 카드를 덱으로 되돌림)
    const canPung = !!game.isPungWindowOpen && !!lastDiscarded && game.lastActorPlayerId !== myId && canPungWith(myHand, lastDiscarded);
    // STOP: 내 턴에 뽑아서 3장 + 패가 2장인 사람이 본인 포함 2명 이상. 뻥으로 가져온 턴에는 불가 — 서버 DeclareStop과 동일
    const canStop = isMyTurn && myHand.length === 3 && !game.isPungTurn
        && players.filter(p => p.playerId === myId || p.handCount === 2).length >= 2;

    const handleStop = () => {
        if (!canStop) return;
        if (window.confirm("STOP을 선언하시겠습니까? 이번에 카드를 버리면 게임이 종료됩니다.")) {
            connection.invoke("DeclareStop", roomId)
                .catch(err => console.error("STOP 호출 실패:", err));
        }
    };

    return (
        <div className={`game-container ${isMyTurn ? "my-turn-flash" : ""}`}>
            {turnSecLeft != null && (
                // 헤더(50px) 아래 — 기권 버튼을 가리지 않도록
                <div className={`turn-timer ${turnSecLeft <= 10 ? "urgent" : ""}`}>⏱ {turnSecLeft}s</div>
            )}

            {errorMsg && <div className="game-toast">⚠️ {errorMsg}</div>}

            <button className="rule-btn-fixed" onClick={openRules}>📜 게임 족보</button>

            <div className="game-header">
                <div className="header-left">
                    <span className="set-info">ROUND {game.currentRound}</span>
                    <span className="room-info">ROOM: {roomId}</span>
                </div>
                <div className="player-scores" onClick={() => setShowScoreHistory(true)} title="라운드별 점수 히스토리 보기">
                    {players.map(p => (
                        <span key={p.playerId} className={`score-item ${p.playerId === myId ? "my-score" : ""} ${currentTurnId === p.playerId ? "turn-highlight" : ""}`}>
                            {p.name}: {p.totalScore ?? 0}점
                        </span>
                    ))}
                </div>
                <button className="exit-btn" onClick={handleExit}>기권</button>
            </div>

            <div className="game-table-area">
                <div className="table-oval">
                    <div className="table-center-stack">
                        {/* 덱과 바닥 카드 */}
                        <div className="table-cards-row">
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} onClick={() => { if (canDraw) connection.invoke("DrawCard", roomId); }}>
                                <span className="label">DECK</span>
                                <div className="count">{deckCount}</div>
                                {isHost && deckCount === 0 && <button className="reshuffle-badge" onClick={(e) => { e.stopPropagation(); handleReshuffle(); }}>🔄 셔플</button>}
                            </div>
                            <CardFace card={lastDiscarded} variant="floor" hwatu={hwatu} onClick={() => setShowDiscardModal(true)} />
                        </div>

                        {/* 뻥 · STOP · 자연바가지 */}
                        <div className="table-actions">
                            <button
                                className={`interrupt-btn ${canPung ? 'pung-active' : ''}`}
                                onClick={() => { if (canPung) connection.invoke("InterruptDiscard", roomId); }}
                                disabled={!canPung}
                            >
                                {canPung ? "🔥 뻥!!" : "뻥"}
                            </button>
                            <button className={`interrupt-btn stop-btn ${canStop ? 'stop-active' : ''}`} onClick={handleStop} disabled={!canStop}>
                                {canStop ? "🛑 STOP" : "STOP"}
                            </button>
                            <button className="win-btn highlight" onClick={() => connection.invoke("ToggleNaturalBagaji", roomId)}>
                                자연바가지 {game.isNaturalBagajiEnabled ? "ON" : "OFF"}
                            </button>
                        </div>
                    </div>

                    {others.map((player, idx) => {
                        const seat = seatPosition(idx + 1, others.length + 1);
                        return (
                            <div
                                key={player.playerId}
                                className={`player-box seat ${currentTurnId === player.playerId ? 'active-turn' : ''} ${player.isBagaji ? 'bagaji' : ''}`}
                                style={{ top: seat.top, left: seat.left }}
                            >
                                <div className="player-name">{player.name}</div>
                                <div className="opponent-card-back">{player.handCount ?? 0}</div>
                                {/* 🔴 바가지 표시 — 박스 위쪽 배지 (박스 자체도 .bagaji로 빨갛게 깜빡임). 상대 손패는 마스킹되어 서버 계산값 사용 */}
                                {player.isBagaji && <div className="bagaji-label">🔥 바가지</div>}
                            </div>
                        );
                    })}
                </div>
            </div>

            <div className="hand-area">
                <div className="turn-status-container">
                    <span className={`status-text ${isMyTurn || canPung ? "active-text" : ""} ${canPung ? "pung-ready" : ""}`}>
                        {isMyTurn ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") : (canPung ? "🔥 지금 바로 '뻥'이 가능합니다!" : "상대방의 턴입니다...")}
                    </span>
                    {canDeclareWin && (
                        <button className="win-btn highlight" onClick={() => connection.invoke("DeclareWin", roomId)}>🏆 승리 선언</button>
                    )}
                </div>

                {winHint && (
                    <div className={`win-hint ${winHint.isWinning ? "winning" : ""}`}>
                        {winHint.isWinning
                            ? `🏆 지금 승리 선언 가능 — ${winHint.winType} (${winHint.score}점)${isFirstLapDone ? "" : " · 첫 바퀴가 끝나야 선언 가능"}`
                            : `💡 ${formatOuts(winHint.outs ?? [], hwatu)}`}
                    </div>
                )}

                <div className="cards-in-hand">
                    {sortCards(myHand).map(card => {
                        const cardKey = getCardKey(card);
                        const rankKey = card.rank.toUpperCase();
                        const groupClass = isJokerRank(rankKey) ? "rank-group-joker" : (rankGroups[rankKey] ?? "");
                        return (
                            <CardFace
                                key={cardKey}
                                card={card}
                                variant="hand"
                                hwatu={hwatu}
                                className={`${cardKey === lastDrawnCardKey ? "new-card-highlight" : ""} ${groupClass} ${canDiscardOrWin ? "clickable" : ""}`}
                                onClick={() => { if (canDiscardOrWin) connection.invoke("PlayCard", roomId, card); }}
                            />
                        );
                    })}
                </div>
            </div>

            {/* 라운드 결과창: 게임이 완전히 끝나지 않았을 때 */}
            {showRoundResult && !game.isFinished && (
                <RoundResultModal game={game} hwatu={hwatu} isHost={isHost} onNextRound={handleNextRoundRequest} />
            )}

            {/* 최종 결과창: 게임 종료 (1라운드 게임이면 바로 이 창) */}
            {game.isFinished && (
                <FinalResultModal game={game} hwatu={hwatu} onConfirm={handleReturnToRoom} onShowStats={() => setShowStats(true)} />
            )}

            {showStats && <StatsModal myName={myProfile.name} onClose={() => setShowStats(false)} />}

            {showScoreHistory && (
                <ScoreHistoryModal history={game.roundScoreHistory ?? []} players={players} onClose={() => setShowScoreHistory(false)} />
            )}

            {showDiscardModal && (
                <DiscardPileModal cards={game.discardPile ?? []} hwatu={hwatu} onClose={() => setShowDiscardModal(false)} />
            )}

            <GameChat roomId={roomId} myName={myProfile.name} />
        </div>
    );
}
