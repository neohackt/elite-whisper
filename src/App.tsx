import { useState, useEffect, useRef } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { WebviewWindow } from '@tauri-apps/api/webviewWindow';
import { writeFile, mkdir, BaseDirectory } from '@tauri-apps/plugin-fs';
import { appLocalDataDir, join } from '@tauri-apps/api/path';
import { writeWavHeader, floatTo16BitPCM } from './audioHelpers';
import { exists } from '@tauri-apps/plugin-fs'; // Add exists for model checking
import { AI_PROFILES, AUTO_MODE_RULES } from './config/profiles';
import { AVAILABLE_MODELS } from './config/models';
import { Wand2, Sparkles, X } from 'lucide-react'; // Added icons

// Components
import { Sidebar } from './components/Sidebar';
import { TopBar } from './components/TopBar';
import { Dashboard, DashboardStats } from './components/Dashboard';

import { HistoryView } from './components/HistoryView';
import { SettingsView } from './components/SettingsView';
import { ModelsView } from './components/ModelsView';
import { HistoryItem } from './components/HistoryInfoPanel';
import { VocabularyView } from './components/VocabularyView';
import { ModesView, PromptTemplate } from './components/ModesView'; // Import Modes
import { getLLMProvider } from './services/llm'; // Import LLM Service

const STORAGE_KEY_RECORDING_PATH = 'elite_whisper_recording_path';

