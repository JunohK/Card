import { useEffect, useState } from "react";
import { pingServer } from "./api/cardApi";

import CreateRoom from "./components/CreateRoom";
import JoinRoom from "./components/JoinRoom";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import Home from "./pages/Home";
import Room from "./pages/Room";

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/room/:roomId" element={<Room />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;


// 서버 연결 코드

// import { useEffect, useState } from "react";
// import { pingServer } from "./api/cardApi";

// function App(){
//     const [message, setMessage] = useState("로딩중...");

//     useEffect(() => {
//         pingServer()
//         .then(setMessage)
//         .catch(() => setMessage("서버 연결 실패"));
//     }, []);

//     return(
//         <div style={{ padding:20}}>
//             <h1>카드</h1>
//             <p>API 응답 : {message}</p>
//         </div>
//     );
// }

// export default App;