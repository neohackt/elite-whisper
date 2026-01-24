import "./index.css";
import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import Widget from "./Widget";
import { getCurrentWindow } from "@tauri-apps/api/window";

const currentWindow = getCurrentWindow();
const label = currentWindow.label;

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    {(label.includes('widget') || window.location.pathname.includes('widget') || window.innerWidth < 400) ? <Widget /> : <App />}
  </React.StrictMode>
);
