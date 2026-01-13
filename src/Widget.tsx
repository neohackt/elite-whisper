import { useState, useEffect, useRef } from 'react';
import { getCurrentWindow, LogicalSize } from '@tauri-apps/api/window';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { writeFile, BaseDirectory } from '@tauri-apps/plugin-fs';
import { writeWavHeader, floatTo16BitPCM } from './audioHelpers';

export default function Widget() {
    const [isExpanded, setIsExpanded] = useState(false);
    const [isRecording, setIsRecording] = useState(false);
    const [isHovered, setIsHovered] = useState(false);

    // Microphone State
    const [devices, setDevices] = useState<{ deviceId: string; label: string }[]>([]);
    const [selectedMic, setSelectedMic] = useState<string>("");

    // Visualizer State
    const [audioData, setAudioData] = useState<Uint8Array>(new Uint8Array(40).fill(10));
    const animationFrameRef = useRef<number>();

    // Refs for Audio Recording
    const mediaRecorder = useRef<any>(null);
    const isRecordingRef = useRef(false);

    // Sync ref with state
    useEffect(() => { isRecordingRef.current = isRecording; }, [isRecording]);

    // Window sizing constants
    const PILL_WIDTH = 300;
    const PILL_HEIGHT = 120;
    const EXPANDED_WIDTH = 550;
    const EXPANDED_HEIGHT = 140;

    // Initial Setup
    useEffect(() => {
        // Disable shadow to remove glass box artifact
        invoke('cmd_disable_shadow').catch(console.error);

        // Fetch microphones
        const getDevices = async () => {
            try {
                const devs = await navigator.mediaDevices.enumerateDevices();
                const audioInputs = devs
                    .filter(d => d.kind === 'audioinput')
                    .map(d => ({ deviceId: d.deviceId, label: d.label || `Microphone ${d.deviceId.slice(0, 5)}...` }));

                setDevices(audioInputs);

                // Only change selection if currrent one is invalid or not set
                setSelectedMic(prev => {
                    if (prev && audioInputs.find(d => d.deviceId === prev)) return prev;
                    /* Default to first */
                    return audioInputs.length > 0 ? audioInputs[0].deviceId : "";
                });
            } catch (err) {
                console.error("Error fetching devices:", err);
            }
        };

        // Initial fetch with permission request
        const initDevices = async () => {
            try {
                // Request permission first to get labels
                const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                stream.getTracks().forEach(t => t.stop()); // Stop immediately
                await getDevices();
            } catch (err) {
                console.error("Error getting permission:", err);
            }
        };
        initDevices();

        // Listen for hardware changes (USB/Bluetooth plug/unplug)
        navigator.mediaDevices.addEventListener('devicechange', getDevices);

        // Listen for global shortcut
        const unlistenPromise = listen('global-shortcut', () => {
            console.log("Global shortcut F2 triggered in Widget");
            if (isRecordingRef.current) {
                stopRecording();
            } else {
                startRecording();
            }
        });

        return () => {
            navigator.mediaDevices.removeEventListener('devicechange', getDevices);
            unlistenPromise.then(unlisten => unlisten());
            if (animationFrameRef.current) cancelAnimationFrame(animationFrameRef.current);
        };
    }, []);

    // Handle Window Resizing based on state
    useEffect(() => {
        const updateWindowSize = async () => {
            const appWindow = getCurrentWindow();
            if (isExpanded) {
                await appWindow.setSize(new LogicalSize(EXPANDED_WIDTH, EXPANDED_HEIGHT));
            } else {
                await appWindow.setSize(new LogicalSize(PILL_WIDTH, PILL_HEIGHT)); // Reset to pill size
            }
        };
        updateWindowSize();
    }, [isExpanded]);

    // Handle Global Keydown 
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && isExpanded) {
                setIsExpanded(false);
            }
            if (e.key === 'F2') {
                if (!isRecording) startRecording();
                else stopRecording();
            }
        };
        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, [isExpanded, isRecording]);

    const startRecording = async () => {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    deviceId: selectedMic ? { exact: selectedMic } : undefined, // USE SELECTED MIC
                    sampleRate: 16000,
                    channelCount: 1
                }
            });

            const audioContext = new AudioContext({ sampleRate: 16000 });
            await audioContext.resume();

            const source = audioContext.createMediaStreamSource(stream);
            // Analyser for visualization
            const analyser = audioContext.createAnalyser();
            analyser.fftSize = 128; // Smaller FFT size for fewer bars (64 bins)
            analyser.smoothingTimeConstant = 0.5; // Smooth out the bars

            const processor = audioContext.createScriptProcessor(4096, 1, 1);

            source.connect(analyser);
            analyser.connect(processor);
            processor.connect(audioContext.destination);

            // Animation Loop
            const bufferLength = analyser.frequencyBinCount;
            const dataArray = new Uint8Array(bufferLength);

            // Explicitly mark as recording for the ref-based loop
            isRecordingRef.current = true;

            const updateVisualizer = () => {
                if (!isRecordingRef.current) return;

                analyser.getByteFrequencyData(dataArray);

                // We want 40 bars for expanded, maybe 15 for pill. 
                // We'll store enough for expanded (40), and slice for pill render.
                const relevantData = dataArray.slice(0, 40);
                setAudioData(new Uint8Array(relevantData));

                animationFrameRef.current = requestAnimationFrame(updateVisualizer);
            };
            updateVisualizer();

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
                },
                getBuffers: () => buffers
            };

            setIsRecording(true);
            // Removed toggleExpand() - recording keeps it in pill mode now (Stealth Mode)
        } catch (err) {
            console.error("Error accessing microphone:", err);
        }
    };

    const stopRecording = async () => {
        if (!mediaRecorder.current) return;

        const recorder = mediaRecorder.current;
        mediaRecorder.current = null;

        recorder.stop();
        // Reset visuals
        setAudioData(new Uint8Array(40).fill(10));

        const rawBuffers = recorder.getBuffers() as Float32Array[];
        setIsRecording(false);

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

        new Uint8Array(wavData.buffer).set(new Uint8Array(header), 0);
        floatTo16BitPCM(wavData, 44, mergedSamples);

        const uint8Array = new Uint8Array(wavData.buffer);
        const fileName = `recording_widget_${Date.now()}.wav`;

        try {
            // Save file
            await writeFile(fileName, uint8Array, { baseDir: BaseDirectory.Download });

            // Transcribe
            const result = await invoke<string>('cmd_transcribe', {
                audioData: Array.from(uint8Array)
            });

            // Global Auto-Type
            await invoke('cmd_type_text', { text: result });

            // Save to History (Using main app's history command)
            await invoke('cmd_save_history', { transcript: result, filename: fileName });

            console.log("Transcription done:", result);

        } catch (err) {
            console.error(err);
        }
    };

    const dragStartRef = useRef<{ x: number, y: number } | null>(null);
    const isDraggingRef = useRef(false);

    const handleMouseDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return; // Only left click
        dragStartRef.current = { x: e.clientX, y: e.clientY };
        isDraggingRef.current = false;
    };

    const handleMouseMove = (e: React.MouseEvent) => {
        if (!dragStartRef.current) return;
        const dx = e.clientX - dragStartRef.current.x;
        const dy = e.clientY - dragStartRef.current.y;
        const distance = Math.sqrt(dx * dx + dy * dy);

        if (distance > 5) {
            isDraggingRef.current = true;
            getCurrentWindow().startDragging();
            dragStartRef.current = null; // Stop tracking once drag starts
        }
    };

    const handleMouseUp = (e: React.MouseEvent) => {
        // If mouse didn't move enough to start drag, treat as click
        // In new design, clicking the pill background no longer expands.
        // Expansion is handled by the explicit expand button.
        dragStartRef.current = null;
        isDraggingRef.current = false;
    };

    return (
        <div
            className="w-full h-full flex items-center justify-center"
            style={{ backgroundColor: 'transparent' }}
        >
            {!isExpanded ? (
                /* --- PILL STATE --- */
                <div
                    className="bg-white/60 backdrop-blur-xl text-gray-900 rounded-full w-[140px] h-[40px] flex items-center justify-between px-4 gap-2 cursor-pointer shadow-md transition-all border border-white/40"
                    onMouseDown={handleMouseDown}
                    onMouseMove={handleMouseMove}
                    onMouseUp={handleMouseUp}
                    onMouseLeave={() => { setIsHovered(false); dragStartRef.current = null; }}
                    onMouseEnter={() => setIsHovered(true)}
                >
                    {/* Left: Always Mic Icon */}
                    <span className="text-lg pointer-events-none">🎙️</span>

                    {/* Right: Content switches on hover */}
                    {!isHovered ? (
                        /* Default: Mini Visualizer */
                        <div className="flex items-center justify-center gap-[2px] h-4 w-full ml-2 pointer-events-none">
                            {Array.from({ length: 12 }).map((_, i) => {
                                const value = audioData[i * 2] || 0; // Skip every other bin for wider view
                                const height = isRecording
                                    ? Math.max(20, (value / 255) * 100)
                                    : 20 + Math.sin(i * 0.8) * 15;
                                return (
                                    <div
                                        key={i}
                                        className={`w-1 rounded-full transition-all duration-75 ${isRecording ? 'bg-red-500' : 'bg-gray-300'}`}
                                        style={{ height: `${height}%` }}
                                    />
                                );
                            })}
                        </div>
                    ) : (
                        /* Hover: Buttons */
                        <div className="flex items-center gap-2 animate-fadeIn">
                            {/* Record Button */}
                            <button
                                className={`p-1.5 rounded-full hover:bg-gray-100 transition-colors ${isRecording ? 'text-red-500' : 'text-gray-600'}`}
                                onClick={(e) => { e.stopPropagation(); isRecording ? stopRecording() : startRecording(); }}
                                title={isRecording ? "Stop Recording (F2)" : "Start Recording (F2)"}
                            >
                                {isRecording ? (
                                    <div className="w-3 h-3 bg-current rounded-sm" />
                                ) : (
                                    <div className="w-3 h-3 bg-current rounded-full" />
                                )}
                            </button>

                            {/* Expand Button */}
                            <button
                                className="p-1.5 rounded-full hover:bg-gray-100 transition-colors text-gray-600 hover:text-gray-900"
                                onClick={(e) => { e.stopPropagation(); setIsExpanded(true); }}
                                title="Expand Widget"
                            >
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
                                    <polyline points="15 3 21 3 21 9"></polyline>
                                    <polyline points="9 21 3 21 3 15"></polyline>
                                    <line x1="21" y1="3" x2="14" y2="10"></line>
                                    <line x1="3" y1="21" x2="10" y2="14"></line>
                                </svg>
                            </button>
                        </div>
                    )}
                </div>
            ) : (
                /* --- EXPANDED STATE (New Design) --- */
                <div
                    className="bg-white rounded-2xl shadow-xl w-full h-full flex flex-col px-6 py-4"
                    data-tauri-drag-region
                >
                    {/* Top Section: Waveform (Fixed Height) - Reduced Opacity */}
                    <div className="w-full h-12 flex items-center justify-between gap-[2px] mb-4 opacity-40 pointer-events-none select-none">
                        {Array.from({ length: 40 }).map((_, i) => {
                            // Scale 0-255 byte data to percentage height (10% to 100%)
                            const value = audioData[i] || 0;
                            const height = isRecording
                                ? Math.max(15, (value / 255) * 100)
                                : 20 + Math.sin(i * 0.3) * 10; // Idle wave

                            return (
                                <div
                                    key={i}
                                    className={`w-1.5 rounded-full transition-all duration-75 ${isRecording ? 'bg-indigo-500' : 'bg-gray-300'
                                        }`}
                                    style={{ height: `${height}%` }}
                                />
                            );
                        })}
                    </div>

                    {/* Bottom Section: Controls Row */}
                    <div className="flex items-center justify-between w-full" data-tauri-drag-region>

                        {/* Left: Mic Info */}
                        <div className="flex items-center gap-3">
                            <span className="text-xl">🎙️</span>
                            <div className="flex flex-col items-start">
                                <span className="text-xs font-bold text-gray-500 uppercase tracking-wider mb-0.5">Microphone</span>
                                {/* Microphone Dropdown */}
                                <select
                                    value={selectedMic}
                                    onChange={(e) => setSelectedMic(e.target.value)}
                                    className="text-sm font-semibold text-gray-900 bg-transparent border-none outline-none cursor-pointer hover:bg-gray-100 rounded px-1 -ml-1 transition-colors max-w-[150px] truncate appearance-none"
                                    title="Select Microphone"
                                >
                                    {devices.map(device => (
                                        <option key={device.deviceId} value={device.deviceId}>
                                            {device.label}
                                        </option>
                                    ))}
                                    {devices.length === 0 && <option value="">Default Mic</option>}
                                </select>
                            </div>
                        </div>

                        {/* Right: Actions */}
                        <div className="flex items-center gap-4">
                            {/* Record/Stop Button */}
                            <button
                                className={`flex flex-col items-center justify-center px-4 py-1.5 rounded-lg transition-all ${isRecording ? 'bg-red-50 text-red-600' : 'bg-gray-50 text-gray-700 hover:bg-gray-100'
                                    }`}
                                onClick={(e) => { e.stopPropagation(); isRecording ? stopRecording() : startRecording(); }}
                            >
                                <span className="text-sm font-bold">{isRecording ? "Stop" : "Record"}</span>
                                <span className="text-[10px] opacity-60 font-mono">F2</span>
                            </button>

                            {/* Cancel Button */}
                            <button
                                className="flex flex-col items-center justify-center px-3 py-1.5 rounded-lg hover:bg-red-50 text-gray-500 hover:text-red-500 transition-all"
                                onClick={(e) => { e.stopPropagation(); setIsExpanded(false); }}
                            >
                                <span className="text-sm font-medium">Cancel</span>
                                <span className="text-[10px] opacity-60 font-mono">Esc</span>
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
