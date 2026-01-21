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
                await connection.invoke("JoinRoom", roomId, null);
            }
        };

        initRoom();

        connection.on("RoomUpdated", (updatedRoom: GameRoom) => {
            console.log("서버로부터 방 정보 수신:", updatedRoom);
            setRoom(updatedRoom);
            if (updatedRoom.isStarted) {
                navigate(`/game/${roomId}`);
            }
        });

        connection.on("GameStarted", (gameData: any) => {
            navigate(`/game/${roomId}`);
        });

        return () => {
            connection.off("RoomUpdated");
            connection.off("GameStarted");
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

    if (!room) return <div className="room-container">불러오는 중...</div>;

    const isHost = room.hostPlayerId === connection.connectionId;
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
                    {room.players.map((player) => (
                        <div 
                            key={player.playerId} 
                            className={`player-item ${player.playerId === connection.connectionId ? 'me' : ''}`}
                        >
                            <div className="player-info-content">
                                <span className="status-dot"></span>
                                <b>{player.name}</b> {player.playerId === connection.connectionId && "(나)"}
                            </div>
                            {player.playerId === room.hostPlayerId && <span className="host-icon">👑</span>}
                        </div>
                    ))}
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