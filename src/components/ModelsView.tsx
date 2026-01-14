import { useState, useEffect } from 'react';
import { Download, Check, Cpu, Brain, Activity, Zap, Trash2 } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { exists, BaseDirectory, mkdir, remove } from '@tauri-apps/plugin-fs';
import { appLocalDataDir, join } from '@tauri-apps/api/path';

interface ModelCardProps {
    id: string;
    name: string;
    description: string;
    language: string;
    size: string;
    speed: number;
    accuracy: number;
    url?: string;
    files?: any;
    filename: string;
    type: 'whisper' | 'sherpa';
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
                    {/* Delete Icon */}
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
        type: 'whisper' as const,
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
        type: 'whisper' as const,
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
        type: 'whisper' as const,
        name: 'Whisper Small',
        description: 'Higher accuracy model for professional work. Slightly slower processing.',
        language: 'English',
        size: '466 MB',
        speed: 60,
        accuracy: 92,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin',
        filename: 'ggml-small.en.bin'
    },
    {
        id: 'parakeet-0.6b-v2',
        type: 'sherpa' as const,
        name: 'Parakeet 0.6B v2',
        description: 'High-accuracy streaming-ready RNN-T model by NVIDIA. Extremely fast inference.',
        language: 'English',
        size: '~660 MB',
        speed: 95,
        accuracy: 94,
        filename: 'parakeet-0.6b-v2',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/decoder.int8.onnx',
            'joiner.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/joiner.int8.onnx',
        }
    },
    {
        id: 'parakeet-0.6b-v3',
        type: 'sherpa' as const,
        name: 'Parakeet 0.6B v3',
        description: 'Improved version with better multilingual support and timestamp accuracy.',
        language: 'English+',
        size: '~660 MB',
        speed: 92,
        accuracy: 96,
        filename: 'parakeet-0.6b-v3',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/decoder.int8.onnx',
            'joiner.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/joiner.int8.onnx',
        }
    },
    {
        id: 'distil-whisper-large-v3',
        type: 'sherpa' as const,
        name: 'Distil-Whisper Large v3',
        description: 'Distilled Version of Whisper Large v3. Optimized for Sherpa-ONNX. Fast and accurate.',
        language: 'English',
        size: '~1.1 GB', // encoder 668MB + decoder 315MB + weights
        speed: 90,
        accuracy: 95,
        filename: 'distil-whisper-large-v3',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-decoder.int8.onnx',
        }
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
        invoke<string>('get_current_model').then(_name => {
            // We can't easily map back to ID from name without more complex logic, 
            // but activeModel state will track standard usage.
        });
    }, []);

    const checkDownloadedModels = async () => {
        try {
            await mkdir('models', { baseDir: BaseDirectory.AppLocalData, recursive: true });
        } catch (e) { }

        const found = new Set<string>();
        for (const model of AVAILABLE_MODELS) {
            try {
                if (model.type === 'whisper') {
                    const existsResult = await exists(`models/${model.filename}`, { baseDir: BaseDirectory.AppLocalData });
                    if (existsResult) found.add(model.id);
                } else {
                    const existsResult = await exists(`models/${model.filename}/tokens.txt`, { baseDir: BaseDirectory.AppLocalData });
                    if (existsResult) found.add(model.id);
                }
            } catch (e) { }
        }
        setDownloadedModels(found);
    };

    const handleDownload = async (model: typeof AVAILABLE_MODELS[0]) => {
        try {
            setDownloading(model.id);
            setProgress(0);
            setError(null);

            await mkdir('models', { baseDir: BaseDirectory.AppLocalData, recursive: true });

            // Set up progress listener
            const unlisten = await listen<any>('download-progress', (event) => {
                // Check if this progress event matches our current download
                // Simple check: if filename is part of what we are downloading
                if (model.type === 'sherpa') {
                    // For sherpa, we might get progress for subfiles. 
                    // We roughly map individual file progress to total progress or just show current file progress.
                    // For simplicity in this crash-fix, we'll rely on the backend emission.
                    // However, handling multiple files means 'progress' from backend is 0-100 per file.
                    // We can just show that, or try to aggregate.
                    // The shared logic below will just show whatever comes in.
                    if (event.payload.filename.includes(model.filename)) {
                        setProgress(event.payload.progress);
                    }
                } else {
                    if (event.payload.filename === model.filename) {
                        setProgress(event.payload.progress);
                    }
                }
            });

            if (model.type === 'whisper' && model.url) {
                await invoke('cmd_download_file', {
                    url: model.url,
                    filename: model.filename
                });
            } else if (model.type === 'sherpa' && model.files) {
                const folder = model.filename;
                // Create folder first to be safe, though backend does it too
                await mkdir(`models/${folder}`, { baseDir: BaseDirectory.AppLocalData, recursive: true });

                const fileList = Object.entries(model.files);
                // const totalFiles = fileList.length;
                let completedFiles = 0;

                for (const [fname, url] of fileList) {
                    // Reset progress for next file (visual feedback)
                    setProgress(0);

                    await invoke('cmd_download_file', {
                        url: url,
                        filename: `${folder}/${fname}`
                    });

                    completedFiles++;
                    // Optional: Update overall progress if we want a "Total" bar
                    // setProgress(Math.round((completedFiles / totalFiles) * 100));
                }
            }

            unlisten();
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
            let modelPath = await join(appData, 'models', model.filename);

            await invoke('cmd_load_model', { modelPath });
            setActiveModel(model.filename);
        } catch (err) {
            console.error("Failed to load model:", err);
            setError(`Failed to load model: ${err}`);
        }
    };

    const handleDelete = async (model: typeof AVAILABLE_MODELS[0]) => {
        try {
            if (model.type === 'whisper') {
                await remove(`models/${model.filename}`, { baseDir: BaseDirectory.AppLocalData });
            } else {
                await remove(`models/${model.filename}`, { baseDir: BaseDirectory.AppLocalData, recursive: true });
            }

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
                        <p className="text-gray-500 mt-1">Manage and switch between local speech recognition models (Whisper & Parakeet)</p>
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

                    <div className="border border-dashed border-gray-300 rounded-2xl p-8 flex items-center justify-center text-center bg-gray-50/50">
                        <div className="flex flex-col items-center gap-2">
                            <div className="w-10 h-10 bg-gray-100 rounded-full flex items-center justify-center text-gray-400">
                                <Cpu size={20} />
                            </div>
                            <h3 className="font-bold text-gray-600">More Models Coming Soon</h3>
                            <p className="text-sm text-gray-400">
                                We are actively optimizing more models for local execution.
                            </p>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}
