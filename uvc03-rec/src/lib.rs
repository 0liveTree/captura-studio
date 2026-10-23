use std::sync::atomic::{AtomicBool, AtomicI64, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::process::Command;
use std::time::{Duration, Instant};

use windows::core::{HSTRING, HRESULT, Interface, VARIANT};
use windows::Win32::Media::MediaFoundation::*;
use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_MULTITHREADED};

pub const MF_VERSION: u32 = 0x0002_0070;

pub const VIDEO_LINK: &str = r"\\?\usb#vid_1de1&pid_f113&mi_00#7&2378a992&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global";
pub const AUDIO_ID: &str = r"{0.0.1.00000000}.{55928d63-c8e8-4731-9374-6fb5adfd6b12}";

const FIRST_VIDEO: u32 = 0xFFFF_FFFC;
const FIRST_AUDIO: u32 = 0xFFFF_FFFD;
const SIGNAL_STALL_MS: u64 = 2_500;
const FIRST_FRAME_TIMEOUT_MS: u64 = 40_000;
const RECOVERY_FRAME_TIMEOUT_MS: u64 = 40_000;
const RECOVERY_FLUSH_TIMEOUT_MS: u64 = 7_000;
const MAX_VIDEO_RECOVERIES: u64 = 2;

#[derive(Clone, Debug)]
pub struct Config {
    pub out: String,
    pub w: u32,
    pub h: u32,
    pub fps: u32,
    pub bitrate: u32,
    pub hevc: bool,
    pub profile: u32,
    pub hw: bool,
    pub audio: bool,
    pub audio_kbps: u32,
    pub audio_codec: String,
    pub audio_rate: u32,
    pub audio_channels: u16,
    pub audio_bits: u16,
    pub seconds: Option<u64>,
    pub container: String,
    pub cbr: bool,
    pub gop: u32,
    pub pcm: bool,
    pub video_link: String,
    pub audio_id: String,
    pub format: String,
    pub color_space: String,
    pub preview_port: Option<u16>,
    pub raw_video: bool,
    pub color_range: String,
}

impl Default for Config {
    fn default() -> Self {
        Config {
            out: "uvc03.mp4".into(),
            w: 1920,
            h: 1080,
            fps: 30,
            bitrate: 12_000_000,
            hevc: false,
            profile: 100,
            hw: true,
            audio: true,
            audio_kbps: 192,
            audio_codec: "aac".into(),
            audio_rate: 48_000,
            audio_channels: 2,
            audio_bits: 24,
            seconds: None,
            container: "mp4".into(),
            cbr: true,
            gop: 60,
            pcm: false,
            video_link: VIDEO_LINK.into(),
            audio_id: AUDIO_ID.into(),
            format: "auto".into(),
            color_space: "709".into(),
            preview_port: None,
            raw_video: false,
            color_range: "limited".into(),
        }
    }
}

pub struct Status {
    pub running: AtomicBool,
    pub finished: AtomicBool,
    pub frames: AtomicU64,
    pub audio_packets: AtomicU64,
    pub audio: AtomicBool,
    pub error: Mutex<Option<String>>,
    pub started: Mutex<Option<Instant>>,
    pub path: Mutex<String>,
    pub level: Arc<AtomicU32>,
}

impl Status {
    pub fn new() -> Self {
        Status {
            running: AtomicBool::new(false),
            finished: AtomicBool::new(false),
            frames: AtomicU64::new(0),
            audio_packets: AtomicU64::new(0),
            audio: AtomicBool::new(false),
            error: Mutex::new(None),
            started: Mutex::new(None),
            path: Mutex::new(String::new()),
            level: Arc::new(AtomicU32::new((-60.0f32).to_bits())),
        }
    }
    pub fn elapsed(&self) -> Duration {
        self.started
            .lock()
            .ok()
            .and_then(|g| *g)
            .map(|t| t.elapsed())
            .unwrap_or_default()
    }
}

pub struct Stats {
    pub frames: u64,
    pub audio_packets: u64,
    pub elapsed: Duration,
    pub audio: bool,
}

/// Guarda o pico do buffer em dBFS (f32 bits) para o medidor da GUI.
pub fn set_level_peak(level: &AtomicU32, bytes: &[u8], bits: u32) {
    let mut peak: i32 = 0;
    if bits == 24 {
        for c in bytes.chunks_exact(3) {
            let v = i32::from_ne_bytes([0, c[0], c[1], c[2]]) >> 8;
            let a = v.abs();
            if a > peak {
                peak = a;
            }
        }
        let db = 20.0 * (peak as f32 / 8_388_608.0).max(1e-6).log10();
        level.store(db.to_bits(), Ordering::Relaxed);
    } else {
        for c in bytes.chunks_exact(2) {
            let v = i16::from_le_bytes([c[0], c[1]]) as i32;
            let a = v.abs();
            if a > peak {
                peak = a;
            }
        }
        let db = 20.0 * (peak as f32 / 32_768.0).max(1e-6).log10();
        level.store(db.to_bits(), Ordering::Relaxed);
    }
}

struct SendReader(IMFSourceReader);
unsafe impl Send for SendReader {}
impl Clone for SendReader {
    fn clone(&self) -> Self { Self(self.0.clone()) }
}

struct SendSource(IMFMediaSource);
unsafe impl Send for SendSource {}
impl SendReader {
    fn read_sample(
        &self,
        stream: u32,
    ) -> windows::core::Result<(u32, i64, Option<IMFSample>)> {
        let mut flags = 0u32;
        let mut ts = 0i64;
        let mut sample: Option<IMFSample> = None;
        unsafe {
            self.0.ReadSample(
                stream,
                0,
                None,
                Some(&mut flags),
                Some(&mut ts),
                Some(&mut sample),
            )?;
        }
        Ok((flags, ts, sample))
    }

    fn flush(&self, stream: u32) {
        unsafe { let _ = self.0.Flush(stream); }
    }
}

struct SharedVideoReader(Mutex<Option<SendReader>>);
impl SharedVideoReader {
    fn new(reader: IMFSourceReader) -> Self {
        Self(Mutex::new(Some(SendReader(reader))))
    }

    fn replace(&self, reader: Option<IMFSourceReader>) {
        if let Ok(mut current) = self.0.lock() {
            *current = reader.map(SendReader);
        }
    }

    fn flush(&self, stream: u32) {
        let reader = self.0.lock().ok().and_then(|g| g.as_ref().cloned());
        if let Some(reader) = reader { reader.flush(stream); }
    }
}

struct ComApartment(bool);
impl ComApartment {
    fn mta() -> Self {
        Self(unsafe { CoInitializeEx(None, COINIT_MULTITHREADED).is_ok() })
    }
}
impl Drop for ComApartment {
    fn drop(&mut self) {
        if self.0 { unsafe { CoUninitialize(); } }
    }
}

unsafe fn open_raw_video_reader(cfg: &Config) -> windows::core::Result<(IMFMediaSource, IMFSourceReader)> {
    let mut va_opt: Option<IMFAttributes> = None;
    MFCreateAttributes(&mut va_opt, 2)?;
    let va = va_opt.unwrap();
    va.SetGUID(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID)?;
    va.SetString(
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
        &HSTRING::from(cfg.video_link.as_str()),
    )?;
    let src = MFCreateDeviceSource(&va)?;

    let mut ra_opt: Option<IMFAttributes> = None;
    MFCreateAttributes(&mut ra_opt, 2)?;
    let ra = ra_opt.unwrap();
    ra.SetUINT32(&windows::core::GUID::from_u128(0x0d41e4a1_3d9e_4f9b_a1ea_0d1e2b379b8b), 1)?;
    ra.SetUINT32(&windows::core::GUID::from_u128(0xfb394f3d_ccf1_42ee_bbb3_f9b845d5681d), 1)?;
    let reader = MFCreateSourceReaderFromMediaSource(&src, Some(&ra))?;

    let nv12 = MFCreateMediaType()?;
    nv12.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
    nv12.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_NV12)?;
    nv12.SetUINT64(&MF_MT_FRAME_SIZE, ((cfg.w as u64) << 32) | cfg.h as u64)?;
    nv12.SetUINT64(&MF_MT_FRAME_RATE, ((cfg.fps as u64) << 32) | 1)?;
    nv12.SetUINT32(&MF_MT_INTERLACE_MODE, 2)?;
    let _ = apply_color(&nv12, cfg);
    reader.SetCurrentMediaType(FIRST_VIDEO, None, &nv12)?;
    Ok((src, reader))
}

struct SendWriter(IMFSinkWriter);
unsafe impl Send for SendWriter {}
unsafe impl Sync for SendWriter {}

/// Runs the recorder until `stop` is set. Blocking; call from a thread.

/// Escala do preview: no máximo ~1280 px de largura (o cartão da app é bem menor).
fn preview_scale(w: u32) -> u32 {
    ((w.max(1) + 1279) / 1280).max(1)
}

/// NV12 → BGRX (o mesmo layout que o `MFVideoFormat_RGB32` do Media Foundation,
/// que a app escreve directamente num WriteableBitmap BGRA8). Reduz por um factor
/// inteiro para o preview não gastar CPU/largura de banda a 1080p.
fn nv12_to_bgrx(
    src: &[u8],
    w: usize,
    h: usize,
    scale: usize,
    cs2020: bool,
    full: bool,
) -> (usize, usize, Vec<u8>) {
    let scale = scale.max(1);
    let w2 = (w / scale).max(1);
    let h2 = (h / scale).max(1);
    let mut out = vec![0u8; w2 * h2 * 4];
    let ysize = w * h;
    if src.len() < ysize + ysize / 2 {
        return (w2, h2, out);
    }
    let (yplane, uvplane) = src.split_at(ysize);
    let (lo, hi) = if full { (0.0f32, 255.0f32) } else { (16.0f32, 235.0f32) };
    let sc = 255.0f32 / (hi - lo);
    for oy in 0..h2 {
        let sy = (oy * scale).min(h - 1);
        let yrow = sy * w;
        let uvrow = (sy / 2) * w;
        let orow = oy * w2 * 4;
        for ox in 0..w2 {
            let sx = (ox * scale).min(w - 1);
            let y = yplane[yrow + sx] as f32;
            let uvx = (sx / 2) * 2;
            let u = uvplane[uvrow + uvx] as f32 - 128.0;
            let v = uvplane[uvrow + uvx + 1] as f32 - 128.0;
            let yv = (y - lo) * sc;
            let (r, g, b) = if cs2020 {
                (yv + 1.4746 * v, yv - 0.16455 * u - 0.57135 * v, yv + 1.8814 * u)
            } else {
                (yv + 1.5748 * v, yv - 0.1873 * u - 0.4681 * v, yv + 1.8556 * u)
            };
            let o = orow + ox * 4;
            out[o] = b.clamp(0.0, 255.0) as u8;
            out[o + 1] = g.clamp(0.0, 255.0) as u8;
            out[o + 2] = r.clamp(0.0, 255.0) as u8;
            out[o + 3] = 0;
        }
    }
    (w2, h2, out)
}

