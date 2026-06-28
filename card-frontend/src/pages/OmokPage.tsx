import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection, ensureConnection, setActiveRoom } from "../signalr/connection";
import { useTurnTimeout } from "../util/useTurnTimeout";
import { playSound } from "../sound/sound";
import { pushHistory } from "../util/gameHistory";

const BOARD_SIZE = 15;
const CELL = 46;       // 셀 1칸 픽셀
const PADDING = 28;    // 보드 가장자리 여백
const BOARD_PX = (BOARD_SIZE - 1) * CELL + PADDING * 2;

type StoneColor = 0 | 1 | 2; // 0=빈칸, 1=흑, 2=백

interface OmokMove {
    index: number;
    row: number;
    col: number;
    color: StoneColor;
}

interface OmokState {
    board: StoneColor[][];
    blackPlayerId: string;
    whitePlayerId: string;
    currentTurn: StoneColor;
    moves: OmokMove[];
    isFinished: boolean;
    winnerColor: StoneColor;
    winnerPlayerId?: string;
    isDraw: boolean;
    endReason?: string;
    winningLine: number[][];
}

interface OmokGameRoom {
    roomId: string;
    title: string;
    players: { playerId: string; name: string }[];
    hostPlayerId: string;
    isStarted: boolean;
    isFinished: boolean;
    omok?: OmokState;
}

// 서버 응답의 camelCase/PascalCase 어느 쪽이든 안전하게 꺼내기
function pick<T = any>(obj: any, ...keys: string[]): T | undefined {
    if (!obj) return undefined;
    for (const k of keys) {
        if (obj[k] !== undefined) return obj[k];
    }
    return undefined;
}

