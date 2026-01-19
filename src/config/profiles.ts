
export interface AIProfile {
    id: string;
    label: string;
    description: string;
    icon: 'zap' | 'activity' | 'check' | 'award';
    primaryModelId: string;
    fallbackModelId?: string; // For CPU-only or if primary missing
}

export const AI_PROFILES: AIProfile[] = [
    {
        id: 'ultra_fast',
        label: 'Ultra Fast',
        description: 'Near-instant transcription. Ideal for short quick commands.',
        icon: 'zap',
        primaryModelId: 'parakeet-0.6b-v3', // Fast GPU model
        fallbackModelId: 'whisper-tiny-en'  // Fallback CPU model
    },
    {
        id: 'balanced',
        label: 'Balanced',
        description: 'Best compromise between speed and accuracy.',
        icon: 'activity',
        primaryModelId: 'parakeet-0.6b-v2', // Slightly smaller V2
        fallbackModelId: 'whisper-base-en'  // Standard CPU base
    },
    {
        id: 'high_accuracy',
        label: 'High Accuracy',
        description: 'Improved accuracy for longer dictations.',
        icon: 'check',
        primaryModelId: 'distil-whisper-large-v3', // Distilled Large
        fallbackModelId: 'whisper-small-en'      // Reliable CPU small
    },
    {
        id: 'max_accuracy',
        label: 'Maximum Accuracy',
        description: 'The most powerful model available. Slower but precise.',
        icon: 'award',
        primaryModelId: 'distil-whisper-large-v3',
        fallbackModelId: undefined // No fallback, force best
    }
];

export const DEFAULT_PROFILE = 'balanced';

export interface AutoModeRule {
    maxDuration?: number; // seconds
    minDuration?: number; // seconds
    profileId: string;
}

export const AUTO_MODE_RULES: AutoModeRule[] = [
    { maxDuration: 10, profileId: 'ultra_fast' },
    { minDuration: 10, maxDuration: 45, profileId: 'balanced' },
    { minDuration: 45, profileId: 'high_accuracy' }
];