/// Preview servido pelo próprio processo que tem a placa.
///
/// Nesta placa, dois leitores MF em simultâneo disputam o dispositivo
/// (`0xC00D3EA3`), por isso o preview não pode abrir um segundo leitor durante a
/// gravação: copia os frames NV12 já lidos para um thread que converte e envia.
/// Se não houver cliente ligado, não há cópia nem conversão.
struct PreviewHub {
    vtx: Option<std::sync::mpsc::SyncSender<Vec<u8>>>,
    atx: Option<std::sync::mpsc::SyncSender<Vec<u8>>>,
    von: Arc<AtomicBool>,
    aon: Arc<AtomicBool>,
}

impl PreviewHub {
    fn start(cfg: &Config, port: u16) -> Option<PreviewHub> {
        let lv = std::net::TcpListener::bind(("127.0.0.1", port)).ok()?;
        let (vtx, vrx) = std::sync::mpsc::sync_channel::<Vec<u8>>(1);
        let von = Arc::new(AtomicBool::new(false));
        {
            let von = von.clone();
            let (pw, ph, pscale) = (
                cfg.w.max(1) as usize,
                cfg.h.max(1) as usize,
                preview_scale(cfg.w) as usize,
            );
            let cs2020 = cfg.color_space.eq_ignore_ascii_case("2020");
            let full = cfg.color_range.eq_ignore_ascii_case("full");
            thread::spawn(move || {
                let Ok((mut s, _)) = lv.accept() else { return };
                von.store(true, Ordering::SeqCst);
                let _ = s.set_nodelay(true);
                use std::io::Write;
                while let Ok(buf) = vrx.recv() {
                    let (w2, h2, rgb) = nv12_to_bgrx(&buf, pw, ph, pscale, cs2020, full);
                    let mut hdr = [0u8; 12];
                    hdr[0..4].copy_from_slice(&(w2 as u32).to_le_bytes());
                    hdr[4..8].copy_from_slice(&(h2 as u32).to_le_bytes());
                    hdr[8..12].copy_from_slice(&(rgb.len() as u32).to_le_bytes());
                    if s.write_all(&hdr).is_err() || s.write_all(&rgb).is_err() {
                        break;
                    }
                }
                von.store(false, Ordering::SeqCst);
            });
        }

        let (atx, aon) = if cfg.audio {
            let la = std::net::TcpListener::bind(("127.0.0.1", port + 1)).ok()?;
            let (atx, arx) = std::sync::mpsc::sync_channel::<Vec<u8>>(16);
            let aon = Arc::new(AtomicBool::new(false));
            let aon2 = aon.clone();
            thread::spawn(move || {
                let Ok((mut s, _)) = la.accept() else { return };
                aon2.store(true, Ordering::SeqCst);
                let _ = s.set_nodelay(true);
                use std::io::Write;
                while let Ok(buf) = arx.recv() {
                    if s.write_all(&buf).is_err() {
                        break;
                    }
                }
                aon2.store(false, Ordering::SeqCst);
            });
            (Some(atx), aon)
        } else {
            (None, Arc::new(AtomicBool::new(false)))
        };

        Some(PreviewHub { vtx: Some(vtx), atx, von, aon })
    }

    fn video(&self, bytes: &[u8]) {
        if !self.von.load(Ordering::Relaxed) {
            return;
        }
        if let Some(tx) = &self.vtx {
            let _ = tx.try_send(bytes.to_vec());
        }
    }

    unsafe fn video_sample(&self, s: &IMFSample) {
        if !self.von.load(Ordering::Relaxed) {
            return;
        }
        if let Ok(buf) = s.ConvertToContiguousBuffer() {
            let mut ptr: *mut u8 = std::ptr::null_mut();
            let mut max = 0u32;
            let mut cur = 0u32;
            if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_ok() {
                let slice = std::slice::from_raw_parts(ptr, cur as usize);
                self.video(slice);
                let _ = buf.Unlock();
            }
        }
    }

    /// Envia PCM para a porta de preview de áudio, convertendo 24-bit → 16-bit
    /// (o NAudio da app usa PCM16 na taxa/canais da captura).
    fn audio(&self, bytes: &[u8], bits: u16) {
        if !self.aon.load(Ordering::Relaxed) {
            return;
        }
        let Some(tx) = &self.atx else { return };
        if bits == 16 {
            let _ = tx.try_send(bytes.to_vec());
        } else if bits == 24 {
            let mut out = Vec::with_capacity(bytes.len() / 3 * 2);
            for c in bytes.chunks_exact(3) {
                out.push(c[1]);
                out.push(c[2]);
            }
            let _ = tx.try_send(out);
        }
    }
}

