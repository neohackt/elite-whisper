import React from 'react';

interface TopBarProps {
    devices: MediaDeviceInfo[];
    selectedDeviceId: string;
    onSelectDevice: (deviceId: string) => void;
}

export function TopBar({ devices, selectedDeviceId, onSelectDevice }: TopBarProps) {
    const selectedLabel = devices.find(d => d.deviceId === selectedDeviceId)?.label || "System default microphone";
    const [isOpen, setIsOpen] = React.useState(false);
    const dropdownRef = React.useRef<HTMLDivElement>(null);

    React.useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        }
        document.addEventListener("mousedown", handleClickOutside);
        return () => document.removeEventListener("mousedown", handleClickOutside);
    }, []);

    return (
        <div className="h-14 flex items-center px-8 border-b border-transparent">
            {/* Microphone Dropdown */}
            <div className="relative" ref={dropdownRef}>
                <button
                    onClick={() => setIsOpen(!isOpen)}
                    className="flex items-center gap-2 text-gray-400 hover:text-gray-600 transition-colors text-sm font-medium focus:outline-none"
                >
                    <div className="p-1 rounded bg-transparent">
                        <span className="text-[10px] border border-gray-300 rounded px-1 py-0.5">↕</span>
                    </div>
                    <span className="max-w-[200px] truncate">{selectedLabel || "Default Microphone"}</span>
                </button>

                {/* Dropdown Menu */}
                {isOpen && (
                    <div className="absolute top-full left-0 mt-1 w-64 bg-white border border-gray-200 rounded-lg shadow-lg py-1 z-50 animate-in fade-in zoom-in-95 duration-100">
                        <div className="px-3 py-2 text-xs font-semibold text-gray-400 uppercase tracking-wider">
                            Select Microphone
                        </div>
                        {devices.length > 0 ? (
                            devices.map(device => (
                                <button
                                    key={device.deviceId}
                                    onClick={() => {
                                        onSelectDevice(device.deviceId);
                                        setIsOpen(false);
                                    }}
                                    className={`w-full text-left px-4 py-2 text-sm hover:bg-indigo-50 transition-colors ${selectedDeviceId === device.deviceId ? 'text-indigo-600 font-medium bg-indigo-50' : 'text-gray-700'}`}
                                >
                                    {device.label || `Microphone ${device.deviceId.slice(0, 5)}...`}
                                </button>
                            ))
                        ) : (
                            <div className="px-4 py-2 text-sm text-gray-500 italic">No devices found</div>
                        )}
                    </div>
                )}
            </div>

            <div className="ml-auto flex gap-2">
                {/* Window controls if we were drawing them */}
            </div>
        </div>
    );
}
