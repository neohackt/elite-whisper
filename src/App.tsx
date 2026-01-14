import React, { useState, useEffect, useRef } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { WebviewWindow } from '@tauri-apps/api/webviewWindow';
import { writeFile, mkdir, BaseDirectory } from '@tauri-apps/plugin-fs';
import { appLocalDataDir, join } from '@tauri-apps/api/path';
import { writeWavHeader, floatTo16BitPCM } from './audioHelpers';

// Components
import { Sidebar } from './components/Sidebar';
import { TopBar } from './components/TopBar';
import { Dashboard } from './components/Dashboard';

import { HistoryView } from './components/HistoryView';
import { SettingsView } from './components/SettingsView';
import { HistoryItem } from './components/HistoryInfoPanel';
const STORAGE_KEY_RECORDING_PATH = 'elite_whisper_recording_path';



function App() {
  // --- STATE ---
  const [content, setContent] = useState("Click record button to start...");
  const [noteTitle, setNoteTitle] = useState("Untitled Note");
  const [status, setStatus] = useState("Ready");
  const [isRecording, setIsRecording] = useState(false);
  const [selectedModel, setSelectedModel] = useState("openai/whisper-base");
  const [currentModel, setCurrentModel] = useState("");

  // Microphone selection state
  const [inputDevices, setInputDevices] = useState<MediaDeviceInfo[]>([]);
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");
  const [lastDuration, setLastDuration] = useState<number>(0);
  const [lastRecordingPath, setLastRecordingPath] = useState<string>("");


  // Navigation State
  // valid views: 'home', 'recorder', 'modes', 'vocabulary', 'configuration', 'sound', 'history'
  const [view, setView] = useState<string>('home');
  const [history, setHistory] = useState<HistoryItem[]>([]);

  // Visualizer State
  const [audioData, setAudioData] = useState<Uint8Array>(new Uint8Array(40).fill(10));
  const animationFrameRef = useRef<number | undefined>(undefined);

  // Refs for Audio Recording
  const mediaRecorder = useRef<any>(null);
  const isGlobalRef = useRef(false);
  const isRecordingRef = useRef(false);

  // Sync ref with state
  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);




  // --- WINDOW MANAGEMENT ---
  const openWidgetWindow = async () => {
    try {
      const widgetWin = await WebviewWindow.getByLabel('widget');
      if (widgetWin) {
        await widgetWin.show();
        await widgetWin.setFocus();
      }
    } catch (err) {
      console.error("Failed to open widget:", err);
    }
  };

  // 1.1 Load History
  const loadHistory = async () => {
    try {
      const items = await invoke<HistoryItem[]>('cmd_get_history');
      setHistory(items);
    } catch (err) {
      console.error("Failed to load history:", err);
    }
  };


  // 1.2 View Change Effect
  useEffect(() => {
    if (view === 'history') {
      loadHistory();
    }
  }, [view]);

  // 1. Load Model Status on Startup
  useEffect(() => {
    async function init() {
      try {
        const model = await invoke<string>('get_current_model');
        setCurrentModel(model);
        setSelectedModel(model);

        // Auto-open widget on launch
        setTimeout(() => {
          openWidgetWindow();
        }, 1000); // Slight delay to ensure Tauri is ready
      } catch (err) {
        setStatus("Error Loading Model");
        console.error(err);
      }
    }
    init();
  }, []);

  // Fetch audio input devices
  useEffect(() => {
    const fetchDevices = async () => {
      try {
        // Request permission first to get labels
        await navigator.mediaDevices.getUserMedia({ audio: true });
        const devices = await navigator.mediaDevices.enumerateDevices();
        const audioInputs = devices.filter(device => device.kind === 'audioinput');
        setInputDevices(audioInputs);

        // Set default if not set
        if (audioInputs.length > 0 && !selectedDeviceId) {
          // If "default" exists, pick it, otherwise pick first
          const defaultDevice = audioInputs.find(d => d.deviceId === 'default');
          setSelectedDeviceId(defaultDevice ? defaultDevice.deviceId : audioInputs[0].deviceId);
        }
      } catch (err) {
        console.error("Error fetching devices:", err);
      }
    };

    fetchDevices();

    // Listen for device changes
    navigator.mediaDevices.addEventListener('devicechange', fetchDevices);
    return () => {
      navigator.mediaDevices.removeEventListener('devicechange', fetchDevices);
    };
  }, []);

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

      const constraints: MediaStreamConstraints = {
        audio: {
          sampleRate: 16000,
          channelCount: 1,
          deviceId: selectedDeviceId ? { exact: selectedDeviceId } : undefined
        }
      };

      const stream = await navigator.mediaDevices.getUserMedia(constraints);

      const audioContext = new AudioContext({ sampleRate: 16000 });
      await audioContext.resume();

      const source = audioContext.createMediaStreamSource(stream);
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 128;
      analyser.smoothingTimeConstant = 0.5;

      const processor = audioContext.createScriptProcessor(4096, 1, 1);

      source.connect(analyser);
      analyser.connect(processor);
      processor.connect(audioContext.destination);

      const bufferLength = analyser.frequencyBinCount;
      const dataArray = new Uint8Array(bufferLength);

      const updateVisualizer = () => {
        if (!isRecordingRef.current) return;
        analyser.getByteFrequencyData(dataArray);
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
          setAudioData(new Uint8Array(40).fill(10));
        },
        getBuffers: () => buffers
      };

      setIsRecording(true);
      isRecordingRef.current = true;
      setContent("");
      setStatus("Listening...");
      updateVisualizer();

    } catch (err) {
      console.error("Error accessing microphone:", err);
      setStatus("Mic Error");
    }
  };

  const stopRecording = async (isGlobalShortcut = false) => {
    if (!mediaRecorder.current) return;

    const recorder = mediaRecorder.current;
    mediaRecorder.current = null;

    recorder.stop();
    const rawBuffers = recorder.getBuffers() as Float32Array[];
    setIsRecording(false);
    setStatus("Processing Audio...");

    const totalLength = rawBuffers.reduce((acc, buf) => acc + buf.length, 0);
    const mergedSamples = new Float32Array(totalLength);
    let offset = 0;
    for (const buf of rawBuffers) {
      mergedSamples.set(buf, offset);
      offset += buf.length;
    }

    const header = writeWavHeader(16000, 1, totalLength);
    const wavData = new DataView(new ArrayBuffer(header.byteLength + totalLength * 2));
    new Uint8Array(wavData.buffer).set(new Uint8Array(header), 0);
    floatTo16BitPCM(wavData, 44, mergedSamples);

    const uint8Array = new Uint8Array(wavData.buffer);
    const fileName = `recording_${Date.now()}.wav`; // Keep generic filename variable

    let fullPath = '';
    const customPath = localStorage.getItem(STORAGE_KEY_RECORDING_PATH);

    try {
      if (customPath) {
        // Custom Path Logic
        try {
          await mkdir(customPath, { recursive: true });
        } catch (e) { console.log("Dir check/create error (might exist):", e); }

        fullPath = await join(customPath, fileName);
        await writeFile(fullPath, uint8Array); // Write to absolute path
      } else {
        // Default Logic
        try {
          await mkdir('recorded_audio', { baseDir: BaseDirectory.AppLocalData, recursive: true });
        } catch (e) { }

        await writeFile(`recorded_audio/${fileName}`, uint8Array, { baseDir: BaseDirectory.AppLocalData });

        const appDataDir = await appLocalDataDir();
        fullPath = await join(appDataDir, 'recorded_audio', fileName);
      }

      setStatus("Transcribing locally...");

      const result = await invoke<string>('cmd_transcribe', {
        audioData: Array.from(uint8Array)
      });

      setLastDuration(totalLength / 16000);
      setLastRecordingPath(fullPath);

      if (isGlobalShortcut || isGlobalRef.current) {
        await invoke('cmd_type_text', { text: result });

        // Auto-save for global shortcut
        const newHistoryItem = await invoke<HistoryItem>('cmd_save_history', {
          transcript: result,
          filename: fullPath,
          title: noteTitle || "Untitled Note",
          duration: totalLength / 16000
        });
        setHistory(prev => [newHistoryItem, ...prev]);
        setStatus("Done");
      } else {
        // Manual recording: Wait for explicit save
        setStatus("Unsaved");
      }

      setContent(result);
    } catch (err) {
      setStatus(`Error: ${err} `);
      console.error(err);
    }
  };

  const saveNote = async () => {
    try {
      if (!content.trim()) return;

      // Use the path stored from stopRecording
      const fullPath = lastRecordingPath;

      if (!fullPath) {
        console.error("No recording path found");
        setStatus("Error: No Audio");
        return;
      }

      const newHistoryItem = await invoke<HistoryItem>('cmd_save_history', {
        transcript: content,
        filename: fullPath,
        title: noteTitle || "Untitled Note",
        duration: lastDuration
      });

      setHistory(prev => [newHistoryItem, ...prev]);
      setStatus("Saved");
    } catch (err) {
      console.error("Failed to save:", err);
      setStatus("Save Failed");
    }
  };



  const getStats = () => {
    const now = new Date();
    const oneWeekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);

    // Filter items from the last week
    const weeklyItems = history.filter(item => {
      // Assuming item.timestamp is in seconds (Unix timestamp)
      const itemDate = new Date(item.timestamp * 1000);
      return itemDate >= oneWeekAgo;
    });

    // 1. Words this week
    const wordsThisWeek = weeklyItems.reduce((acc, item) => {
      return acc + (item.transcript ? item.transcript.trim().split(/\s+/).length : 0);
    }, 0);

    // 2. Apps used (Unique apps in the last week)
    const uniqueApps = new Set(weeklyItems.map(item => item.app_name).filter(Boolean));
    const appsUsed = uniqueApps.size;

    // 3. WPM (Average Speed)
    // Formula: Total Words / Total Minutes
    const totalWordsAllTime = history.reduce((acc, item) => acc + (item.transcript ? item.transcript.trim().split(/\s+/).length : 0), 0);
    const totalDurationSeconds = history.reduce((acc, item) => acc + (item.duration || 0), 0);
    const totalDurationMinutes = totalDurationSeconds / 60;

    // Default to a reasonable number if no data (e.g., 0 or user's last speed)
    // If totalDurationMinutes is extremely small, avoid Infinity.
    const wpm = totalDurationMinutes > 0.1 ? Math.round(totalWordsAllTime / totalDurationMinutes) : 0;


    // 4. Saved this week (Time saved)
    // Formula: Words this week / 40 WPM (Average typing speed)
    const minutesSaved = Math.round(wordsThisWeek / 40);
    const savedTime = `${minutesSaved} minute${minutesSaved !== 1 ? 's' : ''}`;

    return {
      wpm: wpm > 0 ? wpm : 0, // Fallback
      wordsThisWeek,
      appsUsed,
      savedTime
    };
  };

  // --- RENDER HELPERS ---
  const renderContent = () => {
    switch (view) {
      case 'home':
        return <Dashboard onStartRecording={() => { setView('recorder'); }} stats={getStats()} />;

      case 'recorder':
        return (
          <div className="flex flex-col h-full">
            <header className="flex justify-between items-center px-8 py-5 border-b border-gray-200 bg-white">
              <input
                type="text"
                value={noteTitle}
                onChange={(e) => setNoteTitle(e.target.value)}
                className="font-bold text-xl text-gray-900 border-none outline-none bg-transparent placeholder-gray-400 focus:ring-0 w-full"
                placeholder="Untitled Note"
              />
              <div className="flex items-center gap-4">
                {isRecording && (
                  <div className="flex items-center gap-[2px] h-8 w-32 mr-4">
                    {Array.from({ length: 20 }).map((_, i) => {
                      const value = audioData[i * 2] || 0;
                      const height = Math.max(20, (value / 255) * 100);
                      return (
                        <div key={i} className="w-1 bg-indigo-500 rounded-full transition-all duration-75" style={{ height: `${height}%` }} />
                      );
                    })}
                  </div>
                )}
                <div className={`px-3 py-1 rounded-full text-xs font-semibold ${status === 'Ready' || status === 'Done' ? 'bg-green-100 text-green-700' : status.includes('Error') ? 'bg-red-100 text-red-700' : 'bg-indigo-100 text-indigo-700'}`}>
                  {status}
                </div>
              </div>
            </header>

            <div className="flex-1 p-8 overflow-hidden relative">
              <textarea
                value={content}
                onChange={(e) => setContent(e.target.value)}
                className="w-full h-full bg-white text-gray-800 p-6 rounded-xl border border-gray-200 focus:border-indigo-500 focus:ring-4 focus:ring-indigo-500/10 transition-all resize-none shadow-sm text-lg leading-relaxed outline-none placeholder-gray-400"
                placeholder="Transcription will appear here..."
              />
            </div>

            <div className="px-8 py-6 bg-white border-t border-gray-200 flex justify-between items-center shadow-[0_-4px_6px_-1px_rgba(0,0,0,0.02)]">
              <button
                className="px-5 py-2.5 bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium rounded-lg transition-colors w-24"
                onClick={() => setView('home')}
              >
                Back
              </button>

              <button
                className={`px-8 py-3 rounded-full font-bold shadow-lg shadow-indigo-200 transition-all transform active:scale-95 flex items-center justify-center gap-2 min-w-[180px]
                        ${isRecording ? 'bg-red-500 hover:bg-red-600 text-white animate-pulse shadow-red-200' : 'bg-indigo-600 hover:bg-indigo-700 text-white'}`}
                onClick={() => isRecording ? stopRecording(false) : startRecording(false)}
              >
                {isRecording ? "Stop" : "Record"}
              </button>

              <button
                className="px-5 py-2.5 bg-indigo-50 hover:bg-indigo-100 text-indigo-700 font-medium rounded-lg transition-colors w-24 disabled:opacity-50 disabled:cursor-not-allowed"
                onClick={saveNote}
                disabled={isRecording || !content}
              >
                Save
              </button>
            </div>
          </div>
        );

      case 'history':
        return (
          <div className="flex-1 overflow-hidden bg-[#f2f2f2]">
            <HistoryView items={history} onDelete={deleteHistoryItem} />
          </div>
        );

      case 'settings':
        return <SettingsView />;

      case 'modes':
      case 'vocabulary':
      case 'sound':
        return (
          <div className="flex-1 flex items-center justify-center text-gray-400 flex-col gap-2">
            <span className="text-4xl">🚧</span>
            <span className="font-medium">Coming Soon</span>
            <span className="text-sm">Current View: {view}</span>
          </div>
        );

      default:
        return <div>Unknown View</div>;
    }
  };

  return (
    <div className="flex h-screen bg-[#f9f9f9] text-gray-900 relative font-sans overflow-hidden">
      <Sidebar currentView={view} setView={setView} />

      <div className="flex-1 flex flex-col h-full">
        <TopBar
          devices={inputDevices}
          selectedDeviceId={selectedDeviceId}
          onSelectDevice={setSelectedDeviceId}
        />
        {renderContent()}
      </div>
    </div>
  );
}

export default App;
