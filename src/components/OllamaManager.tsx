import { useState, useEffect } from 'react';
import { RefreshCw, Download, Trash2, HardDrive, Check, AlertTriangle, Terminal, Play, Folder } from 'lucide-react';
import { Command } from '@tauri-apps/plugin-shell';
import { open } from '@tauri-apps/plugin-dialog';

interface OllamaModel {
    name: string;
    size: number;
    digest: string;
    details: {
        format: string;
        family: string;
        families: string[];
        parameter_size: string;
        quantization_level: string;
    };
}

interface RecommendedModel {
    id: string;
    name: string;
    description: string;
    size: string;
    params: string;
}

const RECOMMENDED_MODELS: RecommendedModel[] = [
    { id: 'llama3.2', name: 'Llama 3.2 (3B)', description: 'Fast, efficient, great for general tasks.', size: '2.0 GB', params: '3B' },
    { id: 'deepseek-r1-distill-llama-8b', name: 'DeepSeek R1 (8B)', description: 'State-of-the-art reasoning.', size: '4.7 GB', params: '8B' },
    { id: 'phi3.5', name: 'Phi 3.5 Mini', description: 'Microsoft High capability small model.', size: '2.2 GB', params: '3.8B' },
    { id: 'mistral', name: 'Mistral v0.3', description: 'Reliable all-rounder.', size: '4.1 GB', params: '7B' }
];

