// Prevents console window from popping up on Windows
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use hound;
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq)]
pub enum SherpaModelType {
    Transducer,
    Whisper,
    SenseVoice,
}

#[derive(Clone, Debug)]
pub struct SherpaConfig {
    model_type: SherpaModelType,
    encoder: Option<String>,
    decoder: Option<String>,
    joiner: Option<String>,
    sense_voice_model: Option<String>,
    tokens: String,
    _model_name: String,
}
use std::fs::File;
use std::io::Cursor;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use tauri::{Emitter, Manager, State};
use uuid::Uuid;
use whisper_rs::{FullParams, SamplingStrategy, WhisperContext};

pub enum TranscriptionEngine {
    Whisper(WhisperContext),
    Sherpa(SherpaConfig),
    None,
}

pub struct AppState {
    engine: Mutex<TranscriptionEngine>,
    current_model_name: Mutex<String>,
}

impl AppState {
    pub fn new() -> Self {
        Self {
            engine: Mutex::new(TranscriptionEngine::None),
            current_model_name: Mutex::new("None".to_string()),
        }
    }
}

#[tauri::command]
async fn cmd_load_default_model(state: State<'_, AppState>) -> Result<String, String> {
    let model_path = "d:/Personal/voiceapp/src-tauri/whisper.cpp/ggml-base.en.bin";
    println!("Loading default model from: {}", model_path);

    let engine = match WhisperContext::new_with_params(model_path, Default::default()) {
        Ok(ctx) => {
            println!("Default Whisper model loaded successfully!");
            TranscriptionEngine::Whisper(ctx)
        }
        Err(e) => {
            println!("Failed to load default model: {}", e);
            return Err(e.to_string());
        }
    };

    let mut engine_guard = state.engine.lock().map_err(|_| "Failed to lock state")?;
    let mut name_guard = state
        .current_model_name
        .lock()
        .map_err(|_| "Failed to lock name")?;

    *engine_guard = engine;
    *name_guard = "Whisper Base (Local)".to_string();

    Ok("Whisper Base (Local)".to_string())
}

