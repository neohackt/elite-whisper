import React from 'react';
import { Home, Sparkles, Book, Settings, Volume2, History, Cpu } from 'lucide-react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

interface NavItem {
    id: string;
    label: string;
    icon: React.ElementType;
    bgClass: string;
    iconClass: string;
}

const navItems: NavItem[] = [
    { id: 'home', label: 'Home', icon: Home, bgClass: 'bg-orange-400', iconClass: 'text-white' },
    { id: 'modes', label: 'Modes', icon: Sparkles, bgClass: 'bg-blue-500', iconClass: 'text-white' },
    { id: 'vocabulary', label: 'Vocabulary', icon: Book, bgClass: 'bg-blue-500', iconClass: 'text-white' },
    { id: 'settings', label: 'Settings', icon: Settings, bgClass: 'bg-gray-700', iconClass: 'text-white' },
    { id: 'models', label: 'AI Models', icon: Cpu, bgClass: 'bg-purple-500', iconClass: 'text-white' },
    { id: 'sound', label: 'Sound', icon: Volume2, bgClass: 'bg-gray-700', iconClass: 'text-white' },
    { id: 'history', label: 'History', icon: History, bgClass: 'bg-indigo-500', iconClass: 'text-white' },
];

interface SidebarProps {
    currentView: string;
    setView: (view: string) => void;
}

export function Sidebar({ currentView, setView }: SidebarProps) {
    return (
        <div className="w-64 bg-[#f2f2f2] flex flex-col justify-between h-full border-r border-gray-200/50">
            <div className="p-4 flex flex-col gap-2 mt-2">
                {navItems.map((item) => {
                    const isActive = currentView === item.id;
                    return (
                        <button
                            key={item.id}
                            onClick={() => setView(item.id)}
                            className={twMerge(
                                "flex items-center gap-3 p-2 rounded-lg transition-all duration-200 text-left group",
                                isActive ? "bg-white shadow-sm" : "hover:bg-gray-200/50"
                            )}
                        >
                            <div className={twMerge(
                                "w-7 h-7 rounded-md flex items-center justify-center shadow-sm transition-transform group-hover:scale-105",
                                item.bgClass
                            )}>
                                <item.icon size={16} className={item.iconClass} strokeWidth={2.5} />
                            </div>
                            <span className={clsx(
                                "font-semibold text-[15px]",
                                isActive ? "text-gray-900" : "text-gray-500 group-hover:text-gray-700"
                            )}>
                                {item.label}
                            </span>
                        </button>
                    );
                })}
            </div>

            <div className="p-4 mb-4">
                <div className="bg-gray-200/50 border border-gray-300/50 rounded-lg p-3 flex items-center justify-center shadow-sm">
                    <span className="text-sm font-bold text-gray-500">Elite Whisper <span className="bg-gray-400 text-white text-[10px] px-1 py-0.5 rounded ml-1">PRO</span></span>
                </div>
            </div>
        </div>
    );
}
