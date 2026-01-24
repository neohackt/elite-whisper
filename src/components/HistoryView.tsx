import React, { useState, useMemo } from 'react';
import { Search, Play, Pause, Info, Copy, Trash2 } from 'lucide-react';
import { HistoryInfoPanel, HistoryItem } from './HistoryInfoPanel';
import { twMerge } from 'tailwind-merge';
import { join, appLocalDataDir } from '@tauri-apps/api/path';
import { readFile } from '@tauri-apps/plugin-fs';

interface HistoryViewProps {
    items: HistoryItem[];
    onDelete: (id: string) => void;
}

export function HistoryView({ items, onDelete }: HistoryViewProps) {
    const [searchQuery, setSearchQuery] = useState("");
    const [expandedId, setExpandedId] = useState<string | null>(null);
    const [selectedItem, setSelectedItem] = useState<HistoryItem | null>(null);

    const [playingId, setPlayingId] = useState<string | null>(null);
    const audioRef = React.useRef<HTMLAudioElement | null>(null);

    // Filter items
    const filteredItems = useMemo(() => {
        if (!searchQuery) return items;
        const lowerQuery = searchQuery.toLowerCase();
        return items.filter(item =>
            (item.title && item.title.toLowerCase().includes(lowerQuery)) ||
            (item.transcript && item.transcript.toLowerCase().includes(lowerQuery))
        );
    }, [items, searchQuery]);

    // Group items by date
    const groupedItems = useMemo(() => {
        const groups: Record<string, HistoryItem[]> = {};

        filteredItems.forEach(item => {
            const date = new Date(item.timestamp * 1000);
            const today = new Date();
            const yesterday = new Date();
            yesterday.setDate(yesterday.getDate() - 1);

            let key = "Earlier";

            if (date.toDateString() === today.toDateString()) {
                key = "Today";
            } else if (date.toDateString() === yesterday.toDateString()) {
                key = "Yesterday";
            } else {
                // Simple "X days ago" or "Earlier this week" logic can be expanded
                const diffTime = Math.abs(today.getTime() - date.getTime());
                const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
                if (diffDays < 7) {
                    key = diffDays === 1 ? "1 day ago" : `${diffDays} days ago`; // Should ideally be caught by Yesterday but simple fallback
                    if (date.getDay() < today.getDay()) {
                        key = "Earlier this week";
                    } else {
                        key = `${diffDays} days ago`; // Fallback
                    }
                } else {
                    key = "Older";
                }

                // Overwrite simple logic for specific "Yesterday" check if needed, 
                // but `toDateString` comparison handles exact dates best.
                if (diffDays > 1 && diffDays < 7) {
                    key = "Earlier this week";
                } else if (diffDays >= 7) {
                    key = "Last Month"; // Bucketing broadly for now
                }
            }

            // Let's stick to the screenshot's "Yesterday", "2 days ago", "Earlier this week"
            const diffTime = Math.abs(today.getTime() - date.getTime());
            const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));

            if (date.toDateString() === today.toDateString()) key = "Today";
            else if (date.toDateString() === yesterday.toDateString()) key = "Yesterday";
            else if (diffDays < 7) key = diffDays + " days ago";
            else key = "Earlier this week"; // Broad bucket for older for this demo

            if (!groups[key]) groups[key] = [];
            groups[key].push(item);
        });

        return groups;
    }, [filteredItems]);

    const toggleExpand = (id: string) => {
        setExpandedId(prev => prev === id ? null : id);
    };

    const handleCopy = (e: React.MouseEvent, text: string) => {
        e.stopPropagation();
        navigator.clipboard.writeText(text);
        // Could add toast here
    };

    const handleDelete = (e: React.MouseEvent, id: string) => {
        e.stopPropagation();
        onDelete(id);
        if (selectedItem?.id === id) setSelectedItem(null);
    };

    const loadedIdRef = React.useRef<string | null>(null);

    const handlePlay = async (e: React.MouseEvent, item: HistoryItem) => {
        e.stopPropagation();

        // Case 1: This item is already loaded
        if (loadedIdRef.current === item.id && audioRef.current) {
            if (playingId === item.id) {
                // Currently playing -> Pause
                audioRef.current.pause();
                setPlayingId(null);
            } else {
                // Currently paused -> Resume
                await audioRef.current.play();
                setPlayingId(item.id);
            }
            return;
        }

        // Case 2: New item (or nothing loaded)
        // Stop/Clear previous
        if (audioRef.current) {
            audioRef.current.pause();
            audioRef.current = null;
            setPlayingId(null);
        }

        try {
            // Handle both legacy (filename only) and new (absolute path)
            let filePath = item.filename;
            const isAbsolute = item.filename.includes('/') || item.filename.includes('\\');

            if (!isAbsolute) {
                const appDataDir = await appLocalDataDir();
                filePath = await join(appDataDir, 'recorded_audio', item.filename);
                console.log("Resolved relative path to:", filePath);
            }

            console.log("Attempting to play audio from:", filePath);

            // USE BLOB STRATEGY instead of asset protocol
            const fileData = await readFile(filePath);
            const blob = new Blob([fileData], { type: 'audio/wav' });
            const assetUrl = URL.createObjectURL(blob);

            console.log("Generated Blob URL:", assetUrl);

            const audio = new Audio(assetUrl);
            audioRef.current = audio;
            loadedIdRef.current = item.id;

            audio.onended = () => {
                console.log("Audio ended");
                setPlayingId(null);
            };
            audio.onerror = (err) => {
                console.error("Audio playback error", err, audio.error);
                setPlayingId(null);
                loadedIdRef.current = null;
            };

            await audio.play();
            console.log("Audio playback started");
            setPlayingId(item.id);

        } catch (error) {
            console.error("Failed to play audio:", error);
            loadedIdRef.current = null;
        }
    };

    const handleStop = (e: React.MouseEvent) => {
        e.stopPropagation();
        if (audioRef.current) {
            audioRef.current.pause();
            setPlayingId(null);
        }
    };

    const toggleInfo = (e: React.MouseEvent, item: HistoryItem) => {
        e.stopPropagation();
        setSelectedItem(prev => prev?.id === item.id ? null : item);
    };

    return (
        <div className="flex h-full relative">
            {/* Main List Area */}
            <div className="flex-1 flex flex-col h-full overflow-hidden">
                {/* Search Bar */}
                <div className="px-8 pt-8 pb-4">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" size={16} />
                        <input
                            type="text"
                            placeholder="Search recordings..."
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            className="w-full pl-10 pr-4 py-2.5 bg-white border border-transparent focus:border-indigo-300 rounded-lg text-sm text-gray-700 placeholder-gray-400 outline-none shadow-sm transition-all"
                        />
                    </div>
                </div>

                {/* Scrollable List */}
                <div className="flex-1 overflow-y-auto px-8 pb-20">
                    {Object.entries(groupedItems).map(([group, groupItems]) => (
                        <div key={group} className="mb-8">
                            <h3 className="text-xs font-bold text-gray-400 mb-3 uppercase tracking-wide pl-1">{group}</h3>
                            <div className="flex flex-col gap-3">
                                {groupItems.map(item => {
                                    const isExpanded = expandedId === item.id;
                                    return (
                                        <div
                                            key={item.id}
                                            onClick={() => toggleExpand(item.id)}
                                            className={twMerge(
                                                "bg-[#eaeaea] hover:bg-[#e4e4e4] rounded-xl p-4 transition-all cursor-pointer border border-transparent",
                                                isExpanded ? "shadow-md bg-white hover:bg-white" : ""
                                            )}
                                        >
                                            {/* Expanded View */}
                                            {isExpanded ? (
                                                <div className="flex flex-col animate-in fade-in duration-200">
                                                    <div className="mb-4">
                                                        <h4 className="font-bold text-gray-800 text-sm mb-1">{item.title || "Untitled"}</h4>
                                                        {/* Waveform Placeholder - replicating the look */}
                                                        <div className="h-12 flex items-center gap-2 my-3 px-2">
                                                            <button
                                                                onClick={(e) => playingId === item.id ? handleStop(e) : handlePlay(e, item)}
                                                                className="w-6 h-6 rounded-full bg-gray-400/20 flex items-center justify-center text-gray-600 hover:bg-gray-400/40 transition-colors"
                                                            >
                                                                {playingId === item.id ? <Pause size={10} fill="currentColor" /> : <Play size={10} fill="currentColor" />}
                                                            </button>
                                                            <div className="flex-1 h-8 flex items-center gap-[2px] opacity-30">
                                                                {/* Fake waveform bars */}
                                                                {Array.from({ length: 60 }).map((_, i) => (
                                                                    <div key={i} className="w-[3px] bg-gray-800 rounded-full" style={{ height: `${Math.max(20, Math.random() * 100)}%` }}></div>
                                                                ))}
                                                            </div>
                                                            <span className="text-[10px] text-gray-400 font-mono">0:02</span>
                                                        </div>
                                                    </div>

                                                    {/* Actions */}
                                                    <div className="flex justify-end items-center gap-2 mt-2">
                                                        {/* Icon 1 - Skipped as requested */}
                                                        <button
                                                            onClick={(e) => toggleInfo(e, item)}
                                                            className={twMerge("p-1.5 rounded-lg text-gray-400 hover:text-gray-700 hover:bg-gray-200/50 transition-colors", selectedItem?.id === item.id ? "text-indigo-600 bg-indigo-50" : "")}
                                                            title="Info"
                                                        >
                                                            <Info size={16} />
                                                        </button>
                                                        <button
                                                            onClick={(e) => handleCopy(e, item.transcript)}
                                                            className="p-1.5 rounded-lg text-gray-400 hover:text-gray-700 hover:bg-gray-200/50 transition-colors"
                                                            title="Copy"
                                                        >
                                                            <Copy size={16} />
                                                        </button>
                                                        <button
                                                            onClick={(e) => handleDelete(e, item.id)}
                                                            className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50 transition-colors"
                                                            title="Delete"
                                                        >
                                                            <Trash2 size={16} />
                                                        </button>
                                                    </div>
                                                </div>
                                            ) : (
                                                /* Collapsed View */
                                                <div className="flex flex-col">
                                                    <p className="text-sm text-gray-600 font-medium line-clamp-2 leading-relaxed">
                                                        {item.transcript || "No text content"}
                                                    </p>
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    ))}

                    <div className="text-center py-8">
                        <span className="text-xs text-gray-300 font-medium">End of history</span>
                    </div>
                </div>
            </div>

            {/* Side Info Panel */}
            {selectedItem && (
                <div className="absolute right-0 top-0 h-full animate-in slide-in-from-right duration-300 shadow-2xl">
                    <HistoryInfoPanel item={selectedItem} onClose={() => setSelectedItem(null)} />
                </div>
            )}
        </div>
    );
}
