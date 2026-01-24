import { useState, useEffect } from 'react';
import { open } from '@tauri-apps/plugin-dialog';
import { appLocalDataDir, join } from '@tauri-apps/api/path';
import { Folder, RotateCcw, Check, Key, Eye, EyeOff, Globe } from 'lucide-react';
import { OllamaManager } from './OllamaManager';

const APIKeyInput = ({ label, placeholder, storageKey, type = "password" }: { label: string, placeholder: string, storageKey: string, type?: string }) => {
    const [value, setValue] = useState('');
    const [show, setShow] = useState(false);

    useEffect(() => {
        const saved = localStorage.getItem(storageKey);
        if (saved) setValue(saved);
    }, [storageKey]);

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const newVal = e.target.value;
        setValue(newVal);
        localStorage.setItem(storageKey, newVal);
    };

    return (
        <div className="mb-4 last:mb-0">
            <label className="block text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1.5 ml-1">{label}</label>
            <div className="relative">
                <input
                    type={show ? "text" : type}
                    value={value}
                    onChange={handleChange}
                    placeholder={placeholder}
                    className="w-full pl-4 pr-10 py-2.5 bg-gray-50 border border-gray-200 rounded-lg text-sm text-gray-800 focus:ring-2 focus:ring-indigo-500/20 focus:border-indigo-500 outline-none transition-all placeholder-gray-400"
                />
                {type === "password" && (
                    <button
                        onClick={() => setShow(!show)}
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 p-1"
                    >
                        {show ? <EyeOff size={16} /> : <Eye size={16} />}
                    </button>
                )}
            </div>
        </div>
    );
};

const APIKeySection = () => {
    return (
        <div className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
            <div className="border-b border-gray-100 bg-gray-50/50 p-4 px-6 flex items-center justify-between">
                <h2 className="font-semibold text-gray-800 flex items-center gap-2">
                    <Key size={18} className="text-gray-500" />
                    Intelligence Providers for Post-Processing
                </h2>
            </div>
            <div className="p-6">
                <p className="text-gray-600 mb-6 text-sm">
                    Configure LLM providers to enable intelligent features like "Summarize", "Fix Grammar", and "Draft Email".
                    Keys are stored locally.
                </p>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                    {/* Cloud Providers */}
                    <div>
                        <h3 className="text-sm font-bold text-gray-900 flex items-center gap-2 mb-4">
                            <Globe size={16} className="text-indigo-500" /> Cloud APIs
                        </h3>
                        <div className="space-y-1">
                            <APIKeyInput
                                label="OpenAI API Key"
                                placeholder="sk-..."
                                storageKey="elite_whisper_openai_key"
                            />
                            <APIKeyInput
                                label="Anthropic API Key"
                                placeholder="sk-ant-..."
                                storageKey="elite_whisper_anthropic_key"
                            />
                        </div>
                    </div>

                    {/* Local Providers */}
                    <div className="md:col-span-2">
                        <OllamaManager />
                    </div>
                </div>
            </div>
        </div>
    );
};

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
                    {/* ... existing storage content ... */}
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

                {/* Intelligence Settings */}
                <APIKeySection />

            </div>
        </div>
    );
}
