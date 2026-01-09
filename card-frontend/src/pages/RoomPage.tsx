import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection } from "../signalr/connection";

type Player = {
    playerId : string;
    name: string;
}

type RoomState = {
    roomId: string;
    players: Player[];
    isStarted: boolean;
    hostPlayerId?: string;
};

export default function RoomPage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();

    const [room, setRoom] = useState<RoomState | null>(null);
    const [connected, setConnected] = useState(false);

    useEffect(() => {
        if(!roomId) return;

        // 방 상태 갱신
        connection.on("RoomUpdated",(room:RoomState) => {
            setRoom(room);
        });

        // 게임 시작
        connection.on("GameStarted",(room:RoomState) => {
            setRoom(room);
            alert("게임 시작");
        });

        if(connection.state === "Disconnected") {
            connection.start()
                .then(() => setConnected(true))
                .catch(console.error);
        }

        // connection
        //     .start()
        //     .then(() => {
        //         setConnected(true);
        //         connection.invoke("JoinRoom", roomId);
        //     })
        //     .catch(console.error);

        return () => {
            connection.off("RoomUpdated");
            connection.off("GameStarted");
        };
    }, [roomId]);

    const leaveRoom = async () => {
        if (roomId) {
            await connection.invoke("LeaveRoom",roomId);
        }
        navigate("/lobby");
    };

    const startGame = async() => {
        if(!roomId) return;
        await connection.invoke("StartGame",roomId, 1);
    };

    const isHost = room?.players?.[0]?.playerId === room?.hostPlayerId;

    return (
        <div className="min-h-screen bg-gray-100 p-6">
            <div className="max-w-3xl mx-auto bg-white rounded-xl shadow p-6">
                <h1 className="text-xl font-bold mb-4">
                    방 ID : {roomId}
                </h1>

                <p className="text-sm mb-4">
                    SignalR : {connected ? "Connected" : "Disconnected"}
                </p>

                {/* 플레이어 목록 */}
                <div className="mb-6">
                    <h2 className="font-semibold mb-2">플레이어</h2>
                    <ul className="border rounded p-3">
                        {room?.players?.map((p) => (
                            <li key={p.playerId} className="text-sm py-1">
                                {p.name}
                                {p.playerId === room.hostPlayerId && " 우승~ "}
                            </li>
                        ))}
                    </ul>
                </div>

                {/* 방장 - 게임시작 */}
                {!room?.isStarted && isHost && (
                    <button
                        onClick={startGame}
                        className="bg-blue-600 text-white px-4 py-2 rounded mr-2"
                    >
                        게임 시작
                    </button>
                )}

                <button
                    onClick={leaveRoom}
                    className="border px-4 py-2 rounded"
                >
                    나가기
                </button>
            </div>
        </div>
    );
}