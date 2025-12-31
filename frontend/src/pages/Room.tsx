//방 화면(자동 입장 및 상태 표시)

import { useEffect, useState } from "react";
import { useParams, useLocation } from "react-router-dom";
import { joinRoom, getRoom } from "../api/gameApi";
import { GameRoom } from "../types/game";

function Room(){
    const { roomId } = useParams<{ roomId: string}>();
    const location = useLocation();
    const playerName = location.state?.playerName;

    const [room, setRoom] = useState<GameRoom | null>(null);
    const [error, setError] =useState("");

    useEffect(() => {
        if(!roomId || !playerName) return;

        const enter = async () => {
            try{
                const data = await joinRoom(roomId, playerName);
                setRoom(data);
            } catch {
                setError("방에 입장할 수 없습니다.");
            }
        };

        enter();
    }, [roomId, playerName]);

    if(error) return <p>{error}</p>;
    if(!room) return <p>입장 중...</p>

    return (
        <div style={{ padding: 20 }}>
            <h2>Room : {room.roomId}</h2>

            <h3>플레이어 목록</h3>
            <ul>
                {room.players.map((p) => (
                    <li key={p.playerId}>{p.name}</li>
                ))}
            </ul>
        </div>
    );
}

export default Room;