/// Captura NV12 em bruto para stdout (consumido pelo FFmpeg com -f rawvideo).
/// Se `cfg.audio`, o PCM é escrito em `<out>.wav` numa thread própria.
/// Se `cfg.preview_port`, o preview continua a ser servido por este processo
/// (o único que pode falar com a placa): vídeo em `port`, áudio em `port + 1`.
/// O stdout fica exclusivamente para o vídeo em bruto — o estado vai para stderr.
pub fn record_raw(cfg: &Config, stop: &Arc<AtomicBool>) -> windows::core::Result<Stats> {
    unsafe {
        eprintln!("CAPTURE_WAIT w={} h={} target={}", cfg.w, cfg.h, cfg.fps);
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        MFStartup(MF_VERSION, 0)?;

        // O preview vem dos frames que este processo lê (ver PreviewHub).
        // Abre os sockets antes do preflight para a app poder ligar já.
        let hub = cfg
            .preview_port
            .and_then(|p| PreviewHub::start(cfg, p))
            .map(Arc::new);

        // Preflight: enumerar os modos nativos força o driver a largar o stream
        // anterior (a UVC03 por vezes mantém a resolução da sessão anterior).
        let _ = video_modes(&cfg.video_link);

        let (src, reader) = open_raw_video_reader(cfg)?;
        let reader_control = Arc::new(SharedVideoReader::new(reader.clone()));

        let dbg = std::env::var("UVC03_RAW_DEBUG").is_ok();
        if dbg {
            if let Ok(mt) = reader.GetCurrentMediaType(FIRST_VIDEO) {
                let sub = mt.GetGUID(&MF_MT_SUBTYPE).unwrap_or_default();
                let size = mt.GetUINT64(&MF_MT_FRAME_SIZE).unwrap_or(0);
                let rate = mt.GetUINT64(&MF_MT_FRAME_RATE).unwrap_or(0);
                eprintln!(
                    "DEBUG mt sub={sub:?} {}x{}@{}/{}",
                    size >> 32,
                    size & 0xFFFF_FFFF,
                    rate >> 32,
                    rate & 0xFFFF_FFFF
                );
            }
        }
        let t0 = MFGetSystemTime();
        let vstart = Arc::new(AtomicI64::new(-1));
        let astart = Arc::new(AtomicI64::new(-1));

        // ---------- áudio: WAV ao lado do vídeo ----------
        // Nível para o medidor da app (LVL no stderr, como no caminho MF).
        let level = Arc::new(AtomicU32::new((-60f32).to_bits()));
        {
            let lvl = level.clone();
            let st = stop.clone();
            thread::spawn(move || {
                while !st.load(Ordering::Relaxed) {
                    let db = f32::from_bits(lvl.load(Ordering::Relaxed));
                    eprintln!("LVL {:.1}", db);
                    thread::sleep(std::time::Duration::from_millis(100));
                }
            });
        }
        let audio_thread = if cfg.audio {
            let wav_path = format!("{}.wav", cfg.out);
            match open_audio_pcm(cfg) {
                Ok((rd, fmt)) => {
                    let stop_a = stop.clone();
                    let a0 = astart.clone();
                    let wpath = wav_path.clone();
                    let hub_a = hub.clone();
                    let level_a = level.clone();
                    eprintln!(
                        "AUDIO wav={} rate={} ch={} bits={}",
                        wpath, fmt.rate, fmt.ch, fmt.bits
                    );
                    Some(thread::spawn(move || {
                        let rd = rd;
                        let reader = rd.0;
                        write_wav_pcm(&reader, fmt, &wpath, &stop_a, Some(&level_a), hub_a, || {
                            a0.store((MFGetSystemTime() - t0) / 10_000, Ordering::Relaxed);
                        })
                    }))
                }
                Err(e) => {
                    eprintln!("AVISO: audio indisponivel: {e}");
                    None
                }
            }
        } else {
            None
        };

        // Publica o SYNC assim que houver os dois offsets (a app pode precisar
        // dele mesmo que o motor seja morto antes do fim).
        {
            let vs = vstart.clone();
            let as_ = astart.clone();
            let st = stop.clone();
            thread::spawn(move || {
                let mut printed = false;
                while !st.load(Ordering::Relaxed) {
                    if !printed {
                        let v = vs.load(Ordering::Relaxed);
                        let a = as_.load(Ordering::Relaxed);
                        if v >= 0 && a >= 0 {
                            eprintln!("SYNC v={v} a={a} audio_bytes=0");
                            printed = true;
                        }
                    }
                    thread::sleep(Duration::from_millis(50));
                }
            });
        }

        let started = Instant::now();
        let mut first_frame: Option<Instant> = None;
        let first_frame_watch = Arc::new(Mutex::new(None::<Instant>));
        let mut frames = 0u64;
        let expected_frame = (cfg.w as usize * cfg.h as usize * 3) / 2;
        let mut dbg_ts0 = 0i64;
        let mut dbg_ts1 = 0i64;
        // O rawvideo do FFmpeg não tem timestamps: a placa pode entregar mais ou
        // menos fps do que o pedido (e, quando perde frames, os timestamps do MF
        // podem continuar a fingir 30 fps). O relógio é a referência certa para
        // manter o vídeo com a mesma duração do áudio.
        let fps_i = cfg.fps.max(1) as i64;
        let mut next_idx: i64 = 0;
        let source_frames = Arc::new(AtomicU64::new(0));
        let output_frames = Arc::new(AtomicU64::new(0));
        let dropped_frames = Arc::new(AtomicU64::new(0));
        let repeated_frames = Arc::new(AtomicU64::new(0));
        let last_source_ms = Arc::new(AtomicU64::new(0));
        let recovery_request = Arc::new(AtomicU64::new(0));
        let recovery_attempts = Arc::new(AtomicU64::new(0));
        let recovery_pending = Arc::new(AtomicBool::new(false));
        let recovery_started_ms = Arc::new(AtomicU64::new(0));
        let recovery_flush_done = Arc::new(AtomicBool::new(true));
        let monitor_started = Instant::now();

        // O leitor partilhado permite interromper ReadSample e reconstruir a
        // origem de vídeo após uma falha prolongada do dispositivo.
        let stop_watch = stop.clone();
        let first_watch = first_frame_watch.clone();
        let source_watch = source_frames.clone();
        let output_watch = output_frames.clone();
        let dropped_watch = dropped_frames.clone();
        let repeated_watch = repeated_frames.clone();
        let last_source_watch = last_source_ms.clone();
        let recovery_request_watch = recovery_request.clone();
        let recovery_attempts_watch = recovery_attempts.clone();
        let recovery_pending_watch = recovery_pending.clone();
        let recovery_started_watch = recovery_started_ms.clone();
        let recovery_flush_done_watch = recovery_flush_done.clone();
        let reader_control_watch = reader_control.clone();
        let monitor_start_watch = monitor_started;
        let target_fps = cfg.fps.max(1);
        let duration = cfg.seconds;
        let watchdog = thread::spawn(move || {
            let mut report_at = Instant::now();
            let mut last_source = 0u64;
            let mut last_output = 0u64;
            loop {
                if stop_watch.load(Ordering::Relaxed) {
                    let flusher = reader_control_watch.clone();
                    thread::spawn(move || flusher.flush(FIRST_VIDEO));
                    break;
                }

                let now = Instant::now();
                if let Some(sec) = duration {
                    let first = first_watch.lock().ok().and_then(|g| *g);
                    if first.is_some_and(|t| t.elapsed() >= Duration::from_secs(sec)) {
                        stop_watch.store(true, Ordering::SeqCst);
                        continue;
                    }
                }

                let elapsed = now.duration_since(report_at);
                if elapsed >= Duration::from_secs(1) {
                    let source = source_watch.load(Ordering::Relaxed);
                    let output = output_watch.load(Ordering::Relaxed);
                    let dropped = dropped_watch.load(Ordering::Relaxed);
                    let repeated = repeated_watch.load(Ordering::Relaxed);
                    let attempts = recovery_attempts_watch.load(Ordering::Relaxed);
                    let seconds = elapsed.as_secs_f64().max(0.001);
                    eprintln!(
                        "CAPTURE_FPS source={:.1} output={:.1} target={} dropped={} repeated={} recoveries={}",
                        (source - last_source) as f64 / seconds,
                        (output - last_output) as f64 / seconds,
                        target_fps,
                        dropped,
                        repeated,
                        attempts
                    );
                    last_source = source;
                    last_output = output;
                    report_at = now;
                }

                let now_ms = monitor_start_watch.elapsed().as_millis() as u64;
                let source_count = source_watch.load(Ordering::Relaxed);
                let pending = recovery_pending_watch.load(Ordering::Acquire);
                let recovery_age = now_ms.saturating_sub(recovery_started_watch.load(Ordering::Relaxed));
                let age_ms = if source_count == 0 { recovery_age }
                    else { now_ms.saturating_sub(last_source_watch.load(Ordering::Relaxed)) };
                let flush_stuck = recovery_attempts_watch.load(Ordering::Relaxed) > 0
                    && !recovery_flush_done_watch.load(Ordering::Acquire)
                    && recovery_age >= RECOVERY_FLUSH_TIMEOUT_MS;
                let timed_out = if pending {
                    recovery_age >= RECOVERY_FRAME_TIMEOUT_MS
                } else if source_count == 0 && recovery_attempts_watch.load(Ordering::Relaxed) == 0 {
                    age_ms >= FIRST_FRAME_TIMEOUT_MS
                } else {
                    age_ms >= SIGNAL_STALL_MS
                };
                if flush_stuck {
                    let attempts = recovery_attempts_watch.load(Ordering::Relaxed);
                    eprintln!(
                        "CAPTURE_SIGNAL_LOST attempts={} source_frames={} reason=reader_flush_timeout",
                        attempts, source_count
                    );
                    stop_watch.store(true, Ordering::SeqCst);
                    break;
                } else if timed_out && recovery_flush_done_watch.load(Ordering::Acquire) {
                    let attempts = recovery_attempts_watch.load(Ordering::Relaxed);
                    if attempts < MAX_VIDEO_RECOVERIES {
                        let attempt = attempts + 1;
                        recovery_attempts_watch.store(attempt, Ordering::SeqCst);
                        recovery_pending_watch.store(true, Ordering::SeqCst);
                        recovery_started_watch.store(now_ms, Ordering::SeqCst);
                        recovery_flush_done_watch.store(false, Ordering::SeqCst);
                        recovery_request_watch.store(attempt, Ordering::SeqCst);
                        eprintln!("CAPTURE_STALL age_ms={} source_frames={}", age_ms, source_count);
                        eprintln!("CAPTURE_RECOVERY attempt={} max={}", attempt, MAX_VIDEO_RECOVERIES);
                        let flusher = reader_control_watch.clone();
                        let flush_done = recovery_flush_done_watch.clone();
                        thread::spawn(move || {
                            flusher.flush(FIRST_VIDEO);
                            flush_done.store(true, Ordering::Release);
                        });
                    } else {
                        eprintln!(
                            "CAPTURE_SIGNAL_LOST attempts={} source_frames={} dropped={} repeated={} reason=recovery_exhausted",
                            attempts,
                            source_count,
                            dropped_watch.load(Ordering::Relaxed),
                            repeated_watch.load(Ordering::Relaxed)
                        );
                        stop_watch.store(true, Ordering::SeqCst);
                        let flusher = reader_control_watch.clone();
                        thread::spawn(move || flusher.flush(FIRST_VIDEO));
                        break;
                    }
                }
                thread::sleep(Duration::from_millis(25));
            }
        });

        // A leitura da placa fica desacoplada da escrita para o FFmpeg. Se o
        // encoder/pipe atrasar, continuamos a drenar a UVC03 e descartamos frames
        // antigos da fila limitada, em vez de bloquear ReadSample com o buffer MF
        // ainda ocupado.
        let (frame_tx, frame_rx) = std::sync::mpsc::sync_channel::<(Vec<u8>, i64)>(2);
        let capture_reader = Some(SendReader(reader));
        let capture_source = SendSource(src);
        let stop_capture = stop.clone();
        let source_capture = source_frames.clone();
        let dropped_capture = dropped_frames.clone();
        let last_source_capture = last_source_ms.clone();
        let recovery_request_capture = recovery_request.clone();
        let recovery_attempts_capture = recovery_attempts.clone();
        let recovery_pending_capture = recovery_pending.clone();
        let recovery_started_capture = recovery_started_ms.clone();
        let recovery_flush_done_capture = recovery_flush_done.clone();
        let reader_control_capture = reader_control.clone();
        let capture_started = monitor_started;
        let capture_cfg = cfg.clone();
        let capture = thread::spawn(move || -> windows::core::Result<()> {
            let _com = ComApartment::mta();
            let mut active_source = Some(capture_source);
            let mut active_reader = capture_reader;
            let mut warned_size = false;
            let mut debug_samples = 0u64;
            let mut handled_recovery = 0u64;
            loop {
                if stop_capture.load(Ordering::Relaxed) {
                    break;
                }
                let read_result = active_reader.as_ref().expect("active video reader")
                    .read_sample(FIRST_VIDEO);
                let requested_recovery = recovery_request_capture.load(Ordering::Acquire);
                if requested_recovery > handled_recovery {
                    handled_recovery = requested_recovery;
                    let wait_started = Instant::now();
                    while !recovery_flush_done_capture.load(Ordering::Acquire)
                        && wait_started.elapsed() < Duration::from_secs(5)
                    {
                        thread::sleep(Duration::from_millis(20));
                    }
                    if !recovery_flush_done_capture.load(Ordering::Acquire) {
                        eprintln!("CAPTURE_RECOVERY_FAILED attempt={} error=flush_timeout", handled_recovery);
                        recovery_pending_capture.store(false, Ordering::SeqCst);
                        last_source_capture.store(capture_started.elapsed().as_millis() as u64, Ordering::SeqCst);
                        continue;
                    }

                    reader_control_capture.replace(None);
                    drop(read_result);
                    active_reader.take();
                    if let Some(old_source) = active_source.take() {
                        let _ = old_source.0.Shutdown();
                        drop(old_source);
                    }
                    thread::sleep(Duration::from_millis(300));
                    match open_raw_video_reader(&capture_cfg) {
                        Ok((new_source, new_reader)) => {
                            active_source = Some(SendSource(new_source));
                            active_reader = Some(SendReader(new_reader.clone()));
                            reader_control_capture.replace(Some(new_reader));
                            recovery_started_capture.store(capture_started.elapsed().as_millis() as u64, Ordering::SeqCst);
                            eprintln!("CAPTURE_RECOVERY_OPENED attempt={}", handled_recovery);
                        }
                        Err(e) => {
                            eprintln!("CAPTURE_RECOVERY_FAILED attempt={} error={}", handled_recovery, e);
                            recovery_pending_capture.store(false, Ordering::SeqCst);
                            recovery_started_capture.store(capture_started.elapsed().as_millis() as u64, Ordering::SeqCst);
                            last_source_capture.store(capture_started.elapsed().as_millis() as u64, Ordering::SeqCst);
                        }
                    }
                    continue;
                }
                let (flags, ts, sample) = match read_result {
                    Ok(v) => v,
                    Err(_) if stop_capture.load(Ordering::Relaxed) => break,
                    Err(e) => return Err(e),
                };
                if flags & 1 != 0 {
                    break;
                }
                let Some(sample) = sample else { continue };
                let Ok(buf) = sample.ConvertToContiguousBuffer() else { continue };
                let mut ptr: *mut u8 = std::ptr::null_mut();
                let mut max = 0u32;
                let mut cur = 0u32;
                if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_err() {
                    continue;
                }
                let bytes = if cur as usize == expected_frame {
                    let slice = std::slice::from_raw_parts(ptr, cur as usize);
                    if dbg && debug_samples < 6 {
                        eprintln!("DEBUG sample frames={} size={} max={} ts={}", debug_samples, cur, max, ts);
                    }
                    debug_samples += 1;
                    Some(slice.to_vec())
                } else {
                    dropped_capture.fetch_add(1, Ordering::Relaxed);
                    if !warned_size {
                        warned_size = true;
                        eprintln!(
                            "AVISO: frame de {cur} bytes (esperado {expected_frame}); a ignorar"
                        );
                    }
                    None
                };
                let _ = buf.Unlock();
                if let Some(bytes) = bytes {
                    source_capture.fetch_add(1, Ordering::Relaxed);
                    last_source_capture.store(capture_started.elapsed().as_millis() as u64, Ordering::Relaxed);
                    if recovery_pending_capture.swap(false, Ordering::SeqCst) {
                        eprintln!("CAPTURE_RECOVERED attempt={}", recovery_attempts_capture.load(Ordering::Relaxed));
                    }
                    match frame_tx.try_send((bytes, ts)) {
                        Ok(()) => {}
                        Err(std::sync::mpsc::TrySendError::Full(_)) => {
                            dropped_capture.fetch_add(1, Ordering::Relaxed);
                        }
                        Err(std::sync::mpsc::TrySendError::Disconnected(_)) => break,
                    }
                }
            }
            Ok(())
        });

        let stdout = std::io::stdout();
        let mut out = stdout.lock();
        use std::io::Write;
        let mut capture_error: Option<windows::core::Error> = None;
        let mut last_frame: Option<Vec<u8>> = None;
        let mut frame_generation = 0u64;
        let mut last_output_generation = 0u64;
        let mut ready_sent = false;
        loop {
            if stop.load(Ordering::Relaxed) {
                break;
            }
            let wait = if let Some(first) = first_frame {
                let deadline = first + Duration::from_secs_f64(next_idx.max(0) as f64 / fps_i as f64);
                deadline.saturating_duration_since(Instant::now())
            } else {
                Duration::from_millis(250)
            };
            match frame_rx.recv_timeout(wait) {
                Ok((frame, ts)) => {
                    last_frame = Some(frame);
                    frame_generation = frame_generation.saturating_add(1);
                    if dbg_ts0 == 0 { dbg_ts0 = ts; }
                    dbg_ts1 = ts;
                    if let (Some(h), Some(bytes)) = (&hub, &last_frame) {
                        h.video(bytes);
                    }
                }
                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {}
                Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => break,
            }
            // Mantém o frame mais recente da fila para limitar a latência após
            // uma pausa breve do encoder; a aquisição continua a drenar a placa.
            loop {
                match frame_rx.try_recv() {
                    Ok((frame, ts)) => {
                        last_frame = Some(frame);
                        frame_generation = frame_generation.saturating_add(1);
                        if dbg_ts0 == 0 { dbg_ts0 = ts; }
                        dbg_ts1 = ts;
                        if let (Some(h), Some(bytes)) = (&hub, &last_frame) {
                            h.video(bytes);
                        }
                    }
                    Err(std::sync::mpsc::TryRecvError::Empty)
                    | Err(std::sync::mpsc::TryRecvError::Disconnected) => break,
                }
            }

            if first_frame.is_none() {
                if last_frame.is_none() { continue; }
                let first = Instant::now();
                first_frame = Some(first);
                if let Ok(mut shared) = first_frame_watch.lock() {
                    *shared = Some(first);
                }
            }
            if let (Some(sec), Some(tf)) = (cfg.seconds, first_frame) {
                if tf.elapsed() >= Duration::from_secs(sec) {
                    break;
                }
            }

            let secs = first_frame.unwrap().elapsed().as_secs_f64();
            let target = (secs * fps_i as f64).floor() as i64;
            if target < next_idx { continue; }
            let mut copies = target - next_idx + 1;
            // Re-sincroniza após pausas longas para não bloquear o encoder com
            // uma rajada enorme; o último frame é repetido durante pausas curtas.
            let max_copies = (fps_i / 2).max(4);
            if copies > max_copies {
                next_idx = target + 1;
                copies = 1;
            } else {
                next_idx += copies;
            }

            let Some(bytes) = last_frame.as_deref() else { continue };
            let mut written = 0u64;
            for _ in 0..copies {
                if out.write_all(bytes).is_err() { break; }
                written += 1;
            }
            if written == 0 { break; }
            let fresh = frame_generation.saturating_sub(last_output_generation);
            if fresh > 1 {
                dropped_frames.fetch_add(fresh - 1, Ordering::Relaxed);
            }
            repeated_frames.fetch_add(written.saturating_sub(u64::from(fresh > 0)), Ordering::Relaxed);
            last_output_generation = frame_generation;
            frames += written;
            output_frames.fetch_add(written, Ordering::Relaxed);
            if !ready_sent {
                ready_sent = true;
                vstart.store((MFGetSystemTime() - t0) / 10_000, Ordering::Relaxed);
                eprintln!("CAPTURE_READY w={} h={} target={}", cfg.w, cfg.h, cfg.fps);
            }
        }
        let _ = out.flush();
        drop(out);

        // Fecha o WAV e corrige os tamanhos antes de o processo terminar.
        stop.store(true, Ordering::SeqCst);
        let _ = watchdog.join();
        match capture.join() {
            Ok(Ok(())) => {}
            Ok(Err(e)) => capture_error = Some(e),
            Err(_) => capture_error = Some(windows::core::Error::new(
                HRESULT(0x8000_4005u32 as i32),
                "thread de captura terminou inesperadamente",
            )),
        }
        let audio_bytes = match audio_thread {
            Some(h) => h.join().unwrap_or(0),
            None => 0,
        };
        let v_ms = vstart.load(Ordering::Relaxed);
        let a_ms = astart.load(Ordering::Relaxed);
        eprintln!(
            "CAPTURE_METRICS source={} output={} dropped={} repeated={} recoveries={}",
            source_frames.load(Ordering::Relaxed),
            output_frames.load(Ordering::Relaxed),
            dropped_frames.load(Ordering::Relaxed),
            repeated_frames.load(Ordering::Relaxed),
            recovery_attempts.load(Ordering::Relaxed)
        );
        if dbg && frames > 1 {
            let span_ms = (dbg_ts1 - dbg_ts0) / 10_000;
            let source_total = source_frames.load(Ordering::Relaxed);
            let fps = if span_ms > 0 {
                source_total.saturating_sub(1) as f64 * 1000.0 / span_ms as f64
            } else {
                0.0
            };
            eprintln!("DEBUG ts span_ms={span_ms} source_fps={fps:.2} source_frames={source_total} output_frames={frames}");
        }
        eprintln!(
            "SYNC v={} a={} audio_bytes={}",
            v_ms.max(0),
            a_ms.max(0),
            audio_bytes
        );
        if let Some(e) = capture_error {
            return Err(e);
        }
        Ok(Stats {
            frames,
            audio_packets: 0,
            elapsed: started.elapsed(),
            audio: audio_bytes > 0,
        })
    }
}
pub fn record(cfg: &Config, stop: &Arc<AtomicBool>, st: &Arc<Status>) -> windows::core::Result<Stats> {
    unsafe {
        eprintln!("CAPTURE_WAIT w={} h={} target={}", cfg.w, cfg.h, cfg.fps);
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        MFStartup(MF_VERSION, 0)?;

        // Escolhe o modo mais próximo que a placa suporta (avisa se ajustar).
        let (rw, rh, rfps) = resolve_mode(&cfg.video_link, cfg.w, cfg.h, cfg.fps, &cfg.format);

        // O preview é servido com os frames do próprio leitor (um segundo leitor
        // MF durante a gravação disputa o dispositivo nesta placa).
        let hub = if cfg.preview_port.is_some() {
            let mut pc = cfg.clone();
            pc.w = rw;
            pc.h = rh;
            pc.fps = rfps;
            cfg.preview_port
                .and_then(|p| PreviewHub::start(&pc, p))
                .map(Arc::new)
        } else {
            None
        };
        let final_path = cfg.out.clone();
        let need_remux = cfg.container != "mp4";
        let rec_path = if need_remux { format!("{}.part.mp4", final_path) } else { final_path.clone() };
        *st.path.lock().unwrap() = rec_path.clone();
        let pcm = cfg.pcm && cfg.container == "mkv";
        let wav_path = format!("{}.wav", rec_path);

        // ---------- video source (NV12) ----------
        let mut va_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut va_opt, 2)?;
        let va = va_opt.unwrap();
        va.SetGUID(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID,
        )?;
        va.SetString(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
            &HSTRING::from(cfg.video_link.as_str()),
        )?;
        let vsource = MFCreateDeviceSource(&va)?;
        let mut vra_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut vra_opt, 2)?;
        let vra = vra_opt.unwrap();
        vra.SetUINT32(&windows::core::GUID::from_u128(0x0d41e4a1_3d9e_4f9b_a1ea_0d1e2b379b8b), 1)?;
        vra.SetUINT32(&windows::core::GUID::from_u128(0xfb394f3d_ccf1_42ee_bbb3_f9b845d5681d), 1)?;
        let vreader = MFCreateSourceReaderFromMediaSource(&vsource, Some(&vra))?;

        let nv12 = MFCreateMediaType()?;
        nv12.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        nv12.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_NV12)?;
        nv12.SetUINT64(&MF_MT_FRAME_SIZE, ((rw as u64) << 32) | rh as u64)?;
        nv12.SetUINT64(&MF_MT_FRAME_RATE, ((rfps as u64) << 32) | 1)?;
        nv12.SetUINT32(&MF_MT_INTERLACE_MODE, 2)?;
        let _ = apply_color(&nv12, cfg);
        vreader.SetCurrentMediaType(FIRST_VIDEO, None, &nv12)?;
        let vflush = vreader.clone();

        // ---------- sink writer ----------
        let mut wa_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut wa_opt, 2)?;
        let wa = wa_opt.unwrap();
        wa.SetUINT32(&MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, if cfg.hw { 1 } else { 0 })?;
        let writer = MFCreateSinkWriterFromURL(&HSTRING::from(rec_path.as_str()), None, Some(&wa))?;

        let vout = MFCreateMediaType()?;
        vout.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        vout.SetGUID(
            &MF_MT_SUBTYPE,
            if cfg.hevc { &MFVideoFormat_HEVC } else { &MFVideoFormat_H264 },
        )?;
        vout.SetUINT32(&MF_MT_AVG_BITRATE, cfg.bitrate)?;
        vout.SetUINT32(&MF_MT_INTERLACE_MODE, 2)?;
        vout.SetUINT64(&MF_MT_FRAME_SIZE, ((rw as u64) << 32) | rh as u64)?;
        vout.SetUINT64(&MF_MT_FRAME_RATE, ((rfps as u64) << 32) | 1)?;
        if !cfg.hevc {
            vout.SetUINT32(&MF_MT_MPEG2_PROFILE, cfg.profile)?;
        }
        let cs2020 = cfg.color_space.eq_ignore_ascii_case("2020");
        let full = cfg.color_range.eq_ignore_ascii_case("full");
        vout.SetUINT32(&MF_MT_YUV_MATRIX, if cs2020 { MFVideoTransferMatrix_BT2020_10.0 as u32 } else { MFVideoTransferMatrix_BT709.0 as u32 })?;
        vout.SetUINT32(&MF_MT_VIDEO_PRIMARIES, if cs2020 { MFVideoPrimaries_BT2020.0 as u32 } else { MFVideoPrimaries_BT709.0 as u32 })?;
        vout.SetUINT32(&MF_MT_TRANSFER_FUNCTION, if cs2020 { MFVideoTransFunc_2020.0 as u32 } else { MFVideoTransFunc_709.0 as u32 })?;
        vout.SetUINT32(&MF_MT_VIDEO_NOMINAL_RANGE, if full { MFNominalRange_0_255.0 as u32 } else { MFNominalRange_16_235.0 as u32 })?;
        let vidx = writer.AddStream(&vout)?;
        writer.SetInputMediaType(vidx, &nv12, None)?;
        configure_encoder(&writer, vidx, cfg);

        // ---------- audio (optional) ----------
        let mut areader: Option<IMFSourceReader> = None;
        let mut aidx: u32 = 0;
        let mut ablock: u32 = 4;
        let mut arate: u32 = cfg.audio_rate;

        if cfg.audio {
            match open_audio(cfg) {
                Ok((rd, used)) => {
                    let aout = MFCreateMediaType()?;
                    aout.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
                    let mp3 = cfg.audio_codec.eq_ignore_ascii_case("mp3");
                    let flac = cfg.audio_codec.eq_ignore_ascii_case("flac");
                    let sub = if mp3 { &MFAudioFormat_MP3 } else if flac { &MFAudioFormat_FLAC } else { &MFAudioFormat_AAC };
                    aout.SetGUID(&MF_MT_SUBTYPE, sub)?;
                    aout.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, cfg.audio_rate)?;
                    aout.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, cfg.audio_channels as u32)?;
                    aout.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, if mp3 { 0 } else { 16 })?;
                    if flac { aout.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, cfg.audio_rate * cfg.audio_channels as u32 * 2)?; }
                    aout.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, if mp3 { 1 } else { cfg.audio_channels as u32 * 2 })?;
                    aout.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, cfg.audio_kbps * 1000 / 8)?;
                    aout.SetUINT32(&MF_MT_AAC_PAYLOAD_TYPE, 0)?;
                    aout.SetUINT32(&MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29)?;
                    match writer.AddStream(&aout) {
                        Ok(i) => match writer.SetInputMediaType(i, &used, None) {
                            Ok(_) => {
                                aidx = i;
                                if let Ok(b) = used.GetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT) {
                                    if b > 0 {
                                        ablock = b;
                                    }
                                }
                                if let Ok(r) = used.GetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND) {
                                    if r > 0 {
                                        arate = r;
                                    }
                                }
                                areader = Some(rd);
                            }
                            Err(_) => {}
                        },
                        Err(_) => {}
                    }
                }
                Err(_) => {}
            }
        }

        let t0 = MFGetSystemTime();
        let vstart = Arc::new(AtomicI64::new(-1));
        let astart = Arc::new(AtomicI64::new(-1));

        let pcm_thread = if pcm {
            match open_audio_pcm(cfg) {
                Ok((rd, fmt)) => {
                    let stop_p = stop.clone();
                    let astart_t = astart.clone();
                    let wpath = wav_path.clone();
                    let lvl_p = st.level.clone();
                    let hub_p = hub.clone();
                    Some(thread::spawn(move || {
                        let rd = rd;
                        let reader = rd.0;
                        write_wav_pcm(&reader, fmt, &wpath, &stop_p, Some(&lvl_p), hub_p, || {
                            astart_t.store((MFGetSystemTime() - t0) / 10_000, Ordering::Relaxed);
                        });
                    }))
                }
                Err(_) => None,
            }
        } else {
            None
        };

        writer.BeginWriting()?;
        st.audio.store(aidx != 0, Ordering::SeqCst);
        *st.started.lock().unwrap() = Some(Instant::now());
        st.running.store(true, Ordering::SeqCst);

        let vframes = Arc::new(AtomicU64::new(0));
        let aframes = Arc::new(AtomicU64::new(0));
        let w = Arc::new(Mutex::new(SendWriter(writer)));

        // ---------- video thread ----------
        let frames_v = vframes.clone();
        let st_v = st.clone();
        let w_v = w.clone();
        let vr = SendReader(vreader);
        let ready = Arc::new(AtomicBool::new(false));
        let ready_v = ready.clone();
        let fps = rfps;
        let stop_v = stop.clone();
        let vstart_t = vstart.clone();
        let hub_v = hub.clone();
        let vh = thread::spawn(move || -> windows::core::Result<()> {
            let vr = vr;
            let reader = vr.0;
            let mut base: Option<i64> = None;
            let mut off: i64 = 0;
            let mut fps_window = Instant::now();
            let mut fps_frames = 0u64;
            loop {
                if stop_v.load(Ordering::Relaxed) {
                    break;
                }
                let mut flags = 0u32;
                let mut ts = 0i64;
                let mut sample: Option<IMFSample> = None;
                reader.ReadSample(FIRST_VIDEO, 0, None, Some(&mut flags), Some(&mut ts), Some(&mut sample))?;
                if flags & 1 != 0 {
                    break;
                }
                let Some(s) = sample else { continue };
                if base.is_none() {
                    base = Some(ts);
                    off = MFGetSystemTime() - t0;
                    vstart_t.store(off / 10_000, Ordering::Relaxed);
                }
                s.SetSampleTime(ts - base.unwrap() + off)?;
                s.SetSampleDuration(10_000_000i64 / fps as i64)?;
                if let Some(h) = &hub_v {
                    h.video_sample(&s);
                }
                {
                    let guard = w_v.lock().unwrap();
                    guard.0.WriteSample(vidx, &s)?;
                }
                frames_v.fetch_add(1, Ordering::Relaxed);
                st_v.frames.fetch_add(1, Ordering::Relaxed);
                fps_frames += 1;
                if !ready_v.swap(true, Ordering::SeqCst) {
                    eprintln!("CAPTURE_READY w={} h={} target={}", rw, rh, fps);
                }
                let elapsed = fps_window.elapsed();
                if elapsed >= Duration::from_secs(1) {
                    let measured = fps_frames as f64 / elapsed.as_secs_f64();
                    eprintln!(
                        "CAPTURE_FPS source={measured:.1} output={measured:.1} target={fps}"
                    );
                    fps_frames = 0;
                    fps_window = Instant::now();
                }
            }
            Ok(())
        });

        // ---------- audio thread ----------
        let mut ah: Option<thread::JoinHandle<windows::core::Result<()>>> = None;
        let aflush = areader.as_ref().map(|r| r.clone());
        let hub_a = hub.clone();
        if let Some(rd) = areader {
            let frames_a = aframes.clone();
            let w_a = w.clone();
            let stop_a = stop.clone();
            let lvl_a = st.level.clone();
            let ar = SendReader(rd);
            ah = Some(thread::spawn(move || -> windows::core::Result<()> {
                let ar = ar;
                let reader = ar.0;
                let mut base: Option<i64> = None;
                let mut off: i64 = 0;
                loop {
                    if stop_a.load(Ordering::Relaxed) {
                        break;
                    }
                    let mut flags = 0u32;
                    let mut ts = 0i64;
                    let mut sample: Option<IMFSample> = None;
                    reader.ReadSample(FIRST_AUDIO, 0, None, Some(&mut flags), Some(&mut ts), Some(&mut sample))?;
                    if flags & 1 != 0 {
                        break;
                    }
                    let Some(s) = sample else { continue };
                    if base.is_none() {
                        base = Some(ts);
                        off = MFGetSystemTime() - t0;
                    }
                    s.SetSampleTime(ts - base.unwrap() + off)?;
                    if let Ok(buf) = s.GetBufferByIndex(0) {
                        if let Ok(len) = buf.GetCurrentLength() {
                            if ablock > 0 {
                                let nsamples = len / ablock;
                                let dur = (nsamples as i64 * 10_000_000i64) / arate as i64;
                                s.SetSampleDuration(dur).ok();
                            }
                        }
                        let mut ptr: *mut u8 = std::ptr::null_mut();
                        let mut max = 0u32;
                        let mut cur = 0u32;
                        if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_ok() {
                            let sl = std::slice::from_raw_parts(ptr, cur as usize);
                            set_level_peak(&lvl_a, sl, 16);
                            if let Some(h) = &hub_a {
                                h.audio(sl, 16);
                            }
                            let _ = buf.Unlock();
                        }
                    }
                    {
                        let guard = w_a.lock().unwrap();
                        guard.0.WriteSample(aidx, &s)?;
                    }
                    frames_a.fetch_add(1, Ordering::Relaxed);
                }
                Ok(())
            }));
        }

        // ---------- run until stop ----------
        if let Some(sec) = cfg.seconds {
            let s = stop.clone();
            let ready_w = ready.clone();
            thread::spawn(move || {
                // A duração começa no primeiro frame realmente escrito. Uma
                // placa em arranque 4K pode demorar mais de 15 segundos.
                while !ready_w.load(Ordering::Relaxed) && !s.load(Ordering::Relaxed) {
                    thread::sleep(Duration::from_millis(20));
                }
                let t1 = Instant::now();
                while t1.elapsed() < Duration::from_secs(sec) && !s.load(Ordering::Relaxed) {
                    thread::sleep(Duration::from_millis(20));
                }
                s.store(true, Ordering::SeqCst);
            });
        }
        let start = Instant::now();
        while !stop.load(Ordering::Relaxed) {
            thread::sleep(Duration::from_millis(20));
        }

        // close the readers so the loops end even without new frames
        stop.store(true, Ordering::Relaxed);
        let _ = vflush.Flush(FIRST_VIDEO);
        if let Some(af) = &aflush {
            let _ = af.Flush(FIRST_AUDIO);
        }

        let mut err: Option<windows::core::Error> = None;
        if let Ok(Err(e)) = vh.join() {
            err = Some(e);
        }
        if let Some(h) = ah {
            if let Ok(Err(e)) = h.join() {
                if err.is_none() {
                    err = Some(e);
                }
            }
        }

        {
            let guard = w.lock().unwrap();
            guard.0.Finalize().ok();
        }

        st.running.store(false, Ordering::SeqCst);
        st.frames.store(vframes.load(Ordering::Relaxed), Ordering::SeqCst);
        st.audio_packets.store(aframes.load(Ordering::Relaxed), Ordering::SeqCst);
        let _ = MFShutdown();

        if let Some(e) = err {
            return Err(e);
        }

        if let Some(h) = pcm_thread {
            let _ = h.join();
        }

        if pcm {
            let v_ms = vstart.load(Ordering::Relaxed);
            let a_ms = astart.load(Ordering::Relaxed);
            let sync = if v_ms >= 0 && a_ms >= 0 { a_ms - v_ms } else { 0 };
            mux_pcm(&rec_path, &wav_path, &final_path, sync).map_err(|e| {
                windows::core::Error::new(HRESULT(0x8000_4005u32 as i32), format!("mux: {e}"))
            })?;
            let _ = std::fs::remove_file(&rec_path);
            let _ = std::fs::remove_file(&wav_path);
        } else if need_remux {
            remux(&rec_path, &final_path, &cfg.container).map_err(|e| {
                windows::core::Error::new(
                    HRESULT(0x8000_4005u32 as i32),
                    format!("remux: {e}"),
                )
            })?;
            let _ = std::fs::remove_file(&rec_path);
        }

        Ok(Stats {
            frames: vframes.load(Ordering::Relaxed),
            audio_packets: aframes.load(Ordering::Relaxed),
            elapsed: start.elapsed(),
            audio: aidx != 0,
        })
    }
}

