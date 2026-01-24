import { useState, useRef, useEffect } from 'react';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { PhysicalPosition } from '@tauri-apps/api/dpi';
import './widget.css';

export default function Widget() {
    // Refs for manual drag
    const dragStartRef = useRef<{ x: number, y: number } | null>(null);
    const isDraggingRef = useRef(false);
    const [isHovered, setIsHovered] = useState(false);

    useEffect(() => {
        // Enforce DOM transparency
        document.documentElement.style.background = 'transparent';
        document.body.style.background = 'transparent';
        const root = document.getElementById('root');
        if (root) root.style.background = 'transparent';
    }, []);

    // Manual Drag Logic (Bypasses OS Window Chrome artifacts)
    const handleMouseDown = (e: React.MouseEvent) => {
        if (e.button !== 0) return;
        dragStartRef.current = { x: e.screenX, y: e.screenY };
        isDraggingRef.current = false;
    };

    const handleMouseMove = async (e: React.MouseEvent) => {
        if (!dragStartRef.current) return;

        const dx = e.screenX - dragStartRef.current.x;
        const dy = e.screenY - dragStartRef.current.y;

        // Threshold to reduce jitter
        if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
            isDraggingRef.current = true;
        }

        if (isDraggingRef.current) {
            const window = getCurrentWindow();
            try {
                const pos = await window.outerPosition();
                await window.setPosition(new PhysicalPosition(pos.x + dx, pos.y + dy));
                dragStartRef.current = { x: e.screenX, y: e.screenY };
            } catch (err) {
                console.error(err);
            }
        }
    };

    const handleMouseUp = () => {
        dragStartRef.current = null;
        setTimeout(() => { isDraggingRef.current = false; }, 50);
    };

    return (
        <div
            className="w-screen h-screen flex items-center justify-center bg-transparent overflow-hidden"
            onMouseDown={handleMouseDown}
            onMouseMove={handleMouseMove}
            onMouseUp={handleMouseUp}
            onMouseLeave={handleMouseUp}
        >
            {/* The Pill */}
            <div
                className={`
                    w-[150px] h-[60px] 
                    bg-white/90 backdrop-blur-md 
                    rounded-full 
                    shadow-lg border border-white/40 
                    flex items-center justify-center gap-3
                    transition-all duration-200
                    ${isHovered ? 'scale-105 shadow-xl' : ''}
                `}
                onMouseEnter={() => setIsHovered(true)}
                onMouseLeave={() => setIsHovered(false)}
            >
                {/* Visual Content Only - No logic yet */}
                <span className="text-2xl select-none">🎙️</span>
                <div className="flex gap-1 h-3 items-center">
                    {[...Array(5)].map((_, i) => (
                        <div key={i} className="w-1.5 h-1.5 bg-gray-400 rounded-full" />
                    ))}
                </div>
            </div>
        </div>
    );
}
