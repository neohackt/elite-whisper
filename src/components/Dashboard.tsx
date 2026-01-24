import { StatsCard } from './StatsCard';
import { Command, Sparkles, Book } from 'lucide-react';

export interface DashboardStats {
    wpm: number;
    wordsThisWeek: number;
    appsUsed: number;
    savedTime: string; // e.g., "12 minutes"
}

interface DashboardProps {
    onStartRecording: () => void;
    stats: DashboardStats;
}

export function Dashboard({ onStartRecording, stats }: DashboardProps) {
    return (
        <div className="flex-1 p-8 overflow-y-auto">
            {/* Stats Row */}
            <div className="bg-white rounded-2xl p-6 shadow-[0_2px_10px_-4px_rgba(0,0,0,0.05)] mb-10 flex justify-between items-start">
                <StatsCard label="Average speed" value={`${stats.wpm} WPM`} subtext="" />
                <StatsCard label="Words this week" value={stats.wordsThisWeek} subtext="" />
                <StatsCard label="Apps used" value={stats.appsUsed} subtext="" />
                <StatsCard label="Saved this week" value={stats.savedTime} subtext="" hasSettings />
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-12">
                {/* Get Started Column */}
                <div>
                    <h3 className="text-sm font-bold text-gray-800 mb-4">Get started</h3>
                    <div className="flex flex-col gap-6">

                        {/* Action Item: Start Recording */}
                        <div
                            className="group flex gap-4 cursor-pointer hover:bg-white/50 p-2 -ml-2 rounded-xl transition-colors"
                            onClick={onStartRecording}
                        >
                            <div className="mt-1 w-6 h-6 rounded-full border border-gray-300 flex items-center justify-center bg-white group-hover:border-indigo-400 transition-colors">
                                <div className="w-2 h-2 rounded-full bg-gray-400 group-hover:bg-indigo-500" />
                            </div>
                            <div className="flex-1">
                                <div className="flex justify-between items-center mb-0.5">
                                    <h4 className="text-sm font-bold text-gray-800">Start recording</h4>
                                    <div className="flex gap-1">
                                        <span className="text-[10px] bg-gray-200 text-gray-500 px-1 rounded">F2</span>
                                    </div>
                                </div>
                                <p className="text-xs text-gray-500">Turn your voice to text with a single click.</p>
                            </div>
                        </div>

                        {/* Action Item: Customize shortcuts */}
                        <div className="group flex gap-4 cursor-pointer hover:bg-white/50 p-2 -ml-2 rounded-xl transition-colors">
                            <div className="mt-1">
                                <Command size={16} className="text-gray-400 group-hover:text-indigo-500" />
                            </div>
                            <div className="flex-1">
                                <h4 className="text-sm font-bold text-gray-800">Customize your shortcuts</h4>
                                <p className="text-xs text-gray-500">Change the keyboard shortcuts for Superwhisper.</p>
                            </div>
                        </div>

                        {/* Action Item: Create a mode */}
                        <div className="group flex gap-4 cursor-pointer hover:bg-white/50 p-2 -ml-2 rounded-xl transition-colors">
                            <div className="mt-1">
                                <Sparkles size={16} className="text-gray-400 group-hover:text-indigo-500" />
                            </div>
                            <div className="flex-1">
                                <h4 className="text-sm font-bold text-gray-800">Create a mode</h4>
                                <p className="text-xs text-gray-500">Build the perfect mode for your workflow.</p>
                            </div>
                        </div>

                        {/* Action Item: Add vocabulary */}
                        <div className="group flex gap-4 cursor-pointer hover:bg-white/50 p-2 -ml-2 rounded-xl transition-colors">
                            <div className="mt-1">
                                <Book size={16} className="text-gray-400 group-hover:text-indigo-500" />
                            </div>
                            <div className="flex-1">
                                <h4 className="text-sm font-bold text-gray-800">Add vocabulary</h4>
                                <p className="text-xs text-gray-500">Teach Superwhisper custom words, names, or industry terms.</p>
                            </div>
                        </div>

                    </div>
                </div>

                {/* What's New Column */}
                <div>
                    <div className="flex justify-between items-center mb-4">
                        <h3 className="text-sm font-bold text-gray-800">What's new?</h3>
                        <button className="text-[10px] text-gray-500 hover:text-gray-800">View all changes</button>
                    </div>

                    <div className="flex flex-col gap-6 relative">
                        {/* Timeline line effect could be added here but simple list for now */}

                        <div className="flex gap-4">
                            <span className="text-[10px] text-gray-400 w-10 pt-0.5 text-right font-mono">Jan 5</span>
                            <div className="flex-1 border-l border-gray-200 pl-4 pb-2">
                                <h4 className="text-sm font-bold text-gray-800 mb-1">Parakeet multi-language local model</h4>
                                <p className="text-xs text-gray-500 leading-relaxed">
                                    Announcing Parakeet: a local multi-language voice model for transcription and translation. Currently experimental.
                                </p>
                            </div>
                        </div>

                        <div className="flex gap-4">
                            <span className="text-[10px] text-gray-400 w-10 pt-0.5 text-right font-mono">Nov 11</span>
                            <div className="flex-1 border-l border-gray-200 pl-4 pb-2">
                                <h4 className="text-sm font-bold text-gray-800 mb-1">Custom models</h4>
                                <p className="text-xs text-gray-500 leading-relaxed">
                                    Hook into OpenAI, Groq, and Anthropic with your own API keys.
                                </p>
                            </div>
                        </div>

                        <div className="flex gap-4">
                            <span className="text-[10px] text-gray-400 w-10 pt-0.5 text-right font-mono">Nov 10</span>
                            <div className="flex-1 border-l border-gray-200 pl-4 pb-2">
                                <h4 className="text-sm font-bold text-gray-800 mb-1">Superwhisper S1 experimental model</h4>
                                <p className="text-xs text-gray-500 leading-relaxed">
                                    Enable experimental models in advanced settings to try Superwhisper's fastest cloud voice and language model, S1.
                                </p>
                            </div>
                        </div>

                    </div>

                </div>
            </div>
        </div>
    );
}
