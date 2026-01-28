import { useNavigate } from "react-router-dom";
import "../css/MainPage.css"; 

export default function MainPage() {
    const navigate = useNavigate();

    return (
        <div className="main-wrapper"> 
            <div className="main-card">
                <div className="logo-section">
                    <span className="logo-icon">🃏</span>
                    <h1 className="main-title">CARD GAME</h1>

                </div>

                <div className="button-group">
                    <button
                        className="btn btn-login"
                        onClick={() => navigate("/login")}
                    >
                        로그인
                    </button>

                    <button
                        className="btn btn-signup"
                        onClick={() => navigate("/signup")}
                    >
                        회원가입
                    </button>
                </div>

                <div className="footer-text">
                    © 2026 Card Game Project. All rights reserved.
                </div>
            </div>
        </div>
    );
}