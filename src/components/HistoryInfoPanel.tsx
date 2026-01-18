import { X } from 'lucide-react';
import { invoke } from '@tauri-apps/api/core';
import { join, appLocalDataDir } from '@tauri-apps/api/path';

export interface HistoryItem {
    id: string;
    filename: string;
    transcript: string;
    timestamp: number;
    title: string;
    // Optional fields for future/metadata
    processingTime?: string;
    processing_time?: number; // Backend field
    mode?: string;
    model?: string;
    language?: string;
    device?: string;
    appVersion?: string;
    duration?: number;
    app_name?: string;
}

interface HistoryInfoPanelProps {
    item: HistoryItem;
    onClose: () => void;
}

export function HistoryInfoPanel({ item, onClose }: HistoryInfoPanelProps) {
    const date = new Date(item.timestamp * 1000);
    const formattedDate = date.toLocaleString('en-US', {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
        hour: 'numeric',
        minute: 'numeric',
        hour12: true
    });

    const handleOpenLocation = async () => {
        try {
            let filePath = item.filename;
            const isAbsolute = item.filename.includes('/') || item.filename.includes('\\');

            if (!isAbsolute) {
                const appDataDir = await appLocalDataDir();
                filePath = await join(appDataDir, 'recorded_audio', item.filename);
            }

            console.log("Opening file location:", filePath);
            await invoke('cmd_show_in_folder', { path: filePath });
        } catch (error) {
            console.error("Failed to open file location:", error);
        }
    };

    return (
        <div className="w-80 border-l border-gray-200 bg-[#f9f9f9] flex flex-col h-full shadow-xl z-10 transition-transform">
            {/* Header */}
            <div className="flex justify-between items-center p-4 border-b border-gray-200/50">
                <h2 className="font-bold text-gray-800">Information</h2>
                <button onClick={onClose} className="p-1 hover:bg-gray-200 rounded-md text-gray-500 transition-colors">
                    <X size={16} />
                </button>
            </div>

            {/* Content */}
            <div className="flex-1 p-4 overflow-y-auto">
                <div className="space-y-4 text-xs">

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Date & time</span>
                        <span className="text-gray-900">{formattedDate}</span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Processing time</span>
                        <span className="text-gray-900">
                            {item.processing_time !== undefined
                                ? `${item.processing_time.toFixed(2)}s`
                                : item.processingTime || "0.0s"}
                        </span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Mode</span>
                        <span className="text-gray-900">{item.mode || "Default"}</span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Voice Model</span>
                        <span className="text-gray-900">{item.model || "Standard"}</span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Language</span>
                        <span className="text-gray-900">{item.language || "English"}</span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">Recording Device</span>
                        <span className="text-gray-900 text-right max-w-[120px] truncate" title={item.device || "System Default"}>
                            {item.device || "System Default"}
                        </span>
                    </div>

                    <div className="flex justify-between py-2 border-b border-gray-200/50">
                        <span className="text-gray-500 font-medium">App Version</span>
                        <span className="text-gray-900">{item.appVersion || "1.0.10"}</span>
                    </div>

                </div>
            </div>

            {/* Footer Actions */}
            <div className="p-4 border-t border-gray-200/50 flex flex-col gap-3">
                <button
                    onClick={handleOpenLocation}
                    className="w-full bg-white border border-gray-200 text-gray-700 font-medium py-2 rounded-lg text-xs hover:bg-gray-50 transition-colors shadow-sm"
                >
                    Open file location
                </button>
                <button className="w-full bg-gray-200/50 border border-transparent text-gray-600 font-medium py-2 rounded-lg text-xs hover:bg-gray-200 transition-colors">
                    Report Issue
                </button>
            </div>
        </div>
    );
}