unsafe fn open_audio(cfg: &Config) -> windows::core::Result<(IMFSourceReader, IMFMediaType)> {
    let mut aa_opt: Option<IMFAttributes> = None;
    MFCreateAttributes(&mut aa_opt, 2)?;
    let aa = aa_opt.unwrap();
    aa.SetGUID(
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID,
    )?;
    aa.SetString(
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ENDPOINT_ID,
        &HSTRING::from(cfg.audio_id.as_str()),
    )?;
    let src = MFCreateDeviceSource(&aa)?;
    let reader = MFCreateSourceReaderFromMediaSource(&src, None)?;

    let pcm = MFCreateMediaType()?;
    pcm.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
    pcm.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_PCM)?;
    pcm.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, cfg.audio_rate)?;
    pcm.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, cfg.audio_channels as u32)?;
    pcm.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, 16)?;
    pcm.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, cfg.audio_channels as u32 * 2)?;
    pcm.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, cfg.audio_rate * cfg.audio_channels as u32 * 2)?;

    match reader.SetCurrentMediaType(FIRST_AUDIO, None, &pcm) {
        Ok(_) => Ok((reader, pcm)),
        Err(_) => {
            let native = reader.GetCurrentMediaType(FIRST_AUDIO)?;
            Ok((reader, native))
        }
    }
}