function App() {
  // Ensure title is correct when back in App
  useEffect(() => { document.title = "Elite Whisper"; }, []);

  // --- STATE ---
  const [content, setContent] = useState("Click record button to start...");
  const [noteTitle, setNoteTitle] = useState("Untitled Note");
  const [status, setStatus] = useState("Ready");
  const [isRecording, setIsRecording] = useState(false);
  const [_selectedModel, setSelectedModel] = useState("openai/whisper-base");

  const [_currentModel, setCurrentModel] = useState("");
  const [isAutoMode, setIsAutoMode] = useState(false); // Add Auto Mode State

  // Microphone selection state
  const [inputDevices, setInputDevices] = useState<MediaDeviceInfo[]>([]);
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");
  const [lastDuration, setLastDuration] = useState<number>(0);
  const [lastProcessingTime, setLastProcessingTime] = useState<number>(0);
  const [lastRecordingPath, setLastRecordingPath] = useState<string>("");


  // Navigation State
  // valid views: 'home', 'recorder', 'modes', 'vocabulary', 'configuration', 'sound', 'history'
  const [view, setView] = useState<string>('home');
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const [dashboardStats, setDashboardStats] = useState<DashboardStats>({
    wpm: 0,
    wordsThisWeek: 0,
    appsUsed: 0,
    savedTime: "0 minutes"
  });

  // Post-Processing State
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [isProcessingLLM, setIsProcessingLLM] = useState(false);
  const [llmError, setLlmError] = useState<string | null>(null);


  // Visualizer State
  const [audioData, setAudioData] = useState<Uint8Array>(new Uint8Array(40).fill(10));
  const animationFrameRef = useRef<number | undefined>(undefined);

  // Refs for Audio Recording
  const mediaRecorder = useRef<any>(null);
  const isGlobalRef = useRef(false);
  const isRecordingRef = useRef(false);
  const hasInitialized = useRef(false);

  // Sync ref with state
  useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

  // Load Templates
  useEffect(() => {
    const saved = localStorage.getItem('elite_whisper_prompt_templates');
    if (saved) {
      setTemplates(JSON.parse(saved));
    } else {
      // Defaults if not loaded (or wait for first ModesView visit, but better to duplicate default logic or move it to shared config)
      // For now, empty or basic check
    }
  }, [view]); // Reload when view changes (e.g. coming back from Modes)


  // --- WINDOW MANAGEMENT ---
  const openWidgetWindow = async () => {
    try {
      const widgetWin = await WebviewWindow.getByLabel('widget_overlay');
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

  const loadStats = async () => {
    try {
      const stats = await invoke<DashboardStats>('cmd_get_dashboard_stats');
      setDashboardStats(stats);
    } catch (err) {
      console.error("Failed to load stats:", err);
    }
  };


  // 1.2 View Change Effect
  useEffect(() => {
    if (view === 'history') {
      loadHistory();
    }
    // Refresh stats when returning to home, to ensure latest numbers
    if (view === 'home') {
      loadStats();
    }
  }, [view]);

  // 1. Load Model Status on Startup
  useEffect(() => {
    if (hasInitialized.current) return;
    hasInitialized.current = true;

    async function init() {
      try {
        // Load stats on startup
        loadStats();

        // Check for saved profile preference FIRST to ensure synchronization
        const savedProfileId = localStorage.getItem('elite_whisper_active_profile');
        const isMultilingual = localStorage.getItem('elite_whisper_multilingual_mode') === 'true';

        let modelToLoad = null;

        if (savedProfileId) {
          const profile = AI_PROFILES.find(p => p.id === savedProfileId);
          if (profile) {
            const targetModelId = isMultilingual ? profile.models.multilingual : profile.models.english;
            const modelDef = AVAILABLE_MODELS.find(m => m.id === targetModelId);
            if (modelDef) {
              modelToLoad = modelDef.filename;
              console.log(`Startup: Found active profile '${profile.label}' (${isMultilingual ? 'Multi' : 'En'}), loading model: ${modelToLoad}`);
            }
          }
        }

        // Fallback to legacy direct model reference if no profile-based model found
        if (!modelToLoad) {
          modelToLoad = localStorage.getItem('elite_whisper_active_model');
        }

        if (modelToLoad) {
          try {
            // Construct path for saved model
            const appData = await appLocalDataDir();
            const modelPath = await join(appData, 'models', modelToLoad);

            // Attempt to load it
            await invoke('cmd_load_model', { modelPath });
            setCurrentModel(modelToLoad);
            setSelectedModel(modelToLoad);
            console.log("Restored saved model:", modelToLoad);
          } catch (e) {
            console.error("Failed to restore saved model, falling back to default:", e);
            // Fallback to whatever backend has
            const model = await invoke<string>('get_current_model');
            setCurrentModel(model);
            setSelectedModel(model);
          }
        } else {
          // No preference, load default explicitly
          try {
            const model = await invoke<string>('cmd_load_default_model');
            setCurrentModel(model);
            setSelectedModel(model);
          } catch (e) {
            console.error("Failed to load default model:", e);
            setStatus("Error: No Model");
          }
        }

        // Restore Auto Mode preference
        const savedAuto = localStorage.getItem('elite_whisper_auto_mode');
        if (savedAuto === 'true') setIsAutoMode(true);

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

  // Listen for history updates from Widget or other windows
  useEffect(() => {
    const setupListener = async () => {
      const unlisten = await listen<HistoryItem>('history-updated', (event) => {
        console.log("Received history update:", event.payload);
        setHistory(prev => {
          if (prev.some(item => item.id === event.payload.id)) return prev;
          return [event.payload, ...prev];
        });
        // Also refresh stats
        loadStats();
      });
      return () => unlisten();
    };

    // We handle the unlisten cleanup via the returned function from useEffect
    let unlistenFn: (() => void) | undefined;
    setupListener().then(fn => { unlistenFn = fn; });

    return () => {
      if (unlistenFn) unlistenFn();
    };
  }, []);

  const deleteHistoryItem = async (id: string) => {
    try {
      await invoke('cmd_delete_history', { id });
      setHistory(prev => prev.filter(item => item.id !== id));
      // Optionally reload stats if we want immediate sync, but view change handles it mostly
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

      // --- AUTO MODE LOGIC ---

      if (isAutoMode) {
        const duration = totalLength / 16000;
        const matchedRule = AUTO_MODE_RULES.find(rule => {
          const minOk = rule.minDuration ? duration >= rule.minDuration : true;
          const maxOk = rule.maxDuration ? duration < rule.maxDuration : true;
          return minOk && maxOk;
        });

        if (matchedRule) {
          const profile = AI_PROFILES.find(p => p.id === matchedRule.profileId);
          if (profile) {
            // Check multilingual preference dynamically
            const isMultilingual = localStorage.getItem('elite_whisper_multilingual_mode') === 'true';
            const targetModelId = isMultilingual ? profile.models.multilingual : profile.models.english;

            const modelDef = AVAILABLE_MODELS.find(m => m.id === targetModelId);
            if (modelDef) {
              try {
                // Check if model exists
                const modelFile = modelDef.filename;

                const checkPath = modelDef.type === 'sherpa' ? `models/${modelFile}/tokens.txt` : `models/${modelFile}`;

                const hasModel = await exists(checkPath, { baseDir: BaseDirectory.AppLocalData });

                if (hasModel) {
                  // If current loaded model is different, switch!
                  // We store full filename in _currentModel state usually, or get from backend
                  const currentName = await invoke<string>('get_current_model');
                  if (currentName !== modelFile) {
                    setStatus(`Switching to ${profile.label}...`);
                    const appData = await appLocalDataDir();
                    const modelPath = await join(appData, 'models', modelFile);
                    await invoke('cmd_load_model', { modelPath });
                    setCurrentModel(modelFile);
                    // Don't update "selectedModel" or profile because this is temporary for this recording? 
                    // Or maybe we should? For now, transient switch.
                  }
                } else {
                  console.warn(`AutoMode: Model for ${profile.label} not found locally.`);
                }
              } catch (e) {
                console.error("AutoMode Error:", e);
              }
            }
          }
        }
      }

      setStatus("Transcribing locally...");

      const startTime = performance.now();
      const result = await invoke<string>('cmd_transcribe', {
        audioData: Array.from(uint8Array)
      });
      const procTime = (performance.now() - startTime) / 1000;

      setLastDuration(totalLength / 16000);
      setLastRecordingPath(fullPath);
      setLastProcessingTime(procTime);

      if (isGlobalShortcut || isGlobalRef.current) {
        await invoke('cmd_type_text', { text: result });

        // Auto-save for global shortcut
        const newHistoryItem = await invoke<HistoryItem>('cmd_save_history', {
          transcript: result,
          filename: fullPath,
          title: noteTitle || "Untitled Note",
          duration: totalLength / 16000,
          processingTime: procTime
        });
        setHistory(prev => {
          if (prev.some(item => item.id === newHistoryItem.id)) return prev;
          return [newHistoryItem, ...prev];
        });
        // Update stats
        loadStats();
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
        duration: lastDuration,
        processingTime: lastProcessingTime
      });

      setHistory(prev => {
        if (prev.some(item => item.id === newHistoryItem.id)) return prev;
        return [newHistoryItem, ...prev];
      });
      loadStats();
      setStatus("Saved");
    } catch (err) {
      console.error("Failed to save:", err);
      setStatus("Save Failed");
    }
  };

  // --- LLM ACTION HANDLER ---
  const handleLLMAction = async (template: PromptTemplate) => {
    if (!content.trim() || isProcessingLLM) return;

    const provider = getLLMProvider();
    if (!provider) {
      setLlmError("No AI Provider configured. Please add an API Key in Settings.");
      setTimeout(() => setLlmError(null), 4000);
      return;
    }

    setIsProcessingLLM(true);
    const originalStatus = status;
    setStatus(`Thinking (${template.name})...`);

    try {
      const response = await provider.generate({
        systemPrompt: template.systemPrompt,
        userPrompt: content
      });

      if (response.error) {
        setLlmError(response.error);
        setStatus("AI Error");
      } else {
        setContent(response.text);
        setStatus(originalStatus === "Saved" ? "Unsaved (AI Edited)" : "Unsaved");
        // Optionally auto-update title based on first line?
      }
    } catch (e) {
      setLlmError("Unknown AI Error");
    } finally {
      setIsProcessingLLM(false);
      if (status.includes("Thinking")) setStatus(originalStatus); // revert if not updated
    }
  };

  // --- RENDER HELPERS ---
  const renderContent = () => {
    switch (view) {
      case 'home':
        return <Dashboard onStartRecording={() => { setView('recorder'); }} stats={dashboardStats} />;

      case 'recorder':
        const showSmartActions = !isRecording && content && content !== "Click record button to start..." && (status.includes('Done') || status.includes('Unsaved') || status.includes('Saved'));

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

            <div className="flex-1 p-8 overflow-hidden relative flex flex-col">
              <textarea
                value={content}
                onChange={(e) => setContent(e.target.value)}
                className="w-full flex-1 bg-white text-gray-800 p-6 rounded-xl border border-gray-200 focus:border-indigo-500 focus:ring-4 focus:ring-indigo-500/10 transition-all resize-none shadow-sm text-lg leading-relaxed outline-none placeholder-gray-400"
                placeholder="Transcription will appear here..."
              />

              {/* Smart Actions Bar (Floating/Overlay) */}
              {showSmartActions && (
                <div className="mt-4 animate-fade-in-up">
                  <div className="flex items-center gap-2 mb-2 text-xs font-bold text-indigo-500 uppercase tracking-wider">
                    <Sparkles size={12} /> Smart Actions
                  </div>
                  {llmError && (
                    <div className="mb-2 bg-red-50 text-red-600 px-3 py-2 rounded-lg text-xs flex items-center justify-between">
                      <span>{llmError}</span>
                      <button onClick={() => setLlmError(null)}><X size={12} /></button>
                    </div>
                  )}
                  <div className="flex flex-wrap gap-2">
                    {templates.slice(0, 4).map(template => (
                      <button
                        key={template.id}
                        onClick={() => handleLLMAction(template)}
                        disabled={isProcessingLLM}
                        className="px-4 py-2 bg-white border border-indigo-100 shadow-sm rounded-lg text-sm font-medium text-gray-700 hover:bg-indigo-50 hover:text-indigo-600 hover:border-indigo-200 transition-all flex items-center gap-2 disabled:opacity-50"
                      >
                        <Wand2 size={14} className={isProcessingLLM ? "animate-spin" : ""} />
                        {isProcessingLLM ? "Processing..." : template.name}
                      </button>
                    ))}
                    <button
                      onClick={() => setView('modes')}
                      className="px-3 py-2 text-xs text-gray-400 hover:text-indigo-500"
                    >
                      + Manage
                    </button>
                  </div>
                </div>
              )}
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

      case 'models':
        return <ModelsView isAutoMode={isAutoMode} setAutoMode={(val: boolean) => {
          setIsAutoMode(val);
          localStorage.setItem('elite_whisper_auto_mode', String(val));
        }} />;

      case 'vocabulary':
        return <VocabularyView />;

      case 'modes':
        return <ModesView />;

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