fn read_wav_from_bytes(data: Vec<u8>) -> Result<Vec<f32>, String> {
    let cursor = Cursor::new(data);
    let mut reader =
        hound::WavReader::new(cursor).map_err(|e| format!("WavReader error: {}", e))?;
    let spec = reader.spec();
    println!("Received WAV Spec: {:?}", spec);

    // We expect 16kHz for Whisper and Sherpa
    if spec.sample_rate != 16000 {
        return Err(format!(
            "WAV file must be 16kHz, found {}",
            spec.sample_rate
        ));
    }

    // Convert to mono f32
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

// Helper to write temporary WAV file for sidecar
fn write_temp_wav(samples: &[f32], app_handle: &tauri::AppHandle) -> Result<PathBuf, String> {
    let spec = hound::WavSpec {
        channels: 1,
        sample_rate: 16000,
        bits_per_sample: 16,
        sample_format: hound::SampleFormat::Int,
    };

    let temp_dir = app_handle
        .path()
        .app_cache_dir()
        .map_err(|e| e.to_string())?;
    std::fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;

    let temp_path = temp_dir.join(format!("temp_rec_{}.wav", Uuid::new_v4()));

    let mut writer = hound::WavWriter::create(&temp_path, spec)
        .map_err(|e| format!("Failed to create WAV writer: {}", e))?;

    for &sample in samples {
        let amplitude = i16::MAX as f32;
        let val = (sample * amplitude) as i16;
        writer
            .write_sample(val)
            .map_err(|e| format!("Failed to write sample: {}", e))?;
    }
    writer
        .finalize()
        .map_err(|e| format!("Failed to finalize WAV: {}", e))?;

    Ok(temp_path)
}

#[tauri::command]
async fn cmd_transcribe(
    app: tauri::AppHandle,
    audio_data: Vec<u8>,
    state: State<'_, AppState>,
) -> Result<String, String> {
    println!("Transcribing received bytes: {} bytes", audio_data.len());

    let audio_input = read_wav_from_bytes(audio_data)?;
    println!("Audio loaded, {} samples", audio_input.len());

    let engine_guard = state.engine.lock().map_err(|_| "Failed to lock state")?;

    match &*engine_guard {
        TranscriptionEngine::Whisper(ctx) => {
            let mut state = ctx
                .create_state()
                .map_err(|e| format!("Failed to create Whisper state: {}", e))?;

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
        TranscriptionEngine::Sherpa(config) => {
            // Sanitize samples to remove NaNs or Infinities which might result in bad WAV data
            let safe_samples: Vec<f32> = audio_input
                .iter()
                .map(|&s| if s.is_finite() { s } else { 0.0 })
                .collect();

            let temp_wav_path = write_temp_wav(&safe_samples, &app)?;

            // Ensure path is absolute and clean (no extended prefixes if possible, though Rust handles it)
            // We use dunce to canonicalize if available, but std::fs::canonicalize adds \\?\ on Windows.
            // We'll trust the path but log it.
            let temp_wav_str = temp_wav_path.to_string_lossy().to_string();

            let resource_dir = app
                .path()
                .resource_dir()
                .map_err(|e| format!("Failed to get resource dir: {}", e))?;

            // Construct potential paths
            let mut sidecar_path = resource_dir
                .join("bin")
                .join("sherpa-onnx-x86_64-pc-windows-msvc.exe");

            // Fallback for dev mode
            if !sidecar_path.exists() {
                #[cfg(debug_assertions)]
                {
                    let dev_path = std::env::current_dir()
                        .unwrap_or_default()
                        .join("bin")
                        .join("sherpa-onnx-x86_64-pc-windows-msvc.exe");
                    if dev_path.exists() {
                        sidecar_path = dev_path;
                    }
                }
            }

            println!("Spawning Sherpa process from: {:?}", sidecar_path);

            let mut args = vec![format!("--tokens={}", config.tokens)];

            match config.model_type {
                SherpaModelType::SenseVoice => {
                    if let Some(model) = &config.sense_voice_model {
                        args.push(format!("--sense-voice-model={}", model));
                        args.push("--model-type=sense-voice".to_string()); // Explicit type often helps
                    }
                }
                SherpaModelType::Transducer => {
                    if let (Some(enc), Some(dec), Some(join)) =
                        (&config.encoder, &config.decoder, &config.joiner)
                    {
                        args.push(format!("--encoder={}", enc));
                        args.push(format!("--decoder={}", dec));
                        args.push(format!("--joiner={}", join));

                        // Decoding method for Transducer
                        // Check hotwords (simplified logic relative to before)
                        let hotwords_path = get_hotwords_file_path(&app);
                        if hotwords_path.exists() {
                            args.push(format!(
                                "--hotwords-file={}",
                                hotwords_path.to_string_lossy()
                            ));
                            args.push("--hotwords-score=2.0".to_string());
                            args.push("--decoding-method=modified_beam_search".to_string());
                        } else {
                            args.push("--decoding-method=greedy_search".to_string());
                        }
                    }
                }
                SherpaModelType::Whisper => {
                    if let (Some(enc), Some(dec)) = (&config.encoder, &config.decoder) {
                        args.push(format!("--whisper-encoder={}", enc));
                        args.push(format!("--whisper-decoder={}", dec));
                        args.push("--whisper-language=en".to_string());
                        args.push("--whisper-task=transcribe".to_string());
                        args.push("--model-type=whisper".to_string());
                    }
                }
            }

            args.push("--num-threads=4".to_string());
            args.push(temp_wav_str.clone());

            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x08000000;

            let output = std::process::Command::new(&sidecar_path)
                .args(&args)
                .creation_flags(CREATE_NO_WINDOW)
                .output()
                .map_err(|e| format!("Failed to execute Sherpa process: {}", e))?;

            // Debugging: Check file size
            if let Ok(metadata) = std::fs::metadata(&temp_wav_path) {
                println!("Temp WAV size: {} bytes", metadata.len());
            } else {
                println!("Temp WAV not found before cleanup!");
            }

            // Only remove if successful
            if output.status.success() {
                let _ = std::fs::remove_file(&temp_wav_path);
            } else {
                println!("Keeping temp wav for debugging: {:?}", temp_wav_path);
            }

            let stderr = String::from_utf8_lossy(&output.stderr);
            let stdout = String::from_utf8_lossy(&output.stdout);

            println!("Sherpa Exit Code: {:?}", output.status.code());
            println!("Sherpa Raw Stderr: {}", stderr);
            println!("Sherpa Raw Stdout: {}", stdout);

            if !output.status.success() {
                return Err(format!(
                    "Sherpa exit code: {:?}. Stderr: {}. Stdout: {}",
                    output.status.code(),
                    stderr,
                    stdout
                ));
            }

            // Sherpa-ONNX prints the JSON result to stderr or stdout depending on the build/flags.
            // We search for a line containing "text":
            fn extract_text_from_json(output: &str) -> Option<String> {
                for line in output.lines() {
                    if let Ok(v) = serde_json::from_str::<serde_json::Value>(line) {
                        if let Some(text) = v.get("text").and_then(|t| t.as_str()) {
                            return Some(text.to_string());
                        }
                    }
                }
                None
            }

            // Prioritize searching stderr as observed in logs
            if let Some(text) = extract_text_from_json(&stderr) {
                return Ok(text);
            }
            if let Some(text) = extract_text_from_json(&stdout) {
                return Ok(text);
            }

            // Fallback: Return raw stdout if not empty, otherwise we might have failed silently
            Ok(stdout.trim().to_string())
        }

        TranscriptionEngine::None => Err("No model loaded".to_string()),
    }
}

#[tauri::command]
async fn get_current_model(state: State<'_, AppState>) -> Result<String, String> {
    let name = state.current_model_name.lock().map_err(|_| "Lock error")?;
    Ok(name.clone())
}

#[tauri::command]
async fn set_model(model_id: String) -> Result<(), String> {
    // Deprecated in favor of cmd_load_model, but kept for compatibility
    println!("Frontend requested model change to: {}", model_id);
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
    #[serde(default)]
    duration: f64,
    #[serde(default)]
    app_name: String,
    #[serde(default)]
    processing_time: f64,
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
    duration: f64,
    processing_time: f64,
) -> Result<HistoryItem, String> {
    println!("cmd_save_history called for: {}", filename);
    let path = get_history_file_path(&app);

    let mut history: Vec<HistoryItem> = if path.exists() {
        let file = File::open(&path).map_err(|e| e.to_string())?;
        match serde_json::from_reader(file) {
            Ok(h) => h,
            Err(e) => {
                println!("Failed to parse history.json (using empty): {}", e);
                Vec::new()
            }
        }
    } else {
        Vec::new()
    };

    // Detect active window - Disabled for stability
    let app_name = "Unknown".to_string();

    let item = HistoryItem {
        id: Uuid::new_v4().to_string(),
        filename,
        transcript,
        timestamp: std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs(),
        title,
        duration,
        app_name,
        processing_time,
    };

    history.insert(0, item.clone());

    if let Some(parent) = path.parent() {
        if let Err(e) = std::fs::create_dir_all(parent) {
            println!("Failed to create history directory: {}", e);
        }
    }

    println!("Writing history to: {:?}", path);
    let file = File::create(&path).map_err(|e| e.to_string())?;
    serde_json::to_writer_pretty(file, &history).map_err(|e| e.to_string())?;

    // Emit event for real-time sync
    let _ = app.emit("history-updated", &item);
    println!("History saved successfully.");

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

#[derive(Debug, Serialize, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
struct DashboardStats {
    wpm: u64,
    words_this_week: u64,
    apps_used: u64,
    saved_time: String,
}

#[tauri::command]
fn cmd_get_dashboard_stats(app: tauri::AppHandle) -> Result<DashboardStats, String> {
    let path = get_history_file_path(&app);
    let history: Vec<HistoryItem> = if path.exists() {
        let file = File::open(&path).map_err(|e| e.to_string())?;
        serde_json::from_reader(file).unwrap_or_default()
    } else {
        Vec::new()
    };

    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap()
        .as_secs();
    let one_week_ago = now.saturating_sub(7 * 24 * 60 * 60);

    let weekly_items: Vec<&HistoryItem> = history
        .iter()
        .filter(|item| item.timestamp >= one_week_ago)
        .collect();

    let words_this_week: u64 = weekly_items
        .iter()
        .map(|item| item.transcript.split_whitespace().count() as u64)
        .sum();

    let mut unique_apps = std::collections::HashSet::new();
    for item in &weekly_items {
        if !item.app_name.is_empty() {
            unique_apps.insert(&item.app_name);
        }
    }
    let apps_used = unique_apps.len() as u64;

    let total_words_all_time: u64 = history
        .iter()
        .map(|item| item.transcript.split_whitespace().count() as u64)
        .sum();
    let total_duration_seconds: f64 = history.iter().map(|item| item.duration).sum();
    let total_duration_minutes = total_duration_seconds / 60.0;

    let wpm = if total_duration_minutes > 0.1 {
        (total_words_all_time as f64 / total_duration_minutes).round() as u64
    } else {
        0
    };

    let minutes_saved = (words_this_week as f64 / 40.0).round() as u64;
    let saved_time = format!(
        "{} minute{}",
        minutes_saved,
        if minutes_saved != 1 { "s" } else { "" }
    );

    Ok(DashboardStats {
        wpm,
        words_this_week,
        apps_used,
        saved_time,
    })
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
fn cmd_disable_shadow(window: tauri::WebviewWindow) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use tauri::window::Color;
        let _ = window.set_shadow(false);
        let _ = window.set_decorations(false);
        // Set window background to fully transparent
        let _ = window.set_background_color(Some(Color(0, 0, 0, 0)));
    }
    Ok(())
}

#[tauri::command]
async fn cmd_show_in_folder(path: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer")
            .args(["/select,", &path])
            .spawn()
            .map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
async fn cmd_load_model(model_path: String, state: State<'_, AppState>) -> Result<String, String> {
    println!("Loading new model from: {}", model_path);

    let path = Path::new(&model_path);
    let mut engine_guard = state.engine.lock().map_err(|_| "Failed to lock state")?;
    let mut name_guard = state
        .current_model_name
        .lock()
        .map_err(|_| "Failed to lock name")?;

    if path.is_dir() {
        // Assume Sherpa-ONNX model directory
        println!("Detected directory, initializing Sherpa engine...");

        let tokens = path.join("tokens.txt");
        if !tokens.exists() {
            return Err("Missing tokens.txt".to_string());
        }
        let tokens_str = tokens.to_string_lossy().to_string();

        let model_name = path
            .file_name()
            .unwrap_or_default()
            .to_string_lossy()
            .to_string();

        // 1. Check for SenseVoice
        if path.join("model.int8.onnx").exists() {
            let model_file = path.join("model.int8.onnx");
            let config = SherpaConfig {
                model_type: SherpaModelType::SenseVoice,
                tokens: tokens_str,
                sense_voice_model: Some(model_file.to_string_lossy().to_string()),
                encoder: None,
                decoder: None,
                joiner: None,
                _model_name: model_name.clone(),
            };
            *engine_guard = TranscriptionEngine::Sherpa(config);
            *name_guard = model_name;
            println!("Loaded SenseVoice model!");
            return Ok("SenseVoice Loaded".to_string());
        }
        // 2. Check for Standard Transducer/Whisper
        else {
            // Flexible check for encoder/decoder names (int8 or standard)
            let encoder = if path.join("encoder.int8.onnx").exists() {
                path.join("encoder.int8.onnx")
            } else {
                path.join("encoder.onnx")
            };

            let decoder = if path.join("decoder.int8.onnx").exists() {
                path.join("decoder.int8.onnx")
            } else {
                path.join("decoder.onnx")
            };

            // Joiner is optional (Whisper vs Transducer)
            let joiner_int8 = path.join("joiner.int8.onnx");
            let joiner_std = path.join("joiner.onnx");

            let joiner = if joiner_int8.exists() {
                Some(joiner_int8)
            } else if joiner_std.exists() {
                Some(joiner_std)
            } else {
                None
            };

            if !encoder.exists() || !decoder.exists() {
                return Err(
                    "Missing model files: need either 'model.int8.onnx' (SenseVoice) or 'encoder/decoder' (Transducer/Whisper)".to_string(),
                );
            }

            let model_type = if joiner.is_some() {
                SherpaModelType::Transducer
            } else {
                SherpaModelType::Whisper
            };

            let config = SherpaConfig {
                model_type,
                tokens: tokens_str,
                encoder: Some(encoder.to_string_lossy().to_string()),
                decoder: Some(decoder.to_string_lossy().to_string()),
                joiner: joiner.map(|p| p.to_string_lossy().to_string()),
                sense_voice_model: None,
                _model_name: model_name.clone(),
            };

            *engine_guard = TranscriptionEngine::Sherpa(config);
            *name_guard = model_name;

            println!("Sherpa model loaded successfully!");
            Ok("Sherpa Model Loaded".to_string())
        }
    } else {
        // Assume Whisper .bin file
        println!("Detected file, initializing Whisper engine...");

        let ctx = WhisperContext::new_with_params(&model_path, Default::default())
            .map_err(|e| format!("Failed to load Whisper model: {}", e))?;

        *engine_guard = TranscriptionEngine::Whisper(ctx);
        *name_guard = path
            .file_name()
            .unwrap_or_default()
            .to_string_lossy()
            .into();

        println!("Whisper model loaded successfully!");
        Ok("Whisper Model Loaded".to_string())
    }
}

#[tauri::command]
async fn cmd_download_file(
    app: tauri::AppHandle,
    url: String,
    filename: String,
) -> Result<(), String> {
    use futures_util::StreamExt;
    use std::io::Write;

    let app_data_dir = app.path().app_local_data_dir().map_err(|e| e.to_string())?;
    let models_dir = app_data_dir.join("models");

    // Ensure target directory exists (handles subdirectories if filename contains /)
    let target_path = models_dir.join(&filename);
    if let Some(parent) = target_path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

    println!("Downloading {} to {:?}", url, target_path);

    let client = reqwest::Client::new();
    let res = client
        .get(&url)
        .send()
        .await
        .map_err(|e| format!("Request failed: {}", e))?;

    let total_size = res.content_length().unwrap_or(0);

    let mut file =
        File::create(&target_path).map_err(|e| format!("Failed to create file: {}", e))?;
    let mut downloaded: u64 = 0;
    let mut stream = res.bytes_stream();

    while let Some(item) = stream.next().await {
        let chunk = item.map_err(|e| format!("Error while downloading chunk: {}", e))?;
        file.write_all(&chunk)
            .map_err(|e| format!("Error while writing to file: {}", e))?;

        downloaded += chunk.len() as u64;

        if total_size > 0 {
            let progress = (downloaded as f64 / total_size as f64) * 100.0;
            // Emit progress event
            // We use a specific event name that includes the filename or ID so frontend can filter
            // Structure: "download-progress", { filename: "...", progress: 50.5 }
            let _ = app.emit(
                "download-progress",
                serde_json::json!({
                    "filename": filename,
                    "progress": progress as u64, // simplified to integer %
                    "total": total_size,
                    "downloaded": downloaded
                }),
            );
        }
    }

    println!("Download complete: {:?}", target_path);
    Ok(())
}

fn get_hotwords_file_path(app: &tauri::AppHandle) -> PathBuf {
    let mut path = app
        .path()
        .app_local_data_dir()
        .expect("failed to get app local data dir");
    path.push("hotwords.txt");
    path
}

#[tauri::command]
fn cmd_get_vocabulary(app: tauri::AppHandle) -> Result<Vec<String>, String> {
    let path = get_hotwords_file_path(&app);
    if !path.exists() {
        return Ok(Vec::new());
    }
    let content = std::fs::read_to_string(path).map_err(|e| e.to_string())?;
    Ok(content.lines().map(|s| s.to_string()).collect())
}

#[tauri::command]
fn cmd_save_vocabulary(app: tauri::AppHandle, words: Vec<String>) -> Result<(), String> {
    let path = get_hotwords_file_path(&app);
    let content = words.join("\n");
    std::fs::write(path, content).map_err(|e| e.to_string())?;
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
        .setup(|app| {
            use tauri::{Manager, WebviewUrl, WebviewWindowBuilder};

            // Programmatically create the widget window - CLEAN PRESET
            // Using "widget_overlay" label
            let widget_window = WebviewWindowBuilder::new(
                app,
                "widget_overlay",
                WebviewUrl::App("/widget_overlay".into()),
            )
            .title("")
            .inner_size(150.0, 60.0)
            .decorations(false)
            .transparent(true)
            .always_on_top(true)
            .skip_taskbar(true)
            .shadow(false)
            .resizable(false) // FINAL FIX: Must be false to remove title bar
            .visible(false)
            .build()
            .expect("Failed to create widget window");

            // Explicitly clear background - THE KEY FIX for Windows
            use tauri::window::Color;
            let _ = widget_window.set_background_color(Some(Color(0, 0, 0, 0)));
            // Ensure decorations are definitely off
            let _ = widget_window.set_decorations(false);

            // --- NUCLEAR OPTION: Direct WinAPI Style Removal ---
            #[cfg(target_os = "windows")]
            {
                use windows::Win32::Foundation::HWND;
                use windows::Win32::UI::WindowsAndMessaging::{
                    GetWindowLongW, SetWindowLongW, GWL_STYLE, WS_BORDER, WS_CAPTION, WS_DLGFRAME,
                    WS_MAXIMIZEBOX, WS_MINIMIZEBOX, WS_SYSMENU, WS_THICKFRAME,
                };

                if let Ok(hwnd) = widget_window.hwnd() {
                    let hwnd = HWND(hwnd.0 as _);
                    unsafe {
                        let mut style = GetWindowLongW(hwnd, GWL_STYLE);
                        // Strip all border/caption bits forcefully
                        style &= !(WS_CAPTION.0 as i32
                            | WS_THICKFRAME.0 as i32
                            | WS_BORDER.0 as i32
                            | WS_DLGFRAME.0 as i32
                            | WS_SYSMENU.0 as i32
                            | WS_MAXIMIZEBOX.0 as i32
                            | WS_MINIMIZEBOX.0 as i32);
                        SetWindowLongW(hwnd, GWL_STYLE, style);
                    }
                }
            }

            println!("Widget window created (Clean Preset + Nuclear WinAPI Fix)");

            // Delayed show logic
            let w_clone = widget_window.clone();
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_millis(200));
                w_clone.show().unwrap();
            });

            Ok(())
        })
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { .. } = event {
                if window.label() == "main" {
                    window.app_handle().exit(0);
                }
            }
        })
        .invoke_handler(tauri::generate_handler![
            cmd_transcribe,
            get_current_model,
            set_model,
            cmd_save_history,
            cmd_get_history,
            cmd_get_dashboard_stats,
            cmd_delete_history,
            cmd_type_text,
            cmd_disable_shadow,
            cmd_show_in_folder,
            cmd_load_model,
            cmd_load_default_model,
            cmd_download_file,
            cmd_get_vocabulary,
            cmd_save_vocabulary
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