export default function OmokPage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();

    // 재접속(네트워크 끊김 자동 재연결) 시 자동 재입장할 방 등록
    useEffect(() => {
        setActiveRoom(roomId ?? null);
        return () => setActiveRoom(null);
    }, [roomId]);

    const [room, setRoom] = useState<OmokGameRoom | null>(null);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);
    const [endMsg, setEndMsg] = useState<string | null>(null);
    const [chatInput, setChatInput] = useState("");
    const [chatMessages, setChatMessages] = useState<string[]>([]);
    const [showChat, setShowChat] = useState(true);
    const [forbiddenPoints, setForbiddenPoints] = useState<number[][]>([]);
    const chatEndRef = useRef<HTMLDivElement>(null);
    const prevMovesCountRef = useRef(0);
    const prevIsMyTurnRef = useRef(false);

    useEffect(() => {
        let mounted = true;

        const setup = async () => {
            await ensureConnection();
            // 새로고침 케이스 — 방 정보 다시 받아오기 위해 join 호출은 RoomPage에서 처리됨
        };

        const onRoomUpdated = (updatedRoom: any) => {
            if (!mounted) return;
            console.log("[OmokPage] RoomUpdated raw:", updatedRoom);
            console.log("[OmokPage] myConnectionId:", connection.connectionId);
            // 서버는 PascalCase로 보낼 수도 있어 정규화
            const normalized: OmokGameRoom = {
                roomId: pick(updatedRoom, 'roomId', 'RoomId') ?? '',
                title: pick(updatedRoom, 'title', 'Title') ?? '',
                players: (pick<any[]>(updatedRoom, 'players', 'Players') ?? []).map((p: any) => ({
                    playerId: pick(p, 'playerId', 'PlayerId') ?? '',
                    name: pick(p, 'name', 'Name') ?? ''
                })),
                hostPlayerId: pick(updatedRoom, 'hostPlayerId', 'HostPlayerId') ?? '',
                isStarted: pick(updatedRoom, 'isStarted', 'IsStarted') ?? false,
                isFinished: pick(updatedRoom, 'isFinished', 'IsFinished') ?? false,
                omok: (() => {
                    const o = pick<any>(updatedRoom, 'omok', 'Omok');
                    if (!o) return undefined;
                    return {
                        board: pick(o, 'board', 'Board') ?? [],
                        blackPlayerId: pick(o, 'blackPlayerId', 'BlackPlayerId') ?? '',
                        whitePlayerId: pick(o, 'whitePlayerId', 'WhitePlayerId') ?? '',
                        currentTurn: pick(o, 'currentTurn', 'CurrentTurn') ?? 1,
                        moves: pick(o, 'moves', 'Moves') ?? [],
                        isFinished: pick(o, 'isFinished', 'IsFinished') ?? false,
                        winnerColor: pick(o, 'winnerColor', 'WinnerColor') ?? 0,
                        winnerPlayerId: pick(o, 'winnerPlayerId', 'WinnerPlayerId'),
                        isDraw: pick(o, 'isDraw', 'IsDraw') ?? false,
                        endReason: pick(o, 'endReason', 'EndReason'),
                        winningLine: pick(o, 'winningLine', 'WinningLine') ?? []
                    };
                })()
            };
            setRoom(normalized);

            // 방장이 게임 끝낸 후 isStarted=false 면 대기실로 복귀
            if (!normalized.isStarted && normalized.isFinished) {
                // 게임 종료 후 대기 — 사용자가 직접 로비/방으로
            }
        };

        const onGameStarted = onRoomUpdated;

        const onErrorMessage = (msg: string) => {
            if (!mounted) return;
            setErrorMsg(msg);
            setTimeout(() => mounted && setErrorMsg(null), 3500);
        };

        const onOmokGameEnded = (data: any) => {
            if (!mounted) return;
            const reason = pick<string>(data, 'reason', 'Reason') ?? '게임 종료';
            const isDraw = pick<boolean>(data, 'isDraw', 'IsDraw') ?? false;
            const winnerId = pick<string>(data, 'winnerPlayerId', 'WinnerPlayerId');
            const winnerName = pick<string>(data, 'winnerName', 'WinnerName') ?? '';
            setEndMsg(isDraw ? '🤝 무승부' : `🏆 ${reason}${winnerId ? '' : ''}`);
            setForbiddenPoints([]); // 종료되면 금수 표시 제거

            // 효과음: 무승부면 X, 본인 승리면 win, 그 외 (패배/기권)는 resign
            if (!isDraw) {
                if (winnerId === connection.connectionId) playSound("win");
                else playSound("resign");
            }

            // 게임 결과 통계 — localStorage
            pushHistory({
                mode: "Omok",
                winnerName: isDraw ? "무승부" : (winnerName || "승자 미확정"),
                detail: reason,
                isMyWin: !isDraw && winnerId === connection.connectionId
            });
        };

        const onForbiddenPoints = (data: any) => {
            if (!mounted) return;
            const pts = pick<number[][]>(data, 'points', 'Points') ?? [];
            setForbiddenPoints(pts);
        };

        const onChat = (user: string, message: string) => {
            if (mounted) setChatMessages(prev => [...prev, `${user} : ${message}`]);
        };

        const onGameTerminated = () => {
            if (mounted) navigate(`/room/${roomId}`);
        };

        connection.on("RoomUpdated", onRoomUpdated);
        connection.on("GameStarted", onGameStarted);
        connection.on("ErrorMessage", onErrorMessage);
        connection.on("OmokGameEnded", onOmokGameEnded);
        connection.on("OmokForbiddenPoints", onForbiddenPoints);
        connection.on("ReceiveMessage", onChat);
        connection.on("GameTerminated", onGameTerminated);

        setup();

        return () => {
            mounted = false;
            connection.off("RoomUpdated", onRoomUpdated);
            connection.off("GameStarted", onGameStarted);
            connection.off("ErrorMessage", onErrorMessage);
            connection.off("OmokGameEnded", onOmokGameEnded);
            connection.off("OmokForbiddenPoints", onForbiddenPoints);
            connection.off("ReceiveMessage", onChat);
            connection.off("GameTerminated", onGameTerminated);
        };
    }, [roomId, navigate]);

    useEffect(() => {
        chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [chatMessages]);

    // 보드 변화에 따른 효과음 트리거
    useEffect(() => {
        const omok = room?.omok;
        if (!omok) return;

        // 새 돌이 놓였으면 "딱" 소리 (양쪽 모두에게)
        if (omok.moves.length > prevMovesCountRef.current) {
            playSound("place-stone");
        }
        prevMovesCountRef.current = omok.moves.length;

        // 본인 차례 시작 (false → true)
        const myId = connection.connectionId;
        const myColor = omok.blackPlayerId === myId ? 1 : omok.whitePlayerId === myId ? 2 : 0;
        const isMyTurnNow = !omok.isFinished && myColor !== 0 && omok.currentTurn === myColor;
        if (isMyTurnNow && !prevIsMyTurnRef.current) {
            playSound("my-turn");
        }
        prevIsMyTurnRef.current = isMyTurnNow;
    }, [room]);

    // 턴 타임아웃 — 현재 차례 색의 플레이어에 대해 카운트다운 (진행 중일 때만)
    const turnSecLeft = useTurnTimeout(
        roomId,
        room?.omok ? (room.omok.currentTurn === 1 ? room.omok.blackPlayerId : room.omok.whitePlayerId) : null,
        !!room?.omok && !room.omok.isFinished && !!room.isStarted,
        (room as any)?.turnTimeoutSec ?? 60
    );

    if (!room) {
        return (
            <div style={{ minHeight: '100vh', background: '#0f172a', color: '#f1f5f9', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <div>방 정보를 불러오는 중... (room: {roomId})</div>
            </div>
        );
    }

    const omok = room.omok;
    const myId = connection.connectionId;
    const myColor: StoneColor =
        omok?.blackPlayerId === myId ? 1 :
        omok?.whitePlayerId === myId ? 2 : 0;
    const isMyTurn = !!omok && !omok.isFinished && omok.currentTurn === myColor;
    const board = omok?.board ?? [];

    const blackPlayerName = room.players.find(p => p.playerId === omok?.blackPlayerId)?.name ?? '?';
    const whitePlayerName = room.players.find(p => p.playerId === omok?.whitePlayerId)?.name ?? '?';

    const placeStone = (row: number, col: number) => {
        if (!omok || omok.isFinished) return;
        if (!isMyTurn) {
            setErrorMsg('상대 차례입니다.');
            setTimeout(() => setErrorMsg(null), 2000);
            return;
        }
        if (board[row]?.[col] !== 0) return;
        // 흑 차례 + 금수 자리면 클라이언트에서 차단
        if (myColor === 1 && forbiddenPoints.some(p => p[0] === row && p[1] === col)) {
            setErrorMsg('33 금수 — 둘 수 없는 자리입니다.');
            setTimeout(() => setErrorMsg(null), 2000);
            return;
        }
        connection.invoke("PlaceStone", roomId, row, col).catch(err => {
            console.error("PlaceStone error:", err);
        });
    };

    const handleResign = () => {
        if (!omok || omok.isFinished) return;
        if (!window.confirm("정말 기권하시겠습니까?")) return;
        connection.invoke("ResignOmok", roomId).catch(err => console.error("ResignOmok error:", err));
    };

    const handleLeave = () => {
        navigate(`/room/${roomId}`);
    };

    const sendChat = async () => {
        if (!chatInput.trim()) return;
        await connection.invoke("SendRoomChat", roomId, chatInput);
        setChatInput("");
    };

    // 가장 최근에 놓인 돌 (lastMove 강조용)
    const lastMove = omok?.moves?.[omok.moves.length - 1];

    // 승리 라인 좌표 Set (강조용)
    const winningLineSet = new Set((omok?.winningLine ?? []).map(p => `${p[0]},${p[1]}`));
    // 금수 자리 — 흑 차례인 본인에게만 시각화 (myColor === 1 + isMyTurn 보장)
    const showForbidden = myColor === 1 && isMyTurn;
    const forbiddenSet = showForbidden
        ? new Set(forbiddenPoints.map(p => `${p[0]},${p[1]}`))
        : new Set<string>();

    return (
        <div style={{ minHeight: '100vh', width: '100%', background: '#0f172a', color: '#f1f5f9', padding: '1rem', position: 'relative', boxSizing: 'border-box' }}>
            {turnSecLeft != null && (
                <div style={{ position: 'fixed', top: 8, right: 8, zIndex: 9999, background: turnSecLeft <= 10 ? '#dc2626' : 'rgba(30,41,59,0.9)', color: '#fff', padding: '4px 10px', borderRadius: 8, fontWeight: 'bold', fontSize: 13 }}>
                    ⏱ {turnSecLeft}s
                </div>
            )}
            {/* 헤더 */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
                <div>
                    <span style={{ color: '#f1c40f', fontWeight: 'bold' }}>⚫ 오목</span>
                    <span style={{ marginLeft: '1rem', color: '#94a3b8' }}>ROOM: {roomId}</span>
                </div>
                <div style={{ display: 'flex', gap: '2rem', alignItems: 'center' }}>
                    <span style={{ color: omok?.currentTurn === 1 ? '#f1c40f' : '#94a3b8', fontWeight: omok?.currentTurn === 1 ? 'bold' : 'normal' }}>
                        ⚫ {blackPlayerName} {omok?.currentTurn === 1 && !omok?.isFinished && '◀'}
                    </span>
                    <span style={{ color: omok?.currentTurn === 2 ? '#f1c40f' : '#94a3b8', fontWeight: omok?.currentTurn === 2 ? 'bold' : 'normal' }}>
                        ⚪ {whitePlayerName} {omok?.currentTurn === 2 && !omok?.isFinished && '◀'}
                    </span>
                </div>
                <button
                    onClick={omok?.isFinished ? handleLeave : handleResign}
                    style={{
                        padding: '0.5rem 1rem',
                        background: omok?.isFinished ? '#22c55e' : 'transparent',
                        border: `1px solid ${omok?.isFinished ? '#22c55e' : '#ef4444'}`,
                        color: omok?.isFinished ? 'white' : '#ef4444',
                        borderRadius: '0.5rem',
                        cursor: 'pointer',
                        fontWeight: 'bold'
                    }}
                >
                    {omok?.isFinished ? '나가기' : '기권'}
                </button>
            </div>

            {/* 에러/종료 메시지 */}
            {errorMsg && (
                <div style={{ position: 'fixed', top: '4rem', left: '50%', transform: 'translateX(-50%)', background: 'rgba(231, 76, 60, 0.95)', color: 'white', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', zIndex: 1000 }}>
                    ⚠️ {errorMsg}
                </div>
            )}

            {/* 보드는 정중앙, 채팅은 보드 우측에 absolute로 붙임 */}
            <div>
              <div style={{ position: 'relative', width: BOARD_PX, margin: '0 auto' }}>
                {/* 보드 */}
                <div style={{
                    position: 'relative',
                    width: BOARD_PX,
                    height: BOARD_PX,
                    background: '#d6a857',
                    borderRadius: '0.5rem',
                    boxShadow: '0 10px 30px rgba(0,0,0,0.6)'
                }}>
                    {/* 격자 그리기 (SVG) */}
                    <svg width={BOARD_PX} height={BOARD_PX} style={{ position: 'absolute', top: 0, left: 0, pointerEvents: 'none' }}>
                        {Array.from({ length: BOARD_SIZE }).map((_, i) => (
                            <g key={i}>
                                <line x1={PADDING + i * CELL} y1={PADDING} x2={PADDING + i * CELL} y2={PADDING + (BOARD_SIZE - 1) * CELL} stroke="#2c1810" strokeWidth="1" />
                                <line x1={PADDING} y1={PADDING + i * CELL} x2={PADDING + (BOARD_SIZE - 1) * CELL} y2={PADDING + i * CELL} stroke="#2c1810" strokeWidth="1" />
                            </g>
                        ))}
                        {/* 화점 (15x15 기본: 중앙 + 4모서리 표시점) */}
                        {[[3, 3], [3, 11], [11, 3], [11, 11], [7, 7]].map(([r, c]) => (
                            <circle key={`star-${r}-${c}`} cx={PADDING + c * CELL} cy={PADDING + r * CELL} r="3" fill="#2c1810" />
                        ))}
                    </svg>

                    {/* 클릭 영역 + 돌 */}
                    {Array.from({ length: BOARD_SIZE }).map((_, r) =>
                        Array.from({ length: BOARD_SIZE }).map((_, c) => {
                            const stone = board[r]?.[c] ?? 0;
                            const isLast = lastMove && lastMove.row === r && lastMove.col === c;
                            const isWinCell = winningLineSet.has(`${r},${c}`);
                            const isForbidden = stone === 0 && forbiddenSet.has(`${r},${c}`);
                            const moveNumber = omok?.moves?.find(m => m.row === r && m.col === c)?.index;

                            return (
                                <div
                                    key={`${r}-${c}`}
                                    onClick={() => placeStone(r, c)}
                                    style={{
                                        position: 'absolute',
                                        width: CELL,
                                        height: CELL,
                                        left: PADDING + c * CELL - CELL / 2,
                                        top: PADDING + r * CELL - CELL / 2,
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        cursor: stone === 0 && isMyTurn && !isForbidden ? 'pointer' : (isForbidden ? 'not-allowed' : 'default')
                                    }}
                                    title={isForbidden ? '33 금수' : undefined}
                                >
                                    {stone !== 0 && (
                                        <div
                                            style={{
                                                width: CELL - 6,
                                                height: CELL - 6,
                                                borderRadius: '50%',
                                                background: stone === 1
                                                    ? 'radial-gradient(circle at 30% 30%, #555, #000)'
                                                    : 'radial-gradient(circle at 30% 30%, #fff, #b8b8b8)',
                                                boxShadow: isLast
                                                    ? '0 0 0 2px #ef4444, 0 2px 4px rgba(0,0,0,0.5)'
                                                    : isWinCell
                                                        ? '0 0 0 2px #f1c40f, 0 0 12px rgba(241, 196, 15, 0.8)'
                                                        : '0 2px 4px rgba(0,0,0,0.5)',
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                color: stone === 1 ? '#fff' : '#000',
                                                fontSize: '0.7rem',
                                                fontWeight: 'bold'
                                            }}
                                        >
                                            {moveNumber}
                                        </div>
                                    )}
                                    {isForbidden && (
                                        <div style={{
                                            width: CELL - 10,
                                            height: CELL - 10,
                                            display: 'flex',
                                            alignItems: 'center',
                                            justifyContent: 'center',
                                            color: '#ef4444',
                                            fontWeight: 'bold',
                                            fontSize: '1.4rem',
                                            textShadow: '0 0 4px rgba(0,0,0,0.4)',
                                            pointerEvents: 'none'
                                        }}>
                                            ✕
                                        </div>
                                    )}
                                </div>
                            );
                        })
                    )}
                </div>

                {/* 채팅: 보드 우측에 absolute로 붙음 (보드 정중앙은 그대로 유지) */}
                <div style={{
                    position: 'absolute',
                    left: 'calc(100% + 1.5rem)',
                    top: 0,
                    width: '280px',
                    display: 'flex',
                    flexDirection: 'column',
                    background: '#1e293b',
                    border: '1px solid #334155',
                    borderRadius: '0.5rem',
                    height: BOARD_PX
                }}>
                    <div
                        style={{ padding: '0.75rem', borderBottom: '1px solid #334155', cursor: 'pointer', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
                        onClick={() => setShowChat(s => !s)}
                    >
                        <span style={{ fontWeight: 'bold' }}>💬 LIVE CHAT</span>
                        <span>{showChat ? '▼' : '▲'}</span>
                    </div>
                    {showChat && (
                        <>
                            <div style={{ flex: 1, overflowY: 'auto', padding: '0.5rem', display: 'flex', flexDirection: 'column', gap: '0.25rem' }}>
                                {chatMessages.length === 0 ? (
                                    <div style={{ color: '#64748b', textAlign: 'center', padding: '1rem', fontSize: '0.85rem' }}>아직 메시지가 없습니다.</div>
                                ) : (
                                    chatMessages.map((m, i) => (
                                        <div key={i} style={{ fontSize: '0.85rem', background: '#0f172a', padding: '0.4rem 0.6rem', borderRadius: '0.4rem' }}>{m}</div>
                                    ))
                                )}
                                <div ref={chatEndRef} />
                            </div>
                            <div style={{ display: 'flex', gap: '0.25rem', padding: '0.5rem', borderTop: '1px solid #334155' }}>
                                <input
                                    value={chatInput}
                                    onChange={e => setChatInput(e.target.value)}
                                    onKeyDown={e => e.key === 'Enter' && sendChat()}
                                    placeholder="메시지..."
                                    style={{ flex: 1, padding: '0.4rem', background: '#0f172a', border: '1px solid #334155', borderRadius: '0.4rem', color: '#f1f5f9', fontSize: '0.85rem' }}
                                />
                                <button onClick={sendChat} style={{ padding: '0.4rem 0.6rem', background: '#2563eb', border: 'none', borderRadius: '0.4rem', color: 'white', cursor: 'pointer', fontSize: '0.85rem' }}>
                                    전송
                                </button>
                            </div>
                        </>
                    )}
                </div>
              </div>
            </div>

            {/* 하단 상태 표시 */}
            <div style={{ textAlign: 'center', marginTop: '1rem', fontSize: '1.1rem', fontWeight: 'bold' }}>
                {omok?.isFinished ? (
                    <span style={{ color: '#f1c40f' }}>
                        {omok.isDraw ? '🤝 무승부' : `🏆 ${omok.endReason}`}
                    </span>
                ) : isMyTurn ? (
                    <span style={{ color: '#22c55e' }}>▶ 당신의 차례입니다 ({myColor === 1 ? '⚫ 흑' : '⚪ 백'})</span>
                ) : myColor === 0 ? (
                    <span style={{ color: '#94a3b8' }}>관전 중</span>
                ) : (
                    <span style={{ color: '#94a3b8' }}>상대 차례 ({omok?.currentTurn === 1 ? '⚫ 흑' : '⚪ 백'})</span>
                )}
                {omok && (
                    <span style={{ marginLeft: '1rem', color: '#94a3b8', fontSize: '0.9rem' }}>
                        ({omok.moves.length}수)
                    </span>
                )}
            </div>

            {/* 종료 모달 */}
            {omok?.isFinished && endMsg && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                    <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #f1c40f', textAlign: 'center', maxWidth: '400px' }}>
                        <h2 style={{ color: '#f1c40f', marginTop: 0 }}>{omok.isDraw ? '무승부' : '게임 종료'}</h2>
                        <p style={{ fontSize: '1.2rem', margin: '1.5rem 0' }}>{omok.endReason}</p>
                        {!omok.isDraw && omok.winnerPlayerId && (
                            <p style={{ color: '#bdc3c7' }}>
                                승자: <b style={{ color: '#fff' }}>{room.players.find(p => p.playerId === omok.winnerPlayerId)?.name}</b>
                            </p>
                        )}
                        <div style={{ display: 'flex', gap: '0.5rem', justifyContent: 'center', marginTop: '1rem' }}>
                            <button onClick={handleLeave} style={{ padding: '0.75rem 2rem', background: '#22c55e', border: 'none', borderRadius: '0.5rem', color: 'white', fontWeight: 'bold', cursor: 'pointer', fontSize: '1rem' }}>
                                대기실로
                            </button>
                            <button onClick={() => navigate('/lobby?showStats=1')} style={{ padding: '0.75rem 1.5rem', background: '#7c3aed', border: 'none', borderRadius: '0.5rem', color: 'white', fontWeight: 'bold', cursor: 'pointer', fontSize: '1rem' }}>
                                📊 통계
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
