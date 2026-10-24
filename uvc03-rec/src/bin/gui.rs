#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU32, Ordering};
use std::sync::Arc;
use std::thread::JoinHandle;

use uvc03_rec::{record, Config, Status};
use windows::core::{w, HSTRING, PCWSTR};
use windows::Win32::Foundation::{COLORREF, HINSTANCE, HWND, LPARAM, LRESULT, POINT, WPARAM};
use windows::Win32::Graphics::Gdi::{
    BeginPaint, CreateFontW, CreatePen, CreateSolidBrush, DeleteObject, DrawTextW, EndPaint,
    FillRect, GetStockObject, InvalidateRect, RoundRect, SelectObject, SetBkColor, SetBkMode,
    SetTextColor, CLEARTYPE_QUALITY, CLIP_DEFAULT_PRECIS, DEFAULT_CHARSET, DT_CENTER, DT_SINGLELINE,
    DT_VCENTER, FF_DONTCARE, FW_BOLD, FW_NORMAL, HBRUSH, HDC, HFONT, OUT_DEFAULT_PRECIS, PAINTSTRUCT,
    PS_SOLID,
};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Controls::DRAWITEMSTRUCT;
use windows::Win32::UI::WindowsAndMessaging::*;

static SYS_DARK: AtomicBool = AtomicBool::new(false);
static SYS_ACCENT: AtomicU32 = AtomicU32::new(0);
static HOVER_CTRL: AtomicI32 = AtomicI32::new(-1);
static CARD_BRUSH: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);

#[derive(Clone, Copy)]
struct Palette {
    bg: COLORREF,
    card: COLORREF,
    stroke: COLORREF,
    pill: COLORREF,
    pill_sel: COLORREF,
    text: COLORREF,
    muted: COLORREF,
    accent: COLORREF,
    accent_bg: COLORREF,
    on_accent: COLORREF,
    hover: COLORREF,
    hover_sel: COLORREF,
}

fn reg_dword(sub: PCWSTR, name: PCWSTR) -> Option<u32> {
    unsafe {
        use windows::Win32::System::Registry::{
            RegGetValueW, HKEY_CURRENT_USER, RRF_RT_REG_DWORD,
        };
        let mut val: u32 = 0;
        let mut cb: u32 = 4;
        let rc = RegGetValueW(
            HKEY_CURRENT_USER,
            sub,
            name,
            RRF_RT_REG_DWORD,
            None,
            Some(&mut val as *mut u32 as *mut std::ffi::c_void),
            Some(&mut cb),
        );
        if rc.0 == 0 {
            Some(val)
        } else {
            None
        }
    }
}

fn refresh_system_theme() -> bool {
    let light = reg_dword(
        w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"),
        w!("AppsUseLightTheme"),
    )
    .unwrap_or(1);
    let dark = light == 0;
    let accent = reg_dword(w!("Software\\Microsoft\\Windows\\DWM"), w!("AccentColor"))
        .map(|v| v & 0x00ff_ffff)
        .filter(|v| *v != 0)
        .unwrap_or(0x00bd6c0f);
    let changed =
        SYS_DARK.load(Ordering::Relaxed) != dark || SYS_ACCENT.load(Ordering::Relaxed) != accent;
    SYS_DARK.store(dark, Ordering::Relaxed);
    SYS_ACCENT.store(accent, Ordering::Relaxed);
    changed
}

fn mix(a: u32, b: u32, ta: f32) -> u32 {
    let f = |s: u32| ((a >> s) & 0xff) as f32;
    let g = |s: u32| ((b >> s) & 0xff) as f32;
    let r = (f(0) * ta + g(0) * (1.0 - ta)) as u32;
    let gg = (f(8) * ta + g(8) * (1.0 - ta)) as u32;
    let bb = (f(16) * ta + g(16) * (1.0 - ta)) as u32;
    r | (gg << 8) | (bb << 16)
}

