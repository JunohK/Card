import { useEffect, useState } from 'react'
import reactLogo from './assets/react.svg'
import viteLogo from '/vite.svg'
import './App.css'
import { connection } from "./signalr";

function App() {
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    connection
    .start()
    .then(() => {
      console.log("SignalR connected");
      setConnected(true);
    })
    .catch(console.error);
  }, []);
  
  return (
      <div>
        <h1>Card Game</h1>
        <p>SignalR: {connected ? "Connected" : "Disconnected"}</p>
      </div>
    );
  }

  export default App;