import React, { useState, useEffect, useRef } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { writeFile, BaseDirectory } from '@tauri-apps/plugin-fs';
import { writeWavHeader, floatTo16BitPCM } from './audioHelpers';

interface HistoryItem {
  id: string;
  filename: string;
  transcript: string;
  timestamp: number;
  title: string;
}

import { getCurrentWindow, LogicalSize } from '@tauri-apps/api/window';
import { WebviewWindow } from '@tauri-apps/api/webviewWindow';

function App() {
  // --- STATE ---
  const [content, setContent] = useState("Click record button to start...");
  const [noteTitle, setNoteTitle] = useState("Untitled Note");
  const [status, setStatus] = useState("Initializing Model...");
  const [isRecording, setIsRecording] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [selectedModel, setSelectedModel] = useState("openai/whisper-base");
  const [currentModel, setCurrentModel] = useState("");

  // History State
  const [view, setView] = useState<'recorder' | 'history'>('recorder');
  const [history, setHistory] = useState<HistoryItem[]>([]);

  // Visualizer State
  const [audioData, setAudioData] = useState<Uint8Array>(new Uint8Array(40).fill(10));
  const animationFrameRef = useRef<number>();

  // Floating Mode State
  const [isFloating, setIsFloating] = useState(false);
  const [isHovered, setIsHovered] = useState(false);

  // Refs for Audio Recording
  const mediaRecorder = useRef<any>(null);
  const isGlobalRef = useRef(false);
  const isRecordingRef = useRef(false);

  // Sync ref with state
  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

  // Global shortcut removed from App.tsx - handled by Widget.tsx
  // useEffect(() => {
  //   const setupListener = async () => { ... }
  // }, []);

  // 1. Load Model Status on Startup
  useEffect(() => {
    async function init() {
      try {
        const model = await invoke<string>('get_current_model');
        setCurrentModel(model);
        setSelectedModel(model);
        setStatus("Ready");
      } catch (err) {
        setStatus("Error Loading Model");
        console.error(err);
      }
    }
    init();
  }, []);

  // 1.1 Load History
  const loadHistory = async () => {
    try {
      const items = await invoke<HistoryItem[]>('cmd_get_history');
      setHistory(items);
    } catch (err) {
      console.error("Failed to load history:", err);
    }
  };

  // --- WINDOW MANAGEMENT ---
  const openWidgetWindow = async () => {
    // Get the widget window and show it
    const widgetWin = await WebviewWindow.getByLabel('widget');
    if (widgetWin) {
      await widgetWin.show();
      await widgetWin.setFocus();
    }
  };

  useEffect(() => {
    if (view === 'history') {
      loadHistory();
    }
  }, [view]);


  const deleteHistoryItem = async (id: string) => {
    try {
      await invoke('cmd_delete_history', { id });
      setHistory(prev => prev.filter(item => item.id !== id));
    } catch (err) {
      console.error("Failed to delete item:", err);
    }
  };

  const startRecording = async (isGlobalShortcut = false) => {
    try {
      isGlobalRef.current = isGlobalShortcut;
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { sampleRate: 16000, channelCount: 1 } });

      // Setup AudioContext for Visualizer
      const audioContext = new AudioContext({ sampleRate: 16000 });
      await audioContext.resume();

      const source = audioContext.createMediaStreamSource(stream);
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 128; // 64 bins
      analyser.smoothingTimeConstant = 0.5;

      const processor = audioContext.createScriptProcessor(4096, 1, 1);

      source.connect(analyser);
      analyser.connect(processor);
      processor.connect(audioContext.destination);

      // Animation Loop logic
      const bufferLength = analyser.frequencyBinCount;
      const dataArray = new Uint8Array(bufferLength);

      const updateVisualizer = () => {
        if (!isRecordingRef.current) return;
        analyser.getByteFrequencyData(dataArray);

        // Take first 40 bins (low-mid freqs)
        const relevantData = dataArray.slice(0, 40);
        setAudioData(new Uint8Array(relevantData));

        animationFrameRef.current = requestAnimationFrame(updateVisualizer);
      };

      const buffers: Float32Array[] = [];

      processor.onaudioprocess = (e) => {
        const input = e.inputBuffer.getChannelData(0);
        buffers.push(new Float32Array(input));
      };

      mediaRecorder.current = {
        stop: () => {
          if (animationFrameRef.current) cancelAnimationFrame(animationFrameRef.current);
          processor.disconnect();
          analyser.disconnect();
          source.disconnect();
          stream.getTracks().forEach(track => track.stop());
          audioContext.close();
          // Reset visuals
          setAudioData(new Uint8Array(40).fill(10));
        },
        getBuffers: () => buffers
      };

      setIsRecording(true);
      isRecordingRef.current = true; // For immediate loop access
      setContent(""); // Clear previous content? User might want to append? Let's clear for "New Note"
      setStatus("Listening...");

      // Start loop slightly after state update
      updateVisualizer();

    } catch (err) {
      console.error("Error accessing microphone:", err);
      setStatus("Mic Error");
    }
  };

  const stopRecording = async (isGlobalShortcut = false) => {
    if (!mediaRecorder.current) return;

    const recorder = mediaRecorder.current;
    mediaRecorder.current = null; // Prevent re-entry

    recorder.stop();
    const rawBuffers = recorder.getBuffers() as Float32Array[];
    setIsRecording(false);
    setStatus("Processing Audio...");

    // Flatten buffers
    const totalLength = rawBuffers.reduce((acc, buf) => acc + buf.length, 0);
    const mergedSamples = new Float32Array(totalLength);
    let offset = 0;
    for (const buf of rawBuffers) {
      mergedSamples.set(buf, offset);
      offset += buf.length;
    }

    // Encode to WAV (16kHz, Mono, 16-bit)
    const header = writeWavHeader(16000, 1, totalLength);
    const wavData = new DataView(new ArrayBuffer(header.byteLength + totalLength * 2));

    // Copy Header
    new Uint8Array(wavData.buffer).set(new Uint8Array(header), 0);

    // Convert samples to Int16
    floatTo16BitPCM(wavData, 44, mergedSamples); // 44 is header size

    const uint8Array = new Uint8Array(wavData.buffer);
    const fileName = `recording_${Date.now()}.wav`;

    try {
      // Save file using Tauri
      await writeFile(fileName, uint8Array, { baseDir: BaseDirectory.Download });
      setStatus("Transcribing locally...");

      // Call Rust to transcribe
      const result = await invoke<string>('cmd_transcribe', {
        audioData: Array.from(uint8Array)
      });

      // Global Auto-Type
      if (isGlobalShortcut || isGlobalRef.current) {
        await invoke('cmd_type_text', { text: result });
      }

      // Save to History with TITLE
      await invoke('cmd_save_history', {
        transcript: result,
        filename: fileName,
        title: noteTitle || "Untitled Note"
      });

      setContent(result);
      setStatus("Done");
    } catch (err) {
      // Show actual error from Rust
      setStatus(`Error: ${err} `);
      console.error(err);
    }
  };

  // 3. Settings Functions
  const handleSaveSettings = async () => {
    try {
      await invoke('set_model', { modelId: selectedModel });
      setCurrentModel(selectedModel);
      setStatus("Model Switching...");

      // Fake delay to simulate reload
      setTimeout(() => {
        setStatus("Ready");
      }, 1000);

      setShowSettings(false);
    } catch (err) {
      console.error(err);
      setStatus("Error Setting Model");
    }
  };

  // --- RENDERING ---

  return (
    <div className="flex h-screen bg-gray-50 text-gray-900 relative font-sans">

      {/* --- SIDEBAR --- */}
      <div className="w-64 bg-white flex flex-col justify-between border-r border-gray-200 shadow-sm">
        <div className="p-6">
          <h1 className="text-2xl font-extrabold text-gray-900 tracking-tight">
            Elite<span className="text-indigo-600">Whisper</span>
          </h1>
        </div>

        <nav className="flex flex-col gap-2 p-4">
          <button
            className={`flex items-center gap-3 p-3 rounded-lg font-medium transition-all ${view === 'recorder'
              ? 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100'
              : 'hover:bg-gray-100 text-gray-600'
              }`}
            onClick={() => { setView('recorder'); setContent(""); setNoteTitle("Untitled Note"); }}
          >
            <span>🎙️</span>
            <span>New Note</span>
          </button>
          <button
            className={`flex items-center gap-3 p-3 rounded-lg font-medium transition-all ${view === 'history'
              ? 'bg-indigo-50 text-indigo-700 hover:bg-indigo-100'
              : 'hover:bg-gray-100 text-gray-600'
              }`}
            onClick={() => setView('history')}
          >
            <span>📜</span>
            <span>History</span>
          </button>
          <button
            className="flex items-center gap-3 p-3 rounded-lg hover:bg-gray-100 text-gray-600 font-medium transition-all"
            onClick={openWidgetWindow}
          >
            <span>🌐</span>
            <span>Floating</span>
          </button>
        </nav>

        <div className="p-4 border-t border-gray-200">
          {/* Settings Button (Opens Modal) */}
          <button
            className="flex items-center gap-3 p-3 rounded-lg hover:bg-gray-100 text-gray-600 font-medium transition-all w-full"
            onClick={() => setShowSettings(true)}
          >
            <span>⚙️</span>
            <span>Settings</span>
          </button>
        </div>
      </div>

      {/* --- MAIN CONTENT --- */}
      <div className="flex-1 flex flex-col bg-gray-50 h-[calc(100vh)] overflow-hidden">

        {/* Header */}
        <header className="flex justify-between items-center px-8 py-5 border-b border-gray-200 bg-white">
          <div className="flex flex-col flex-1 mr-8">
            {view === 'recorder' ? (
              <input
                type="text"
                value={noteTitle}
                onChange={(e) => setNoteTitle(e.target.value)}
                className="font-bold text-xl text-gray-900 border-none outline-none bg-transparent placeholder-gray-400 focus:ring-0 w-full"
                placeholder="Untitled Note"
              />
            ) : (
              <span className="font-bold text-xl text-gray-900">History</span>
            )}

            {view === 'recorder' && (
              <span className="text-sm text-gray-500 mt-1">
                Model: {
                  currentModel === 'openai/whisper-base' ? 'Whisper Base' :
                    currentModel === 'openai/whisper-small' ? 'Whisper Small' :
                      currentModel === 'nvidia/parakeet-tdt-1.1b' ? 'Parakeet 1.1B' :
                        currentModel
                }
              </span>
            )}
          </div>

          <div className="flex items-center gap-4">
            {/* Visualizer in Header (only when recording) */}
            {isRecording && view === 'recorder' && (
              <div className="flex items-center gap-[2px] h-8 w-32 mr-4">
                {Array.from({ length: 20 }).map((_, i) => {
                  const value = audioData[i * 2] || 0;
                  const height = Math.max(20, (value / 255) * 100);
                  return (
                    <div
                      key={i}
                      className="w-1 bg-indigo-500 rounded-full transition-all duration-75"
                      style={{ height: `${height}%` }}
                    />
                  );
                })}
              </div>
            )}

            <div className={`px-3 py-1 rounded-full text-xs font-semibold
                ${status === 'Ready' || status === 'Done' ? 'bg-green-100 text-green-700' :
                status.includes('Error') ? 'bg-red-100 text-red-700' : 'bg-indigo-100 text-indigo-700'
              } `}>
              {status}
            </div>
          </div>
        </header>

        {/* Content Area */}
        {view === 'recorder' ? (
          <>
            {/* Text Area */}
            <div className="flex-1 p-8 overflow-hidden">
              <textarea
                value={content}
                onChange={(e) => setContent(e.target.value)}
                className="w-full h-full bg-white text-gray-800 p-6 rounded-xl border border-gray-200 focus:border-indigo-500 focus:ring-4 focus:ring-indigo-500/10 transition-all resize-none shadow-sm text-lg leading-relaxed outline-none placeholder-gray-400"
                placeholder="Click start recording to transcribe your voice..."
              />
            </div>

            {/* Bottom Toolbar */}
            <div className="px-8 py-6 bg-white border-t border-gray-200 flex justify-between items-center shadow-[0_-4px_6px_-1px_rgba(0,0,0,0.02)]">
              <div className="flex gap-3">
                <button className="px-5 py-2.5 bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium rounded-lg transition-colors" onClick={() => console.log("Summarize")}>Summarize</button>
                <button className="px-5 py-2.5 bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium rounded-lg transition-colors" onClick={() => console.log("Polish")}>Polish</button>
              </div>

              <button
                className={`px-8 py-3 rounded-full font-bold shadow-lg shadow-indigo-200 transition-all transform active:scale-95 
                    ${isRecording
                    ? 'bg-red-500 hover:bg-red-600 text-white animate-pulse shadow-red-200'
                    : 'bg-indigo-600 hover:bg-indigo-700 text-white'
                  } `}
                onClick={() => isRecording ? stopRecording(false) : startRecording(false)}
              >
                {isRecording ? "Stop Recording" : "Start Recording"}
              </button>
            </div>
          </>
        ) : (
          // --- HISTORY VIEW ---
          <div className="flex-1 p-8 overflow-y-auto">
            {history.length === 0 ? (
              <div className="text-center text-gray-400 mt-20">No history yet.</div>
            ) : (
              <div className="flex flex-col gap-4">
                {history.map(item => (
                  <div key={item.id} className="bg-white p-6 rounded-xl border border-gray-200 shadow-sm hover:shadow-md transition-shadow">
                    <div className="flex justify-between items-start mb-3">
                      <div>
                        {/* Display Title or Fallback */}
                        <h3 className="font-bold text-gray-800 text-lg mb-1">{item.title || "Untitled Note"}</h3>
                        <span className="text-sm text-gray-500">{new Date(item.timestamp * 1000).toLocaleString()}</span>
                        {/* Removed filename display if it's too much clutter, or keep it small */}
                        {/* <div className="text-xs text-gray-400 font-mono bg-gray-100 px-2 py-1 rounded w-fit mt-1">{item.filename}</div> */}
                      </div>
                      <button
                        onClick={(e) => { e.stopPropagation(); deleteHistoryItem(item.id); }}
                        className="text-red-400 hover:text-red-600 hover:bg-red-50 p-2 rounded-full transition-colors"
                        title="Delete"
                      >
                        🗑️
                      </button>
                    </div>
                    <p className="text-gray-600 leading-relaxed font-light line-clamp-3">{item.transcript}</p>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}


      </div>

      {/* --- SETTINGS MODAL --- */}
      {showSettings && (
        <div className="fixed inset-0 flex items-center justify-center bg-black/20 backdrop-blur-sm z-50">
          <div className="bg-white p-8 rounded-2xl w-96 shadow-2xl border border-gray-100 transform transition-all">
            <h2 className="text-xl font-bold mb-6 text-gray-900">Select AI Model</h2>

            <label className="block text-sm font-semibold mb-2 text-gray-700">Speech-to-Text Engine</label>
            <select
              value={selectedModel}
              onChange={(e) => setSelectedModel(e.target.value)}
              className="w-full bg-gray-50 border border-gray-300 text-gray-900 rounded-lg p-3 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 outline-none transition-all appearance-none"
            >
              <option value="openai/whisper-base">Whisper Base (Fast, Small)</option>
              <option value="openai/whisper-small">Whisper Small (More Accurate)</option>
              <option value="nvidia/parakeet-tdt-1.1b">Parakeet 1.1B (NVIDIA)</option>
            </select>

            <div className="mt-4 p-4 bg-indigo-50 border border-indigo-100 rounded-lg">
              <p className="text-xs text-indigo-800 leading-relaxed">
                <strong>Note:</strong> Current backend only supports the local Whisper Base model. Switching models here is for UI demonstration only.
              </p>
            </div>

            <div className="flex justify-end gap-3 mt-8">
              <button
                onClick={() => setShowSettings(false)}
                className="px-5 py-2.5 text-gray-600 hover:bg-gray-100 font-medium rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveSettings}
                className="px-5 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white font-medium rounded-lg shadow-md shadow-indigo-200 transition-all"
              >
                Save & Reload
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default App;