import React, { useState, useEffect } from 'react';
import { open } from '@tauri-apps/plugin-dialog';
import { appLocalDataDir, join } from '@tauri-apps/api/path';
import { Folder, RotateCcw, Check } from 'lucide-react';
import { twMerge } from 'tailwind-merge';

export const STORAGE_KEY_RECORDING_PATH = 'elite_whisper_recording_path';

interface SettingsViewProps {

}

export function SettingsView({ }: SettingsViewProps) {
    const [recordingPath, setRecordingPath] = useState<string | null>(null);
    const [defaultPath, setDefaultPath] = useState<string>("");

    useEffect(() => {
        // Load saved path
        const saved = localStorage.getItem(STORAGE_KEY_RECORDING_PATH);
        if (saved) {
            setRecordingPath(saved);
        }

        // Determine default path for display
        async function loadDefault() {
            const appData = await appLocalDataDir();
            const recDir = await join(appData, 'recorded_audio');
            setDefaultPath(recDir);
        }
        loadDefault();
    }, []);

    const handleBrowse = async () => {
        try {
            const selected = await open({
                directory: true,
                multiple: false,
                title: 'Select Recording Storage Folder',
            });

            if (selected && typeof selected === 'string') {
                setRecordingPath(selected);
                localStorage.setItem(STORAGE_KEY_RECORDING_PATH, selected);
            }
        } catch (error) {
            console.error("Failed to open dialog:", error);
        }
    };

    const handleReset = () => {
        localStorage.removeItem(STORAGE_KEY_RECORDING_PATH);
        setRecordingPath(null);
    };

    const currentEffectivePath = recordingPath || defaultPath;
    const isCustom = !!recordingPath;

    return (
        <div className="flex-1 flex flex-col h-full bg-gray-50 overflow-y-auto">
            {/* Header */}
            <div className="bg-white border-b border-gray-200 px-8 py-6 sticky top-0 z-10">
                <h1 className="text-2xl font-bold text-gray-800">Settings</h1>
                <p className="text-gray-500 mt-1">Manage your application preferences</p>
            </div>

            <div className="flex-1 p-8 max-w-3xl">

                {/* Storage Section */}
                <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden mb-6">
                    <div className="border-b border-gray-100 bg-gray-50/50 p-4 px-6 flex items-center justify-between">
                        <h2 className="font-semibold text-gray-800 flex items-center gap-2">
                            <Folder size={18} className="text-gray-500" />
                            Recording Storage
                        </h2>
                    </div>

                    <div className="p-6">
                        <p className="text-gray-600 mb-4 text-sm">
                            Choose where your audio recordings are saved on your computer.
                        </p>

                        <div className="flex flex-col gap-3">
                            <div className="flex items-center gap-3">
                                <div className="flex-1 bg-gray-100/50 border border-gray-200 rounded-lg px-4 py-3 text-sm font-mono text-gray-700 truncate select-all">
                                    {currentEffectivePath || "Loading..."}
                                </div>
                                <button
                                    onClick={handleBrowse}
                                    className="px-4 py-3 bg-white border border-gray-200 hover:border-gray-300 hover:bg-gray-50 text-gray-700 rounded-lg text-sm font-medium transition-colors shadow-sm"
                                >
                                    Change
                                </button>
                            </div>

                            {isCustom && (
                                <div className="flex items-center gap-2 mt-1">
                                    <button
                                        onClick={handleReset}
                                        className="text-xs text-red-500 hover:text-red-600 flex items-center gap-1 font-medium px-1"
                                    >
                                        <RotateCcw size={12} />
                                        Reset to default location
                                    </button>
                                    <span className="text-xs text-green-600 flex items-center gap-1 ml-auto">
                                        <Check size={12} />
                                        Custom location active
                                    </span>
                                </div>
                            )}

                            {!isCustom && currentEffectivePath && (
                                <p className="text-xs text-gray-400 mt-1">
                                    Using default application storage.
                                </p>
                            )}
                        </div>
                    </div>
                </div>

                {/* Other settings placeholders could go here */}

            </div>
        </div>
    );
}
