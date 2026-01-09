import AppRouter from "./router/AppRouter";
import { AuthProvider } from "./auth/authContext";

export default function App() {
    return (
        <AuthProvider>
            <AppRouter />
        </AuthProvider>
    );
}
