import { createContext, useContext, useState } from "react";
import { login as loginApi } from "./authApi";
import { authStorage } from "./authStorage";
import { connection } from "../signalr/connection";

const AuthContext = createContext<any>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [isAuth, setIsAuth] = useState(!!authStorage.getToken());

  // SignalR 연결은 시작 시점의 토큰으로 서버 신원이 고정된다.
  // 이전 아이디로 맺어진 연결이 남아 있으면 끊어야 다음 진입 때 새 토큰으로 다시 연결된다.
  const login = async (nickname: string, password: string) => {
    const { token } = await loginApi(nickname, password);
    await connection.stop();
    authStorage.setToken(token);
    setIsAuth(true);
  };

  const logout = () => {
    authStorage.clear();
    setIsAuth(false);
    connection.stop().catch(() => { });
  };

  return (
    <AuthContext.Provider value={{ isAuth, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}
