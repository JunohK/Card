import { useState } from "react";
import { useNavigate } from "react-router-dom";

export default function SignupPage() {
    const navigate = useNavigate();

    const [nickname, setNickname] = useState("");
    const [password, setPassword] = useState("");
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setSuccess(false);

        if (!nickname || !password) {
            setError("모든 항목을 입력해주세요.");
            return;
        }

        try {
            setLoading(true);

            const res = await fetch("http://localhost:5101/api/auth/signup", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({ nickname, password }),
            });

            if (!res.ok) {
                const msg = await res.text();
                throw new Error(msg || "회원가입 실패");
            }

            setSuccess(true);
            setNickname("");
            setPassword("");

            setTimeout(() => navigate("/login"), 1000);

        } catch (err: any) {
            setError(err.message);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center bg-zinc-950 text-white">
            <form
                onSubmit={handleSubmit}
                className="w-full max-w-sm bg-zinc-900 p-8 rounded-2xl shadow-lg space-y-4"
            >
                <h1 className="text-2xl font-bold text-center">회원가입</h1>

                <input
                    type="text"
                    placeholder="닉네임"
                    value={nickname}
                    onChange={e => setNickname(e.target.value)}
                    className="w-full p-3 rounded-lg bg-zinc-800 outline-none"
                />
                <input
                    type="password"
                    placeholder="비밀번호"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    className="w-full p-3 rounded-lg bg-zinc-800 outline-none"
                />

                {error && <p className="text-red-400 text-sm">{error}</p>}
                {success && <p className="text-green-400 text-sm">회원가입 완료</p>}

                <div className="flex gap-4">
                    <button
                        type="submit"
                        disabled={loading}
                        className="flex-1 py-3 rounded-xl bg-indigo-600 hover:bg-indigo-500 transition disabled:opacity-50"
                    >
                        {loading ? "처리 중..." : "회원가입"}
                    </button>

                    <button
                        type="button"
                        onClick={() => navigate("/login")}
                        className="flex-1 py-3 rounded-xl bg-zinc-700 hover:bg-zinc-600 transition"
                    >
                        뒤로
                    </button>
                </div>
            </form>
        </div>
    )
}