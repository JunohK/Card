import AppRouter from "./router/AppRouter";
import { AuthProvider } from "./auth/authContext";
import SoundToggle from "./components/SoundToggle";

export default function App() {
    return (
        <AuthProvider>
            <AppRouter />
            <SoundToggle />
        </AuthProvider>
    );
}
