import { useState } from "react";
import { createRoom } from "../api/gameApi";
import { GameRoom } from "../types/game";

function CreateRoom(){
    const [name, setName] = useState<string>("");
    const [room, setRoom] = useState<GameRoom | null>(null);
    const [error, setError] = useState<string>("");

    const handleCreate = async () => {
        if (!name.trim()){
            setError("이름을 입력하세요");
            return;
        }
        try {
            const data = await createRoom(name);
            setRoom(data);
            setError("");
        } catch {
            setError("서버 오류");
        }
    };

    return (
        <div style={{ padding: 20}}>
            <h2>방 만들기</h2>

        <input
            placeholder="플레이어이름"
            value={name}
            onChange={(e) => setName(e.target.value)}
        />

        <button onClick={handleCreate} style={{ marginLeft:10 }}>
            방 생성
        </button>

        {error && <p style={{ color: "red"}}>{error}</p>}

        {room && (
            <div style={{ marginTop: 20 }}>
                <p> 방 생성 성공 </p>
                <p>
                    <strong>Room ID : </strong> {room.roomId}
                </p>
                <p>플레이어 수 : {room.players.length}</p>
            </div>
        )}
        </div>
    );
}

export default CreateRoom;