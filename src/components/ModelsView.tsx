import { useState, useEffect } from 'react';
import { Download, Check, Cpu, Brain, Activity, Zap, Trash2 } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { exists, writeFile, BaseDirectory, mkdir, remove } from '@tauri-apps/plugin-fs';
import { appLocalDataDir, join } from '@tauri-apps/api/path';

interface ModelCardProps {
    id: string;
    name: string;
    description: string;
    language: string;
    size: string;
    speed: number;
    accuracy: number;
    url: string;
    filename: string;
    isActive: boolean;
    isDownloaded: boolean;
    onDownload: () => void;
    onSelect: () => void;
    onDelete: () => void;
    isDownloading: boolean;
    progress: number;
}

const ModelCard = ({ id, name, description, language, size, speed, accuracy, isActive, isDownloaded, onDownload, onSelect, onDelete, isDownloading, progress }: ModelCardProps) => {
    return (
        <div className={`relative overflow-hidden rounded-2xl border transition-all duration-300 ${isActive ? 'border-purple-500 bg-purple-50/40 shadow-sm' : 'border-gray-200 bg-white hover:border-purple-300 hover:shadow-md'}`}>
            <div className="flex items-center p-6 gap-6">
                {/* Icon */}
                <div className={`w-14 h-14 rounded-xl flex-shrink-0 flex items-center justify-center ${isActive ? 'bg-purple-100 text-purple-600' : 'bg-gray-100 text-gray-500'}`}>
                    {id.includes('whisper') ? <Cpu size={28} /> : <Brain size={28} />}
                </div>

                {/* Main Info */}
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-3 mb-1">
                        <h3 className="font-bold text-gray-900 text-lg truncate">{name}</h3>
                        {isActive && (
                            <span className="bg-purple-100 text-purple-700 px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wide flex items-center gap-1">
                                <Check size={10} strokeWidth={3} /> Active
                            </span>
                        )}
                    </div>

                    <div className="flex items-center gap-2 mb-2">
                        <span className="text-[10px] uppercase font-bold text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded border border-gray-200">{language}</span>
                        <span className="text-[10px] font-mono text-gray-400 bg-gray-50 px-1.5 py-0.5 rounded border border-gray-100">{size}</span>
                    </div>

                    <p className="text-sm text-gray-500 leading-relaxed max-w-xl">
                        {description}
                    </p>
                </div>

                {/* Stats */}
                <div className="flex flex-col gap-2 w-48 flex-shrink-0 px-4 border-l border-gray-100">
                    <div className="flex items-center gap-2 text-xs">
                        <div className="flex items-center gap-1.5 text-gray-500 w-20">
                            <Zap size={12} /> Speed
                        </div>
                        <div className="flex-1 h-1.5 bg-gray-100 rounded-full overflow-hidden">
                            <div className="h-full bg-blue-500 rounded-full" style={{ width: `${speed}%` }} />
                        </div>
                    </div>
                    <div className="flex items-center gap-2 text-xs">
                        <div className="flex items-center gap-1.5 text-gray-500 w-20">
                            <Activity size={12} /> Accuracy
                        </div>
                        <div className="flex-1 h-1.5 bg-gray-100 rounded-full overflow-hidden">
                            <div className="h-full bg-green-500 rounded-full" style={{ width: `${accuracy}%` }} />
                        </div>
                    </div>
                </div>

                {/* Action Button */}
                <div className="w-40 flex-shrink-0 flex justify-end items-center gap-2">
                    {/* Delete Icon - Only show if downloaded and not active */}
                    {isDownloaded && !isActive && (
                        <button
                            onClick={(e) => {
                                e.stopPropagation();
                                onDelete();
                            }}
                            className="p-2.5 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded-xl transition-all"
                            title="Delete Model"
                        >
                            <Trash2 size={18} />
                        </button>
                    )}

                    {/* 
                        States:
                        1. Active/In Use -> "In Use" (Green/Purple, Disabled)
                        2. Downloading -> Progress % (Disabled)
                        3. Downloaded -> "Activate" (Active color, Clickable to select)
                        4. Not Downloaded -> "Download" (Outline/White, Clickable)
                    */}

                    {isActive ? (
                        <button
                            disabled
                            className="flex-1 py-2.5 rounded-xl text-sm font-bold bg-green-100 text-green-700 cursor-default border border-transparent shadow-none"
                        >
                            In Use
                        </button>
                    ) : isDownloading ? (
                        <button
                            disabled
                            className="flex-1 py-2.5 bg-gray-50 border border-gray-200 text-gray-500 rounded-xl text-sm font-bold shadow-none cursor-wait flex items-center justify-center gap-2"
                        >
                            <div className="w-3 h-3 border-2 border-gray-400 border-t-transparent rounded-full animate-spin" />
                            <span>{progress > 0 ? `${progress}%` : 'Starting...'}</span>
                        </button>
                    ) : isDownloaded ? (
                        <button
                            onClick={onSelect}
                            className="flex-1 py-2.5 rounded-xl text-sm font-bold transition-all bg-gray-900 text-white hover:bg-gray-800 shadow-sm hover:shadow active:scale-95"
                        >
                            Activate
                        </button>
                    ) : (
                        <button
                            onClick={onDownload}
                            className="flex-1 py-2.5 bg-white border border-gray-200 text-gray-700 hover:bg-gray-50 hover:border-gray-300 rounded-xl text-sm font-semibold transition-all shadow-sm active:scale-95 flex items-center justify-center gap-2"
                        >
                            <Download size={14} />
                            <span>Download</span>
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
};

const AVAILABLE_MODELS = [
    {
        id: 'whisper-base-en',
        name: 'Whisper Base',
        description: 'Optimized for English transcription. Good balance of speed and accuracy.',
        language: 'English',
        size: '142 MB',
        speed: 85,
        accuracy: 80,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin',
        filename: 'ggml-base.en.bin'
    },
    {
        id: 'whisper-tiny-en',
        name: 'Whisper Tiny',
        description: 'Ultra-fast model for quick dictation. Lower accuracy but instant response.',
        language: 'English',
        size: '75 MB',
        speed: 98,
        accuracy: 65,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin',
        filename: 'ggml-tiny.en.bin'
    },
    {
        id: 'whisper-small-en',
        name: 'Whisper Small',
        description: 'Higher accuracy model for professional work. Slightly slower processing.',
        language: 'English',
        size: '466 MB',
        speed: 60,
        accuracy: 92,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin',
        filename: 'ggml-small.en.bin'
    }
];

export function ModelsView() {
    const [downloadedModels, setDownloadedModels] = useState<Set<string>>(new Set());
    const [downloading, setDownloading] = useState<string | null>(null);
    const [progress, setProgress] = useState<number>(0);
    const [activeModel, setActiveModel] = useState<string>('ggml-base.en.bin');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        checkDownloadedModels();
        // Here we could also fetch the currently active model from backend if we persist it there
    }, []);

    const checkDownloadedModels = async () => {
        try {
            await mkdir('models', { baseDir: BaseDirectory.AppLocalData, recursive: true });
        } catch (e) { }

        const found = new Set<string>();
        for (const model of AVAILABLE_MODELS) {
            try {
                // Just checking if we can write there basically implies exist check on Windows for now effectively or 
                // re-using the exists logic properly
                const existsResult = await exists(`models/${model.filename}`, { baseDir: BaseDirectory.AppLocalData });
                if (existsResult) found.add(model.id);
            } catch (e) { console.error(e); }
        }
        setDownloadedModels(found);
    };

    const handleDownload = async (model: typeof AVAILABLE_MODELS[0]) => {
        try {
            setDownloading(model.id);
            setProgress(0);
            setError(null);

            const response = await fetch(model.url);
            if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

            const contentLength = response.headers.get('content-length');
            const total = contentLength ? parseInt(contentLength, 10) : 0;
            let loaded = 0;

            const reader = response.body?.getReader();
            if (!reader) throw new Error("ReadableStream not supported");

            const chunks: Uint8Array[] = [];

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                if (value) {
                    chunks.push(value);
                    loaded += value.length;
                    if (total > 0) {
                        setProgress(Math.round((loaded / total) * 100));
                    }
                }
            }

            // Combine chunks
            const combined = new Uint8Array(loaded);
            let offset = 0;
            for (const chunk of chunks) {
                combined.set(chunk, offset);
                offset += chunk.length;
            }

            await mkdir('models', { baseDir: BaseDirectory.AppLocalData, recursive: true });
            await writeFile(`models/${model.filename}`, combined, { baseDir: BaseDirectory.AppLocalData });

            setDownloadedModels(prev => new Set(prev).add(model.id));
        } catch (err) {
            console.error("Download failed:", err);
            setError(`Failed to download ${model.name}: ${err}`);
        } finally {
            setDownloading(null);
            setProgress(0);
        }
    };

    const handleSelect = async (model: typeof AVAILABLE_MODELS[0]) => {
        try {
            const appData = await appLocalDataDir();
            const modelPath = await join(appData, 'models', model.filename);

            await invoke('cmd_load_model', { modelPath });
            setActiveModel(model.filename);
        } catch (err) {
            console.error("Failed to load model:", err);
            setError(`Failed to load model: ${err}`);
        }
    };

    const handleDelete = async (model: typeof AVAILABLE_MODELS[0]) => {
        try {
            await remove(`models/${model.filename}`, { baseDir: BaseDirectory.AppLocalData });

            setDownloadedModels(prev => {
                const next = new Set(prev);
                next.delete(model.id);
                return next;
            });
        } catch (err) {
            console.error("Delete failed:", err);
            setError(`Failed to delete ${model.name}: ${err}`);
        }
    };

    return (
        <div className="flex-1 flex flex-col h-full bg-gray-50 overflow-y-auto">
            <div className="bg-white border-b border-gray-200 px-8 py-6 sticky top-0 z-10">
                <div className="flex justify-between items-center">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-800">AI Models</h1>
                        <p className="text-gray-500 mt-1">Manage and switch between local speech recognition models</p>
                    </div>
                </div>
            </div>

            <div className="flex-1 p-8 max-w-5xl mx-auto w-full">
                {error && (
                    <div className="mb-6 bg-red-50 border border-red-200 text-red-600 px-4 py-3 rounded-xl text-sm font-medium">
                        {error}
                    </div>
                )}

                <div className="flex flex-col gap-4">
                    {AVAILABLE_MODELS.map(model => (
                        <ModelCard
                            key={model.id}
                            {...model}
                            progress={downloading === model.id ? progress : 0}
                            isActive={activeModel === model.filename}
                            isDownloaded={downloadedModels.has(model.id)}
                            isDownloading={downloading === model.id}
                            onDownload={() => handleDownload(model)}
                            onSelect={() => handleSelect(model)}
                            onDelete={() => handleDelete(model)}
                        />
                    ))}

                    {/* Placeholder for future cloud models */}
                    <div className="border border-dashed border-gray-300 rounded-2xl p-8 flex items-center justify-center text-center bg-gray-50/50">
                        <div className="flex flex-col items-center gap-2">
                            <div className="w-10 h-10 bg-gray-100 rounded-full flex items-center justify-center text-gray-400">
                                <Cpu size={20} />
                            </div>
                            <h3 className="font-bold text-gray-600">More Models Coming Soon</h3>
                            <p className="text-sm text-gray-400">
                                We are optimizing Parakeet and S1 for local execution.
                            </p>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
