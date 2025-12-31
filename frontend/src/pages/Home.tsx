import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { createRoom } from "../api/gameApi";

function Home(){
    const [name, setName] = useState("");
    const [error,setError] = useState("");
    const navigate = useNavigate();

    const handleCreate = async () => {
        if(!name.trim()) {
            setError("이름을 입력하세요");
            return;
        }
        try {
            const room = await createRoom(name);
            navigate(`/room/${room.roomId}`,{
                state: { playerName: name },
            });
        } catch {
            setError("방 생성 실패");
        }
    };

    return (
        <div style={{ padding: 20 }}>
            <h1>게임제목</h1>

            <input
                placeholder="플레이어 이름"
                value={name}
                onChange={(e) => setName(e.target.value)}
            />

            <button onClick={handleCreate} style={{ marginLeft: 10 }}>
                방 생성
            </button>

            {error && <p style={{ color: "red"}}>{error}</p>}
        </div>
    );
}

export default Home;