fn lum(c: u32) -> f32 {
    let r = (c & 0xff) as f32 / 255.0;
    let g = ((c >> 8) & 0xff) as f32 / 255.0;
    let b = ((c >> 16) & 0xff) as f32 / 255.0;
    0.2126 * r + 0.7152 * g + 0.0722 * b
}

fn pal() -> Palette {
    let dark = SYS_DARK.load(Ordering::Relaxed);
    let a = {
        let v = SYS_ACCENT.load(Ordering::Relaxed);
        if v == 0 {
            0x00bd6c0f
        } else {
            v
        }
    };
    if dark {
        let txt = if lum(a) < 0.35 {
            mix(a, 0x00ffffff, 0.62)
        } else {
            a
        };
        Palette {
            bg: COLORREF(0x001f1f1f),
            card: COLORREF(0x00292929),
            stroke: COLORREF(0x003d3d3d),
            pill: COLORREF(0x00292929),
            pill_sel: COLORREF(mix(a, 0x00292929, 0.22)),
            text: COLORREF(0x00ffffff),
            muted: COLORREF(0x00c7c7c7),
            accent: COLORREF(txt),
            accent_bg: COLORREF(a),
            on_accent: COLORREF(if lum(a) > 0.55 { 0x00101010 } else { 0x00ffffff }),
            hover: COLORREF(0x00404040),
            hover_sel: COLORREF(mix(a, 0x00292929, 0.34)),
        }
    } else {
        Palette {
            bg: COLORREF(0x00f5f5f5),
            card: COLORREF(0x00ffffff),
            stroke: COLORREF(0x00d1d1d1),
            pill: COLORREF(0x00ffffff),
            pill_sel: COLORREF(mix(a, 0x00ffffff, 0.12)),
            text: COLORREF(0x00242424),
            muted: COLORREF(0x00616161),
            accent: COLORREF(a),
            accent_bg: COLORREF(a),
            on_accent: COLORREF(if lum(a) > 0.55 { 0x00101010 } else { 0x00ffffff }),
            hover: COLORREF(0x00ededed),
            hover_sel: COLORREF(mix(a, 0x00ffffff, 0.20)),
        }
    }
}

fn card_brush() -> HBRUSH {
    let v = CARD_BRUSH.load(Ordering::Relaxed);
    if v == 0 {
        let b = unsafe { CreateSolidBrush(pal().card) };
        CARD_BRUSH.store(b.0 as usize, Ordering::Relaxed);
        b
    } else {
        HBRUSH(v as *mut std::ffi::c_void)
    }
}

fn reset_card_brush() {
    let v = CARD_BRUSH.swap(0, Ordering::Relaxed);
    if v != 0 {
        unsafe {
            let _ = DeleteObject(HBRUSH(v as *mut std::ffi::c_void));
        }
    }
}

unsafe fn set_caption(hwnd: HWND) {
    use windows::Win32::Graphics::Dwm::{DwmSetWindowAttribute, DWMWINDOWATTRIBUTE};
    let p = pal();
    let dark: u32 = if SYS_DARK.load(Ordering::Relaxed) { 1 } else { 0 };
    let _ = DwmSetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE(20),
        &dark as *const u32 as *const std::ffi::c_void,
        4,
    );
    let cap = p.bg.0;
    let _ = DwmSetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE(35),
        &cap as *const u32 as *const std::ffi::c_void,
        4,
    );
    let txt = p.text.0;
    let _ = DwmSetWindowAttribute(
        hwnd,
        DWMWINDOWATTRIBUTE(36),
        &txt as *const u32 as *const std::ffi::c_void,
        4,
    );
}

unsafe extern "system" fn inv_child(h: HWND, _l: LPARAM) -> windows::Win32::Foundation::BOOL {
    let _ = InvalidateRect(h, None, true);
    windows::Win32::Foundation::BOOL(1)
}

