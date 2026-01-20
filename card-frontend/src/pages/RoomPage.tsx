import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";

type Player = { playerId: string; name: string; };
type RoomState = {
    roomId: string;
    title: string;
    players: Player[];
    isStarted: boolean;
    hostPlayerId?: string;
};

export default function RoomPage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();
    const [room, setRoom] = useState<RoomState | null>(null);
    const [connected, setConnected] = useState(false);
    const [error, setError] = useState<string | null>(null);
    
    // StrictMode 중복 실행 방지용
    const initialized = useRef(false);

    useEffect(() => {
        if (!roomId || initialized.current) return;
        initialized.current = true;

        // [핵심] 리스너 중복 방지를 위해 함수를 별도로 정의
        const onRoomUpdated = (updatedRoom: RoomState) => {
            console.log("RoomUpdated:", updatedRoom);
            setRoom({ ...updatedRoom }); // 새로운 객체 주입
            setError(null);
        };

        const onGameStarted = (startedRoom: RoomState) => {
            setRoom({ ...startedRoom });
            alert("게임이 시작되었습니다!");
            navigate(`/game/${roomId}`);
        };

        // 기존 리스너를 한 번 지우고 다시 등록
        connection.off("RoomUpdated");
        connection.off("GameStarted");
        connection.on("RoomUpdated", onRoomUpdated);
        connection.on("GameStarted", onGameStarted);

        const init = async () => {
            try {
                if (connection.state === "Disconnected") {
                    await connection.start();
                }
                setConnected(true);
                const savedPwd = sessionStorage.getItem(`room_pwd_${roomId}`);
                // JoinRoom 호출
                await connection.invoke("JoinRoom", roomId, savedPwd || null);
            } catch (err: any) {
                setError(err.message || "입장 실패");
            }
        };

        init();

        return () => {
            // 언마운트 시 리스너 해제
            connection.off("RoomUpdated");
            connection.off("GameStarted");
            initialized.current = false;
        };
    }, [roomId]);

    const leaveRoom = async () => {
        if (roomId) await connection.invoke("LeaveRoom", roomId);
        navigate("/lobby");
    };

    const startGame = async () => {
        if (roomId) await connection.invoke("StartGame", roomId);
    };

    // [방장 확인] 서버에서 준 hostPlayerId와 내 connectionId가 같은지 비교
    const isHost = room?.hostPlayerId === connection.connectionId;

    if (error) return (
        <div className="min-h-screen flex flex-col items-center justify-center">
            <h2 className="text-red-600 font-bold text-xl">{error}</h2>
            <button onClick={() => navigate("/lobby")} className="mt-4 bg-gray-800 text-white px-6 py-2 rounded">로비로</button>
        </div>
    );

    return (
        <div className="max-w-3xl mx-auto p-6 bg-white shadow-xl mt-10 rounded-2xl">
            <div className="border-b pb-4 mb-6">
                <h1 className="text-3xl font-black text-gray-800">{room?.title || "연결 중..."}</h1>
                {/* <p className="text-sm text-gray-400 font-mono">My ID: {connection.connectionId}</p> */}
            </div>

            <div className="mb-10">
                <h2 className="text-lg font-bold mb-4">플레이어 ({room?.players?.length ?? 0}/7)</h2>
                <div className="grid gap-3">
                    {room?.players?.map((p) => (
                        <div key={p.playerId} className={`p-4 rounded-xl border-2 flex justify-between items-center ${
                            p.playerId === connection.connectionId ? "border-blue-500 bg-blue-50" : "border-gray-100"
                        }`}>
                            <span className="font-bold">
                                {p.name} {p.playerId === connection.connectionId && "(나)"}
                                {p.playerId === room.hostPlayerId && <span className="bg-yellow-100 text-yellow-700 text-xs px-2 py-1 rounded-full font-bold">👑 방장</span>}
                            </span>
                            <div className="flex gap-2">
                            </div>
                        </div>
                    ))}
                </div>
            </div>

            <div className="flex gap-4">
                {/* [방장 체크 결과 반영] */}
                {!room?.isStarted && isHost && (
                    <button onClick={startGame} className="flex-1 bg-blue-600 text-white font-bold py-4 rounded-xl hover:bg-blue-700 transition-all">게임 시작</button>
                )}
                <button onClick={leaveRoom} className="px-10 bg-gray-100 text-gray-600 font-bold py-4 rounded-xl">나가기</button>
            </div>
        </div>
    );
}