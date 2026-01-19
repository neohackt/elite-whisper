
export interface AIModel {
    id: string;
    type: 'whisper' | 'sherpa';
    name: string;
    description: string;
    language: string;
    size: string;
    speed: number;
    accuracy: number;
    url?: string; // For single-file models
    filename: string;
    files?: { [key: string]: string }; // For multi-file models like Sherpa
}

export const AVAILABLE_MODELS: AIModel[] = [
    {
        id: 'whisper-base-en',
        type: 'whisper',
        name: 'Whisper Base',
        description: 'Optimized for English transcription. Good balance of speed and accuracy.',
        language: 'English',
        size: '142 MB',
        speed: 85,
        accuracy: 80,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin',
        filename: 'ggml-base.en.bin'
    },
    {
        id: 'whisper-tiny-en',
        type: 'whisper',
        name: 'Whisper Tiny',
        description: 'Ultra-fast model for quick dictation. Lower accuracy but instant response.',
        language: 'English',
        size: '75 MB',
        speed: 98,
        accuracy: 65,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin',
        filename: 'ggml-tiny.en.bin'
    },
    {
        id: 'whisper-small-en',
        type: 'whisper',
        name: 'Whisper Small',
        description: 'Higher accuracy model for professional work. Slightly slower processing.',
        language: 'English',
        size: '466 MB',
        speed: 60,
        accuracy: 92,
        url: 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin',
        filename: 'ggml-small.en.bin'
    },
    {
        id: 'parakeet-0.6b-v2',
        type: 'sherpa',
        name: 'Parakeet 0.6B v2',
        description: 'High-accuracy streaming-ready RNN-T model by NVIDIA. Extremely fast inference.',
        language: 'English',
        size: '~660 MB',
        speed: 95,
        accuracy: 94,
        filename: 'parakeet-0.6b-v2',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/decoder.int8.onnx',
            'joiner.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/joiner.int8.onnx',
        }
    },
    {
        id: 'parakeet-0.6b-v3',
        type: 'sherpa',
        name: 'Parakeet 0.6B v3',
        description: 'Improved version with better multilingual support and timestamp accuracy.',
        language: 'English+',
        size: '~660 MB',
        speed: 92,
        accuracy: 96,
        filename: 'parakeet-0.6b-v3',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/decoder.int8.onnx',
            'joiner.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/joiner.int8.onnx',
        }
    },
    {
        id: 'distil-whisper-large-v3',
        type: 'sherpa',
        name: 'Distil-Whisper Large v3',
        description: 'Distilled Version of Whisper Large v3. Optimized for Sherpa-ONNX. Fast and accurate.',
        language: 'English',
        size: '~1.1 GB', // encoder 668MB + decoder 315MB + weights
        speed: 90,
        accuracy: 95,
        filename: 'distil-whisper-large-v3',
        files: {
            'tokens.txt': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-tokens.txt',
            'encoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-encoder.int8.onnx',
            'decoder.int8.onnx': 'https://huggingface.co/csukuangfj/sherpa-onnx-whisper-distil-large-v3/resolve/main/distil-large-v3-decoder.int8.onnx',
        }
    }
];
