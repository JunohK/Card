import { useNavigate } from "react-router-dom";

export default function MainPage(){
    const navigate = useNavigate();

    return (
        <div className="flex items-center justify-center min-h-screen bg-gray-100">
            <div className="bg-white p-8 rounded shadow-md w-96 text-center">
                <h1 className="text-2xl font-bold mb-6"> Card </h1>

                <button
                    className="w-full bg-blue-600 text-white py-2 rounded mb-3 hover:bg-blue-700"
                    onClick={() => navigate("/login")}
                >
                    로그인
                </button>

                <button
                    className="w-full bg-green-600 text-white py-2 rounded hover:bg-green-700"
                    onClick={() => navigate("/signup")}
                >
                    회원가입
                </button>
            </div>
        </div>
    );
}