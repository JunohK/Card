import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/authContext";
import "../css/LoginPage.css";

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
        <div className="login-container">
            <div className="login-card">
                <h1 className="login-title">카드 게임</h1>

                <form onSubmit={handleLogin}>
                    <div className="form-group">
                        <label className="form-label">닉네임</label>
                        <input
                            type="text"
                            value={nickname}
                            onChange={(e) => setNickname(e.target.value)}
                            className="form-input"
                            placeholder="닉네임을 입력하세요"
                            required
                            autoFocus
                        />
                    </div>

                    <div className="form-group">
                        <label className="form-label">비밀번호</label>
                        <input
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            className="form-input"
                            placeholder="비밀번호를 입력하세요"
                            required
                        />
                    </div>

                    {error && (
                        <div style={{ color: '#f87171', fontSize: '0.8rem', textAlign: 'center' }}>
                            {error}
                        </div>
                    )}

                    <button
                        type="submit"
                        disabled={loading}
                        className="login-submit-btn"
                    >
                        {loading ? "로그인 중..." : "로그인"}
                    </button>
                </form>

                <div className="signup-link-wrapper">
                    <p style={{ color: '#64748b', fontSize: '0.8rem', margin: '0 0 0.75rem 0' }}>
                        아직 계정이 없으신가요?
                    </p>
                    <button 
                        type="button"
                        onClick={() => navigate("/signup")}
                        className="signup-btn"
                    >
                        계정 만들기
                    </button>
                </div>
            </div>
        </div>
    );
}