pub fn find_mkvmerge() -> Option<String> {
    let candidates = [
        r"C:\Program Files\MKVToolNix\mkvmerge.exe",
        r"C:\Program Files (x86)\MKVToolNix\mkvmerge.exe",
    ];
    for c in candidates {
        if std::path::Path::new(c).exists() {
            return Some(c.to_string());
        }
    }
    std::env::var("MKVMERGE_PATH").ok().filter(|p| std::path::Path::new(p).exists())
}

pub fn find_vlc() -> Option<String> {
    let candidates = [
        r"C:\Program Files\VideoLAN\VLC\vlc.exe",
        r"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
    ];
    for c in candidates {
        if std::path::Path::new(c).exists() {
            return Some(c.to_string());
        }
    }
    std::env::var("VLC_PATH").ok().filter(|p| std::path::Path::new(p).exists())
}

/// Lossless remux to another container using the installed VLC.
pub fn remux(src: &str, dst: &str, mux: &str) -> std::io::Result<()> {
    if mux == "mkv" {
        if let Some(m) = find_mkvmerge() {
            let st = Command::new(m)
                .args(["-o", dst, src])
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .status()?;
            if st.success() {
                return Ok(());
            }
        }
    }

    let vlc = find_vlc().ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::NotFound, "VLC nao encontrado")
    })?;
    let sout = format!("#std{{access=file,mux={},dst={}}}", mux, dst.replace('\\', "/"));
    let status = Command::new(vlc)
        .args([
            "-I",
            "dummy",
            "--dummy-quiet",
            "--no-video",
            src,
            &format!("--sout={}", sout),
            "vlc://quit",
        ])
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()?;
    if !status.success() {
        return Err(std::io::Error::new(std::io::ErrorKind::Other, "VLC falhou no remux"));
    }
    Ok(())
}




