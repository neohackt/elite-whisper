import { useState, useEffect } from 'react';
import { Download, Check, Zap, Activity, Award, Trash2, MonitorSmartphone } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { exists, BaseDirectory, mkdir, remove } from '@tauri-apps/plugin-fs';
import { appLocalDataDir, join } from '@tauri-apps/api/path';
import { AI_PROFILES, AIProfile } from '../config/profiles';
import { AVAILABLE_MODELS, AIModel } from '../config/models';

interface ProfileCardProps {
    profile: AIProfile;
    isActive: boolean;
    hasRequiredModel: boolean;
    isDownloading: boolean;
    progress: number;
    onSelect: () => void;
    onDownload: () => void;
    onDelete: () => void;
}

const ProfileCard = ({
    profile,
    isActive,
    hasRequiredModel,
    isDownloading,
    progress,
    onSelect,
    onDownload,
    onDelete
}: ProfileCardProps) => {

    // Resolve icon
    const Icon = {
        'zap': Zap,
        'activity': Activity,
        'check': Check,
        'award': Award
    }[profile.icon] || Zap;

    // Get model details for stats
    const model = AVAILABLE_MODELS.find(m => m.id === profile.primaryModelId);
    const speed = model ? model.speed : 0;
    const accuracy = model ? model.accuracy : 0;

    return (
        <div
            className={`relative overflow-hidden rounded-2xl border transition-all duration-300
                ${isActive
                    ? 'border-indigo-500 bg-indigo-50/60 shadow-md ring-1 ring-indigo-500/20'
                    : 'border-gray-200 bg-white hover:border-indigo-300 hover:shadow-lg'
                }
            `}
        >
            <div className="flex flex-col md:flex-row items-center p-6 gap-6">

                {/* Icon & Status */}
                <div className={`w-16 h-16 rounded-2xl flex-shrink-0 flex items-center justify-center transition-colors
                     ${isActive ? 'bg-indigo-600 text-white shadow-indigo-200 shadow-lg' : 'bg-gray-100 text-gray-500'}
                `}>
                    <Icon size={32} strokeWidth={2} />
                </div>

                {/* Main Content */}
                <div className="flex-1 min-w-0 text-center md:text-left">
                    <div className="flex items-center justify-center md:justify-start gap-3 mb-1">
                        <h3 className={`font-bold text-lg ${isActive ? 'text-indigo-900' : 'text-gray-800'}`}>
                            {profile.label}
                        </h3>
                        {isActive && (
                            <span className="flex items-center gap-1 bg-green-100 text-green-700 px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wide">
                                <Check size={10} strokeWidth={3} /> Active
                            </span>
                        )}
                    </div>

                    <p className="text-sm text-gray-500 leading-relaxed max-w-lg mx-auto md:mx-0">
                        {profile.description}
                    </p>
                </div>

                {/* Performance Stats */}
                <div className="flex flex-col gap-2 w-full md:w-48 flex-shrink-0 px-4 md:border-l border-gray-100">
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

                {/* Actions */}
                <div className="flex items-center gap-3 w-full md:w-auto justify-end">
                    {/* Delete Option (only if downloaded and not active) */}
                    {hasRequiredModel && !isActive && (
                        <button
                            onClick={(e) => { e.stopPropagation(); onDelete(); }}
                            className="p-2.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-xl transition-all"
                            title="Delete Cached Model"
                        >
                            <Trash2 size={18} />
                        </button>
                    )}

                    {isDownloading ? (
                        <div className="w-[140px] bg-gray-100 rounded-xl h-10 flex items-center px-3 relative overflow-hidden">
                            <div className="absolute left-0 top-0 bottom-0 bg-indigo-100 transition-all duration-300" style={{ width: `${progress}%` }} />
                            <span className="relative z-10 text-xs font-bold text-indigo-700 mx-auto flex items-center gap-2">
                                <span className="w-3 h-3 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin" />
                                {progress}%
                            </span>
                        </div>
                    ) : hasRequiredModel ? (
                        <button
                            onClick={onSelect}
                            disabled={isActive}
                            className={`px-6 py-2.5 rounded-xl text-sm font-bold transition-all w-[140px]
                                ${isActive
                                    ? 'bg-indigo-100 text-indigo-700 cursor-default opacity-80'
                                    : 'bg-indigo-600 text-white hover:bg-indigo-700 shadow-md hover:shadow-lg active:scale-95'
                                }
                            `}
                        >
                            {isActive ? 'Current' : 'Activate'}
                        </button>
                    ) : (
                        <button
                            onClick={onDownload}
                            className="px-6 py-2.5 bg-white border border-gray-200 text-gray-700 hover:bg-gray-50 hover:border-gray-300 rounded-xl text-sm font-bold transition-all shadow-sm active:scale-95 flex items-center justify-center gap-2 w-[140px]"
                        >
                            <Download size={14} />
                            Download
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
};

interface ModelsViewProps {
    isAutoMode: boolean;
    setAutoMode: (active: boolean) => void;
}

export function ModelsView({ isAutoMode, setAutoMode }: ModelsViewProps) {
    // State
    const [downloadedModels, setDownloadedModels] = useState<Set<string>>(new Set());
    const [downloadingModelId, setDownloadingModelId] = useState<string | null>(null);
    const [progress, setProgress] = useState<number>(0);

    const [activeProfileId, setActiveProfileId] = useState<string>('balanced');
    const [error, setError] = useState<string | null>(null);

    // Initial Load
    useEffect(() => {
        checkDownloadedModels();

        // 1. Check local storage for active profile
        const savedProfile = localStorage.getItem('elite_whisper_active_profile');
        if (savedProfile) {
            setActiveProfileId(savedProfile);
        }
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

    const getModelById = (id: string): AIModel | undefined => {
        return AVAILABLE_MODELS.find(m => m.id === id);
    };

    const handleDownload = async (modelId: string) => {
        const model = getModelById(modelId);
        if (!model) {
            setError("Model definition not found for this profile.");
            return;
        }

        try {
            setDownloadingModelId(model.id);
            setProgress(0);
            setError(null);

            await mkdir('models', { baseDir: BaseDirectory.AppLocalData, recursive: true });

            // Set up progress listener
            const unlisten = await listen<any>('download-progress', (event) => {
                if (model.type === 'sherpa') {
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
                await mkdir(`models/${folder}`, { baseDir: BaseDirectory.AppLocalData, recursive: true });

                const fileList = Object.entries(model.files);
                for (const [fname, url] of fileList) {
                    setProgress(0);
                    await invoke('cmd_download_file', {
                        url: url,
                        filename: `${folder}/${fname}`
                    });
                }
            }

            unlisten();
            setDownloadedModels(prev => new Set(prev).add(model.id));

        } catch (err) {
            console.error("Download failed:", err);
            setError(`Failed to download: ${err}`);
        } finally {
            setDownloadingModelId(null);
            setProgress(0);
        }
    };

    const handleSelectProfile = async (profileId: string) => {
        const profile = AI_PROFILES.find(p => p.id === profileId);
        if (!profile) return;

        const model = getModelById(profile.primaryModelId);
        if (!model) return;

        // Check if downloaded
        if (!downloadedModels.has(model.id)) {
            handleDownload(model.id);
            return;
        }

        try {
            const appData = await appLocalDataDir();
            let modelPath = await join(appData, 'models', model.filename);

            await invoke('cmd_load_model', { modelPath });

            // Success
            setActiveProfileId(profileId);
            localStorage.setItem('elite_whisper_active_profile', profileId);
            localStorage.setItem('elite_whisper_active_model', model.filename);

        } catch (err) {
            console.error("Failed to load model:", err);
            setError(`Failed to switch profile: ${err}`);
        }
    };

    const handleDeleteModel = async (profileId: string) => {
        const profile = AI_PROFILES.find(p => p.id === profileId);
        if (!profile) return;
        const model = getModelById(profile.primaryModelId);
        if (!model) return;

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
            setError(`Delete failed: ${err}`);
        }
    }

    return (
        <div className="flex-1 flex flex-col h-full bg-gray-50 overflow-y-auto">
            {/* Header */}
            <div className="bg-white border-b border-gray-200 px-8 py-6 sticky top-0 z-10 shadow-sm">
                <div className="flex justify-between items-center">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
                            <MonitorSmartphone size={24} className="text-indigo-600" />
                            AI Profiles
                        </h1>
                        <p className="text-gray-500 mt-1 text-sm">Select an optimization profile. The app handles the technical details.</p>
                    </div>

                    {/* Functional Auto Mode Toggle */}
                    <div className="flex items-center gap-1 bg-gray-100 p-1 rounded-lg border border-gray-200">
                        <button
                            onClick={() => setAutoMode(false)}
                            className={`px-4 py-2 text-sm font-bold rounded-md transition-all
                                ${!isAutoMode
                                    ? 'bg-white text-indigo-600 shadow-sm'
                                    : 'text-gray-500 hover:text-gray-700'
                                }
                            `}
                        >
                            Manual
                        </button>
                        <button
                            onClick={() => setAutoMode(true)}
                            className={`px-4 py-2 text-sm font-bold rounded-md transition-all
                                ${isAutoMode
                                    ? 'bg-white text-indigo-600 shadow-sm'
                                    : 'text-gray-500 hover:text-gray-700'
                                }
                            `}
                        >
                            Auto Mode
                        </button>
                    </div>
                </div>
            </div>

            <div className="flex-1 p-8 max-w-5xl mx-auto w-full">
                {error && (
                    <div className="mb-8 bg-red-50 border border-red-200 text-red-600 px-4 py-3 rounded-xl text-sm font-medium flex items-center gap-2">
                        <span className="text-xl">!</span> {error}
                    </div>
                )}

                {isAutoMode && (
                    <div className="mb-6 bg-blue-50 border border-blue-200 text-blue-700 px-4 py-3 rounded-xl text-sm flex items-start gap-3">
                        <div className="bg-blue-100 p-1 rounded-full text-blue-600 mt-0.5">
                            <Zap size={14} />
                        </div>
                        <div>
                            <p className="font-bold">Auto Mode is Active</p>
                            <p className="opacity-90 mt-0.5">
                                For the best experience, ensure you have downloaded models for different profiles (e.g., <strong>Ultra Fast</strong> and <strong>High Accuracy</strong>). The app will automatically switch between them based on your recording length.
                            </p>
                        </div>
                    </div>
                )}

                {/* Profiles List (Horizontal Cards) */}
                <div className="flex flex-col gap-4 mb-10">
                    {AI_PROFILES.map(profile => {
                        const requiredModelId = profile.primaryModelId;
                        const isDownloaded = downloadedModels.has(requiredModelId);

                        return (
                            <ProfileCard
                                key={profile.id}
                                profile={profile}
                                isActive={activeProfileId === profile.id}
                                hasRequiredModel={isDownloaded}
                                isDownloading={downloadingModelId === requiredModelId}
                                progress={progress}
                                onSelect={() => handleSelectProfile(profile.id)}
                                onDownload={() => handleDownload(requiredModelId)}
                                onDelete={() => handleDeleteModel(profile.id)}
                            />
                        );
                    })}
                </div>
            </div>
        </div>
    );
}
