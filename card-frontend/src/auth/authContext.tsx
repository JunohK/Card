import { createContext, useContext, useState } from "react";
import { login as loginApi } from "./authApi";
import { authStorage } from "./authStorage";

const AuthContext = createContext<any>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [isAuth, setIsAuth] = useState(!!authStorage.getToken());

  const login = async (nickname: string, password: string) => {
    const { token } = await loginApi(nickname, password);
    authStorage.setToken(token);
    setIsAuth(true);
  };

  const logout = () => {
    authStorage.clear();
    setIsAuth(false);
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
