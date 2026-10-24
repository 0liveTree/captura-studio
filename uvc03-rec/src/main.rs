use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::Duration;

use uvc03_rec::{record, Config, Status};

/// Lê comandos do stdin: PAUSE, RESUME, STOP. Qualquer outra linha (ou Enter) para.
fn stdin_control(stop: Arc<AtomicBool>) {
    use std::io::BufRead;
    let stdin = std::io::stdin();
    let mut line = String::new();
    loop {
        line.clear();
        let n = stdin.lock().read_line(&mut line).unwrap_or(0);
        if n == 0 {
            break;
        }
        match line.trim().to_ascii_uppercase().as_str() {
            "PAUSE" => {
                uvc03_rec::PAUSED.store(true, Ordering::SeqCst);
                eprintln!("PAUSED");
            }
            "RESUME" => {
                uvc03_rec::PAUSED.store(false, Ordering::SeqCst);
                eprintln!("RESUMED");
            }
            _ => {
                stop.store(true, Ordering::SeqCst);
                break;
            }
        }
    }
}

fn main() {
    let mut cfg = Config::default();
    let mut preview_mode = false;
    let mut list_devices = false;
    let mut list_encoders_flag = false;
    let mut caps: Option<usize> = None;
    let mut no_stdin = false;
    let mut meter = false;
    let mut split_min: f64 = 0.0;
    let mut port: u16 = 45900;
    let mut args = std::env::args().skip(1);

    while let Some(a) = args.next() {
        let mut val = || args.next().unwrap_or_default();
        match a.as_str() {
            "--out" => cfg.out = val(),
            "--w" => cfg.w = val().parse().unwrap_or(1920),
            "--h" => cfg.h = val().parse().unwrap_or(1080),
            "--fps" => cfg.fps = val().parse().unwrap_or(30),
            "--bitrate" => cfg.bitrate = val().parse().unwrap_or(12_000_000),
            "--codec" => {
                let v = val().to_lowercase();
                cfg.hevc = v == "hevc" || v == "h265" || v == "h.265";
            }
            "--profile" => {
                cfg.profile = match val().to_lowercase().as_str() {
                    "baseline" => 66,
                    "main" => 77,
                    _ => 100,
                }
            }
            "--container" => {
                let v = val().to_lowercase();
                cfg.container = match v.as_str() {
                    "mkv" | "matroska" => "mkv".into(),
                    "ts" | "mpegts" => "ts".into(),
                    _ => "mp4".into(),
                };
                if !cfg.out.contains('.') {
                    cfg.out = format!("{}.{}", cfg.out, cfg.container);
                }
            }
            "--sw" => cfg.hw = false,
            "--no-audio" => cfg.audio = false,
            "--pcm" => cfg.pcm = true,
            "--audio-codec" => cfg.audio_codec = val().to_lowercase(),
            "--vbr" => cfg.cbr = false,
            "--gop" => cfg.gop = val().parse().unwrap_or(60),
            "--format" => cfg.format = val().to_lowercase(),
            "--color-space" => cfg.color_space = val().to_lowercase(),
            "--color-range" => cfg.color_range = val().to_lowercase(),
            "--preview" => preview_mode = true,
            "--preview-port" => cfg.preview_port = val().parse().ok(),
            "--raw-video" => cfg.raw_video = true,
            "--port" => port = val().parse().unwrap_or(45900),
            "--list" => list_devices = true,
            "--encoders" => list_encoders_flag = true,
            "--no-stdin" => no_stdin = true,
            "--meter" => meter = true,
            "--caps" => caps = val().parse().ok(),
            "--split-min" => split_min = val().parse().unwrap_or(0.0),
            "--audio-kbps" => cfg.audio_kbps = val().parse().unwrap_or(192),
            "--audio-rate" => cfg.audio_rate = val().parse().unwrap_or(48_000),
            "--audio-channels" => cfg.audio_channels = val().parse().unwrap_or(2),
            "--audio-bits" => cfg.audio_bits = val().parse().unwrap_or(24),
            "--seconds" => cfg.seconds = val().parse().ok(),
            "--video-link" => cfg.video_link = val(),
            "--audio-id" => cfg.audio_id = val(),
            "--device" => {
                let n: usize = val().parse().unwrap_or(0);
                if let Ok(v) = uvc03_rec::list_video_devices() {
                    if let Some(d) = v.get(n) {
                        cfg.video_link = d.link.clone();
                    }
                }
            }
            "--audio-device" => {
                let n: usize = val().parse().unwrap_or(0);
                if let Ok(v) = uvc03_rec::list_audio_devices() {
                    if let Some(d) = v.get(n) {
                        cfg.audio_id = d.id.clone();
                    }
                }
            }
            "-h" | "--help" => {
                println!("uvc03-rec [--out f.mp4] [--container mp4|mkv|ts] [--w 1920] [--h 1080] [--fps 30]");
                println!("          [--bitrate 12000000] [--codec h264|hevc] [--profile baseline|main|high]");
                println!("          [--sw] [--no-audio] [--audio-kbps 192] [--seconds N]");
                println!("          [--list] [--caps N] [--device N] [--audio-device N]");
                println!("          [--split-min M] [--format auto|nv12|mjpeg|yuy2|rgb32]");
                println!("          [--preview --port 45900]");
                println!("          [--raw-video --out f.mp4 [--preview-port 45901] [--seconds N]]");
                println!("            (raw: NV12 no stdout + <out>.wav; parar com Enter no stdin)");
                std::process::exit(0);
            }
            _ => {}
        }
    }

    if list_encoders_flag {
        for (codec, name, hw, vendor) in uvc03_rec::list_encoders() {
            println!("E\t{codec}\t{name}\t{}\t{vendor}", if hw { "HW" } else { "SW" });
        }
        return;
    }

    if list_devices {
        match uvc03_rec::list_video_devices() {
            Ok(v) => {
                for (i, d) in v.iter().enumerate() {
                    println!("V\t{}\t{}\t{}", i, d.name, d.link);
                }
            }
            Err(e) => eprintln!("ERRO video: {e}"),
        }
        match uvc03_rec::list_audio_devices() {
            Ok(v) => {
                for (i, d) in v.iter().enumerate() {
                    println!("A\t{}\t{}\t{}", i, d.name, d.id);
                }
            }
            Err(e) => eprintln!("ERRO audio: {e}"),
        }
        return;
    }

    if let Some(n) = caps {
        match uvc03_rec::list_video_devices() {
            Ok(vs) => {
                if let Some(d) = vs.get(n) {
                    match uvc03_rec::video_modes(&d.link) {
                        Ok(ms) => {
                            for m in ms {
                                println!("M\t{}\t{}\t{}\t{}", m.w, m.h, m.fps, m.fmt);
                            }
                        }
                        Err(e) => eprintln!("ERRO modos: {e}"),
                    }
                }
            }
            Err(e) => eprintln!("ERRO: {e}"),
        }
        return;
    }

    if preview_mode {
        if let Err(e) = uvc03_rec::preview(&cfg, port) {
            eprintln!("ERRO: {e}");
        }
        return;
    }

    let stop = Arc::new(AtomicBool::new(false));
    let st = Arc::new(Status::new());

    if meter {
        let s = stop.clone();
        let stm = st.clone();
        let raw = cfg.raw_video;
        thread::spawn(move || {
            use std::io::Write;
            while !s.load(Ordering::Relaxed) {
                let db = f32::from_bits(stm.level.load(Ordering::Relaxed));
                if raw {
                    // em modo raw o stdout pertence ao vídeo
                    eprintln!("LVL {:.1}", db);
                } else {
                    println!("LVL {:.1}", db);
                    let _ = std::io::stdout().flush();
                }
                thread::sleep(Duration::from_millis(100));
            }
        });
    }

    if split_min > 0.0 {
        let secs = (split_min * 60.0).max(1.0) as u64;
        let base = cfg.out.clone();
        let path = std::path::Path::new(&base);
        let dir = path.parent().map(|p| p.to_path_buf()).unwrap_or_default();
        let stem = path
            .file_stem()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "UVC03".into());
        let ext = path
            .extension()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| cfg.container.clone());
        if !no_stdin {
            let s = stop.clone();
            thread::spawn(move || stdin_control(s));
        }
        println!("A gravar em segmentos de {}s ... prime Enter para parar.", secs);
        let mut idx = 1u32;
        loop {
            let mut c2 = cfg.clone();
            c2.out = dir
                .join(format!("{}_{:03}.{}", stem, idx, ext))
                .to_string_lossy()
                .to_string();
            c2.seconds = Some(secs);
            println!("SEG\t{}", c2.out);
            let seg_stop = Arc::new(AtomicBool::new(false));
            {
                let us = stop.clone();
                let ss = seg_stop.clone();
                thread::spawn(move || {
                    while !ss.load(Ordering::SeqCst) {
                        if us.load(Ordering::SeqCst) {
                            ss.store(true, Ordering::SeqCst);
                            break;
                        }
                        thread::sleep(Duration::from_millis(50));
                    }
                });
            }
            let s2 = seg_stop.clone();
            let st2 = st.clone();
            let c3 = c2.clone();
            let h = thread::spawn(move || record(&c3, &s2, &st2));
            let joined = h.join();
            let frames = match joined {
                Ok(Ok(s)) => s.frames,
                Ok(Err(e)) => {
                    eprintln!("ERRO: {e}");
                    break;
                }
                Err(_) => break,
            };
            if stop.load(Ordering::SeqCst) || frames == 0 {
                break;
            }
            idx += 1;
        }
        println!("OK divisao concluida ({} segmento(s))", idx);
        return;
    }

    if cfg.raw_video {
        // O stdout é o vídeo em bruto: nada de texto por lá.
        // A paragem pode vir do --seconds ou de uma linha no stdin (a app).
        if !no_stdin {
            let s = stop.clone();
            thread::spawn(move || stdin_control(s));
        }
        match uvc03_rec::record_raw(&cfg, &stop) {
            Ok(s) => eprintln!(
                "OK  frames={} elapsed={:.1}s audio={} raw=true",
                s.frames,
                s.elapsed.as_secs_f64(),
                s.audio
            ),
            Err(e) => eprintln!("ERRO: {e}"),
        }
        return;
    }

    let (c2, s2, st2) = (cfg.clone(), stop.clone(), st.clone());
    let handle = thread::spawn(move || record(&c2, &s2, &st2));

    if cfg.seconds.is_some() {
        // A duração é controlada pelo gravador e começa no primeiro frame.
        // Continuamos a aceitar Enter para permitir parar durante o arranque.
        if !no_stdin {
            let s = stop.clone();
            thread::spawn(move || stdin_control(s));
        }
    } else {
        println!("A gravar para {} ... prime Enter para parar.", cfg.out);
        stdin_control(stop.clone());
    }

    match handle.join() {
        Ok(Ok(s)) => println!(
            "OK  frames={} audio_packets={} elapsed={:.1}s audio={} container={}",
            s.frames,
            s.audio_packets,
            s.elapsed.as_secs_f64(),
            s.audio,
            cfg.container
        ),
        Ok(Err(e)) => eprintln!("ERRO: {e}"),
        Err(_) => eprintln!("ERRO: thread terminou inesperadamente"),
    }
}



