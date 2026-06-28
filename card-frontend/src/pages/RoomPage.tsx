import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { connection, ensureConnection, setActiveRoom } from '../signalr/connection';
import { useAuth } from '../auth/authContext';
import '../css/RoomPage.css';

interface Player {
    playerId: string;
    name: string;
}

interface GameRoom {
    roomId: string;
    title: string;
    players: Player[];
    hostPlayerId: string;
    isStarted: boolean;
    maxRounds: number;
    jokerCount?: number;
    cardMode?: number;
    gameMode?: number;
    bangUseDodgeCity?: boolean;
    IsStarted?: boolean;
}

const RoomPage: React.FC = () => {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    useAuth();

    // 재접속(네트워크 끊김 자동 재연결) 시 자동 재입장할 방 등록
    useEffect(() => {
        setActiveRoom(roomId ?? null);
        return () => setActiveRoom(null);
    }, [roomId]);
    const [room, setRoom] = useState<GameRoom | null>(null);

    useEffect(() => {
        const initRoom = async () => {
            const isConnected = await ensureConnection();
            if (isConnected && roomId) {
                // 방 입장 시도 — 새로고침 시 sessionStorage에 저장된 비밀번호 사용
                const savedPwd = sessionStorage.getItem(`room_pwd_${roomId}`) || "";
                try {
                    await connection.invoke("JoinRoom", roomId, savedPwd === "" ? null : savedPwd);
                } catch (err: any) {
                    // 비밀번호 만료 등으로 재입장 실패 시 로비로 복귀
                    alert(err?.message ?? "방 재입장 실패");
                    sessionStorage.removeItem(`room_pwd_${roomId}`);
                    navigate('/lobby');
                }
            }
        };

        initRoom();

        // 1. 방 정보 갱신 처리
        const onRoomUpdated = (updatedRoom: GameRoom) => {
            console.log("서버로부터 방 정보 수신:", updatedRoom);
            setRoom(updatedRoom);

            // 기권 후 돌아왔을 때 isStarted가 false여야 대기실에 머무름
            if (updatedRoom.isStarted) {
                const mode = updatedRoom.gameMode ?? (updatedRoom as any).GameMode ?? 0;
                const path = mode === 1 ? `/omok/${roomId}` : mode === 2 ? `/bang/${roomId}` : `/game/${roomId}`;
                navigate(path);
            }
        };

        // 2. 입장 성공 이벤트 처리 (Warning 해결)
        const onJoinSuccess = (id: string) => {
            console.log("방 입장 성공 신호 수신:", id);
        };

        // 3. 게임 시작 이벤트 처리
        const onGameStarted = (updatedRoom: any) => {
            const mode = updatedRoom?.gameMode ?? updatedRoom?.GameMode ?? 0;
            const path = mode === 1 ? `/omok/${roomId}` : mode === 2 ? `/bang/${roomId}` : `/game/${roomId}`;
            navigate(path);
        };

        // 4. 경고 방지용 빈 핸들러 (roomlist 등)
        const onRoomList = () => {};

        // 리스너 등록
        connection.on("RoomUpdated", onRoomUpdated);
        connection.on("JoinRoomSuccess", onJoinSuccess);
        connection.on("GameStarted", onGameStarted);
        connection.on("RoomList", onRoomList); 

        return () => {
            // 리스너 해제
            connection.off("RoomUpdated");
            connection.off("JoinRoomSuccess");
            connection.off("GameStarted");
            connection.off("RoomList");
        };
    }, [roomId, navigate]);

    // 방장이 버튼을 누를 때 호출되는 함수
    const handleRoundChange = async (rounds: number) => {
        if (!roomId || room?.hostPlayerId !== connection.connectionId) return;

        try {
            // 🔴 서버에 설정 변경 요청
            await connection.invoke("UpdateRoomSettings", roomId, rounds);
        } catch (err) {
            console.error("라운드 설정 변경 실패:", err);
        }
    };

    const handleJokerChange = async (count: number) => {
        if (!roomId || room?.hostPlayerId !== connection.connectionId) return;
        try {
            await connection.invoke("UpdateJokerCount", roomId, count);
        } catch (err) {
            console.error("조커 갯수 변경 실패:", err);
        }
    };

    const handleBangExpansionToggle = async () => {
        if (!roomId || room?.hostPlayerId !== connection.connectionId) return;
        const next = !(room.bangUseDodgeCity ?? (room as any).BangUseDodgeCity);
        try {
            await connection.invoke("UpdateBangExpansion", roomId, next);
        } catch (err) {
            console.error("확장팩 토글 실패:", err);
        }
    };

    const handleCardModeChange = async (mode: number) => {
        if (!roomId || room?.hostPlayerId !== connection.connectionId) return;
        try {
            await connection.invoke("UpdateCardMode", roomId, mode);
        } catch (err) {
            console.error("카드 모드 변경 실패:", err);
        }
    };

    const handleStartGame = async () => {
        if (!roomId || !room) return;
        try {
            const mode = room.gameMode ?? (room as any).GameMode ?? 0;
            if (mode === 1) {
                await connection.invoke("StartOmokGame", roomId);
            } else if (mode === 2) {
                await connection.invoke("StartBangGame", roomId);
            } else {
                await connection.invoke("StartGame", roomId, room.maxRounds);
            }
        } catch (err) {
            console.error("StartGame Error:", err);
        }
    };

    const handleLeave = async () => {
        if (!roomId) return;
        try {
            await connection.invoke("LeaveRoom", roomId);
            navigate('/lobby');
        } catch (err) {
            console.error("Leave Error:", err);
            navigate('/lobby');
        }
    };

    if (!room) {
        return (
            <div className="room-container">
                <h2 style={{ color: 'white' }}>방 데이터를 불러오는 중입니다...</h2>
                <p style={{ color: 'white' }}>방 번호: {roomId}</p>
            </div>
        );
    }

    const isHost = room.hostPlayerId === connection.connectionId;
    // 🔴 현재 선택된 라운드는 서버 데이터(room.maxRounds)를 기준으로 함
    const currentRounds = room.maxRounds || 1;
    // 조커 갯수: 서버에서 받은 값(camelCase/PascalCase 모두 대응), 미설정시 기본 1
    const currentJokerCount = room.jokerCount ?? (room as any).JokerCount ?? 1;
    // 카드 모드: 0=Trump, 1=Hwatu (기본 Trump)
    const currentCardMode = room.cardMode ?? (room as any).CardMode ?? 0;
    // 게임 모드: 0=Cards, 1=Omok, 2=Bang
    const currentGameMode = room.gameMode ?? (room as any).GameMode ?? 0;
    const isOmok = currentGameMode === 1;
    const isBang = currentGameMode === 2;

    return (
        <div className="room-container">
            <div className="room-card">
                <div className="room-header">
                    <h1 className="room-title">{room.title}</h1>
                    <p className="room-code">ROOM CODE: {room.roomId}</p>
                </div>

                {isOmok ? (
                    <>
                        <div className="section-title">게임 모드</div>
                        <div style={{ padding: '1rem', background: '#0f172a', border: '1px solid #334155', borderRadius: '0.5rem', marginBottom: '1rem' }}>
                            <div style={{ color: '#38bdf8', fontWeight: 'bold', marginBottom: '0.5rem' }}>⚫ 오목 (15x15, 렌주 룰)</div>
                            <div style={{ color: '#94a3b8', fontSize: '0.85rem', lineHeight: '1.6' }}>
                                · 정확히 <b>2명</b>이 모이면 시작 가능<br />
                                · 흑/백은 <b>랜덤 배정</b>, 흑이 선공<br />
                                · 5목 완성 시 승리, 무승부 자동 처리<br />
                                · 흑 금수: 6목, 44, 33
                            </div>
                        </div>
                    </>
                ) : isBang ? (
                    <>
                        <div className="section-title">게임 모드</div>
                        <div style={{ padding: '1rem', background: '#0f172a', border: '1px solid #334155', borderRadius: '0.5rem', marginBottom: '1rem' }}>
                            <div style={{ color: '#f59e0b', fontWeight: 'bold', marginBottom: '0.5rem' }}>🤠 뱅 (Bang!)</div>
                            <div style={{ color: '#94a3b8', fontSize: '0.85rem', lineHeight: '1.6' }}>
                                · <b>4~7명</b>이 모이면 시작 가능<br />
                                · 역할: 보안관(공개) / 부보안관 / 무법자 / 배신자<br />
                                · 카드 22종, 캐릭터 16종 + 확장팩 8종, 수동 응답, 거리/사거리, 펴기 메커니즘
                            </div>
                        </div>
                        <div className="section-title">확장팩</div>
                        <div style={{ padding: '1rem', background: '#0f172a', border: '1px solid #334155', borderRadius: '0.5rem', marginBottom: '1rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <div>
                                <div style={{ color: '#a78bfa', fontWeight: 'bold' }}>🎴 Dodge City</div>
                                <div style={{ color: '#94a3b8', fontSize: '0.8rem', marginTop: '0.25rem' }}>
                                    캐릭터 8종 + 새 카드 4종 (Punch / Dodge / Whisky / Tequila)
                                </div>
                            </div>
                            <button
                                onClick={handleBangExpansionToggle}
                                disabled={!isHost}
                                style={{
                                    padding: '0.5rem 1rem',
                                    background: (room.bangUseDodgeCity ?? (room as any).BangUseDodgeCity) ? '#22c55e' : '#334155',
                                    color: 'white',
                                    border: 'none',
                                    borderRadius: '0.4rem',
                                    cursor: isHost ? 'pointer' : 'not-allowed',
                                    fontWeight: 'bold',
                                    opacity: isHost ? 1 : 0.6
                                }}
                            >
                                {(room.bangUseDodgeCity ?? (room as any).BangUseDodgeCity) ? 'ON' : 'OFF'}
                            </button>
                        </div>

                        {/* 봇 추가 (방장만, 게임 시작 전) */}
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.75rem', background: '#0f172a', borderRadius: '0.5rem', marginTop: '0.75rem' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                <img
                                    src="https://cdn.jsdelivr.net/gh/game-icons/icons@master/delapouite/robot-helmet.svg"
                                    alt="봇"
                                    style={{ width: '40px', height: '40px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }}
                                    onError={(e) => { e.currentTarget.style.display = 'none'; }}
                                />
                                <div>
                                    <div style={{ fontWeight: 'bold' }}>🤖 봇 추가</div>
                                    <div style={{ color: '#94a3b8', fontSize: '0.8rem', marginTop: '0.25rem' }}>
                                        인원 부족 시 봇으로 채우기 (최대 7명). 단순 휴리스틱 자동 플레이
                                    </div>
                                </div>
                            </div>
                            <button
                                onClick={() => connection.invoke("AddBangBot", roomId)}
                                disabled={!isHost || (room.players?.length ?? 0) >= 7}
                                style={{
                                    padding: '0.5rem 1rem',
                                    background: (isHost && (room.players?.length ?? 0) < 7) ? '#a855f7' : '#334155',
                                    color: 'white',
                                    border: 'none',
                                    borderRadius: '0.4rem',
                                    cursor: (isHost && (room.players?.length ?? 0) < 7) ? 'pointer' : 'not-allowed',
                                    fontWeight: 'bold'
                                }}
                            >
                                + 봇 추가
                            </button>
                        </div>
                    </>
                ) : (
                    <>
                        <div className="section-title">목표 라운드 선택</div>
                        <div className="round-selector">
                            {[1, 5, 10].map((r) => (
                                <button
                                    key={r}
                                    disabled={!isHost}
                                    onClick={() => handleRoundChange(r)}
                                    className={`round-btn ${currentRounds === r ? 'active' : ''}`}
                                    style={{
                                        cursor: isHost ? 'pointer' : 'not-allowed',
                                        opacity: isHost || currentRounds === r ? 1 : 0.6
                                    }}
                                >
                                    {r}
                                </button>
                            ))}
                        </div>

                        <div className="section-title">조커 갯수</div>
                        <div className="round-selector">
                            {[0, 1, 2].map((c) => (
                                <button
                                    key={c}
                                    disabled={!isHost}
                                    onClick={() => handleJokerChange(c)}
                                    className={`round-btn ${currentJokerCount === c ? 'active' : ''}`}
                                    style={{
                                        cursor: isHost ? 'pointer' : 'not-allowed',
                                        opacity: isHost || currentJokerCount === c ? 1 : 0.6
                                    }}
                                >
                                    {c}
                                </button>
                            ))}
                        </div>

                        <div className="section-title">카드 모드</div>
                        <div className="round-selector">
                            {[
                                { value: 0, label: '트럼프 (1~13)' },
                                { value: 1, label: '화투 (1~12)' }
                            ].map((m) => (
                                <button
                                    key={m.value}
                                    disabled={!isHost}
                                    onClick={() => handleCardModeChange(m.value)}
                                    className={`round-btn ${currentCardMode === m.value ? 'active' : ''}`}
                                    style={{
                                        cursor: isHost ? 'pointer' : 'not-allowed',
                                        opacity: isHost || currentCardMode === m.value ? 1 : 0.6,
                                        flex: 1
                                    }}
                                >
                                    {m.label}
                                </button>
                            ))}
                        </div>
                    </>
                )}

                <div className="section-title">참여 플레이어 ({room.players.length}/{isOmok ? 2 : 7})
                    {isBang && room.players.length < 4 && (
                        <span style={{ marginLeft: '0.5rem', color: '#ef4444', fontSize: '0.8rem' }}>(뱅은 4명 이상 필요)</span>
                    )}
                </div>
                <div className="player-list">
                    {room.players.map((player) => {
                        const isMe = player.playerId === connection.connectionId;
                        return (
                            <div 
                                key={player.playerId} 
                                className={`player-item ${isMe ? 'me' : ''}`}
                                // 모든 플레이어 아이템의 기본 글자색을 하얀색(#ffffff)으로 고정
                                style={{ color: '#ffffff' }}
                            >
                                <div className="player-info-content" style={{ color: '#ffffff' }}>
                                    <span className="status-dot"></span>
                                    <b style={{ color: '#ffffff' }}>{player.name}</b> 
                                    {isMe && <span style={{ marginLeft: '4px', color: '#ffffff' }}>(나)</span>}
                                </div>
                                {player.playerId === room.hostPlayerId && <span className="host-icon">👑</span>}
                            </div>
                        );
                    })}
                </div>

                <div className="action-area">
                    {isHost ? (
                        <button
                            onClick={handleStartGame}
                            className="start-btn"
                            disabled={
                                (isOmok && room.players.length !== 2) ||
                                (isBang && (room.players.length < 4 || room.players.length > 7))
                            }
                        >
                            {isOmok
                                ? (room.players.length === 2 ? '오목 게임 시작' : `오목 시작 대기 (${room.players.length}/2)`)
                                : isBang
                                ? (room.players.length >= 4 && room.players.length <= 7
                                    ? `뱅 게임 시작 (${room.players.length}명)`
                                    : `뱅 시작 대기 (${room.players.length}/4~7)`)
                                : `${currentRounds}라운드 게임 시작`}
                        </button>
                    ) : (
                        <div className="waiting-box">
                            방장이 시작하기를 기다리는 중...
                        </div>
                    )}
                    <button onClick={handleLeave} className="leave-btn">
                        나가기
                    </button>
                </div>
            </div>
        </div>
    );
};

export default RoomPage;