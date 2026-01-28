import { useState } from "react";
import { useNavigate } from "react-router-dom";
import "../css/SignupPage.css"; // CSS 파일 경로

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
        <div className="signup-wrapper">
            <form onSubmit={handleSubmit} className="signup-card">
                <div className="signup-header">
                    <h1 className="signup-title">SIGN UP</h1>
                    <p className="signup-subtitle">새로운 플레이어 등록</p>
                </div>

                <div className="input-group">
                    <input
                        type="text"
                        placeholder="닉네임"
                        value={nickname}
                        onChange={e => setNickname(e.target.value)}
                        className="signup-input"
                    />
                    <input
                        type="password"
                        placeholder="비밀번호"
                        value={password}
                        onChange={e => setPassword(e.target.value)}
                        className="signup-input"
                    />
                </div>

                {error && <p className="status-msg error">{error}</p>}
                {success && <p className="status-msg success">회원가입 완료! 로그인 페이지로 이동합니다.</p>}

                <div className="button-group row">
                    <button
                        type="submit"
                        disabled={loading}
                        className="btn btn-primary"
                    >
                        {loading ? "처리 중..." : "가입하기"}
                    </button>

                    <button
                        type="button"
                        onClick={() => navigate("/login")}
                        className="btn btn-secondary"
                    >
                        취소
                    </button>
                </div>
            </form>
        </div>
    );
}