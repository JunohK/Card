import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { connection, ensureConnection } from '../signalr/connection';
import { useAuth } from '../auth/authContext';
import './RoomPage.css';

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
    maxRounds: number; // 서버에서 받아올 라운드 설정값
    IsStarted?: boolean; // 서버가 대문자로 줄 경우 대비
}

const RoomPage: React.FC = () => {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const { user: currentUser } = useAuth();
    const [room, setRoom] = useState<GameRoom | null>(null);

    useEffect(() => {
        const initRoom = async () => {
            const isConnected = await ensureConnection();
            if (isConnected && roomId) {
                // 방 입장 시도
                await connection.invoke("JoinRoom", roomId, null);
            }
        };

        initRoom();

        // 1. 방 정보 갱신 처리
        const onRoomUpdated = (updatedRoom: GameRoom) => {
            console.log("서버로부터 방 정보 수신:", updatedRoom);
            setRoom(updatedRoom);
            
            // 기권 후 돌아왔을 때 isStarted가 false여야 대기실에 머무름
            if (updatedRoom.isStarted) {
                navigate(`/game/${roomId}`);
            }
        };

        // 2. 입장 성공 이벤트 처리 (Warning 해결)
        const onJoinSuccess = (id: string) => {
            console.log("방 입장 성공 신호 수신:", id);
        };

        // 3. 게임 시작 이벤트 처리
        const onGameStarted = () => {
            navigate(`/game/${roomId}`);
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

    const handleStartGame = async () => {
        if (!roomId || !room) return;
        try {
            // 시작할 때는 현재 서버에 설정된 라운드 값을 사용
            await connection.invoke("StartGame", roomId, room.maxRounds);
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
    const isStarted = room.isStarted ?? (room as any).IsStarted // 대소문자 혼용 방지
    // 🔴 현재 선택된 라운드는 서버 데이터(room.maxRounds)를 기준으로 함
    const currentRounds = room.maxRounds || 1;

    return (
        <div className="room-container">
            <div className="room-card">
                <div className="room-header">
                    <h1 className="room-title">{room.title}</h1>
                    <p className="room-code">ROOM CODE: {room.roomId}</p>
                </div>

                <div className="section-title">목표 라운드 선택</div>
                <div className="round-selector">
                    {[1, 5, 10].map((r) => (
                        <button
                            key={r}
                            disabled={!isHost}
                            // 🔴 클릭 시 로컬 state가 아닌 서버 invoke 호출
                            onClick={() => handleRoundChange(r)}
                            // 🔴 서버가 알려준 currentRounds 값과 같으면 active 클래스 부여
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

                <div className="section-title">참여 플레이어 ({room.players.length}/7)</div>
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
                        <button onClick={handleStartGame} className="start-btn">
                            {currentRounds}라운드 게임 시작
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