const GROUP_RES: usize = 0;
const GROUP_FPS: usize = 1;
const GROUP_CODEC: usize = 2;
const GROUP_PROFILE: usize = 3;
const GROUP_BITRATE: usize = 4;
const GROUP_AUDIO: usize = 5;
const GROUP_ABR: usize = 6;
const GROUP_CONT: usize = 7;
const GROUP_DUR: usize = 8;
const GROUP_HW: usize = 9;
const GROUP_RATE: usize = 10;
const GROUP_GOP: usize = 11;

const RES: [(&str, u32, u32); 5] = [
    ("1920x1080", 1920, 1080),
    ("2560x1440", 2560, 1440),
    ("1280x720", 1280, 720),
    ("3840x2160", 3840, 2160),
    ("640x480", 640, 480),
];
const FPS: [u32; 3] = [30, 60, 15];
const CODECS: [&str; 2] = ["H.264", "H.265"];
const PROFILES: [&str; 3] = ["Baseline", "Main", "High"];
const BPS: [u32; 6] = [4, 8, 12, 20, 30, 50];
const AUDIOS: [&str; 3] = ["Sem audio", "AAC", "PCM 24-bit"];
const ABR: [u32; 3] = [128, 192, 256];
const CONTS: [&str; 3] = ["MP4", "MKV", "TS"];
const DURS: [u64; 5] = [0, 5, 10, 30, 60];
const HWS: [&str; 2] = ["GPU", "CPU"];
const RATES: [&str; 2] = ["CBR", "VBR"];
const GOPS: [u32; 4] = [1, 2, 4, 8];

struct App {
    hwnd: HWND,
    res: usize,
    fps: usize,
    codec: usize,
    profile: usize,
    bitrate: usize,
    audio: usize,
    abr: usize,
    cont: usize,
    dur: usize,
    hw: usize,
    rate: usize,
    gop: usize,
    folder: HWND,
    status: HWND,
    log: HWND,
    stop: Option<Arc<AtomicBool>>,
    st: Option<Arc<Status>>,
    handle: Option<JoinHandle<()>>,
    output: String,
    lines: String,
}

impl App {
    fn recording(&self) -> bool {
        self.handle.is_some()
    }

    fn start(&mut self) {
        if self.recording() {
            return;
        }
        let (_, w, h) = RES[self.res.min(4)];
        let ext = if self.audio == 2 {
            "mkv"
        } else {
            ["mp4", "mkv", "ts"][self.cont.min(2)]
        };
        let folder = get_text(self.folder);
        std::fs::create_dir_all(&folder).ok();
        let out = next_name(&folder, ext);
        let fps = FPS[self.fps.min(2)];
        let cfg = Config {
            out: out.clone(),
            w,
            h,
            fps,
            bitrate: BPS[self.bitrate.min(5)] * 1_000_000,
            hevc: self.codec == 1,
            profile: match self.profile {
                0 => 66,
                1 => 77,
                _ => 100,
            },
            hw: self.hw == 0,
            audio: self.audio != 0,
            audio_kbps: ABR[self.abr.min(2)],
            container: ext.to_string(),
            cbr: self.rate == 0,
            pcm: self.audio == 2,
            gop: GOPS[self.gop.min(3)] * fps,
            seconds: {
                let d = DURS[self.dur.min(4)];
                if d > 0 {
                    Some(d)
                } else {
                    None
                }
            },
            ..Default::default()
        };

        let stop = Arc::new(AtomicBool::new(false));
        let st = Arc::new(Status::new());
        let (c2, s2, st2) = (cfg.clone(), stop.clone(), st.clone());
        let handle = std::thread::spawn(move || {
            if let Err(e) = record(&c2, &s2, &st2) {
                if let Ok(mut g) = st2.error.lock() {
                    *g = Some(e.to_string());
                }
            }
            st2.finished.store(true, Ordering::SeqCst);
        });

        self.output = out.clone();
        self.add_line(&format!("A gravar {}", out));
        self.stop = Some(stop);
        self.st = Some(st);
        self.handle = Some(handle);
    }

    fn stop_rec(&self) {
        if let Some(s) = &self.stop {
            s.store(true, Ordering::SeqCst);
        }
    }