unsafe fn configure_encoder(writer: &IMFSinkWriter, stream: u32, cfg: &Config) {
    let ex = match writer.cast::<IMFSinkWriterEx>() {
        Ok(e) => e,
        Err(_) => return,
    };
    let mut tr: Option<IMFTransform> = None;
    if ex
        .GetTransformForStream(stream, 0, None, &mut tr)
        .is_err()
    {
        return;
    }
    let Some(t) = tr else { return };
    let api = match t.cast::<ICodecAPI>() {
        Ok(a) => a,
        Err(_) => return,
    };

    let mode: u32 = if cfg.cbr { 0 } else { 1 }; // 0=CBR, 1=VBR
    let v = VARIANT::from(mode);
    let _ = api.SetValue(&CODECAPI_AVEncCommonRateControlMode, &v);
    if cfg.bitrate > 0 {
        let v = VARIANT::from(cfg.bitrate);
        let _ = api.SetValue(&CODECAPI_AVEncCommonMeanBitRate, &v);
    }
    if cfg.gop > 0 {
        let v = VARIANT::from(cfg.gop);
        let _ = api.SetValue(&CODECAPI_AVEncMPVGOPSize, &v);
    }
    let v = VARIANT::from(1u32);
    let _ = api.SetValue(&CODECAPI_AVEncH264CABACEnable, &v);
}

#[derive(Clone, Copy)]
pub struct PcmFmt {
    pub rate: u32,
    pub ch: u16,
    pub bits: u16,
    pub block: u16,
}

/// Consome o PCM do leitor e escreve um WAV completo em `path`.
/// Cabeçalho provisório no início e patch dos tamanhos RIFF/data no fim
/// (o mesmo comportamento do `--pcm` do percurso Media Foundation).
/// Devolve os bytes de áudio escritos (0 se falhar).
unsafe fn write_wav_pcm(
    reader: &IMFSourceReader,
    fmt: PcmFmt,
    path: &str,
    stop: &AtomicBool,
    level: Option<&AtomicU32>,
    preview: Option<Arc<PreviewHub>>,
    mut on_first: impl FnMut(),
) -> u64 {
    use std::io::{Seek, SeekFrom, Write};
    let mut f = match std::fs::File::create(path) {
        Ok(f) => f,
        Err(e) => {
            eprintln!("AVISO: nao foi possivel criar {path}: {e}");
            return 0;
        }
    };
    let mut hdr = [0u8; 44];
    hdr[0..4].copy_from_slice(b"RIFF");
    hdr[4..8].copy_from_slice(&36u32.to_le_bytes());
    hdr[8..12].copy_from_slice(b"WAVE");
    hdr[12..16].copy_from_slice(b"fmt ");
    hdr[16..20].copy_from_slice(&16u32.to_le_bytes());
    hdr[20..22].copy_from_slice(&1u16.to_le_bytes());
    hdr[22..24].copy_from_slice(&fmt.ch.to_le_bytes());
    hdr[24..28].copy_from_slice(&fmt.rate.to_le_bytes());
    hdr[28..32].copy_from_slice(&(fmt.rate * fmt.block as u32).to_le_bytes());
    hdr[32..34].copy_from_slice(&fmt.block.to_le_bytes());
    hdr[34..36].copy_from_slice(&fmt.bits.to_le_bytes());
    hdr[36..40].copy_from_slice(b"data");
    if f.write_all(&hdr).is_err() {
        return 0;
    }
    let mut data: u32 = 0;
    let mut first = true;
    loop {
        if stop.load(Ordering::Relaxed) {
            break;
        }
        let mut flags = 0u32;
        let mut ts = 0i64;
        let mut sample: Option<IMFSample> = None;
        if reader
            .ReadSample(FIRST_AUDIO, 0, None, Some(&mut flags), Some(&mut ts), Some(&mut sample))
            .is_err()
        {
            break;
        }
        if flags & 1 != 0 {
            break;
        }
        let Some(s) = sample else { continue };
        if first {
            first = false;
            on_first();
        }
        if let Ok(buf) = s.GetBufferByIndex(0) {
            let mut ptr: *mut u8 = std::ptr::null_mut();
            let mut max = 0u32;
            let mut cur = 0u32;
            if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_ok() {
                let slice = std::slice::from_raw_parts(ptr, cur as usize);
                if let Some(lvl) = level {
                    set_level_peak(lvl, slice, fmt.bits as u32);
                }
                if let Some(hub) = &preview {
                    hub.audio(slice, fmt.bits);
                }
                if f.write_all(slice).is_ok() {
                    data = data.saturating_add(cur);
                }
                let _ = buf.Unlock();
            }
        }
    }
    let _ = f.seek(SeekFrom::Start(4));
    let _ = f.write_all(&(36 + data).to_le_bytes());
    let _ = f.seek(SeekFrom::Start(40));
    let _ = f.write_all(&data.to_le_bytes());
    let _ = f.flush();
    data as u64
}

