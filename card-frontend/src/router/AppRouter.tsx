import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import MainPage from "../pages/MainPage";
import LoginPage from "../pages/LoginPage";
import LobbyPage from "../pages/LobbyPage";
import SignupPage from "../pages/SignupPage";
import { authStorage } from "../auth/authStorage";


export default function AppRouter() {
    const isAuth = !!authStorage.getToken();

    return (
        <BrowserRouter>
            <Routes>
                <Route path="/" element={<MainPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/signup" element={<SignupPage />} />
                <Route
                    path="/lobby"
                    element={isAuth ? <LobbyPage /> : <Navigate to="/login" />}
                />
                <Route path="*" element={<Navigate to="/login" />} />
            </Routes>
        </BrowserRouter>
    );
}