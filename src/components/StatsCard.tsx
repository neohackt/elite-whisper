
import { Settings } from 'lucide-react';

interface StatsCardProps {
    label: string;
    value: string | number;
    subtext: string;
    hasSettings?: boolean;
}

export function StatsCard({ label, value, subtext, hasSettings }: StatsCardProps) {
    return (
        <div className="flex flex-col">
            <div className="flex items-baseline gap-2 mb-1">
                <span className="text-xl font-bold text-gray-900">{value}</span>
            </div>
            <div className="flex items-center gap-1">
                <span className="text-xs text-gray-500 font-medium">{label}</span>
                {hasSettings && <Settings size={10} className="text-gray-400 cursor-pointer hover:text-gray-600" />}
            </div>
            <span className="text-[10px] text-gray-400 mt-1">{subtext}</span>
        </div>
    );
}