export function OllamaManager() {
    const [status, setStatus] = useState<'checking' | 'running' | 'stopped'>('checking');
    const [localModels, setLocalModels] = useState<OllamaModel[]>([]);
    const [pullProgress, setPullProgress] = useState<{ [key: string]: number }>({}); // modelId -> percent
    const [pullStatus, setPullStatus] = useState<{ [key: string]: string }>({}); // modelId -> status text
    const [error, setError] = useState<string | null>(null);
    const [modelPath, setModelPath] = useState<string>('');
    const [applyingPath, setApplyingPath] = useState(false);

    useEffect(() => {
        checkStatus();
        fetchModels();
        // Try to check environment variable for path (tricky from browser, might need Rust cmd)
        // For MVP we just let them set it.
    }, []);

    const checkStatus = async () => {
        try {
            const res = await fetch('http://localhost:11434/api/version');
            if (res.ok) {
                setStatus('running');
            } else {
                setStatus('stopped');
            }
        } catch (e) {
            setStatus('stopped');
        }
    };

    const fetchModels = async () => {
        try {
            const res = await fetch('http://localhost:11434/api/tags');
            if (res.ok) {
                const data = await res.json();
                setLocalModels(data.models || []);
            }
        } catch (e) {
            console.error("Failed to fetch models", e);
        }
    };

    const handlePull = async (modelId: string) => {
        setPullStatus(prev => ({ ...prev, [modelId]: 'Starting download...' }));
        setError(null);

        try {
            const response = await fetch('http://localhost:11434/api/pull', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: modelId, stream: true })
            });

            if (!response.body) throw new Error("No response body");

            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                const chunk = decoder.decode(value, { stream: true });
                // Ollama sends multiple JSON objects in one chunk sometimes
                const lines = chunk.split('\n').filter(l => l.trim() !== '');

                for (const line of lines) {
                    try {
                        const data = JSON.parse(line);

                        if (data.error) {
                            throw new Error(data.error);
                        }

                        if (data.status === 'success') {
                            setPullStatus(prev => ({ ...prev, [modelId]: 'Completed' }));
                            setPullProgress(prev => ({ ...prev, [modelId]: 100 }));
                            fetchModels(); // Refresh list
                        } else if (data.total && data.completed) {
                            const percent = Math.round((data.completed / data.total) * 100);
                            setPullProgress(prev => ({ ...prev, [modelId]: percent }));
                            setPullStatus(prev => ({ ...prev, [modelId]: data.status }));
                        } else {
                            setPullStatus(prev => ({ ...prev, [modelId]: data.status }));
                        }
                    } catch (e) {
                        // ignore parse errors for partial chunks
                    }
                }
            }

        } catch (err: any) {
            setPullStatus(prev => ({ ...prev, [modelId]: 'Failed' }));
            setError(`Download failed: ${err.message}`);
        }
    };

    const handleDelete = async (modelName: string) => {
        if (!confirm(`Are you sure you want to delete ${modelName}?`)) return;

        try {
            const res = await fetch('http://localhost:11434/api/delete', {
                method: 'DELETE',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: modelName })
            });
            if (res.ok) {
                fetchModels();
            } else {
                setError("Failed to delete model");
            }
        } catch (e) {
            setError("Failed to delete model");
        }
    };

    const handleSetStoragePath = async () => {
        try {
            const selected = await open({
                directory: true,
                multiple: false,
                title: 'Select Ollama Models Folder',
            });

            if (selected && typeof selected === 'string') {
                setModelPath(selected);
                setApplyingPath(true);

                // Use setx to set User environment variable
                const output = await Command.create('cmd', ['/C', 'setx', 'OLLAMA_MODELS', selected]).execute();

                if (output.code === 0) {
                    alert("Path updated successfully!\n\nIMPORTANT: You must restart the Ollama application (Quit from tray icon) for this to take effect.");
                } else {
                    setError("Failed to set environment variable: " + output.stderr);
                }
                setApplyingPath(false);
            }
        } catch (e: any) {
            setError("Failed to set path: " + e);
            setApplyingPath(false);
        }
    };

    // Select active model for the app
    const selectActiveModel = (modelName: string) => {
        localStorage.setItem('elite_whisper_local_model', modelName);
        localStorage.setItem('elite_whisper_local_url', 'http://localhost:11434/v1');
        window.dispatchEvent(new Event('storage')); // Notify other components if they listen
        alert(`Selected ${modelName} as the active offline model.`);
    };

    return (
        <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden mt-8">
            <div className="border-b border-gray-100 bg-gray-50/50 p-4 px-6 flex items-center justify-between">
                <h2 className="font-semibold text-gray-800 flex items-center gap-2">
                    <Terminal size={18} className="text-gray-500" />
                    Ollama Manager (Offline AI)
                </h2>

                <div className="flex items-center gap-2">
                    <button onClick={() => { checkStatus(); fetchModels(); }} className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-100 transition-colors" title="Refresh">
                        <RefreshCw size={16} />
                    </button>
                    <div className={`flex items-center gap-2 px-3 py-1 rounded-full text-xs font-bold ${status === 'running' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                        <div className={`w-2 h-2 rounded-full ${status === 'running' ? 'bg-green-500' : 'bg-red-500'}`} />
                        {status === 'running' ? 'Service Running' : 'Service Stopped'}
                    </div>
                </div>
            </div>

            <div className="p-6">
                {/* Connection Guide / Error */}
                {status === 'stopped' && (
                    <div className="mb-6 bg-amber-50 border border-amber-200 text-amber-800 px-4 py-3 rounded-xl text-sm flex items-start gap-3">
                        <AlertTriangle size={18} className="text-amber-600 mt-0.5" />
                        <div>
                            <p className="font-bold">Ollama is not running</p>
                            <p className="mt-1">
                                Please download and install Ollama from <a href="https://ollama.com" target="_blank" className="underline font-bold">ollama.com</a>.
                                Once installed, ensure it is running in your system tray.
                            </p>
                        </div>
                    </div>
                )}

                {error && (
                    <div className="mb-6 bg-red-50 border border-red-200 text-red-600 px-4 py-3 rounded-xl text-sm">
                        {error}
                    </div>
                )}

                {/* --- STORAGE PATH CONFIG --- */}
                <div className="mb-8 p-4 bg-gray-50 rounded-lg border border-gray-200">
                    <h3 className="text-sm font-bold text-gray-700 mb-2 flex items-center gap-2">
                        <HardDrive size={16} /> Model Storage Path
                    </h3>
                    <p className="text-xs text-gray-500 mb-3">
                        Large models can take up significant space. Change the storage location to a different drive (e.g., D:).
                    </p>
                    <div className="flex gap-2">
                        <input
                            type="text"
                            value={modelPath}
                            readOnly
                            placeholder="Default (C:\Users\You\.ollama\models)"
                            className="flex-1 px-3 py-2 bg-white border border-gray-300 rounded-lg text-sm text-gray-600"
                        />
                        <button
                            onClick={handleSetStoragePath}
                            disabled={applyingPath}
                            className="px-4 py-2 bg-white border border-gray-300 hover:bg-gray-50 text-gray-700 font-medium rounded-lg text-sm flex items-center gap-2 shadow-sm"
                        >
                            {applyingPath ? <RefreshCw className="animate-spin" size={14} /> : <Folder size={14} />}
                            Change Location
                        </button>
                    </div>
                </div>


                {/* --- RECOMMENDED MODELS --- */}
                <h3 className="text-sm font-bold text-gray-900 mb-4">Recommended Models</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-2 gap-4 mb-8">
                    {RECOMMENDED_MODELS.map(model => {
                        const isInstalled = localModels.some(m => m.name.startsWith(model.id) || m.name === model.id);
                        const progress = pullProgress[model.id] || 0;
                        const statusText = pullStatus[model.id];

                        return (
                            <div key={model.id} className="border border-gray-200 rounded-xl p-4 hover:border-indigo-200 transition-all bg-white">
                                <div className="flex justify-between items-start mb-2">
                                    <div>
                                        <h4 className="font-bold text-gray-800">{model.name}</h4>
                                        <p className="text-xs text-gray-500">{model.params} • {model.size}</p>
                                    </div>
                                    {isInstalled ? (
                                        <span className="bg-green-100 text-green-700 p-1.5 rounded-lg">
                                            <Check size={16} />
                                        </span>
                                    ) : (
                                        <button
                                            onClick={() => handlePull(model.id)}
                                            disabled={!!statusText && statusText !== 'Failed'}
                                            className="bg-indigo-50 text-indigo-600 hover:bg-indigo-100 p-2 rounded-lg transition-colors"
                                            title="Download"
                                        >
                                            <Download size={18} />
                                        </button>
                                    )}
                                </div>
                                <p className="text-sm text-gray-600 mb-3">{model.description}</p>

                                {/* Progress Bar */}
                                {statusText && !isInstalled && (
                                    <div className="mt-2">
                                        <div className="flex justify-between text-xs text-gray-500 mb-1">
                                            <span>{statusText}</span>
                                            <span>{progress}%</span>
                                        </div>
                                        <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden">
                                            <div className="h-full bg-indigo-500 transition-all duration-300" style={{ width: `${progress}%` }} />
                                        </div>
                                    </div>
                                )}

                                {isInstalled && (
                                    <button
                                        onClick={() => selectActiveModel(model.id)}
                                        className="w-full mt-2 py-1.5 border border-indigo-200 text-indigo-600 text-xs font-bold rounded-lg hover:bg-indigo-50 transition-colors flex items-center justify-center gap-1"
                                    >
                                        <Play size={12} /> Use this Model
                                    </button>
                                )}
                            </div>
                        )
                    })}
                </div>

                {/* --- INSTALLED LIBRARY --- */}
                {localModels.length > 0 && (
                    <>
                        <h3 className="text-sm font-bold text-gray-900 mb-4">Installed Library</h3>
                        <div className="space-y-2">
                            {localModels.map(model => (
                                <div key={model.digest} className="flex items-center justify-between p-3 bg-gray-50 rounded-lg border border-gray-100">
                                    <div className="flex items-center gap-3">
                                        <div className="w-8 h-8 bg-gray-200 rounded-lg flex items-center justify-center text-gray-500 font-bold text-xs">
                                            IO
                                        </div>
                                        <div>
                                            <div className="font-bold text-gray-800 text-sm">{model.name}</div>
                                            <div className="text-xs text-gray-500">{(model.size / 1024 / 1024 / 1024).toFixed(2)} GB</div>
                                        </div>
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <button
                                            onClick={() => selectActiveModel(model.name)}
                                            className="px-3 py-1.5 text-xs font-bold text-indigo-600 bg-white border border-indigo-200 rounded-lg hover:bg-indigo-50"
                                        >
                                            Select
                                        </button>
                                        <button
                                            onClick={() => handleDelete(model.name)}
                                            className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                        >
                                            <Trash2 size={14} />
                                        </button>
                                    </div>
                                </div>
                            ))}
                        </div>
                    </>
                )}
            </div>
        </div>
    );
}
