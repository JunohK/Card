import { useState } from "react";
import { authStorage } from "./authStorage";
import { login as loginApi } from "./authApi";

export function useAuth() {
    const [isAuthenticated, setIsAuthenticated] = useState(
        !!authStorage.getToken()
    );

    const login = async (nickname: string, password : string) => {
        const { token } = await loginApi(nickname, password);
        authStorage.setToken(token);
        setIsAuthenticated(true);
    };

    const logout = () => {
        authStorage.clear();
        setIsAuthenticated(false);
    };

    return { isAuthenticated, login, logout };
}