unsafe fn open_audio_pcm(cfg: &Config) -> windows::core::Result<(SendReader, PcmFmt)> {
    let mut aa_opt: Option<IMFAttributes> = None;
    MFCreateAttributes(&mut aa_opt, 2)?;
    let aa = aa_opt.unwrap();
    aa.SetGUID(
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID,
    )?;
    aa.SetString(
        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ENDPOINT_ID,
        &HSTRING::from(cfg.audio_id.as_str()),
    )?;
    let src = MFCreateDeviceSource(&aa)?;
    let reader = MFCreateSourceReaderFromMediaSource(&src, None)?;

    let mut last: Option<windows::core::Error> = None;
    // prefer 24-bit, fall back to 16-bit
    let ach = cfg.audio_channels.max(1) as u32;
    let fl = if cfg.audio_bits >= 24 { 3u32 } else { 2u32 };
    let arate = cfg.audio_rate;
    let opts: Vec<(u32, u32, u32)> = if fl == 3 {
        vec![(24, ach * 3, arate * ach * 3), (16, ach * 2, arate * ach * 2)]
    } else {
        vec![(16, ach * 2, arate * ach * 2)]
    };
    for (bits, block, avg) in opts {
        let pcm = MFCreateMediaType()?;
        pcm.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio)?;
        pcm.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_PCM)?;
        pcm.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, arate)?;
        pcm.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, ach)?;
        pcm.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, bits)?;
        pcm.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, block)?;
        pcm.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, avg)?;
        match reader.SetCurrentMediaType(FIRST_AUDIO, None, &pcm) {
            Ok(_) => {
                let used = reader.GetCurrentMediaType(FIRST_AUDIO)?;
                let rate = used
                    .GetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND)
                    .unwrap_or(48_000);
                let ch = used.GetUINT32(&MF_MT_AUDIO_NUM_CHANNELS).unwrap_or(2) as u16;
                let b = used.GetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE).unwrap_or(bits) as u16;
                let blk = used
                    .GetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT)
                    .unwrap_or(block) as u16;
                return Ok((
                    SendReader(reader),
                    PcmFmt {
                        rate,
                        ch,
                        bits: b,
                        block: blk,
                    },
                ));
            }
            Err(e) => last = Some(e),
        }
    }
    Err(last.unwrap())
}

pub fn mux_pcm(video: &str, wav: &str, dst: &str, delay_ms: i64) -> std::io::Result<()> {
    let m = find_mkvmerge().ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::NotFound, "mkvmerge nao encontrado")
    })?;
    let mut cmd = Command::new(m);
    cmd.arg("-o").arg(dst).arg("--no-audio").arg(video);
    if delay_ms != 0 {
        cmd.arg("--sync").arg(format!("0:{}", delay_ms));
    }
    cmd.arg(wav);
    let st = cmd
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()?;
    if !st.success() {
        return Err(std::io::Error::new(std::io::ErrorKind::Other, "mkvmerge falhou"));
    }
    Ok(())
}





pub fn preview(cfg: &Config, port: u16) -> windows::core::Result<()> {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        MFStartup(MF_VERSION, 0)?;

        let mut va_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut va_opt, 2)?;
        let va = va_opt.unwrap();
        va.SetGUID(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID,
        )?;
        va.SetString(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
            &HSTRING::from(cfg.video_link.as_str()),
        )?;
        let src = MFCreateDeviceSource(&va)?;

        let mut ra_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut ra_opt, 2)?;
        let ra = ra_opt.unwrap();
        let adv = windows::core::GUID::from_u128(0x0d41e4a1_3d9e_4f9b_a1ea_0d1e2b379b8b);
        ra.SetUINT32(&adv, 1)?;
        let vp = windows::core::GUID::from_u128(0xfb394f3d_ccf1_42ee_bbb3_f9b845d5681d);
        ra.SetUINT32(&vp, 1)?;
        let reader = MFCreateSourceReaderFromMediaSource(&src, Some(&ra))?;

        let nv12 = MFCreateMediaType()?;
        nv12.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        nv12.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_NV12)?;
        nv12.SetUINT64(&MF_MT_FRAME_SIZE, ((cfg.w as u64) << 32) | cfg.h as u64)?;
        nv12.SetUINT64(&MF_MT_FRAME_RATE, ((cfg.fps as u64) << 32) | 1)?;
        nv12.SetUINT32(&MF_MT_INTERLACE_MODE, 2)?;
        let _ = apply_color(&nv12, cfg);
        reader.SetCurrentMediaType(FIRST_VIDEO, None, &nv12)?;

        let rgb = MFCreateMediaType()?;
        rgb.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Video)?;
        rgb.SetGUID(&MF_MT_SUBTYPE, &MFVideoFormat_RGB32)?;
        rgb.SetUINT64(&MF_MT_FRAME_SIZE, ((cfg.w as u64) << 32) | cfg.h as u64)?;
        rgb.SetUINT64(&MF_MT_FRAME_RATE, ((cfg.fps as u64) << 32) | 1)?;
        rgb.SetUINT32(&MF_MT_INTERLACE_MODE, 2)?;
        reader.SetCurrentMediaType(FIRST_VIDEO, None, &rgb)?;
        let cur = reader.GetCurrentMediaType(FIRST_VIDEO)?;
        let sub = cur.GetGUID(&MF_MT_SUBTYPE)?;
        if sub != MFVideoFormat_RGB32 {
            return Err(windows::core::Error::new(HRESULT(0x8000_4005u32 as i32), "RGB32 nao negociado"));
        }

        // audio em paralelo no port+1 (PCM16 48k stereo)
        {
            let alink = cfg.audio_id.clone();
            let aport = port + 1;
            let enabled = cfg.audio;
            thread::spawn(move || {
                if !enabled {
                    return;
                }
                unsafe {
                    let mut aa_opt: Option<IMFAttributes> = None;
                    if MFCreateAttributes(&mut aa_opt, 2).is_err() {
                        return;
                    }
                    let aa = aa_opt.unwrap();
                    let _ = aa.SetGUID(
                        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID,
                    );
                    let _ = aa.SetString(
                        &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ENDPOINT_ID,
                        &HSTRING::from(alink.as_str()),
                    );
                    let src = match MFCreateDeviceSource(&aa) {
                        Ok(s) => s,
                        Err(_) => return,
                    };
                    let reader = match MFCreateSourceReaderFromMediaSource(&src, None) {
                        Ok(r) => r,
                        Err(_) => return,
                    };
                    let pcm = match MFCreateMediaType() {
                        Ok(t) => t,
                        Err(_) => return,
                    };
                    let _ = pcm.SetGUID(&MF_MT_MAJOR_TYPE, &MFMediaType_Audio);
                    let _ = pcm.SetGUID(&MF_MT_SUBTYPE, &MFAudioFormat_PCM);
                    let _ = pcm.SetUINT32(&MF_MT_AUDIO_SAMPLES_PER_SECOND, 48_000);
                    let _ = pcm.SetUINT32(&MF_MT_AUDIO_NUM_CHANNELS, 2);
                    let _ = pcm.SetUINT32(&MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                    let _ = pcm.SetUINT32(&MF_MT_AUDIO_BLOCK_ALIGNMENT, 4);
                    let _ = pcm.SetUINT32(&MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 192_000);
                    if reader.SetCurrentMediaType(FIRST_AUDIO, None, &pcm).is_err() {
                        return;
                    }
                    let listener = match std::net::TcpListener::bind(("127.0.0.1", aport)) {
                        Ok(l) => l,
                        Err(_) => return,
                    };
                    let (mut stream, _) = match listener.accept() {
                        Ok(s) => s,
                        Err(_) => return,
                    };
                    let _ = stream.set_nodelay(true);
                    use std::io::Write;
                    loop {
                        let mut flags = 0u32;
                        let mut ts = 0i64;
                        let mut sample: Option<IMFSample> = None;
                        if reader
                            .ReadSample(FIRST_AUDIO, 0, None, Some(&mut flags), Some(&mut ts), Some(&mut sample))
                            .is_err()
                        {
                            break;
                        }
                        if flags & 1 != 0 {
                            break;
                        }
                        let Some(s) = sample else { continue };
                        if let Ok(buf) = s.GetBufferByIndex(0) {
                            let mut ptr: *mut u8 = std::ptr::null_mut();
                            let mut max = 0u32;
                            let mut cur = 0u32;
                            if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_ok() {
                                let sl = std::slice::from_raw_parts(ptr, cur as usize);
                                if stream.write_all(sl).is_err() {
                                    let _ = buf.Unlock();
                                    break;
                                }
                                let _ = buf.Unlock();
                            }
                        }
                    }
                }
            });
        }
        let listener = std::net::TcpListener::bind(("127.0.0.1", port)).map_err(|e| {
            windows::core::Error::new(HRESULT(0x8000_4005u32 as i32), format!("bind: {e}"))
        })?;
        let (mut stream, _) = listener.accept().map_err(|e| {
            windows::core::Error::new(HRESULT(0x8000_4005u32 as i32), format!("accept: {e}"))
        })?;
        let _ = stream.set_nodelay(true);

        let mut hdr = [0u8; 12];
        loop {
            let mut flags = 0u32;
            let mut ts = 0i64;
            let mut sample: Option<IMFSample> = None;
            if reader
                .ReadSample(FIRST_VIDEO, 0, None, Some(&mut flags), Some(&mut ts), Some(&mut sample))
                .is_err()
            {
                break;
            }
            if flags & 1 != 0 {
                break;
            }
            let Some(s) = sample else { continue };
            if let Ok(buf) = s.GetBufferByIndex(0) {
                let mut ptr: *mut u8 = std::ptr::null_mut();
                let mut max = 0u32;
                let mut cur = 0u32;
                if buf.Lock(&mut ptr, Some(&mut max), Some(&mut cur)).is_ok() {
                    let slice = std::slice::from_raw_parts(ptr, cur as usize);
                    hdr[0..4].copy_from_slice(&cfg.w.to_le_bytes());
                    hdr[4..8].copy_from_slice(&cfg.h.to_le_bytes());
                    hdr[8..12].copy_from_slice(&(cur as u32).to_le_bytes());
                    use std::io::Write;
                    if stream.write_all(&hdr).is_err() || stream.write_all(slice).is_err() {
                        let _ = buf.Unlock();
                        break;
                    }
                    let _ = buf.Unlock();
                }
            }
        }
        Ok(())
    }
}



#[derive(Clone, Debug)]
pub struct VideoDevice {
    pub name: String,
    pub link: String,
}

#[derive(Clone, Debug)]
pub struct AudioDevice {
    pub name: String,
    pub id: String,
}


