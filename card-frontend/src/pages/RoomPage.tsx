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
}

const RoomPage: React.FC = () => {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const { user: currentUser } = useAuth();
    const [room, setRoom] = useState<GameRoom | null>(null);
    const [selectedRounds, setSelectedRounds] = useState<number>(1);

    useEffect(() => {
        const initRoom = async () => {
            const isConnected = await ensureConnection();
            if (isConnected && roomId) {
                await connection.invoke("JoinRoom", roomId, null);
            }
        };

        initRoom();

        connection.on("RoomUpdated", (updatedRoom: GameRoom) => {
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

    const handleStartGame = async () => {
        if (!roomId) return;
        try {
            await connection.invoke("StartGame", roomId, selectedRounds);
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
                            onClick={() => setSelectedRounds(r)}
                            className={`round-btn ${selectedRounds === r ? 'active' : ''}`}
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
                            {selectedRounds}라운드 게임 시작
                        </button>
                    ) : (
                        <div className="waiting-box">
                            방장이 게임을 시작하기를 기다리는 중...
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