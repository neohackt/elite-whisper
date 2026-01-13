import sys
import scipy.io.wavfile as wavfile
from transformers import pipeline
import torch

def main():
    if len(sys.argv) < 2:
        print("Error: No file path provided")
        sys.exit(1)

    audio_path = sys.argv[1]
    
    # Use your existing logic
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"Loading model on {device}...", file=sys.stderr)
    
    pipe = pipeline(
        "automatic-speech-recognition",
        model="openai/whisper-base", # Or whatever you want to default
        device=0 if device == "cuda" else -1,
    )

    result = pipe(audio_path, chunk_length_s=30)
    print(result["text"]) # Output to stdout so Rust can read it

if __name__ == "__main__":
    main()