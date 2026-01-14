// Prevents console window from popping up on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use hound;
use serde::{Deserialize, Serialize};
use std::fs::File;
use std::io::Cursor;
use std::path::PathBuf;
use std::sync::Mutex;
use tauri::{Emitter, Manager, State};
use uuid::Uuid;
use whisper_rs::{FullParams, SamplingStrategy, WhisperContext};

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
                Self {
                    whisper_ctx: Mutex::new(Some(c)),
                }
            }
            Err(e) => {
                println!("Failed to load model: {}", e);
                Self {
                    whisper_ctx: Mutex::new(None),
                }
            }
        }
    }
}

fn read_wav_from_bytes(data: Vec<u8>) -> Result<Vec<f32>, String> {
    let cursor = Cursor::new(data);
    let mut reader =
        hound::WavReader::new(cursor).map_err(|e| format!("WavReader error: {}", e))?;
    let spec = reader.spec();
    println!("Received WAV Spec: {:?}", spec);

    // We expect 16kHz for Whisper
    if spec.sample_rate != 16000 {
        return Err(format!(
            "WAV file must be 16kHz, found {}",
            spec.sample_rate
        ));
    }

    // Whisper expects mono f32
    // If stereo, mix down
    let samples: Vec<f32> = match spec.sample_format {
        hound::SampleFormat::Int => {
            let data: Vec<i32> = reader.samples::<i32>().map(|s| s.unwrap_or(0)).collect();
            // Convert to f32 and normalize
            let max_val = 2_i32.pow(spec.bits_per_sample as u32 - 1) as f32;
            data.iter().map(|&s| s as f32 / max_val).collect()
        }
        hound::SampleFormat::Float => reader.samples::<f32>().map(|s| s.unwrap_or(0.0)).collect(),
    };

    if spec.channels == 2 {
        // Mix stereo to mono
        const CHANNELS: usize = 2;
        let mono: Vec<f32> = samples
            .chunks(CHANNELS)
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
async fn cmd_transcribe(audio_data: Vec<u8>, state: State<'_, AppState>) -> Result<String, String> {
    println!("Transcribing received bytes: {} bytes", audio_data.len());

    let audio_input = read_wav_from_bytes(audio_data)?;
    println!("Audio loaded, {} samples", audio_input.len());

    let ctx_guard = state
        .whisper_ctx
        .lock()
        .map_err(|_| "Failed to lock state")?;
    let ctx = ctx_guard.as_ref().ok_or("Model not loaded")?;

    let mut state = ctx
        .create_state()
        .map_err(|e| format!("Failed to create state: {}", e))?;

    let mut params = FullParams::new(SamplingStrategy::Greedy { best_of: 1 });
    params.set_n_threads(4);
    params.set_print_special(false);
    params.set_print_progress(false);
    params.set_print_realtime(false);
    params.set_print_timestamps(false);

    state
        .full(params, &audio_input[..])
        .map_err(|e| format!("Whisper error: {}", e))?;

    let num_segments = state
        .full_n_segments()
        .map_err(|e| format!("Error getting segments: {}", e))?;
    let mut text = String::new();

    for i in 0..num_segments {
        let segment = state.full_get_segment_text(i).unwrap_or(String::new());
        text.push_str(&segment);
    }

    let mut final_text = text.trim().to_string();

    // Filter common Whisper hallucinations
    let filters = [
        "[BLANK_AUDIO]",
        "[silence]",
        "(music)",
        "[MUSIC]",
        "(silence)",
    ];
    for filter in filters.iter() {
        final_text = final_text.replace(filter, "");
    }

    Ok(final_text.trim().to_string())
}

#[tauri::command]
async fn get_current_model() -> Result<String, String> {
    Ok("Whisper Base (Local)".to_string())
}

#[tauri::command]
async fn set_model(model_id: String) -> Result<(), String> {
    println!("Frontend requested model change to: {}", model_id);
    // For now, we only support the hardcoded local model.
    // In a future update, we could swap the model in AppState here.
    Ok(())
}

#[derive(Debug, Serialize, Deserialize, Clone)]
struct HistoryItem {
    id: String,
    filename: String,
    transcript: String,
    timestamp: u64,
    #[serde(default)]
    title: String,
}

fn get_history_file_path(app: &tauri::AppHandle) -> PathBuf {
    let mut path = app
        .path()
        .app_local_data_dir()
        .expect("failed to get app local data dir");
    path.push("history.json");
    path
}

#[tauri::command]
fn cmd_save_history(
    app: tauri::AppHandle,
    transcript: String,
    filename: String,
    title: String,
) -> Result<HistoryItem, String> {
    let path = get_history_file_path(&app);
    let mut history: Vec<HistoryItem> = if path.exists() {
        let file = File::open(&path).map_err(|e| e.to_string())?;
        serde_json::from_reader(file).unwrap_or_default()
    } else {
        Vec::new()
    };

    let item = HistoryItem {
        id: Uuid::new_v4().to_string(),
        filename,
        transcript,
        timestamp: std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs(),
        title,
    };

    history.insert(0, item.clone());

    // Create dir if not exists
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

    let file = File::create(&path).map_err(|e| e.to_string())?;
    serde_json::to_writer_pretty(file, &history).map_err(|e| e.to_string())?;

    Ok(item)
}

#[tauri::command]
fn cmd_get_history(app: tauri::AppHandle) -> Result<Vec<HistoryItem>, String> {
    let path = get_history_file_path(&app);
    if !path.exists() {
        return Ok(Vec::new());
    }
    let file = File::open(&path).map_err(|e| e.to_string())?;
    let history: Vec<HistoryItem> = serde_json::from_reader(file).unwrap_or_default();
    // Return reverse chronological order
    Ok(history)
}

#[tauri::command]
fn cmd_delete_history(app: tauri::AppHandle, id: String) -> Result<(), String> {
    let path = get_history_file_path(&app);
    if !path.exists() {
        return Ok(());
    }

    let file = File::open(&path).map_err(|e| e.to_string())?;
    let mut history: Vec<HistoryItem> = serde_json::from_reader(file).unwrap_or_default();

    history.retain(|item| item.id != id);

    let file = File::create(&path).map_err(|e| e.to_string())?;
    serde_json::to_writer_pretty(file, &history).map_err(|e| e.to_string())?;

    Ok(())
}

use enigo::{Enigo, Keyboard, Settings};
use tauri_plugin_global_shortcut::ShortcutState;

#[tauri::command]
fn cmd_type_text(text: String) -> Result<(), String> {
    let mut enigo = Enigo::new(&Settings::default()).map_err(|e| e.to_string())?;
    let _ = enigo.text(&text);
    Ok(())
}

#[tauri::command]
fn cmd_disable_shadow(window: tauri::Window) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    let _ = window.set_shadow(false);
    Ok(())
}

#[tauri::command]
async fn cmd_show_in_folder(path: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer")
            .args(["/select,", &path]) // The comma after select is important
            .spawn()
            .map_err(|e| e.to_string())?;
    }
    #[cfg(not(target_os = "windows"))]
    {
        // Fallback for other OSs - just open the parent folder
        // if let Some(parent) = std::path::Path::new(&path).parent() {
        //      // Requires 'open' crate
        //      // open::that(parent).map_err(|e| e.to_string())?;
        // }
    }
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_shortcut("F2")
                .unwrap()
                .with_handler(
                    |app: &tauri::AppHandle,
                     shortcut: &tauri_plugin_global_shortcut::Shortcut,
                     event: tauri_plugin_global_shortcut::ShortcutEvent| {
                        if event.state == ShortcutState::Pressed {
                            if shortcut.matches(
                                tauri_plugin_global_shortcut::Modifiers::empty(),
                                tauri_plugin_global_shortcut::Code::F2,
                            ) {
                                let _ = app.emit("global-shortcut", "F2");
                            }
                        }
                    },
                )
                .build(),
        )
        .manage(AppState::new())
        .invoke_handler(tauri::generate_handler![
            cmd_transcribe,
            get_current_model,
            set_model,
            cmd_save_history,
            cmd_get_history,
            cmd_delete_history,
            cmd_type_text,
            cmd_disable_shadow,
            cmd_show_in_folder
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