    fn add_line(&mut self, s: &str) {
        self.lines.push_str(s);
        self.lines.push_str("\r\n");
        let h = HSTRING::from(self.lines.as_str());
        unsafe {
            SetWindowTextW(self.log, PCWSTR(h.as_ptr()));
            SendMessageW(self.log, 0x00B1, WPARAM(usize::MAX), LPARAM(-1));
        }
    }

    fn tick(&mut self) {
        if refresh_system_theme() {
            reset_card_brush();
            unsafe {
                set_caption(self.hwnd);
                let _ = InvalidateRect(self.hwnd, None, true);
                let _ = EnumChildWindows(self.hwnd, Some(inv_child), LPARAM(0));
            }
        }

        unsafe {
            let mut pt = POINT::default();
            if GetCursorPos(&mut pt).is_ok() {
                let under = WindowFromPoint(pt);
                let id = if under.0.is_null() {
                    -1
                } else {
                    GetDlgCtrlID(under)
                };
                let prev = HOVER_CTRL.swap(id, Ordering::Relaxed);
                if prev != id {
                    if prev >= 0 {
                        if let Ok(h) = GetDlgItem(self.hwnd, prev) {
                            let _ = InvalidateRect(h, None, false);
                        }
                    }
                    if id >= 0 {
                        if let Ok(h) = GetDlgItem(self.hwnd, id) {
                            let _ = InvalidateRect(h, None, false);
                        }
                    }
                }
            }
        }

        let finished = self
            .st
            .as_ref()
            .map(|s| s.finished.load(Ordering::SeqCst))
            .unwrap_or(false);
        if finished && self.handle.is_some() {
            if let Some(h) = self.handle.take() {
                let _ = h.join();
            }
            let err = self
                .st
                .as_ref()
                .and_then(|s| s.error.lock().ok().and_then(|g| g.clone()));
            match err {
                Some(e) => self.add_line(&format!("ERRO: {}", e)),
                None => self.add_line(&format!("Terminado: {}", self.output)),
            }
            self.stop = None;
            set_text(self.status, "Pronto  -  sem gravacao");
            return;
        }

        if let Some(st) = &self.st {
            if self.recording() {
                let el = st.elapsed();
                let frames = st.frames.load(Ordering::Relaxed);
                let fps = if el.as_secs_f64() > 0.0 {
                    frames as f64 / el.as_secs_f64()
                } else {
                    0.0
                };
                let active = st.path.lock().ok().map(|p| p.clone()).unwrap_or_default();
                let size = std::fs::metadata(&active).map(|m| m.len()).unwrap_or(0);
                set_text(
                    self.status,
                    &format!(
                        "A GRAVAR  {:02}:{:02}   {} frames ({:.0} fps)   {:.1} MB",
                        el.as_secs() / 60,
                        el.as_secs() % 60,
                        frames,
                        fps,
                        size as f64 / 1_048_576.0
                    ),
                );
            }
        }
    }
}

impl Drop for App {
    fn drop(&mut self) {
        if let Some(s) = &self.stop {
            s.store(true, Ordering::SeqCst);
        }
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}

fn get_text(h: HWND) -> String {
    let mut buf = [0u16; 512];
    let n = unsafe { GetWindowTextW(h, &mut buf) };
    String::from_utf16_lossy(&buf[..n.max(0) as usize])
}

fn set_text(h: HWND, s: &str) {
    let hs = HSTRING::from(s);
    unsafe {
        SetWindowTextW(h, PCWSTR(hs.as_ptr()));
    }
}

fn next_name(folder: &str, ext: &str) -> String {
    for i in 1..10_000 {
        let p = format!("{}\\UVC03_{:04}.{}", folder, i, ext);
        if !std::path::Path::new(&p).exists() {
            return p;
        }
    }
    format!("{}\\UVC03.{}", folder, ext)
}

fn group_base(g: usize) -> i32 {
    300 + (g as i32) * 10
}

unsafe fn mk(
    parent: HWND,
    class: PCWSTR,
    text: PCWSTR,
    style: u32,
    x: i32,
    y: i32,
    w: i32,
    h: i32,
    id: i32,
    inst: HINSTANCE,
    font: HFONT,
) -> HWND {
    let hw = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        class,
        text,
        WINDOW_STYLE(style),
        x,
        y,
        w,
        h,
        parent,
        HMENU(id as isize as *mut std::ffi::c_void),
        inst,
        None,
    )
    .unwrap_or_default();
    if !hw.0.is_null() {
        SendMessageW(hw, WM_SETFONT, WPARAM(font.0 as usize), LPARAM(1));
    }
    hw
}

