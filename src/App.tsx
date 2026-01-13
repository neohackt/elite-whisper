import React, { useState, useEffect, useRef } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { writeFile, BaseDirectory } from '@tauri-apps/plugin-fs';
import { writeWavHeader, floatTo16BitPCM } from './audioHelpers';

function App() {
  // --- STATE ---
  const [content, setContent] = useState("Click record button to start...");
  const [status, setStatus] = useState("Initializing Model...");
  const [isRecording, setIsRecording] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [selectedModel, setSelectedModel] = useState("openai/whisper-base");
  const [currentModel, setCurrentModel] = useState("");

  // Refs for Audio Recording
  const mediaRecorder = useRef<any>(null);

  // 1. Load Model Status on Startup
  useEffect(() => {
    const checkModel = async () => {
      try {
        const model = await invoke<string>('get_current_model');
        setCurrentModel(model);
        setSelectedModel(model);
        setStatus("Ready");
      } catch (err) {
        console.error(err);
        setStatus("Error Loading Model");
      }
    };
    checkModel();
  }, []);

  // 2. Audio Recording Functions
  const startRecording = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          sampleRate: 16000,
          channelCount: 1
        }
      });

      const audioContext = new AudioContext({ sampleRate: 16000 });
      await audioContext.resume(); // Ensure context is running
      const source = audioContext.createMediaStreamSource(stream);
      // Create a ScriptProcessorNode with a bufferSize of 4096 and a single input and output channel
      const processor = audioContext.createScriptProcessor(4096, 1, 1);

      source.connect(processor);
      processor.connect(audioContext.destination);

      const buffers: Float32Array[] = [];

      processor.onaudioprocess = (e) => {
        const input = e.inputBuffer.getChannelData(0);
        buffers.push(new Float32Array(input));
      };

      mediaRecorder.current = {
        stop: () => {
          processor.disconnect();
          source.disconnect();
          stream.getTracks().forEach(track => track.stop());
          audioContext.close();
        },
        getBuffers: () => buffers
      };

      setIsRecording(true);
      setStatus("Recording...");
    } catch (err) {
      console.error("Error accessing microphone:", err);
      setStatus("Mic Error: " + String(err));
    }
  };

  const stopRecording = async () => {
    if (!mediaRecorder.current) return;

    mediaRecorder.current.stop();
    const rawBuffers = mediaRecorder.current.getBuffers() as Float32Array[];
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

      setContent(result);
      setStatus("Done");
    } catch (err) {
      // Show actual error from Rust
      setStatus(`Error: ${err}`);
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

  return (
    <div className="flex h-screen bg-gray-50 text-gray-900 relative font-sans">

      {/* --- SIDEBAR --- */}
      <div className="w-64 bg-white flex flex-col justify-between border-r border-gray-200 shadow-sm">
        <div className="p-6">
          <h1 className="text-2xl font-extrabold text-gray-900 tracking-tight">
            AI<span className="text-indigo-600">Voice</span>
          </h1>
        </div>

        <nav className="flex flex-col gap-2 p-4">
          <button
            className="flex items-center gap-3 p-3 rounded-lg bg-indigo-50 text-indigo-700 font-medium hover:bg-indigo-100 transition-all"
            onClick={() => setContent("")}
          >
            <span>🎙️</span>
            <span>New Note</span>
          </button>
          <button
            className="flex items-center gap-3 p-3 rounded-lg hover:bg-gray-100 text-gray-600 font-medium transition-all"
            onClick={() => console.log("History")}
          >
            <span>📜</span>
            <span>History</span>
          </button>
          <button
            className="flex items-center gap-3 p-3 rounded-lg hover:bg-gray-100 text-gray-600 font-medium transition-all"
            onClick={() => console.log("Floating")}
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
      <div className="flex-1 flex flex-col bg-gray-50">

        {/* Header */}
        <header className="flex justify-between items-center px-8 py-5 border-b border-gray-200 bg-white">
          <div className="flex flex-col">
            <span className="font-bold text-xl text-gray-900">Untitled Note</span>
            <span className="text-sm text-gray-500 mt-1">Model: {currentModel}</span>
          </div>

          <div className={`px-3 py-1 rounded-full text-xs font-semibold
            ${status === 'Ready' || status === 'Done' ? 'bg-green-100 text-green-700' :
              status.includes('Error') ? 'bg-red-100 text-red-700' : 'bg-indigo-100 text-indigo-700'}`}>
            {status}
          </div>
        </header>

        {/* Text Area */}
        <div className="flex-1 p-8">
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
                : 'bg-indigo-600 hover:bg-indigo-700 text-white'}`}
            onClick={isRecording ? stopRecording : startRecording}
          >
            {isRecording ? "Stop Recording" : "Start Recording"}
          </button>
        </div>
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