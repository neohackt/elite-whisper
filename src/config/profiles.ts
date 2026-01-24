
export interface AIProfile {
    id: string;
    label: string;
    description: string;
    icon: 'zap' | 'activity' | 'check' | 'award';
    models: {
        english: string;
        multilingual: string;
    };
    fallbackModels?: {
        english?: string;
        multilingual?: string;
    };
}

export const AI_PROFILES: AIProfile[] = [
    {
        id: 'ultra_fast',
        label: 'Ultra Fast',
        description: 'Near-instant transcription. Ideal for short quick commands.',
        icon: 'zap',
        models: {
            english: 'whisper-tiny-en',
            multilingual: 'whisper-tiny'
        },
        fallbackModels: {
            english: 'whisper-base-en',
            multilingual: 'whisper-base'
        }
    },
    {
        id: 'balanced',
        label: 'Balanced',
        description: 'Best compromise between speed and accuracy.',
        icon: 'activity',
        models: {
            english: 'parakeet-0.6b-v2',
            multilingual: 'sense-voice-small'
        },
        fallbackModels: {
            english: 'whisper-base-en',
            multilingual: 'whisper-base'
        }
    },
    {
        id: 'high_accuracy',
        label: 'High Accuracy',
        description: 'Improved accuracy for longer dictations.',
        icon: 'check',
        models: {
            english: 'distil-whisper-large-v3',
            multilingual: 'whisper-small'
        },
        fallbackModels: {
            english: 'whisper-small-en',
            multilingual: 'whisper-tiny'
        }
    },
    {
        id: 'max_accuracy',
        label: 'Maximum Accuracy',
        description: 'The most powerful model available. Slower but precise.',
        icon: 'award',
        models: {
            english: 'distil-whisper-large-v3',
            multilingual: 'whisper-large-v3'
        }
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