unsafe fn pill(
    parent: HWND,
    label: &str,
    x: i32,
    y: i32,
    w: i32,
    id: i32,
    inst: HINSTANCE,
    font: HFONT,
) -> HWND {
    let t = HSTRING::from(label);
    mk(
        parent,
        w!("BUTTON"),
        PCWSTR(t.as_ptr()),
        0x5000000B,
        x,
        y,
        w,
        30,
        id,
        inst,
        font,
    )
}

unsafe fn build_ui(hwnd: HWND, inst: HINSTANCE) -> *mut App {
    let face = HSTRING::from("Segoe UI Variable Text");
    let face_display = HSTRING::from("Segoe UI Variable Display");
    let font = CreateFontW(
        -15,
        0,
        0,
        0,
        FW_NORMAL.0 as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET.0 as u32,
        OUT_DEFAULT_PRECIS.0 as u32,
        CLIP_DEFAULT_PRECIS.0 as u32,
        CLEARTYPE_QUALITY.0 as u32,
        FF_DONTCARE.0 as u32,
        PCWSTR(face.as_ptr()),
    );
    let font_small = CreateFontW(
        -12,
        0,
        0,
        0,
        FW_NORMAL.0 as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET.0 as u32,
        OUT_DEFAULT_PRECIS.0 as u32,
        CLIP_DEFAULT_PRECIS.0 as u32,
        CLEARTYPE_QUALITY.0 as u32,
        FF_DONTCARE.0 as u32,
        PCWSTR(face.as_ptr()),
    );
    let font_title = CreateFontW(
        -29,
        0,
        0,
        0,
        FW_BOLD.0 as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET.0 as u32,
        OUT_DEFAULT_PRECIS.0 as u32,
        CLIP_DEFAULT_PRECIS.0 as u32,
        CLEARTYPE_QUALITY.0 as u32,
        FF_DONTCARE.0 as u32,
        PCWSTR(face_display.as_ptr()),
    );

    mk(hwnd, w!("STATIC"), w!("Gravador UVC03"), 0x50000000, 24, 18, 500, 44, -1, inst, font_title);
    mk(
        hwnd,
        w!("STATIC"),
        w!("Display capture-UVC03   -   NV12 (Media Foundation)   -   H.264 / H.265   -   PCM"),
        0x50000000,
        26,
        62,
        800,
        20,
        -1,
        inst,
        font_small,
    );

    let mut app = Box::new(App {
        hwnd,
        res: 0,
        fps: 0,
        codec: 0,
        profile: 2,
        bitrate: 2,
        audio: 1,
        abr: 1,
        cont: 0,
        dur: 0,
        hw: 0,
        rate: 0,
        gop: 1,
        folder: HWND::default(),
        status: HWND::default(),
        log: HWND::default(),
        stop: None,
        st: None,
        handle: None,
        output: String::new(),
        lines: String::new(),
    });

    let label = |text: &str, x: i32, y: i32| -> HWND {
        let t = HSTRING::from(text);
        unsafe { mk(hwnd, w!("STATIC"), PCWSTR(t.as_ptr()), 0x50000000, x, y, 300, 18, -1, inst, font_small) }
    };

    let mut y = 96;
    label("RESOLUCAO", 24, y);
    for (i, r) in RES.iter().enumerate() {
        pill(hwnd, r.0, 24 + (i as i32) * 82, y + 20, 78, group_base(GROUP_RES) + i as i32, inst, font);
    }
    y += 62;
    label("FPS", 24, y);
    for (i, f) in FPS.iter().enumerate() {
        pill(hwnd, &f.to_string(), 24 + (i as i32) * 62, y + 20, 58, group_base(GROUP_FPS) + i as i32, inst, font);
    }
    y += 62;
    label("CODEC", 24, y);
    for (i, c) in CODECS.iter().enumerate() {
        pill(hwnd, c, 24 + (i as i32) * 82, y + 20, 78, group_base(GROUP_CODEC) + i as i32, inst, font);
    }
    y += 62;
    label("PERFIL H.264", 24, y);
    for (i, p) in PROFILES.iter().enumerate() {
        pill(hwnd, p, 24 + (i as i32) * 86, y + 20, 82, group_base(GROUP_PROFILE) + i as i32, inst, font);
    }
    y += 62;
    label("BITRATE (Mbps)", 24, y);
    for (i, b) in BPS.iter().enumerate() {
        pill(hwnd, &b.to_string(), 24 + (i as i32) * 54, y + 20, 50, group_base(GROUP_BITRATE) + i as i32, inst, font);
    }

    let rx = 470;
    let mut ry = 96;
    label("AUDIO", rx, ry);
    for (i, a) in AUDIOS.iter().enumerate() {
        pill(hwnd, a, rx + (i as i32) * 92, ry + 20, 88, group_base(GROUP_AUDIO) + i as i32, inst, font);
    }
    ry += 62;
    label("BITRATE AUDIO (kbps)", rx, ry);
    for (i, a) in ABR.iter().enumerate() {
        pill(hwnd, &a.to_string(), rx + (i as i32) * 62, ry + 20, 58, group_base(GROUP_ABR) + i as i32, inst, font);
    }
    ry += 62;
    label("CONTENTOR", rx, ry);
    for (i, c) in CONTS.iter().enumerate() {
        pill(hwnd, c, rx + (i as i32) * 62, ry + 20, 58, group_base(GROUP_CONT) + i as i32, inst, font);
    }
    ry += 62;
    label("PARAR APOS", rx, ry);
    for (i, d) in DURS.iter().enumerate() {
        let t = if *d == 0 { "Manual".to_string() } else { format!("{} s", d) };
        pill(hwnd, &t, rx + (i as i32) * 68, ry + 20, 64, group_base(GROUP_DUR) + i as i32, inst, font);
    }
    ry += 62;
    label("ENCODER", rx, ry);
    for (i, h) in HWS.iter().enumerate() {
        pill(hwnd, h, rx + (i as i32) * 62, ry + 20, 58, group_base(GROUP_HW) + i as i32, inst, font);
    }
    ry += 62;
    label("RATE CONTROL", rx, ry);
    for (i, r) in RATES.iter().enumerate() {
        pill(hwnd, r, rx + (i as i32) * 62, ry + 20, 58, group_base(GROUP_RATE) + i as i32, inst, font);
    }
    ry += 62;
    label("KEYFRAME", rx, ry);
    for (i, g) in GOPS.iter().enumerate() {
        let t = format!("{} s", g);
        pill(hwnd, &t, rx + (i as i32) * 62, ry + 20, 58, group_base(GROUP_GOP) + i as i32, inst, font);
    }

    let fy = 540;
    label("PASTA", 24, fy - 2);
    let up = std::env::var("USERPROFILE").unwrap_or_else(|_| "C:\\".into());
    let folder = format!("{}\\Videos\\UVC03", up);
    let fh = HSTRING::from(folder.as_str());
    app.folder = mk(hwnd, w!("EDIT"), PCWSTR(fh.as_ptr()), 0x50800080, 90, fy, 560, 28, 109, inst, font_small);
    mk(hwnd, w!("BUTTON"), w!("Abrir"), 0x5000000B, 660, fy, 90, 28, 112, inst, font_small);

    let ay = 590;
    mk(hwnd, w!("BUTTON"), w!("GRAVAR"), 0x5000000B, 24, ay, 170, 46, 110, inst, font);
    mk(hwnd, w!("BUTTON"), w!("PARAR"), 0x5000000B, 206, ay, 120, 46, 111, inst, font);

    app.status = mk(
        hwnd,
        w!("STATIC"),
        w!("Pronto  -  sem gravacao"),
        0x50000000,
        360,
        ay + 12,
        500,
        24,
        113,
        inst,
        font_small,
    );
    app.log = mk(hwnd, w!("EDIT"), w!(""), 0x50A00804, 24, 660, 880, 120, 114, inst, font_small);

    let raw = Box::into_raw(app);
    SetWindowLongPtrW(hwnd, GWLP_USERDATA, raw as isize);
    raw
}

