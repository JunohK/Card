import { createContext, useContext, useEffect, useState } from "react";
import { connection } from "./connection";

type SignalRContextType = {
    connected: boolean;
};

const SignalRContext = createContext<SignalRContextType>({
    connected: false,
});

export const SignalRProvider = ({ children }: { children: React.ReactNode }) => {
    const [connected, setConnected] = useState(false);

    useEffect(() => {
        const start = async () => {
            if(connection.state === "Disconnected") {
                try{
                    await connection.start();
                    setConnected(true);
                    console.log("SignalR Connected");
                } catch (e) {
                    console.error("SignalR start failed", e);
                }
            }
        };

        start();

        connection.onreconnected(() => {
            setConnected(true);
        });

        connection.onclose(() => {
            setConnected(false);
        });

        return() => {

        };
    }, []);

    return (
        <SignalRContext.Provider value={{ connected }}>
            { children }
        </SignalRContext.Provider>
    );
};

export const useSignalR = () => useContext(SignalRContext);