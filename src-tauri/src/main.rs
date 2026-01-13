// Prevents console window from popping up on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::sync::Mutex;
use std::io::Cursor;
use tauri::State;
use whisper_rs::{WhisperContext, FullParams, SamplingStrategy};
use hound;

pub struct AppState {
    whisper_ctx: Mutex<Option<WhisperContext>>,
}

impl AppState {
    pub fn new() -> Self {
        // Hardcoded path for this user environment as per plan
        let model_path = "d:/Personal/voiceapp/src-tauri/whisper.cpp/ggml-base.en.bin";
        println!("Loading model from: {}", model_path);
        
        let ctx = WhisperContext::new_with_params(model_path, Default::default()); 
        
        match ctx {
            Ok(c) => {
                println!("Model loaded successfully!");
                Self { whisper_ctx: Mutex::new(Some(c)) }
            },
            Err(e) => {
                println!("Failed to load model: {}", e);
                Self { whisper_ctx: Mutex::new(None) }
            }
        }
    }
}

fn read_wav_from_bytes(data: Vec<u8>) -> Result<Vec<f32>, String> {
    let cursor = Cursor::new(data);
    let mut reader = hound::WavReader::new(cursor).map_err(|e| format!("WavReader error: {}", e))?;
    let spec = reader.spec();
    println!("Received WAV Spec: {:?}", spec);
    
    // We expect 16kHz for Whisper
    if spec.sample_rate != 16000 {
        return Err(format!("WAV file must be 16kHz, found {}", spec.sample_rate));
    }

    // Whisper expects mono f32
    // If stereo, mix down
    let samples: Vec<f32> = match spec.sample_format {
        hound::SampleFormat::Int => {
            let data: Vec<i32> = reader.samples::<i32>().map(|s| s.unwrap_or(0)).collect();
            // Convert to f32 and normalize
            let max_val = 2_i32.pow(spec.bits_per_sample as u32 - 1) as f32;
            data.iter().map(|&s| s as f32 / max_val).collect()
        },
        hound::SampleFormat::Float => {
            reader.samples::<f32>().map(|s| s.unwrap_or(0.0)).collect()
        }
    };

    if spec.channels == 2 {
        // Mix stereo to mono
        const CHANNELS: usize = 2;
        let mono: Vec<f32> = samples.chunks(CHANNELS)
            .map(|chunk| (chunk[0] + chunk[1]) / 2.0)
            .collect();
        Ok(mono)
    } else if spec.channels == 1 {
        Ok(samples)
    } else {
         Err(format!("Unsupported channel count: {}", spec.channels))
    }
}

#[tauri::command]
async fn cmd_transcribe(
    audio_data: Vec<u8>,
    state: State<'_, AppState>
) -> Result<String, String> {
    println!("Transcribing received bytes: {} bytes", audio_data.len());
    
    let audio_input = read_wav_from_bytes(audio_data)?;
    println!("Audio loaded, {} samples", audio_input.len());

    let ctx_guard = state.whisper_ctx.lock().map_err(|_| "Failed to lock state")?;
    let ctx = ctx_guard.as_ref().ok_or("Model not loaded")?;

    let mut state = ctx.create_state().map_err(|e| format!("Failed to create state: {}", e))?;
    
    let mut params = FullParams::new(SamplingStrategy::Greedy { best_of: 1 });
    params.set_n_threads(4);
    params.set_print_special(false);
    params.set_print_progress(false);
    params.set_print_realtime(false);
    params.set_print_timestamps(false);

    state.full(params, &audio_input[..]).map_err(|e| format!("Whisper error: {}", e))?;
    
    let num_segments = state.full_n_segments().map_err(|e| format!("Error getting segments: {}", e))?;
    let mut text = String::new();
    
    for i in 0..num_segments {
        let segment = state.full_get_segment_text(i).unwrap_or(String::new());
        text.push_str(&segment);
    }

    Ok(text.trim().to_string())
}

#[tauri::command]
async fn get_current_model() -> Result<String, String> {
    Ok("ggml-base.en.bin (Local)".to_string())
}

#[tauri::command]
async fn set_model(model_id: String) -> Result<(), String> {
    println!("Frontend requested model change to: {}", model_id);
    // For now, we only support the hardcoded local model.
    // In a future update, we could swap the model in AppState here.
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_fs::init())
        .manage(AppState::new())
        .invoke_handler(tauri::generate_handler![
            cmd_transcribe,
            get_current_model,
            set_model
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}