unsafe fn draw_pill(d: &DRAWITEMSTRUCT, app: *mut App) {
    let id = d.CtlID as i32;
    let hdc: HDC = d.hDC;
    let mut rc = d.rcItem;
    let (selected, accent) = if id >= 300 {
        let g = ((id - 300) / 10) as usize;
        let i = ((id - 300) % 10) as usize;
        let a = &*app;
        let sel = match g {
            GROUP_RES => a.res == i,
            GROUP_FPS => a.fps == i,
            GROUP_CODEC => a.codec == i,
            GROUP_PROFILE => a.profile == i,
            GROUP_BITRATE => a.bitrate == i,
            GROUP_AUDIO => a.audio == i,
            GROUP_ABR => a.abr == i,
            GROUP_CONT => a.cont == i,
            GROUP_DUR => a.dur == i,
            GROUP_HW => a.hw == i,
            GROUP_RATE => a.rate == i,
            GROUP_GOP => a.gop == i,
            _ => false,
        };
        (sel, false)
    } else if id == 110 {
        (false, true)
    } else {
        (false, false)
    };

    let hovered = HOVER_CTRL.load(Ordering::Relaxed) == id;
    let (bg, border, fg) = if accent {
        (pal().accent_bg, pal().accent_bg, pal().on_accent)
    } else if selected {
        (if hovered { pal().hover_sel } else { pal().pill_sel }, pal().accent, pal().accent)
    } else if hovered {
        (pal().hover, pal().stroke, pal().text)
    } else {
        (pal().pill, pal().stroke, pal().text)
    };

    let brush = CreateSolidBrush(bg);
    let pen = CreatePen(PS_SOLID, 1, border);
    let ob = SelectObject(hdc, brush);
    let op = SelectObject(hdc, pen);
    RoundRect(hdc, rc.left, rc.top, rc.right, rc.bottom, 8, 8);
    SelectObject(hdc, ob);
    SelectObject(hdc, op);
    let _ = DeleteObject(brush);
    let _ = DeleteObject(pen);

    SetBkMode(hdc, windows::Win32::Graphics::Gdi::BACKGROUND_MODE(1));
    SetTextColor(hdc, fg);
    let mut text = [0u16; 128];
    let n = GetWindowTextW(d.hwndItem, &mut text);
    let mut tr = rc;
    DrawTextW(hdc, &mut text[..n.max(0) as usize], &mut tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
}

unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    match msg {
        0x0001 => {
            let cs = lp.0 as *const CREATESTRUCTW;
            let inst = if cs.is_null() {
                HINSTANCE::default()
            } else {
                (*cs).hInstance
            };
            build_ui(hwnd, inst);
            SetTimer(hwnd, 1, 200, None);
            LRESULT(0)
        }
        0x000F => {
            let mut ps = PAINTSTRUCT::default();
            let hdc = BeginPaint(hwnd, &mut ps);
            let bgb = CreateSolidBrush(pal().bg);
            FillRect(hdc, &ps.rcPaint, bgb);
            let _ = DeleteObject(bgb);
            let cardb = CreateSolidBrush(pal().card);
            let pen = CreatePen(PS_SOLID, 1, pal().stroke);
            let ob = SelectObject(hdc, cardb);
            let op = SelectObject(hdc, pen);
            RoundRect(hdc, 16, 84, 908, 570, 16, 16);
            RoundRect(hdc, 16, 650, 908, 792, 16, 16);
            SelectObject(hdc, ob);
            SelectObject(hdc, op);
            let _ = DeleteObject(cardb);
            let _ = DeleteObject(pen);
            let _ = EndPaint(hwnd, &ps);
            LRESULT(0)
        }
        0x0014 => LRESULT(1),
        0x0111 => {
            let id = (wp.0 & 0xFFFF) as i32;
            let app = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut App;
            if !app.is_null() {
                let a = &mut *app;
                if id >= 300 {
                    let g = ((id - 300) / 10) as usize;
                    let i = ((id - 300) % 10) as usize;
                    match g {
                        GROUP_RES => a.res = i,
                        GROUP_FPS => a.fps = i,
                        GROUP_CODEC => a.codec = i,
                        GROUP_PROFILE => a.profile = i,
                        GROUP_BITRATE => a.bitrate = i,
                        GROUP_AUDIO => a.audio = i,
                        GROUP_ABR => a.abr = i,
                        GROUP_CONT => a.cont = i,
                        GROUP_DUR => a.dur = i,
                        GROUP_HW => a.hw = i,
                        GROUP_RATE => a.rate = i,
                        GROUP_GOP => a.gop = i,
                        _ => {}
                    }
                    let base = group_base(g);
                    for k in 0..10 {
                        if let Ok(h) = GetDlgItem(hwnd, base + k) {
                            let _ = InvalidateRect(h, None, false);
                        }
                    }
                } else {
                    match id {
                        110 => a.start(),
                        111 => a.stop_rec(),
                        112 => {
                            let folder = get_text(a.folder);
                            let _ = std::process::Command::new("explorer").arg(folder).spawn();
                        }
                        _ => {}
                    }
                }
            }
            LRESULT(0)
        }
        0x002B => {
            let app = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut App;
            if !app.is_null() {
                let d = &*(lp.0 as *const DRAWITEMSTRUCT);
                draw_pill(d, app);
            }
            LRESULT(1)
        }
        0x0138 | 0x0133 => {
            let hdc = HDC(wp.0 as *mut std::ffi::c_void);
            SetTextColor(hdc, pal().text);
            SetBkColor(hdc, pal().card);
            LRESULT(card_brush().0 as isize)
        }
        0x0113 => {
            let app = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut App;
            if !app.is_null() {
                (*app).tick();
            }
            LRESULT(0)
        }
        0x0010 => {
            let app = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut App;
            if !app.is_null() {
                (*app).stop_rec();
                if let Some(h) = (*app).handle.take() {
                    let _ = h.join();
                }
                let _ = Box::from_raw(app);
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            }
            DestroyWindow(hwnd);
            LRESULT(0)
        }
        0x0002 => {
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wp, lp),
    }
}

fn main() {
    refresh_system_theme();
    unsafe {
        let inst = HINSTANCE(GetModuleHandleW(None).unwrap().0);
        let cls = WNDCLASSW {
            style: WNDCLASS_STYLES(0),
            lpfnWndProc: Some(wndproc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: inst,
            hIcon: HICON::default(),
            hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
            hbrBackground: CreateSolidBrush(pal().bg),
            lpszMenuName: PCWSTR::null(),
            lpszClassName: w!("UVC03Rec3"),
        };
        RegisterClassW(&cls);

        let title = HSTRING::from("Gravador UVC03");
        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            w!("UVC03Rec3"),
            PCWSTR(title.as_ptr()),
            WS_OVERLAPPEDWINDOW,
            80,
            40,
            940,
            840,
            HWND::default(),
            HMENU::default(),
            inst,
            None,
        )
        .unwrap_or_default();
        if hwnd.0.is_null() {
            return;
        }
        set_caption(hwnd);
        ShowWindow(hwnd, SW_SHOW);

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}

