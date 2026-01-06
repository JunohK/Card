import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";

export default function LoginPage() {
    const navigate = useNavigate();
    const { login } = useAuth();

    const [nickname, setNickname] = useState("");
    const [password, setPassword] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setLoading(true);

        try {
            await login(nickname, password);
            navigate("/lobby");
                } catch (err: any) {
            setError(err.message || "로그인 실패");
                } finally {
            setLoading(false);
                }
            };

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-100">
            <form
                onSubmit={handleLogin}
                className="bg-white p-8 rounded-2xl shadow-md w-96"
            >
                <h1 className="text-2xl font-bold mb-6 text-center">로그인</h1>

                <div className="mb-4">
                    <label className="block mb-1 text-sm font-mediu">닉네임</label>
                    <input
                        type="text"
                        value={nickname}
                        onChange={(e) => setNickname(e.target.value)}
                        className="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring"
                        required
                    />
                </div>

                <div className="mb-4">
                    <label className="block mb-1 text-sm font-medium">비밀번호</label>
                    <input
                        type="password"
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        className="w-full border rounded-lg px-3 py-2 focus:outline-none focus:ring"
                        required
                    />
                </div>

                {error && (
                    <p className="text-red-500 text-sm mb-4 text-center">{error}</p>
                )}

                <button
                    type="submit"
                    disabled={loading}
                    className="w-full bg-blue-600 text-white py-2 rounded-lg hover:bg-blue-700 disabled:opacity-50"
                >
                    {loading ? "로그인 중..." : "로그인"}
                </button>

                <p className="text-sm text-center mt-4 text-gray-600">
                    <button 
                        type="button"
                        // className="text-blue-600 cursor-pointer hover:underline"
                        onClick={() => navigate("/signup")}
                        className="mt-4 w-full border py-2 rounded-lg"
                    >
                        회원가입
                    </button>
                </p>
            </form>
        </div>
    )
}