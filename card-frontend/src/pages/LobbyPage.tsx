import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import { connection } from "../signalr/connection";

export default function LobbyPage() {
    const navigate = useNavigate();
    const { logout } = useAuth();

    const [connected, setConnected] = useState(false);
    const [messages, setMessages] = useState<string[]>([]);
    const [input, setInput] = useState("");

    useEffect(() => {
        connection.on("ReceiveMessage", (user: string, message: string) => {
            setMessages((prev) => [...prev, `${user} : ${message}`]);
        });

        connection
            .start()
            .then(() => setConnected(true))
            .catch(console.error);

        return () => {
            connection.off("ReceiveMessage");
            connection.stop();
        };
    }, []);

    const sendMessage = async () => {
        if(!input.trim()) return;

        try{
            await connection.invoke("SendChatMessage", input);
            setInput("");
        } catch (err) {
            console.error(err);
        }
    };

    const handleLogout = () => {
        logout();
        navigate("/login");
    }

    return (
        <div className="min-h-screen bg-gray-100 p-6">
            <div className="max-w-3xl mx-auto bg-white rounded-xl shadow p-6">
                <div className="flex justify-between items-center mb-4">
                    <h1 className="text-xl font-bold">로비</h1>
                    <button
                        onClick={handleLogout}
                        className="text-sm text-red-600"
                    >
                        로그아웃
                    </button>
                </div>

                <p className="text-sm mb-4">
                    signalR 상태 : {connected ? "Connected" : "Disconnected"}
                </p>

                <div className="border rounded p-4 h-64 overflow-y-auto mb-4">
                    {messages.map((m, i) => (
                        <div key={i} className="text-sm mb-1">{m}</div>
                    ))}
                </div>

                <div className="flex gap-2">
                    <input
                        className="flex-1 border rounded px-3 py-2"
                        value={input}
                        onChange={(e) => setInput(e.target.value)}
                        placeholder="메시지 입력"
                    />
                    <button
                        onClick={sendMessage}
                        className="bg-blue-600 text-white px-4 rounded"
                    >
                        전송
                    </button>
                </div>
            </div>
        </div>
    );
}