pub fn list_encoders() -> Vec<(String, String, bool, String)> {
    let mut out = Vec::new();
    unsafe {
        for (codec, sub) in [("AAC", MFAudioFormat_AAC), ("MP3", MFAudioFormat_MP3), ("FLAC", MFAudioFormat_FLAC), ("WMA", MFAudioFormat_WMAudioV9)] {
            let otype = MFT_REGISTER_TYPE_INFO { guidMajorType: MFMediaType_Audio, guidSubtype: sub };
            let mut acts: *mut Option<IMFActivate> = std::ptr::null_mut();
            let mut count: u32 = 0;
            if MFTEnumEx(MFT_CATEGORY_AUDIO_ENCODER, MFT_ENUM_FLAG_ALL, None, Some(&otype), &mut acts, &mut count).is_ok() && !acts.is_null() && count > 0 {
                let slice = std::slice::from_raw_parts(acts, count as usize);
                for a in slice.iter().flatten() {
                    let name = attr_string(a, &MFT_FRIENDLY_NAME_Attribute);
                    let vendor = attr_string(a, &MFT_ENUM_HARDWARE_VENDOR_ID_Attribute);
                    out.push((codec.to_string(), name, !vendor.is_empty(), vendor));
                }
            }
        }
        for (codec, sub) in [("H.264", MFVideoFormat_H264), ("HEVC", MFVideoFormat_HEVC)] {
            let otype = MFT_REGISTER_TYPE_INFO {
                guidMajorType: MFMediaType_Video,
                guidSubtype: sub,
            };
            let mut acts: *mut Option<IMFActivate> = std::ptr::null_mut();
            let mut count: u32 = 0;
            if MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_ALL, None, Some(&otype), &mut acts, &mut count).is_ok()
                && !acts.is_null() && count > 0
            {
                let slice = std::slice::from_raw_parts(acts, count as usize);
                for a in slice.iter().flatten() {
                    let name = attr_string(a, &MFT_FRIENDLY_NAME_Attribute);
                    let vendor = attr_string(a, &MFT_ENUM_HARDWARE_VENDOR_ID_Attribute);
                    out.push((codec.to_string(), name, !vendor.is_empty(), vendor));
                }
            }
        }
    }
    out
}
fn enum_sources(kind: &windows::core::GUID) -> windows::core::Result<Vec<IMFActivate>> {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        MFStartup(MF_VERSION, 0)?;
        let mut a_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut a_opt, 1)?;
        let a = a_opt.unwrap();
        a.SetGUID(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, kind)?;
        let mut devs: *mut Option<IMFActivate> = std::ptr::null_mut();
        let mut count = 0u32;
        MFEnumDeviceSources(&a, &mut devs, &mut count)?;
        let mut out = Vec::new();
        if !devs.is_null() && count > 0 {
            let slice = std::slice::from_raw_parts(devs, count as usize);
            for d in slice.iter().flatten() {
                out.push(d.clone());
            }
            windows::Win32::System::Com::CoTaskMemFree(Some(devs as *const _));
        }
        Ok(out)
    }
}

fn attr_string(a: &IMFActivate, key: &windows::core::GUID) -> String {
    unsafe {
        let mut buf = [0u16; 512];
        let mut len = 0u32;
        if a.GetString(key, &mut buf, Some(&mut len)).is_ok() {
            let n = (len as usize).min(buf.len());
            String::from_utf16_lossy(&buf[..n]).trim_end_matches('\0').to_string()
        } else {
            String::new()
        }
    }
}

pub fn list_video_devices() -> windows::core::Result<Vec<VideoDevice>> {
    let devs = enum_sources(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID)?;
    Ok(devs
        .iter()
        .map(|d| VideoDevice {
            name: attr_string(d, &MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME),
            link: attr_string(d, &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK),
        })
        .collect())
}

pub fn list_audio_devices() -> windows::core::Result<Vec<AudioDevice>> {
    let devs = enum_sources(&MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID)?;
    Ok(devs
        .iter()
        .map(|d| AudioDevice {
            name: attr_string(d, &MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME),
            id: attr_string(d, &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_ENDPOINT_ID),
        })
        .collect())
}



#[derive(Clone, Debug)]
pub struct Mode {
    pub w: u32,
    pub h: u32,
    pub fps: u32,
    pub fmt: String,
}

fn fmt_name(g: &windows::core::GUID) -> String {
    if *g == MFVideoFormat_NV12 {
        "NV12".into()
    } else if *g == MFVideoFormat_MJPG {
        "MJPEG".into()
    } else if *g == MFVideoFormat_YUY2 {
        "YUY2".into()
    } else if *g == MFVideoFormat_RGB32 {
        "RGB32".into()
    } else {
        "outro".into()
    }
}

pub fn video_modes(link: &str) -> windows::core::Result<Vec<Mode>> {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
        MFStartup(MF_VERSION, 0)?;
        let mut va_opt: Option<IMFAttributes> = None;
        MFCreateAttributes(&mut va_opt, 2)?;
        let va = va_opt.unwrap();
        va.SetGUID(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID,
        )?;
        va.SetString(
            &MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK,
            &HSTRING::from(link),
        )?;
        let src = MFCreateDeviceSource(&va)?;
        let reader = MFCreateSourceReaderFromMediaSource(&src, None)?;
        let mut out = Vec::new();
        let mut i = 0u32;
        while i < 256 {
            match reader.GetNativeMediaType(FIRST_VIDEO, i) {
                Ok(mt) => {
                    if mt.GetGUID(&MF_MT_MAJOR_TYPE).map(|g| g == MFMediaType_Video).unwrap_or(false) {
                        let sub = mt.GetGUID(&MF_MT_SUBTYPE).unwrap_or_default();
                        let size = mt.GetUINT64(&MF_MT_FRAME_SIZE).unwrap_or(0);
                        let rate = mt.GetUINT64(&MF_MT_FRAME_RATE).unwrap_or(0);
                        let num = (rate >> 32) as u32;
                        let den = (rate & 0xFFFF_FFFF) as u32;
                        let fps = if den > 0 { num / den } else { num };
                        out.push(Mode {
                            w: (size >> 32) as u32,
                            h: (size & 0xFFFF_FFFF) as u32,
                            fps,
                            fmt: fmt_name(&sub),
                        });
                    }
                    i += 1;
                }
                Err(_) => break,
            }
        }
        Ok(out)
    }
}



fn apply_color(mt: &IMFMediaType, cfg: &Config) -> windows::core::Result<()> {
    unsafe {
        let cs2020 = cfg.color_space.eq_ignore_ascii_case("2020");
        let full = cfg.color_range.eq_ignore_ascii_case("full");
        mt.SetUINT32(&MF_MT_YUV_MATRIX, if cs2020 { MFVideoTransferMatrix_BT2020_10.0 as u32 } else { MFVideoTransferMatrix_BT709.0 as u32 })?;
        mt.SetUINT32(&MF_MT_VIDEO_PRIMARIES, if cs2020 { MFVideoPrimaries_BT2020.0 as u32 } else { MFVideoPrimaries_BT709.0 as u32 })?;
        mt.SetUINT32(&MF_MT_TRANSFER_FUNCTION, if cs2020 { MFVideoTransFunc_2020.0 as u32 } else { MFVideoTransFunc_709.0 as u32 })?;
        mt.SetUINT32(&MF_MT_VIDEO_NOMINAL_RANGE, if full { MFNominalRange_0_255.0 as u32 } else { MFNominalRange_16_235.0 as u32 })?;
    }
    Ok(())
}
fn resolve_mode(link: &str, w: u32, h: u32, fps: u32, format: &str) -> (u32, u32, u32) {
    let all = match video_modes(link) {
        Ok(m) => m,
        Err(_) => return (w, h, fps),
    };
    if all.is_empty() {
        return (w, h, fps);
    }
    let want = format.trim().to_lowercase();
    let filtered: Vec<Mode> = if want.is_empty() || want == "auto" {
        all.clone()
    } else {
        all.iter()
            .filter(|m| m.fmt.to_lowercase() == want)
            .cloned()
            .collect()
    };
    let modes = if filtered.is_empty() {
        if want != "auto" && !want.is_empty() {
            println!("FORMATO {} nao disponivel nesta placa; a usar o modo mais proximo", format);
        }
        all
    } else {
        filtered
    };
    if modes.iter().any(|m| m.w == w && m.h == h && m.fps == fps) {
        return (w, h, fps);
    }
    let target_area = (w as i64) * (h as i64);
    let best = modes
        .iter()
        .min_by_key(|m| ((m.w as i64) * (m.h as i64) - target_area).abs())
        .unwrap();
    let candidates: Vec<&Mode> = modes
        .iter()
        .filter(|m| m.w == best.w && m.h == best.h)
        .collect();
    let chosen = candidates
        .iter()
        .min_by_key(|m| (m.fps as i64 - fps as i64).abs())
        .unwrap();
    let (rw, rh, rf) = (chosen.w, chosen.h, chosen.fps);
    if rw != w || rh != h || rf != fps {
        println!(
            "MODO AJUSTADO: pedido {}x{}@{} -> usado {}x{}@{} (a placa nao suporta o pedido)",
            w, h, fps, rw, rh, rf
        );
    }
    (rw, rh, rf)
}

#[cfg(test)]
mod preview_tests {
    use super::nv12_to_bgrx;

    fn solid(y: u8, u: u8, v: u8, w: usize, h: usize) -> Vec<u8> {
        // NV12: plano Y seguido de UV intercalado (U primeiro).
        let mut f = vec![y; w * h];
        for _ in 0..(w * h / 4) {
            f.push(u);
            f.push(v);
        }
        f
    }

    fn px(rgb: &(usize, usize, Vec<u8>)) -> (u8, u8, u8) {
        (rgb.2[0], rgb.2[1], rgb.2[2]) // B, G, R
    }

    #[test]
    fn bgrx_white_and_black() {
        let w = solid(235, 128, 128, 8, 8);
        let (b, g, r) = px(&nv12_to_bgrx(&w, 8, 8, 1, false, false));
        assert!(b > 245 && g > 245 && r > 245, "branco: {b} {g} {r}");
        let k = solid(16, 128, 128, 8, 8);
        let (b, g, r) = px(&nv12_to_bgrx(&k, 8, 8, 1, false, false));
        assert!(b < 5 && g < 5 && r < 5, "preto: {b} {g} {r}");
    }

    #[test]
    fn bgrx_red_is_bgr_order() {
        let f = solid(63, 102, 240, 8, 8);
        let (b, g, r) = px(&nv12_to_bgrx(&f, 8, 8, 1, false, false));
        assert!(r > 200 && g < 25 && b < 25, "vermelho BGRX: B={b} G={g} R={r}");
    }

    #[test]
    fn bgrx_blue_is_bgr_order() {
        let f = solid(35, 240, 110, 8, 8);
        let (b, g, r) = px(&nv12_to_bgrx(&f, 8, 8, 1, false, false));
        assert!(b > 200 && g < 25 && r < 25, "azul BGRX: B={b} G={g} R={r}");
    }

    #[test]
    fn bgrx_downscale() {
        let f = solid(235, 128, 128, 16, 16);
        let (w, h, out) = nv12_to_bgrx(&f, 16, 16, 2, false, false);
        assert_eq!(w, 8);
        assert_eq!(h, 8);
        assert_eq!(out.len(), 8 * 8 * 4);
    }
}

