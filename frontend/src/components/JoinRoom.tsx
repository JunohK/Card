// 방 입장 컴포넌트

import { useState } from "react";
import { joinRoom, getRoom } from "../api/gameApi";
import { GameRoom } from "../types/game";

function JoinRoom(){
    const [ roomId, setRoomId ] = useState<string>("");
    const [ name, setName ] = useState<string>("");
    const [ room, setRoom ] = useState<GameRoom | null>(null);
    const [ error, setError ] = useState<String>("");

    const handleJoin = async () => {
        if (!roomId.trim() || !name.trim()){
            setError("RoomId와 이름을 입력하세요.");
            return;
        }

        try{
            const data = await joinRoom(roomId, name);
            setRoom(data);
            setError("");
        } catch {
            setError("입장실패.. (RoomId 또는 중복 이름)");
        }
    };

    const handleRefresh = async () => {
        if (!roomId) return;

        try{
            const data = await getRoom(roomId);
            setRoom(data);
        } catch {
            setError("방 정보를 불러올 수 없음");
        }
    };

    return (
        <div style={{ padding: 20}}>
            <h2>방 입장</h2>

            <input
                placeholder="Room ID"
                value={roomId}
                onChange={(e) => setRoomId(e.target.value)}
            />

            <input
                placeholder="플레이어 이름"
                value={name}
                onChange={(e) => setName(e.target.value)}
                style={{ marginLeft: 10}}
            />

            <button onClick={handleJoin} style={{ marginLeft: 10}}>
                입장
            </button>

            <button onClick={handleRefresh} style={{ marginLeft: 10}}>
                새로고침
            </button>

            {error && <p style={{ color: "red"}}>{error}</p>}

            {room && (
                <div style={{ marginTop: 20 }}>
                    <h3>플레이어 목록</h3>
                    <ul>
                        {room.players.map((p) => (
                            <li key={p.playerId}>{p.name}</li>
                        ))}
                    </ul>
                </div>
            )}
        </div>
    );
}

export default JoinRoom;