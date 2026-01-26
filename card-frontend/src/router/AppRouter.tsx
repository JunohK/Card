import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import MainPage from "../pages/MainPage";
import LoginPage from "../pages/LoginPage";
import LobbyPage from "../pages/LobbyPage";
import SignupPage from "../pages/SignupPage";
import RoomPage from "../pages/RoomPage";
import GamePage from "../pages/GamePage";
import RulePage from "../pages/RulePage";
import { useAuth } from "../auth/authContext";

export default function AppRouter() {
    const { isAuth } = useAuth();

    return (
        <BrowserRouter>
            <Routes>
                {/* 공개 페이지 */}
                <Route path="/" element={<MainPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/signup" element={<SignupPage />} />

                {/* 로그인 필수 */}
                <Route
                    path="/lobby"
                    element={isAuth ? <LobbyPage /> : <Navigate to="/login" replace />}
                />

                {/* ✅ 방 대기 페이지 */}
                <Route
                    path="/room/:roomId"
                    element={isAuth ? <RoomPage /> : <Navigate to= "/login" replace />}
                />

                {/* ✅ 실제 게임 화면 페이지 추가 */}
                <Route
                    path="/game/:roomId"
                    element={isAuth ? <GamePage /> : <Navigate to= "/login" replace />}
                />

                {/* fallback */}
                <Route path="*" element={<Navigate to="/" replace />} />

                {/* RulePage */}
                <Route path="/rules" element={<RulePage />} />
            </Routes>
        </BrowserRouter>
    );
}